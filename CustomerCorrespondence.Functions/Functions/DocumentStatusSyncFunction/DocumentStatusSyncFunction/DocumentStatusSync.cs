using DocumentStatusSyncFunction.Services;
using ECMServicesDemo;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Domain.Interfaces;

namespace DocumentStatusSyncFunction;

public class DocumentStatusSync
{
    private readonly ILogger _logger;
    private readonly CorrespondenceDBContext _dbContext;
    private readonly IECMServiceClient _ecmClient;
    private readonly IKeyVaultService _keyVaultService;
    private string awaitingImagingStatusCode;
    private string archivedStatusCode;
    private string failedStatusCode;
    private string docRepository;
    private string docSuperUser;
    private string docSuperUserPwd;
    private string tokenResponse;
    private DocumentumSecurityToken token;

    public DocumentStatusSync(
        ILoggerFactory loggerFactory, 
        CorrespondenceDBContext dbContext, 
        IECMServiceClient ecmClient, 
        IKeyVaultService keyVaultService)
    {
        _logger = loggerFactory.CreateLogger<DocumentStatusSync>();
        _dbContext = dbContext;
        _ecmClient = ecmClient;
        _keyVaultService = keyVaultService;
    }

    [Function("DocumentStatusSync")]
    public async Task Run([TimerTrigger("0 0 */4 * * *")] TimerInfo myTimer)
    {
        try
        {
            _logger.LogInformation("C# Timer trigger function executed at: {executionTime}", DateTime.Now);

            //assign values to global variables from database and environment variables
            AssignInitialValues();

            //get all documents with status 'AwaitingImaging' and no subsequent 'Archived' status
            var awaitImagingDocuments =
                (
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
                select new
                {
                    history.CorrespondenceId,
                    history.StatusCode,
                    request.DocId,
                    history.HistorySequenceNum,
                    history.StatusUpdateTS
                }
                ).ToList();

            _logger.LogInformation("Found {count} records with Statuscode 'AwaitingImaging'.", awaitImagingDocuments.Count);

            foreach (var record in awaitImagingDocuments)
            {
                try
                {
                    _logger.LogInformation("Processing: CorrespondenceId={id}, DocId={docId}", record.CorrespondenceId, record.DocId);
                    _logger.LogInformation("StatusUpdateTS: {timestamp}", record.StatusUpdateTS);
                    var duration = DateTime.UtcNow - record.StatusUpdateTS;
                    _logger.LogInformation("Duration in AwaitingImaging: {hours:F2} hours ({days:F2} days)", duration.TotalHours, duration.TotalDays);
                    
                    // Call SOAP service to verify if document exists in Documentum
                    string latestStatus = await GetDocumentStatusFromSoapAsync(record.DocId);

                    // Document NOT found in Documentum - Execute compensating action logic
                    if (string.IsNullOrEmpty(latestStatus))
                    {
                        _logger.LogInformation("Document {docId} for CorrespondenceId {id} not found in Documentum. Executing compensating action logic.", 
                            record.DocId, record.CorrespondenceId);
                        
                        try
                        {
                            await HandleArchiveVerificationFailure(record.CorrespondenceId, record.DocId, record.HistorySequenceNum, record.StatusUpdateTS);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error executing compensating action for CorrespondenceId {id}, DocId {docId}.", 
                                record.CorrespondenceId, record.DocId);
                            throw; // Re-throw to be caught by outer try-catch in foreach
                        }
                    }
                    // Document FOUND in Documentum and status has changed - Update to Archived
                    else if (latestStatus != record.StatusCode)
                    {
                        var newHistory = new CorrespondenceRequestStatusHistory
                        {
                            CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                            CorrespondenceId = record.CorrespondenceId,
                            HistorySequenceNum = record.HistorySequenceNum + 1,
                            StatusCode = latestStatus,
                            StatusUpdatedBy = "DocumentStatusSyncFunction",
                            StatusUpdateTS = DateTime.UtcNow
                        };

                        _dbContext.CorrespondenceRequestStatusHistories.Add(newHistory);
                        _logger.LogInformation("Document {docId} found in Documentum. Added new history record for CorrespondenceId {id} with status {status}", 
                            record.DocId, record.CorrespondenceId, latestStatus);
                    }
                    else
                    {
                        _logger.LogInformation("Document {docId} found in Documentum but status unchanged for CorrespondenceId {id}.", 
                            record.DocId, record.CorrespondenceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing CorrespondenceId {id}, DocId {docId}. Continuing with next record.", 
                        record.CorrespondenceId, record.DocId);
                }
            }

            //Save all changes at once
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Changes saved to database.");

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation("Next timer schedule at: {nextSchedule}", myTimer.ScheduleStatus.Next);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in DocumentStatusSync function execution.");
            throw;
        }
    }

