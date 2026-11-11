using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Infrastructure.Messaging;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NServiceBus.Logging;
using System.Text.Json;

namespace ProcessRequestEndPoint.Handlers
{
    public class RequestCompositionHandler : IHandleMessages<RequestCompositionHandlerMessage>
    {
        static ILog log = LogManager.GetLogger(typeof(RequestCompositionHandler));
        private readonly IVentureFusionService _ventureFusionService;
        private readonly IEncryptionService _encryptionService;
        private readonly CorrespondenceDBContext _dbContext;
        private readonly bool _isDevelopment;

        // Track partial processing state for rollback
        private readonly record struct ProcessingState(
            bool AdditionalDataMerged,
            bool CompositionRequestSent,
            bool ResponseProcessed,
            Dictionary<string, object>? OriginalAdditionalData,
            VentureFusionApiResponse? OriginalVentureFusionResponse,
            string? VentureFusionTransactionId,
            string? FailedVfStatusCode);

        public RequestCompositionHandler(
           IVentureFusionService ventureFusionService,
           IEncryptionService encryptionService,
           CorrespondenceDBContext dbContext,
           IHostEnvironment environment)
        {
            _ventureFusionService = ventureFusionService ?? throw new ArgumentNullException(nameof(ventureFusionService));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _isDevelopment = (environment ?? throw new ArgumentNullException(nameof(environment))).IsDevelopment();
        }

