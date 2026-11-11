using DocumentStatusSyncFunction.Services;
using ECMServicesDemo;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using System.Text.Json;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;

namespace DocumentStatusSyncFunction;

public class ArchiveReconciliation
{
    private readonly ILogger _logger;
    private readonly CorrespondenceDBContext _dbContext;
    private readonly IECMServiceClient _ecmClient;
    private readonly IKeyVaultService _keyVaultService;
    private readonly IServicebusService _serviceBusService;
    private string? awaitingImagingStatusCode;
    private string? archivedStatusCode;
    private string? failedStatusCode;
    private string? docRepository;
    private string? docSuperUser;
    private string? docSuperUserPwd;
    private string? tokenResponse;
    private DocumentumSecurityToken? token;

    // Constants for status descriptions
    private const string AWAITING_IMAGING_STATUS = "AwaitingImaging";
    private const string ARCHIVED_STATUS = "Archived";
    private const string FAILED_STATUS = "Failed";
    private const string FUNCTION_NAME = "ArchiveReconciliation";
    private const string DOCUMENTUM_QUEUE_NAME = "documentum-queue";

    // Time thresholds for compensating actions
    private static readonly TimeSpan WARNING_THRESHOLD = TimeSpan.FromHours(24);
    private static readonly TimeSpan FAILURE_THRESHOLD = TimeSpan.FromHours(48);

    /// <summary>
    /// Token refresh threshold: Refresh token when it's within 5 minutes of potential expiration
    /// Documentum tokens typically expire after 8 hours, so we refresh proactively
    /// </summary>
    private static readonly TimeSpan TOKEN_REFRESH_THRESHOLD = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of automatic token refresh retries before failing
    /// </summary>
    private const int MAX_TOKEN_REFRESH_RETRIES = 3;

    /// <summary>
    /// Delay between token refresh retry attempts
    /// </summary>
    private static readonly TimeSpan TOKEN_REFRESH_RETRY_DELAY = TimeSpan.FromSeconds(2);
    private DateTime _tokenAcquiredTime = DateTime.MinValue;
    private int _tokenRefreshAttempts = 0;

    public ArchiveReconciliation(
        ILoggerFactory loggerFactory, 
        CorrespondenceDBContext dbContext, 
        IECMServiceClient ecmClient, 
        IKeyVaultService keyVaultService, 
        IServicebusService serviceBusService)
    {
        _logger = loggerFactory.CreateLogger<ArchiveReconciliation>();
        _dbContext = dbContext;
        _ecmClient = ecmClient;
        _keyVaultService = keyVaultService;
        _serviceBusService = serviceBusService;
    }