    /// <summary>
    /// Compensating action logic for when a document is not found in Documentum.
    /// Evaluates how long the document has been in AwaitingImaging status and takes appropriate action:
    /// - Less than 24 hours: Continue polling (no action)
    /// - 24-48 hours: Log warning and continue polling
    /// - 48+ hours: Mark the request as Failed (compensating action)
    /// </summary>
    private async Task HandleArchiveVerificationFailure(Guid correspondenceId, string docId, int currentHistorySequenceNum, DateTime statusUpdateTS)
    {
        try
        {
            var duration = DateTime.UtcNow - statusUpdateTS;

             _logger.LogInformation("COMPENSATING ACTION LOGIC TRIGGERED");
            _logger.LogInformation("DocId: {docId}", docId);
            _logger.LogInformation("Duration in AwaitingImaging: {hours:F2} hours ({days:F2} days)", duration.TotalHours, duration.TotalDays);
           
            if (duration < TimeSpan.FromHours(24))
            {
                // Continue polling (normal retry) - do nothing
                _logger.LogInformation("ACTION: Continue polling (< 24 hours)");
                _logger.LogInformation("Archive verification failure for CorrespondenceId {id} (DocId: {docId}). Document has been in AwaitingImaging for {hours:F2} hours. Continue polling.", 
                    correspondenceId, docId, duration.TotalHours);
            }
            else if (duration >= TimeSpan.FromHours(24) && duration < TimeSpan.FromHours(48))
            {
                // Log warning, continue polling
                _logger.LogInformation("ACTION: Log warning and continue polling (24-48 hours)");
                _logger.LogInformation("Archive verification failure for CorrespondenceId {id} (DocId: {docId}). Document has been in AwaitingImaging for {hours:F2} hours (over 24 hours). Document not found in Documentum. Continue polling.", 
                    correspondenceId, docId, duration.TotalHours);
            }
            else if (duration >= TimeSpan.FromHours(48))
            {
                // Execute compensating action - mark as failed
                _logger.LogInformation("ACTION: Mark as Failed (>= 48 hours)");
                _logger.LogInformation("Archive verification failure for CorrespondenceId {id} (DocId: {docId}). Document has been in AwaitingImaging for {hours:F2} hours (over 48 hours). Document not found in Documentum. Executing compensating action - marking as Failed.", 
                    correspondenceId, docId, duration.TotalHours);

                try
                {
                    var failedHistory = new CorrespondenceRequestStatusHistory
                    {
                        CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                        CorrespondenceId = correspondenceId,
                        HistorySequenceNum = currentHistorySequenceNum + 1,
                        StatusCode = failedStatusCode,
                        StatusUpdatedBy = "DocumentStatusSyncFunction",
                        StatusUpdateTS = DateTime.UtcNow
                    };

                    _dbContext.CorrespondenceRequestStatusHistories.Add(failedHistory);
                    _logger.LogInformation("Compensating action executed: Marked CorrespondenceId {id} (DocId: {docId}) as Failed after {hours:F2} hours in AwaitingImaging status due to document not found in Documentum.", 
                        correspondenceId, docId, duration.TotalHours);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Database error occurred while adding Failed status history record for CorrespondenceId {id} (DocId: {docId})", 
                        correspondenceId, docId);
                    throw; // Re-throw to be caught by outer catch block
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HandleArchiveVerificationFailure for CorrespondenceId {id} (DocId: {docId}). Compensating action failed.", 
                correspondenceId, docId);
        }
    }

    private void AssignInitialValues()
    {
        try
        {
            awaitingImagingStatusCode = _dbContext.CorrespondenceStatusCodes
                .Where(x => x.StatusDescription == "AwaitingImaging")
                .Select(x => x.StatusCode)
                .FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(awaitingImagingStatusCode))
            {
                _logger.LogWarning("No StatusCode found for status 'AwaitingImaging'.");
                return;
            }

            archivedStatusCode = _dbContext.CorrespondenceStatusCodes
               .Where(x => x.StatusDescription == "Archived")
               .Select(x => x.StatusCode)
               .FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(archivedStatusCode))
            {
                _logger.LogWarning("No StatusCode found for status 'Archived'.");
                return;
            }