        public async Task Handle(RequestCompositionHandlerMessage message, IMessageHandlerContext context)
        {
            Console.WriteLine("**** REQUEST COMPOSITION HANDLER STARTED ****");

            // Preserve original state for rollback
            var originalAdditionalData = message.Process.AdditionalData != null
                ? new Dictionary<string, object>(message.Process.AdditionalData)
                : null;
            var originalVentureFusionResponse = message.Process.VentureFusionResponse;

            var processingState = new ProcessingState(
                AdditionalDataMerged: false,
                CompositionRequestSent: false,
                ResponseProcessed: false,
                OriginalAdditionalData: originalAdditionalData,
                OriginalVentureFusionResponse: originalVentureFusionResponse,
                VentureFusionTransactionId: null,
                FailedVfStatusCode: null);

            try
            {
                // Log routing slip debug information
                LogRoutingSlipHeaders(context);

                log.Info("Sending Composition Request to Venture Fusion API...");
                Console.WriteLine($"Processing composition request for DocId: {message.Process.DocId}");

                // Validate DocId is present (should be set by AssignDocumentumIdHandler)
                if (string.IsNullOrEmpty(message.Process.DocId))
                {
                    var error = "DocId is missing on the message. Cannot send composition request.";
                    log.Error(error);
                    Console.WriteLine($"ERROR: {error}");
                    throw new ValidationException("DocId is required but was not provided on the message.", 
                        new Dictionary<string, List<string>> { ["DocId"] = new List<string> { "DocId cannot be null or empty" } });
                }

                // Step 1: Merge MailDeliveryInfo into AdditionalData
                MergeMailDeliveryInfoIntoAdditionalData(message.Process);
                processingState = processingState with { AdditionalDataMerged = true };

                // Step 2: Send composition request to VentureFusion
                var (response, transactionId) = await SendCompositionRequestAsync(message.Process, context.CancellationToken);
                processingState = processingState with
                {
                    CompositionRequestSent = true,
                    VentureFusionTransactionId = transactionId,
                    FailedVfStatusCode = response.IsSuccessStatusCode ? null : $"{(int)response.StatusCode} {response.StatusCode}"
                };

                VentureFusionApiResponse? responseData = null;

                try
                {
                    // Step 3: Process the response
                    responseData = await ProcessVentureFusionResponse(response, message.Process);
                    processingState = processingState with { ResponseProcessed = true };

                    // Step 4: Attach response data to the process object
                    message.Process.VentureFusionResponse = responseData;
                    Console.WriteLine($"VentureFusion response attached: IsSuccessful={responseData.IsSuccessful}, StatusCode={responseData.StatusCode}");

                    log.Info("RequestCompositionHandler completed successfully.");
                    Console.WriteLine("**** REQUEST COMPOSITION HANDLER - Business logic completed ****");
                }
                catch (CommunicationException)
                {
                    // Attach the response data before re-throwing
                    if (responseData != null)
                    {
                        message.Process.VentureFusionResponse = responseData;
                    }

                    // Re-throw to trigger rollback
                    throw;
                }

                // Use RoutingSlipForwarder to handle next step or completion
                await RoutingSlipForwarder.ForwardToNextStepIfAny(context, message);
                Console.WriteLine("REQUEST COMPOSITION HANDLER - Routing slip forwarding completed");
                Console.WriteLine("========================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine("========================================");
                Console.WriteLine($"ERROR in RequestCompositionHandler: {ex.Message}");
                Console.WriteLine("========================================");
                // Attempt rollback of partial processing
                await RollbackPartialProcessingAsync(message, processingState, ex, context);
                throw;
            }
        }

        /// <summary>
        /// Attempts to rollback any partial processing that occurred before the failure.
        /// </summary>
        private async Task RollbackPartialProcessingAsync(RequestCompositionHandlerMessage message,ProcessingState state,Exception originalException,IMessageHandlerContext context)
        {
            log.Warn($"Initiating rollback for DocId: {message.Process.DocId}. " +
                     $"AdditionalDataMerged: {state.AdditionalDataMerged}, " +
                     $"CompositionRequestSent: {state.CompositionRequestSent}, " +
                     $"ResponseProcessed: {state.ResponseProcessed}");
            Console.WriteLine($"**** ROLLBACK INITIATED for RequestCompositionHandler ****");

            var rollbackErrors = new List<string>();

            try
            {
                // Step 1: Restore original AdditionalData if it was modified
                if (state.AdditionalDataMerged)
                {
                    message.Process.AdditionalData = state.OriginalAdditionalData;
                    log.Info("Restored original AdditionalData state.");
                    Console.WriteLine("Rollback: AdditionalData restored to original state");
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to restore AdditionalData: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            try
            {
                // Step 2: Restore original VentureFusionResponse
                message.Process.VentureFusionResponse = state.OriginalVentureFusionResponse;
                log.Info("Restored original VentureFusionResponse state.");
                Console.WriteLine("Rollback: VentureFusionResponse restored to original state");
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to restore VentureFusionResponse: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            // Step 3: Mark request as Archive Incomplete and save error code
            var isFirstFailureRecord = false;
            try
            {
                if (state.CompositionRequestSent)
                {
                    isFirstFailureRecord = await MarkRequestAsArchiveIncompleteAsync(message.Process.DocId, originalException.Message);

                    if (!string.IsNullOrEmpty(state.FailedVfStatusCode) && !string.IsNullOrEmpty(message.Process.DocId))
                    {
                        var correspondenceRequest = await _dbContext.CorrespondenceRequests
                            .FirstOrDefaultAsync(cr => cr.DocId == message.Process.DocId);
                        if (correspondenceRequest != null)
                        {
                            correspondenceRequest.VFResponseId = state.FailedVfStatusCode;
                            await _dbContext.SaveChangesAsync();
                            log.Info($"Saved VFResponseId '{state.FailedVfStatusCode}' for DocId: {message.Process.DocId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to mark request as Archive Incomplete in database: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
                // Continue with other rollback steps even if database update fails
            }

            try
            {
                // Step 4: Publish compensation event whenever the composition request was sent
                if (state.CompositionRequestSent)
                {
                    await PublishCompensationEventAsync(message.Process, state, originalException, context);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to publish compensation event: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            try
            {
                // Step 5: Log failure for audit trail
                await LogFailureForAuditAsync(message.Process, state, originalException, context);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to log failure for audit: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            // Log summary of rollback
            if (rollbackErrors.Count > 0)
            {
                log.Error($"Rollback completed with {rollbackErrors.Count} error(s): {string.Join("; ", rollbackErrors)}");
                Console.WriteLine($"**** ROLLBACK COMPLETED WITH {rollbackErrors.Count} ERROR(S) ****");
            }
            else
            {
                log.Info($"Rollback completed successfully for DocId: {message.Process.DocId}");
                Console.WriteLine($"**** ROLLBACK COMPLETED SUCCESSFULLY ****");
            }
        }

        /// <summary>
        /// Marks the correspondence request as Failed in the database when Venture Fusion archive completion fails.
        /// /// <param name="docId">The document ID to mark as failed</param>
        /// <param name="failureReason">The reason for failure (exception message)</param>
        private async Task<bool> MarkRequestAsArchiveIncompleteAsync(string? docId, string failureReason)
        {
            // Validate that DocId exists before attempting database operations
            if (string.IsNullOrEmpty(docId))
            {
                log.Warn("Cannot mark request as Failed - DocId is null or empty");
                return false;
            }

            try
            {
                log.Info($"Marking DocId {docId} as Failed in database due to Venture Fusion failure");
                Console.WriteLine($"Compensating Action: Marking DocId {docId} as Failed");

                // Use EF Core's ExecuteSqlRaw which goes through the ManagedIdentityDbConnectionInterceptor
                // Step 1: Find the CorrespondenceId by DocId
                var request = await _dbContext.CorrespondenceRequests
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.DocId == docId);

                if (request == null)
                {
                    log.Warn($"Cannot mark as Failed - CorrespondenceRequest with DocId {docId} not found in database");
                    return false;
                }

                // Step 2: Look up the Failed status code
                var failedStatusCode = await _dbContext.CorrespondenceStatusCodes
                    .Where(s => s.StatusDescription == "Failed")
                    .Select(s => s.StatusCode)
                    .FirstOrDefaultAsync() ?? "08";

                // Step 3: Insert "08" status only if it doesn't already exist
                var correspondenceId = request.CorrespondenceId;
                var alreadyExists = await _dbContext.CorrespondenceRequestStatusHistories
                    .AsNoTracking()
                    .AnyAsync(h => h.CorrespondenceId == correspondenceId && h.StatusCode == failedStatusCode);

                log.Info($"[DIAG] CorrespondenceId: {correspondenceId}, StatusCode: {failedStatusCode}, AlreadyExists: {alreadyExists}");

                if (!alreadyExists)
                {
                    var maxSeq = await _dbContext.CorrespondenceRequestStatusHistories
                        .Where(h => h.CorrespondenceId == correspondenceId)
                        .Select(h => (int?)h.HistorySequenceNum)
                        .MaxAsync() ?? 0;

                    var statusHistory = new CorrespondenceRequestStatusHistory
                    {
                        CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                        CorrespondenceId = correspondenceId,
                        HistorySequenceNum = maxSeq + 1,
                        StatusCode = failedStatusCode,
                        StatusUpdatedBy = "VentureFusionCompensation",
                        StatusUpdateTS = DateTime.UtcNow
                    };

                    _dbContext.CorrespondenceRequestStatusHistories.Add(statusHistory);
                    await _dbContext.SaveChangesAsync();

                    log.Info($"Marked DocId {docId} as Failed. StatusCode: {failedStatusCode}");
                    return true;
                }
                else
                {
                    log.Info($"Marked DocId {docId} as Failed. StatusCode: {failedStatusCode} (already exists, skipped)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error marking DocId {docId} as Failed: {ex.Message}", ex);
                throw new DatabaseException($"Failed to mark DocId {docId} as Failed", "DB_UPDATE_ERROR", "Update", "CorrespondenceRequestStatusHistory", ex);
            }
        }


        /// <summary>
        /// Publishes a compensation event to notify downstream systems of the failure.
        /// </summary>
        private async Task PublishCompensationEventAsync(SequentialProcess process,ProcessingState state,Exception originalException,IMessageHandlerContext context)
        {
            var compensationEvent = new CompositionRequestCompensationEvent
            {
                DocId = process.DocId,
                VentureFusionTransactionId = state.VentureFusionTransactionId,
                CompensationReason = originalException.Message,
                CompensationTimestamp = DateTime.UtcNow,
                HandlerName = nameof(RequestCompositionHandler),
                WasRequestSentToVentureFusion = state.CompositionRequestSent,
                WasResponseProcessed = state.ResponseProcessed,
                PolicyNumber = process.ECCOTrackingPolicyNumber,
                ClaimNumber = process.ECCOTrackingClaimNumber,
                DeliveryMethod = process.DeliveryMethod
            };

            await context.Publish(compensationEvent);
            log.Info($"Published composition compensation event for DocId: {process.DocId}");
            Console.WriteLine($"Compensation event published for DocId: {process.DocId}");
        }

        /// <summary>
        /// Logs detailed failure information for audit purposes.
        /// </summary>
        private Task LogFailureForAuditAsync(SequentialProcess process,ProcessingState state,Exception originalException,IMessageHandlerContext context)
        {
            var auditInfo = new
            {
                DocId = process.DocId,
                DeliveryMethod = process.DeliveryMethod,
                PolicyNumber = process.ECCOTrackingPolicyNumber,
                ClaimNumber = process.ECCOTrackingClaimNumber,
                HandlerName = nameof(RequestCompositionHandler),
                FailureTimestamp = DateTime.UtcNow,
                ProcessingState = new
                {
                    state.AdditionalDataMerged,
                    state.CompositionRequestSent,
                    state.ResponseProcessed,
                    state.VentureFusionTransactionId
                },
                ErrorMessage = originalException.Message,
                StackTrace = originalException.StackTrace
            };

            var auditJson = JsonSerializer.Serialize(auditInfo, new JsonSerializerOptions { WriteIndented = true });
            log.Error($"RequestCompositionHandler failure audit:\n{auditJson}");
            Console.WriteLine($"Failure audit logged:\n{auditJson}");

            return Task.CompletedTask;
        }

        // ADDED: New method to merge MailDeliveryInfo into AdditionalData
        private void MergeMailDeliveryInfoIntoAdditionalData(SequentialProcess process)
        {
            try
            {
                Console.WriteLine("Starting MergeMailDeliveryInfoIntoAdditionalData...");
                
                // Initialize AdditionalData if null
                if (process.AdditionalData == null)
                {
                    process.AdditionalData = new Dictionary<string, object>();
                    Console.WriteLine("  - Initialized new AdditionalData dictionary");
                }
                else
                {
                    Console.WriteLine($"  - AdditionalData already exists with {process.AdditionalData.Count} properties");
                }

                // Handle delivery method-specific flags
                if (process.IsMailDelivery)
                {
                    process.AdditionalData["MailFlag"] = "Y";
                    process.AdditionalData["ImageFlag"] = "Y";
                    Console.WriteLine($"  - Mail delivery: MailFlag=Y, ImageFlag=Y");
                }
                else if (process.IsEPresentDelivery)
                {
                    process.AdditionalData["MailFlag"] = "N";
                    process.AdditionalData["ImageFlag"] = "Y";
                    process.AdditionalData["EPresentFlag"] = "Y";
                    Console.WriteLine($"  - ePresent delivery: MailFlag=N, ImageFlag=Y, EPresentFlag=Y");

                    // Add JourneyDeliveryInfo for ePresent
                    if (process.JourneyDeliveryInfo != null)
                    {
                        process.AdditionalData["JourneyDeliveryInfo"] = new Dictionary<string, object>
                        {
                            ["AccountId"] = process.JourneyDeliveryInfo.AccountId,
                            ["MessageBody"] = new Dictionary<string, object>
                            {
                                ["ContactKey"] = process.JourneyDeliveryInfo.MessageBody.ContactKey,
                                ["EventDefinitionKey"] = process.JourneyDeliveryInfo.MessageBody.EventDefinitionKey,
                                ["Data"] = new Dictionary<string, object>
                                {
                                    ["SubscriberKey"] = process.JourneyDeliveryInfo.MessageBody.Data.SubscriberKey,
                                    ["EmailAddress"] = process.JourneyDeliveryInfo.MessageBody.Data.EmailAddress ?? string.Empty,
                                    ["SourceSystem"] = process.JourneyDeliveryInfo.MessageBody.Data.SourceSystem ?? string.Empty,
                                    ["FirstName"] = process.JourneyDeliveryInfo.MessageBody.Data.FirstName ?? string.Empty,
                                    ["AttachmentName"] = process.JourneyDeliveryInfo.MessageBody.Data.AttachmentName ?? string.Empty
                                }
                            }
                        };
                        Console.WriteLine("  - Added JourneyDeliveryInfo to AdditionalData");
                    }
                }
                else
                {
                    throw new ValidationException($"Invalid DeliveryMethod '{process.DeliveryMethod}'. Supported methods: Mail, ePresent.", 
                        new Dictionary<string, List<string>> { ["DeliveryMethod"] = new List<string> { $"'{process.DeliveryMethod}' is not valid. Use 'Mail' or 'ePresent'." } });
                }
                // Add VersionCode from CompositionInfo
                if (process.CompositionInfo != null && !string.IsNullOrEmpty(process.CompositionInfo.VersionCode))
                {
                    process.AdditionalData["VersionCode"] = process.CompositionInfo.VersionCode;
                    Console.WriteLine($"  - Added VersionCode={process.CompositionInfo.VersionCode}");
                }

                // Add MailDeliveryInfo as a nested object if it exists
                if (process.MailDeliveryInfo != null)
                {
                    Console.WriteLine("  - Building MailDeliveryInfo nested object...");
                    
                    var mailDeliveryInfoDict = new Dictionary<string, object>
                    {
                        ["RecipientFullName"] = process.MailDeliveryInfo.RecipientFullName ?? string.Empty,
                        ["RecipientFirstName"] = process.MailDeliveryInfo.RecipientFirstName ?? string.Empty,
                        ["RecipientLastName"] = process.MailDeliveryInfo.RecipientLastName ?? string.Empty,
                        ["NonUSAddressFlag"] = process.MailDeliveryInfo.NonUSAddressFlag?.ToString() ?? string.Empty,
                        ["BadAddressFlag"] = process.MailDeliveryInfo.BadAddressFlag?.ToString() ?? string.Empty
                    };

                    // Add RecipientAddress as nested object
                    if (process.MailDeliveryInfo.RecipientAddress != null)
                    {
                        Console.WriteLine("  - Building RecipientAddress nested object...");
                        
                        mailDeliveryInfoDict["RecipientAddress"] = new Dictionary<string, object>
                        {
                            ["RecipientAddressLine1"] = process.MailDeliveryInfo.RecipientAddress.RecipientAddressLine1 ?? string.Empty,
                            ["RecipientAddressLine2"] = process.MailDeliveryInfo.RecipientAddress.RecipientAddressLine2 ?? string.Empty,
                            ["RecipientAddressLine3"] = process.MailDeliveryInfo.RecipientAddress.RecipientAddressLine3 ?? string.Empty,
                            ["RecipientAddressCity"] = process.MailDeliveryInfo.RecipientAddress.RecipientAddressCity ?? string.Empty,
                            ["RecipientAddressST"] = process.MailDeliveryInfo.RecipientAddress.RecipientAddressST ?? string.Empty,
                            ["RecipientAddressZIP"] = process.MailDeliveryInfo.RecipientAddress.RecipientAddressZIP ?? string.Empty,
                            ["RecipientCountry"] = process.MailDeliveryInfo.RecipientAddress.RecipientCountry ?? string.Empty
                        };
                    }

                    process.AdditionalData["MailDeliveryInfo"] = mailDeliveryInfoDict;
                    Console.WriteLine("  - Added MailDeliveryInfo to AdditionalData");
                }

                Console.WriteLine($"MergeDeliveryInfoIntoAdditionalData completed. Total properties: {process.AdditionalData.Count}");
                log.Info($"DeliveryInfo merged into AdditionalData. Total properties: {process.AdditionalData.Count}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to merge MailDeliveryInfo into AdditionalData: {ex.Message}", ex);
                Console.WriteLine($"ERROR in MergeMailDeliveryInfoIntoAdditionalData: {ex.Message}");
                throw;
            }
        }

        private void LogRoutingSlipHeaders(IMessageHandlerContext context)
        {
            try
            {
                if (context.MessageHeaders != null)
                {
                    context.MessageHeaders.TryGetValue("RoutingSlip-Route", out var routeJson);
                    context.MessageHeaders.TryGetValue("RoutingSlip-Index", out var idx);
                    context.MessageHeaders.TryGetValue("RoutingSlip-OriginatingId", out var originating);

                    log.Info($"[DEBUG] Routing slip headers - Route: '{routeJson ?? "(missing)"}', Index: '{idx ?? "(missing)"}', OriginatingId: '{originating ?? "(missing)"}'");
                    Console.WriteLine($"Composition Handler - Routing slip: Route={routeJson ?? "missing"}, Index={idx ?? "missing"}, OriginatingId={originating ?? "missing"}");
                }
                else
                {
                    log.Info("[DEBUG] context.MessageHeaders is null");
                }
            }
            catch (Exception ex)
            {
                log.Warn("Failed to write debug headers in RequestCompositionHandler", ex);
            }
        }

        private async Task<(HttpResponseMessage Response, string? TransactionId)> SendCompositionRequestAsync(SequentialProcess message, CancellationToken cancellationToken)
        {
            // Log AdditionalData (after merge - already done in Handle)
            if (message.AdditionalData != null)
            {
                Console.WriteLine($"\nAdditionalData (MERGED - Total properties: {message.AdditionalData.Count}):");
                var serialized = JsonSerializer.Serialize(message.AdditionalData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                Console.WriteLine(serialized);
            }
            else
            {
                Console.WriteLine("\nAdditionalData: NULL");
            }

            Console.WriteLine("\n========================================");
            Console.WriteLine("Calling VentureFusion Service...");
            Console.WriteLine("========================================");

            // Local development: the real Venture Fusion API isn't configured/reachable locally.
            // Skip the actual HTTP call and synthesize a successful response so downstream
            // processing (status updates, DB persistence) can proceed for local testing.
            if (_isDevelopment)
            {
                log.Warn($"Skipping real Venture Fusion API call (Development) — DocId: {message.DocId}");
                Console.WriteLine($"**** DEVELOPMENT: Skipping real Venture Fusion API call for DocId: {message.DocId} ****");

                var fakeResponse = new HttpResponseMessage(System.Net.HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"status\":\"local-dev-simulated\"}")
                };
                var fakeTransactionId = $"local-dev-{Guid.NewGuid():N}";
                fakeResponse.Headers.Add("X-Transaction-Id", fakeTransactionId);

                return (fakeResponse, fakeTransactionId);
            }

            // Call Venture Fusion API with merged data
            HttpResponseMessage? response = null;
            try
            {
                response = await _ventureFusionService.PostDataRecords(message);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to send composition request to Venture Fusion API: {ex.Message}", ex);
                Console.WriteLine($"\nERROR: Venture Fusion API call failed");
                Console.WriteLine($"Error: {ex.Message}");
                throw new CommunicationException("Failed to send composition request to Venture Fusion API", "VF_COMMUNICATION_ERROR", "VentureFusion", ex);
            }

            // Check for null response BEFORE processing
            if (response == null)
            {
                var error = "No response received from Venture Fusion API.";
                log.Error(error);
                Console.WriteLine($"ERROR: {error}");
                throw new CommunicationException("Venture Fusion API did not respond", "VF_NO_RESPONSE", "VentureFusion", new InvalidOperationException(error));
            }

            Console.WriteLine($"\nVentureFusion API Response Status: {response.StatusCode} ({(int)response.StatusCode})");
            Console.WriteLine("========================================");

            // Extract transaction ID from response headers if available
            string? transactionId = null;
            if (response.Headers.TryGetValues("X-Transaction-Id", out var transactionIds))
            {
                transactionId = transactionIds.FirstOrDefault();
            }
            else if (response.Headers.TryGetValues("X-Request-Id", out var requestIds))
            {
                transactionId = requestIds.FirstOrDefault();
            }

            log.Info($"Venture Fusion API call completed with status: {response.StatusCode}, TransactionId: {transactionId ?? "(none)"}");

            return (response, transactionId);
        }

        private async Task<VentureFusionApiResponse> ProcessVentureFusionResponse(HttpResponseMessage response, SequentialProcess message)
        {
            var responseData = new VentureFusionApiResponse
            {
                StatusCode = response.StatusCode,
                ResponseTimestamp = DateTime.UtcNow,
                ResponseHeaders = new Dictionary<string, string>()
            };

            try
            {
                // Capture response headers
                Console.WriteLine("\nVentureFusion Response Headers:");
                foreach (var header in response.Headers)
                {
                    var headerValue = string.Join(", ", header.Value);
                    responseData.ResponseHeaders[header.Key] = headerValue;
                    Console.WriteLine($"  {header.Key}: {headerValue}");
                }

                responseData.ResponseContent = await response.Content.ReadAsStringAsync();

                // Now check the status code
                if (response.StatusCode == System.Net.HttpStatusCode.Created)
                {
                    log.Info("Record Created successfully at Venture Fusion (201)");
                    Console.WriteLine("\nSUCCESS: Venture Fusion returned 201 Created");
                    Console.WriteLine("Composition request completed successfully");

                    responseData.IsSuccessful = true;

                    if (!string.IsNullOrEmpty(responseData.ResponseContent))
                    {
                        Console.WriteLine($"\nResponse Content ({responseData.ResponseContent.Length} characters):");
                        Console.WriteLine(responseData.ResponseContent);
                        log.Debug($"Venture Fusion success response: {responseData.ResponseContent}");
                    }
                }
                else if (response.IsSuccessStatusCode)
                {
                    log.Info($"Venture Fusion API completed successfully with status: {response.StatusCode}");
                    Console.WriteLine($"\nSUCCESS: VentureFusion API returned {response.StatusCode}");

                    responseData.IsSuccessful = true;
                    
                    if (!string.IsNullOrEmpty(responseData.ResponseContent))
                    {
                        Console.WriteLine($"\nResponse Content ({responseData.ResponseContent.Length} characters):");
                        Console.WriteLine(responseData.ResponseContent);
                    }
                }
                else
                {
                    var error = $"Venture Fusion API error (Status: {response.StatusCode}): {responseData.ResponseContent}";
                    log.Error(error);
                    Console.WriteLine($"\nERROR: VentureFusion API failed with status {response.StatusCode}");
                    Console.WriteLine($"Error details: {responseData.ResponseContent}");

                    responseData.IsSuccessful = false;
                    responseData.ErrorMessage = error;

                    throw new CommunicationException($"Venture Fusion API failed with status {response.StatusCode}", "VF_REQUEST_FAILED", 
                        "VentureFusion", (int)response.StatusCode, new InvalidOperationException(responseData.ResponseContent));
                }

                return responseData;
            }
            catch (Exception ex) when (ex is not CommunicationException)
            {
                log.Error($"Error processing Venture Fusion response: {ex.Message}", ex);
                Console.WriteLine($"\nERROR: Failed to process API response");
                Console.WriteLine($"Error Message: {ex.Message}");

                responseData.IsSuccessful = false;
                responseData.ErrorMessage = ex.Message;

                throw new CommunicationException($"Failed to process Venture Fusion API response: {ex.Message}", "VF_RESPONSE_PROCESSING_ERROR", "VentureFusion", ex);
            }
        }


        /// <summary>
        /// Event published when a composition request needs compensation due to handler failure.
        /// </summary>
        public class CompositionRequestCompensationEvent : IEvent
        {
            public string? DocId { get; set; }
            public string? VentureFusionTransactionId { get; set; }
            public string? CompensationReason { get; set; }
            public DateTime CompensationTimestamp { get; set; }
            public string? HandlerName { get; set; }
            public bool WasRequestSentToVentureFusion { get; set; }
            public bool WasResponseProcessed { get; set; }
            public string? PolicyNumber { get; set; }
            public string? ClaimNumber { get; set; }
            public string? DeliveryMethod { get; set; }
        }
    }
}