    [Function("ArchiveReconciliation")]
    public async Task Run([TimerTrigger("* * * * * *")] TimerInfo myTimer)
    {
        try
        {
            _logger.LogInformation("C# Timer trigger function executed at: {executionTime}", DateTime.Now);

            //assign values to global variables from database and environment variables
            if (!await AssignInitialValuesAsync())
            {
                _logger.LogError("Failed to initialize configuration. Aborting execution.");
                return;
            }

            //get all documents with status 'AwaitingImaging' and no subsequent 'Archived' status
            var awaitImagingDocuments = await GetAwaitingImagingDocumentsAsync();

            _logger.LogInformation("Found {count} records with Statuscode 'AwaitingImaging'.", awaitImagingDocuments.Count);

            int successCount = 0;
            int failureCount = 0;
            int metadataVerificationFailureCount = 0;

            foreach (var record in awaitImagingDocuments)
            {
                try
                {
                    _logger.LogInformation("Processing: CorrespondenceId={id}, DocId={docId}", record.CorrespondenceId, record.DocId);
                    _logger.LogInformation("StatusUpdateTS: {timestamp}", record.StatusUpdateTS);
                    var duration = DateTime.UtcNow - record.StatusUpdateTS;
                    _logger.LogInformation("Duration in AwaitingImaging: {hours:F2} hours ({days:F2} days)", duration.TotalHours, duration.TotalDays);

                    // ===== VALIDATE AND REFRESH TOKEN IF NEEDED =====
                    if (!await EnsureValidTokenAsync())
                    {
                        _logger.LogError("Failed to obtain or refresh Documentum token. Skipping document processing.");
                        failureCount++;
                        continue;
                    }

                    // Retrieve and verify document from Documentum
                    var verificationResult = await VerifyDocumentInDocumentumAsync(record);

                    // Handle based on verification result
                    if (verificationResult.Status == VerificationStatus.NotFound)
                    {
                        // Document NOT found in Documentum - Execute compensating action logic
                        _logger.LogInformation("Document {docId} for CorrespondenceId {id} not found in Documentum. Executing compensating action logic.",
                            record.DocId, record.CorrespondenceId);

                        await HandleArchiveVerificationFailure(
                            record.CorrespondenceId,
                            record.DocId,
                            record.HistorySequenceNum,
                            record.StatusUpdateTS,
                            VerificationFailureReason.DocumentNotFound);

                        failureCount++;
                    }
                    else if (verificationResult.Status == VerificationStatus.MetadataMismatch)
                    {
                        // Document found but metadata does not match - Verification FAILED
                        _logger.LogWarning("Metadata verification FAILED for DocId {docId}, CorrespondenceId {id}. Mismatches: {mismatches}",
                            record.DocId, record.CorrespondenceId, string.Join("; ", verificationResult.MetadataMismatches ?? new List<string>()));

                        await HandleArchiveVerificationFailure(
                            record.CorrespondenceId,
                            record.DocId,
                            record.HistorySequenceNum,
                            record.StatusUpdateTS,
                            VerificationFailureReason.MetadataMismatch,
                            verificationResult.MetadataMismatches);

                        metadataVerificationFailureCount++;
                    }
                    else if (verificationResult.Status == VerificationStatus.Success)
                    {
                        // Document FOUND and metadata VERIFIED - Update to Archived
                        var newHistory = new CorrespondenceRequestStatusHistory
                        {
                            CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                            CorrespondenceId = record.CorrespondenceId,
                            HistorySequenceNum = record.HistorySequenceNum + 1,
                            StatusCode = archivedStatusCode ?? string.Empty,
                            StatusUpdatedBy = FUNCTION_NAME,
                            StatusUpdateTS = DateTime.UtcNow
                        };

                        _dbContext.CorrespondenceRequestStatusHistories.Add(newHistory);
                        _logger.LogInformation("Document {docId} verified successfully in Documentum. Added new history record for CorrespondenceId {id} with status {status}",
                            record.DocId, record.CorrespondenceId, archivedStatusCode);

                        successCount++;
                    }

                    if(successCount > 0)
                    {
                        // Send message to Service Bus queue after successful verification
                        await SendDocumentumQueueMessageAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing CorrespondenceId {id}, DocId {docId}. Continuing with next record.",
                        record.CorrespondenceId, record.DocId);
                    failureCount++;
                }
            }

            //Save all changes at once
            if (successCount > 0 || metadataVerificationFailureCount > 0 || failureCount > 0)
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Archive reconciliation completed: {success} successful verifications, {metadataFailures} metadata verification failures, {failures} other failures",
                    successCount, metadataVerificationFailureCount, failureCount);
            }

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation("Next timer schedule at: {nextSchedule}", myTimer.ScheduleStatus.Next);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in ArchiveReconciliation function execution.");
            throw;
        }
    }

    /// <summary>
    /// Sends a message to the documentum-queue Service Bus queue after successful verification.
    /// </summary>
    private async Task SendDocumentumQueueMessageAsync()
    {
        try
        {
            var message = new
            {
                VerificationStatus = "Success",
                VerifiedAt = DateTime.UtcNow,
                Source = FUNCTION_NAME
            };

            var messageJson = JsonSerializer.Serialize(message);

            await _serviceBusService.SendMessageAsync(DOCUMENTUM_QUEUE_NAME, messageJson);

            _logger.LogInformation("Successfully sent message to '{queueName}'", DOCUMENTUM_QUEUE_NAME);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to Service Bus queue. Document verification was successful but notification failed.");
            // Don't throw - we don't want to fail the entire process if queue send fails
            // The document status has already been updated successfully
        }
    }

    /// <summary>
    /// Ensures token is valid and not near expiration.
    /// Automatically refreshes token if needed.
    /// </summary>
    private async Task<bool> EnsureValidTokenAsync()
    {
        try
        {
            // Check if token is null
            if (token == null || string.IsNullOrEmpty(token.token))
            {
                _logger.LogWarning("Token is null or empty. Acquiring new token.");
                return await RefreshTokenAsync();
            }

            // Check if token is near expiration
            if (IsTokenNearExpiration())
            {
                _logger.LogInformation("Token approaching expiration. Refreshing token proactively.");
                return await RefreshTokenAsync();
            }

            _logger.LogInformation("Token is valid. Time since acquisition: {minutes:F2} minutes",
                (DateTime.UtcNow - _tokenAcquiredTime).TotalMinutes);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in EnsureValidTokenAsync");
            return false;
        }
    }

    /// <summary>
    /// Checks if token is near expiration based on acquisition time.
    /// Assumes Documentum tokens expire after approximately 8 hours.
    /// </summary>
    private bool IsTokenNearExpiration()
    {
        if (_tokenAcquiredTime == DateTime.MinValue)
        {
            return true; // Token not yet acquired
        }

        var tokenAge = DateTime.UtcNow - _tokenAcquiredTime;
        const int TOKEN_LIFETIME_HOURS = 8;
        var tokenExpiration = TimeSpan.FromHours(TOKEN_LIFETIME_HOURS);

        // Check if token is within refresh threshold of expiration
        var timeUntilExpiration = tokenExpiration - tokenAge;
        var isNearExpiration = timeUntilExpiration <= TOKEN_REFRESH_THRESHOLD;

        if (isNearExpiration)
        {
            _logger.LogWarning("Token expiration warning: Token will expire in {minutes:F2} minutes (Token age: {hours:F2} hours)",
                timeUntilExpiration.TotalMinutes, tokenAge.TotalHours);
        }

        return isNearExpiration;
    }

    /// <summary>
    /// Refreshes Documentum token with automatic retry logic.
    /// Implements exponential backoff for retries.
    /// </summary>
    private async Task<bool> RefreshTokenAsync()
    {
        _tokenRefreshAttempts = 0;

        while (_tokenRefreshAttempts < MAX_TOKEN_REFRESH_RETRIES)
        {
            try
            {
                _logger.LogInformation("Attempting to refresh token (Attempt {attempt}/{maxAttempts})",
                    _tokenRefreshAttempts + 1, MAX_TOKEN_REFRESH_RETRIES);

                // Acquire new token
                tokenResponse = await _ecmClient.getTokenAsync(docRepository ?? string.Empty,
                    docSuperUser ?? string.Empty,
                    docSuperUserPwd ?? string.Empty) ?? string.Empty;

                // Validate token response
                if (string.IsNullOrEmpty(tokenResponse))
                {
                    _logger.LogWarning("Token response is empty on attempt {attempt}", _tokenRefreshAttempts + 1);
                    _tokenRefreshAttempts++;

                    if (_tokenRefreshAttempts < MAX_TOKEN_REFRESH_RETRIES)
                    {
                        await Task.Delay(TOKEN_REFRESH_RETRY_DELAY);
                        continue;
                    }

                    _logger.LogError("Failed to refresh token after {maxAttempts} attempts", MAX_TOKEN_REFRESH_RETRIES);
                    return false;
                }

                // Update token and tracking
                token = new DocumentumSecurityToken
                {
                    token = tokenResponse,
                    repository = docRepository ?? string.Empty,
                    superuser = docSuperUser ?? string.Empty
                };
                _tokenAcquiredTime = DateTime.UtcNow;
                _tokenRefreshAttempts = 0; // Reset retry counter on success

                _logger.LogInformation("Token refreshed successfully at {time}", DateTime.UtcNow);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token refresh attempt {attempt} failed: {message}",
                    _tokenRefreshAttempts + 1, ex.Message);
                _tokenRefreshAttempts++;

                if (_tokenRefreshAttempts < MAX_TOKEN_REFRESH_RETRIES)
                {
                    // Exponential backoff: delay increases with each retry
                    var delayMs = (int)(TOKEN_REFRESH_RETRY_DELAY.TotalMilliseconds * Math.Pow(2, _tokenRefreshAttempts - 1));
                    _logger.LogInformation("Waiting {delayMs}ms before retry attempt", delayMs);
                    await Task.Delay(delayMs);
                }
            }
        }

        _logger.LogError("Failed to refresh token after {maxAttempts} attempts", MAX_TOKEN_REFRESH_RETRIES);
        return false;
    }

    /// <summary>
    /// Verifies document exists in Documentum and validates its metadata against expected values.
    /// </summary>
    private async Task<DocumentVerificationResult> VerifyDocumentInDocumentumAsync(AwaitingImagingDocument record)
    {
        try
        {
            if (string.IsNullOrEmpty(record.DocId))
            {
                _logger.LogWarning("DocId is null or empty for CorrespondenceId {id}", record.CorrespondenceId);
                return new DocumentVerificationResult { Status = VerificationStatus.NotFound };
            }
            
            // Query Documentum for document and metadata
            var dqlQuery = $"SELECT r_object_id, cmg_doc_id, object_name, cmg_doc_type, cmg_doc_subtype FROM cmg_doc WHERE cmg_doc_id = '{record.DocId}'";
            var dqlResponse = await _ecmClient.execDQLAsync(token ?? new DocumentumSecurityToken(), docSuperUser ?? string.Empty, docRepository ?? string.Empty, dqlQuery);

            if (string.IsNullOrWhiteSpace(dqlResponse))
            {
                _logger.LogWarning("DQL query returned null or empty response for DocId {docId}", record.DocId);
                return new DocumentVerificationResult { Status = VerificationStatus.NotFound };
            }

            // Parse document metadata from response
            var documentumMetadata = ParseDocumentumMetadata(dqlResponse, record.DocId);

            if (documentumMetadata == null)
            {
                _logger.LogWarning("Document {docId} not found in Documentum", record.DocId);
                return new DocumentVerificationResult { Status = VerificationStatus.NotFound };
            }

            // Verify metadata matches expected values
            var metadataValidation = ValidateArchiveMetadata(record, documentumMetadata);

            if (!metadataValidation.IsValid)
            {
                _logger.LogWarning("Metadata validation FAILED for DocId {docId}. Mismatches: {mismatches}",
                    record.DocId, string.Join("; ", metadataValidation.Mismatches ?? new List<string>()));

                return new DocumentVerificationResult
                {
                    Status = VerificationStatus.MetadataMismatch,
                    MetadataMismatches = metadataValidation.Mismatches ?? new List<string>(),
                    DocumentumMetadata = documentumMetadata
                };
            }

            _logger.LogInformation("Document {docId} found and metadata verified successfully in Documentum", record.DocId);

            return new DocumentVerificationResult
            {
                Status = VerificationStatus.Success,
                DocumentumMetadata = documentumMetadata,
                MetadataMismatches = new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying document {docId} in Documentum", record.DocId);
            return new DocumentVerificationResult { Status = VerificationStatus.NotFound };
        }
    }

    /// <summary>
    /// Validates archive metadata from Documentum against expected values from database.
    /// NOTE: This is generic validation code. Will be updated based on finalized metadata
    /// requirements from Venture, Liz, Lisa, and Documentum team.
    /// </summary>
    private MetadataValidationResult ValidateArchiveMetadata(AwaitingImagingDocument dbRecord, DocumentumMetadata documentumData)
    {
        var result = new MetadataValidationResult { IsValid = true, Mismatches = new List<string>() };

        // Validation 1: Verify DocId matches
        if (dbRecord.DocId != documentumData.CmgDocId)
        {
            result.IsValid = false;
            result.Mismatches.Add($"DocId mismatch - Expected: {dbRecord.DocId}, Found: {documentumData.CmgDocId}");
        }

        // Validation 2: Verify Document Type (if present in DB)
        // NOTE: This validation will be enhanced based on Venture's metadata specifications
        if (!string.IsNullOrEmpty(dbRecord.ArchiveDocumentTypeCode) &&
            !string.IsNullOrEmpty(documentumData.DocumentType) &&
            dbRecord.ArchiveDocumentTypeCode != documentumData.DocumentType)
        {
            result.IsValid = false;
            result.Mismatches.Add($"DocumentType mismatch - Expected: {dbRecord.ArchiveDocumentTypeCode}, Found: {documentumData.DocumentType}");
        }

        // Validation 3: Verify Document SubType (if present in DB)
        // NOTE: This validation will be enhanced based on Venture's metadata specifications
        if (!string.IsNullOrEmpty(dbRecord.ArchiveDocumentSubTypeCode) &&
            !string.IsNullOrEmpty(documentumData.DocumentSubType) &&
            dbRecord.ArchiveDocumentSubTypeCode != documentumData.DocumentSubType)
        {
            result.IsValid = false;
            result.Mismatches.Add($"DocumentSubType mismatch - Expected: {dbRecord.ArchiveDocumentSubTypeCode}, Found: {documentumData.DocumentSubType}");
        }

        // TODO: Add additional metadata validations as requirements are finalized:
        // - Customer ID validation
        // - Policy number validation
        // - Document date validation
        // - Any other custom metadata fields from Venture

        if (!result.IsValid)
        {
            _logger.LogWarning("Archive metadata validation failed for DocId {docId}. Mismatches found: {count}",
                dbRecord.DocId, result.Mismatches.Count);
        }

        return result;
    }

    /// <summary>
    /// Parses Documentum metadata from DQL XML response.
    /// </summary>
    private DocumentumMetadata? ParseDocumentumMetadata(string returnXml, string expectedDocId)
    {
        if (string.IsNullOrWhiteSpace(returnXml))
        {
            _logger.LogError("DQL response XML is null or empty");
            return null;
        }

        try
        {
            var doc = XDocument.Parse(returnXml);
            var root = doc.Element("root");
            if (root == null)
            {
                _logger.LogError("DQL response XML does not contain 'root' element");
                return null;
            }

            var obj = root.Element("object");
            if (obj == null)
            {
                _logger.LogInformation("DQL response XML does not contain 'object' element - document may not exist in Documentum");
                return null;
            }

            var metadata = new DocumentumMetadata
            {
                ObjectId = obj.Element("r_object_id")?.Value ?? string.Empty,
                CmgDocId = obj.Element("cmg_doc_id")?.Value ?? string.Empty,
                ObjectName = obj.Element("object_name")?.Value ?? string.Empty,
                DocumentType = obj.Element("cmg_doc_type")?.Value ?? string.Empty,
                DocumentSubType = obj.Element("cmg_doc_subtype")?.Value ?? string.Empty
                // TODO: Add additional metadata fields as requirements are finalized:
                // CustomerId = obj.Element("customer_id")?.Value ?? string.Empty,
                // PolicyNumber = obj.Element("policy_number")?.Value ?? string.Empty,
            };

            // Validate required fields
            if (string.IsNullOrEmpty(metadata.ObjectId) || string.IsNullOrEmpty(metadata.CmgDocId))
            {
                _logger.LogError("DQL response is missing required fields (r_object_id or cmg_doc_id)");
                return null;
            }

            // Verify we got the correct document
            if (metadata.CmgDocId != expectedDocId)
            {
                _logger.LogError("DQL response returned different document. Expected: {expected}, Got: {actual}",
                    expectedDocId, metadata.CmgDocId);
                return null;
            }

            _logger.LogInformation("Successfully parsed Documentum metadata for DocId {docId}: ObjectId={objectId}, Type={type}, SubType={subType}",
                metadata.CmgDocId, metadata.ObjectId, metadata.DocumentType, metadata.DocumentSubType);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing DQL response for DocId {docId}", expectedDocId);
            return null;
        }
    }

    /// <summary>
    /// Compensating action logic for archive verification failures.
    /// Handles both document not found and metadata mismatch scenarios.
    /// Evaluates how long the document has been in AwaitingImaging status and takes appropriate action:
    /// - Metadata mismatch: Execute compensating action immediately
    /// - Document not found (less than 24 hours): Continue polling (no action)
    /// - Document not found (24-48 hours): Log warning and continue polling
    /// - Document not found (48+ hours): Mark the request as Failed (compensating action)
    /// </summary>
    private async Task HandleArchiveVerificationFailure(
        Guid correspondenceId,
        string? docId,
        int currentHistorySequenceNum,
        DateTime statusUpdateTS,
        VerificationFailureReason failureReason,
        List<string>? metadataMismatches = null)
    {
        try
        {
            var duration = DateTime.UtcNow - statusUpdateTS;

            _logger.LogInformation("COMPENSATING ACTION LOGIC TRIGGERED");
            _logger.LogInformation("CorrespondenceId: {corrId}, DocId: {docId}", correspondenceId, docId);
            _logger.LogInformation("Failure Reason: {reason}", failureReason);
            _logger.LogInformation("Duration in AwaitingImaging: {hours:F2} hours ({days:F2} days)", duration.TotalHours, duration.TotalDays);

            if (metadataMismatches != null && metadataMismatches.Any())
            {
                _logger.LogInformation("Metadata Mismatches: {mismatches}", string.Join("; ", metadataMismatches));
            }

            // For metadata mismatches, execute compensating action immediately
            if (failureReason == VerificationFailureReason.MetadataMismatch)
            {
                _logger.LogWarning("ACTION: Execute compensating action immediately (Metadata Mismatch)");
                _logger.LogWarning("Archive verification FAILED for CorrespondenceId {id} (DocId: {docId}). Metadata does not match expected values. Executing compensating action - marking as Failed.",
                    correspondenceId, docId);

                await ExecuteCompensatingActionAsync(correspondenceId, docId, currentHistorySequenceNum, failureReason, metadataMismatches);
                return;
            }

            // For document not found, use time-based compensating action
            if (duration < WARNING_THRESHOLD)
            {
                // Continue polling (normal retry) - do nothing
                _logger.LogInformation("ACTION: Continue polling (< 24 hours)");
                _logger.LogInformation("Archive verification failure for CorrespondenceId {id} (DocId: {docId}). Document has been in AwaitingImaging for {hours:F2} hours. Continue polling.",
                    correspondenceId, docId, duration.TotalHours);
            }
            else if (duration >= WARNING_THRESHOLD && duration < FAILURE_THRESHOLD)
            {
                // Log warning, continue polling
                _logger.LogWarning("ACTION: Log warning and continue polling (24-48 hours)");
                _logger.LogWarning("Archive verification failure for CorrespondenceId {id} (DocId: {docId}). Document has been in AwaitingImaging for {hours:F2} hours (over 24 hours). Document not found in Documentum. Continue polling.",
                    correspondenceId, docId, duration.TotalHours);
            }
            else if (duration >= FAILURE_THRESHOLD)
            {
                // Execute compensating action - mark as failed
                _logger.LogError("ACTION: Mark as Failed (>= 48 hours)");
                _logger.LogError("Archive verification failure for CorrespondenceId {id} (DocId: {docId}). Document has been in AwaitingImaging for {hours:F2} hours (over 48 hours). Document not found in Documentum. Executing compensating action - marking as Failed.",
                    correspondenceId, docId, duration.TotalHours);

                await ExecuteCompensatingActionAsync(correspondenceId, docId, currentHistorySequenceNum, failureReason, metadataMismatches);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleArchiveVerificationFailure for CorrespondenceId {id} (DocId: {docId}). Compensating action failed.",
                correspondenceId, docId);
            throw;
        }
    }

    /// <summary>
    /// Executes the compensating action by marking the request as Failed.
    /// Updates the request status accordingly.
    /// </summary>
    private async Task ExecuteCompensatingActionAsync(
        Guid correspondenceId,
        string? docId,
        int currentHistorySequenceNum,
        VerificationFailureReason failureReason,
        List<string>? metadataMismatches = null)
    {
        try
        {
            var failedHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = correspondenceId,
                HistorySequenceNum = currentHistorySequenceNum + 1,
                StatusCode = failedStatusCode ?? string.Empty,
                StatusUpdatedBy = FUNCTION_NAME,
                StatusUpdateTS = DateTime.UtcNow
            };

            _dbContext.CorrespondenceRequestStatusHistories.Add(failedHistory);

            var failureDetails = failureReason == VerificationFailureReason.MetadataMismatch && metadataMismatches != null
                ? $"Metadata mismatches: {string.Join("; ", metadataMismatches)}"
                : "Document not found in Documentum after 48 hours";

            _logger.LogInformation("Compensating action executed: Marked CorrespondenceId {id} (DocId: {docId}) as Failed. Reason: {reason}. Details: {details}",
                correspondenceId, docId, failureReason, failureDetails);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database error occurred while executing compensating action for CorrespondenceId {id} (DocId: {docId})",
                correspondenceId, docId);
            throw;
        }
    }

    private async Task<bool> AssignInitialValuesAsync()
    {
        try
        {
            awaitingImagingStatusCode = await _dbContext.CorrespondenceStatusCodes
                .Where(x => x.StatusDescription == AWAITING_IMAGING_STATUS)
                .Select(x => x.StatusCode)
                .FirstOrDefaultAsync() ?? string.Empty;

            if (string.IsNullOrEmpty(awaitingImagingStatusCode))
            {
                _logger.LogError("No StatusCode found for status '{status}'.", AWAITING_IMAGING_STATUS);
                return false;
            }

            archivedStatusCode = await _dbContext.CorrespondenceStatusCodes
               .Where(x => x.StatusDescription == ARCHIVED_STATUS)
               .Select(x => x.StatusCode)
               .FirstOrDefaultAsync() ?? string.Empty;

            if (string.IsNullOrEmpty(archivedStatusCode))
            {
                _logger.LogError("No StatusCode found for status '{status}'.", ARCHIVED_STATUS);
                return false;
            }

            failedStatusCode = await _dbContext.CorrespondenceStatusCodes
               .Where(x => x.StatusDescription == FAILED_STATUS)
               .Select(x => x.StatusCode)
               .FirstOrDefaultAsync() ?? string.Empty;

            if (string.IsNullOrEmpty(failedStatusCode))
            {
                _logger.LogError("No StatusCode found for status '{status}'.", FAILED_STATUS);
                return false;
            }

            //documentum connection values
            docRepository = Environment.GetEnvironmentVariable("DocumentumRepo") ?? string.Empty;
            docSuperUser = Environment.GetEnvironmentVariable("DocumentumSuperUser") ?? string.Empty;

            var secretName = Environment.GetEnvironmentVariable("SecretNames__SA-ECCO-ECMTOKEN");
            if (string.IsNullOrEmpty(secretName))
            {
                _logger.LogError("Secret name for Documentum password not configured");
                return false;
            }

            docSuperUserPwd = await _keyVaultService.GetSecretAsync(secretName) ?? string.Empty;

            if (string.IsNullOrEmpty(docRepository) || string.IsNullOrEmpty(docSuperUser) || string.IsNullOrEmpty(docSuperUserPwd))
            {
                _logger.LogError("Documentum connection configuration is incomplete");
                return false;
            }

            // ===== ACQUIRE INITIAL TOKEN ===== 
            if (!await RefreshTokenAsync())
            {
                _logger.LogError("Failed to obtain initial Documentum token");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning initial values from database and environment variables");
            return false;
        }
    }

    private async Task<List<AwaitingImagingDocument>> GetAwaitingImagingDocumentsAsync()
    {
        return await (
            from history in _dbContext.CorrespondenceRequestStatusHistories
            join request in _dbContext.CorrespondenceRequests
                on history.CorrespondenceId equals request.CorrespondenceId
            where history.HistorySequenceNum ==
                _dbContext.CorrespondenceRequestStatusHistories
                    .Where(h => h.CorrespondenceId == history.CorrespondenceId)
                    .Max(h => h.HistorySequenceNum)
            && history.StatusCode == awaitingImagingStatusCode
            && !_dbContext.CorrespondenceRequestStatusHistories.Any(h2 =>
                h2.CorrespondenceId == history.CorrespondenceId &&
                h2.StatusCode == archivedStatusCode)
            select new AwaitingImagingDocument
            {
                CorrespondenceId = history.CorrespondenceId,
                StatusCode = history.StatusCode ?? string.Empty,
                DocId = request.DocId ?? string.Empty,
                HistorySequenceNum = history.HistorySequenceNum,
                StatusUpdateTS = history.StatusUpdateTS,
                ArchiveDocumentTypeCode = request.ArchiveDocumentTypeCode,
                ArchiveDocumentSubTypeCode = request.ArchiveDocumentSubTypeCode
            }
        ).ToListAsync();
    }

    #region Helper Classes

    /// <summary>
    /// Represents a document awaiting imaging status with its metadata.
    /// </summary>
    private class AwaitingImagingDocument
    {
        public Guid CorrespondenceId { get; set; }
        public string StatusCode { get; set; } = string.Empty;
        public string DocId { get; set; } = string.Empty;
        public int HistorySequenceNum { get; set; }
        public DateTime StatusUpdateTS { get; set; }
        public string? ArchiveDocumentTypeCode { get; set; }
        public string? ArchiveDocumentSubTypeCode { get; set; }
    }

    /// <summary>
    /// Represents metadata retrieved from Documentum.
    /// NOTE: Will be updated based on finalized requirements from Venture and Documentum team.
    /// </summary>
    private class DocumentumMetadata
    {
        public string ObjectId { get; set; } = string.Empty;
        public string CmgDocId { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentSubType { get; set; } = string.Empty;
        // TODO: Add additional metadata fields as requirements are finalized:
        // public string CustomerId { get; set; } = string.Empty;
        // public string PolicyNumber { get; set; } = string.Empty;
        // public DateTime? DocumentDate { get; set; }
        // public string VentureSpecificField1 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of document verification in Documentum.
    /// </summary>
    private class DocumentVerificationResult
    {
        public VerificationStatus Status { get; set; }
        public List<string> MetadataMismatches { get; set; } = new List<string>();
        public DocumentumMetadata? DocumentumMetadata { get; set; }
    }

    /// <summary>
    /// Result of metadata validation.
    /// </summary>
    private class MetadataValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Mismatches { get; set; } = new List<string>();
    }

    /// <summary>
    /// Verification status enum.
    /// </summary>
    private enum VerificationStatus
    {
        Success,
        NotFound,
        MetadataMismatch
    }

    /// <summary>
    /// Verification failure reason enum.
    /// </summary>
    private enum VerificationFailureReason
    {
        DocumentNotFound,
        MetadataMismatch
    }

    #endregion
}