            failedStatusCode = _dbContext.CorrespondenceStatusCodes
               .Where(x => x.StatusDescription == "Failed")
               .Select(x => x.StatusCode)
               .FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(failedStatusCode))
            {
                _logger.LogWarning("No StatusCode found for status 'Failed'.");
                return;
            }

            //documentum connection values
            docRepository = Environment.GetEnvironmentVariable("DocumentumRepo") ?? string.Empty;
            docSuperUser = Environment.GetEnvironmentVariable("DocumentumSuperUser") ?? string.Empty;
            docSuperUserPwd = _keyVaultService.GetSecretAsync(Environment.GetEnvironmentVariable("SecretNames__SA-ECCO-ECMTOKEN")).GetAwaiter().GetResult() ?? string.Empty;
            tokenResponse = _ecmClient.getTokenAsync(docRepository, docSuperUser, docSuperUserPwd).GetAwaiter().GetResult() ?? string.Empty;
            token = new DocumentumSecurityToken { token = tokenResponse, repository = docRepository, superuser = docSuperUser };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning initial values from database and environment variables");
            throw;
        }
    }

    private async Task<string> GetDocumentStatusFromSoapAsync(string DocId)
    {
        //below is test docid for testing purpose
       // DocId = "0300000824810064125";

        //cmg_sample_a is a subtype of cmg_doc and contains the cmg_doc_id attribute which is the same as DocId in our database. 
        //keeping here for reference to show how to query for cmg_sample_a using cmg_doc_id, but ultimately we need to query cmg_doc to get the r_object_id which is needed for subsequent queries to get the status.
        //var dqlQuery2 = $"select * from cmg_sample_a where cmg_doc_id = '{testcorrespondenceId}'";
        //var dqlResponse = await _ecmClient.execDQLAsync(token, docSuperUser, docRepository, dqlQuery2);

        var dqlQuery = $"SELECT * FROM cmg_doc WHERE cmg_doc_id = '{DocId}'";
        var dqlResponse = await _ecmClient.execDQLAsync(token, docSuperUser, docRepository, dqlQuery);

        //calling the method to parse the DQL response and check if the document exists and we can retrieve the status. We can enhance this method to also extract the status value from the DQL response if needed.
        var docExists = ParseStatusFromDqlResponse(dqlResponse);

        await Task.Delay(10); // Simulate async call
        if(docExists)
        {
            _logger.LogInformation("Document {id} status retrieved from SOAP service: ", DocId);

            if(archivedStatusCode != null) {
                _logger.LogInformation("Mapping SOAP status to local status code: {status}", archivedStatusCode);
                return archivedStatusCode; // Map SOAP status to your local status code
            }
            return null; // Or return a default status if mapping is not found
        }
        else
        {
            _logger.LogWarning("Failed to retrieve document status for {id} from SOAP service", DocId);
            return null; // Or return the old status if you want to keep it unchanged
        }
    }

    private bool ParseStatusFromDqlResponse(string returnXml)
    {
        if (string.IsNullOrWhiteSpace(returnXml))
        {
            _logger.LogError("DQL response XML is null or empty");
            return false;
        }

        try
        {
            var doc = XDocument.Parse(returnXml);
            var root = doc.Element("root");
            if (root == null)
            {
                _logger.LogError("DQL response XML does not contain 'root' element");
                return false;
            }

            var obj = root.Element("object");
            if (obj == null)
            {
                _logger.LogError("DQL response XML does not contain 'object' element");
                return false;
            }

            // Validate that required fields are present in the 'object' element
            var requiredFields = new[] { "r_object_id", "cmg_doc_id", "object_name" };
            foreach (var item in requiredFields)
            {
                if(obj.Element(item) == null)
                {
                    _logger.LogError("DQL response XML 'object' element does not contain required field '{field}'", item);
                    return false;
                }
            }

            //comparing the cmg_doc_id from the DQL response with the DocId we queried for to ensure we got the correct document. 
            var idAttr = obj.Attribute("ID")?.Value;
            var cmgDocId = obj.Element("cmg_doc_id")?.Value;
            if(!string.IsNullOrEmpty(idAttr) && !string.IsNullOrEmpty(cmgDocId))
            {
                _logger.LogInformation("Parsed DQL response successfully: ID={id}, cmg_doc_id={cmgDocId}", idAttr, cmgDocId);
                return true; 
            }
            else
            {
                _logger.LogError("DQL response XML 'object' element is missing required attributes or elements");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing DQL response");
            return false;
        }
    }
}