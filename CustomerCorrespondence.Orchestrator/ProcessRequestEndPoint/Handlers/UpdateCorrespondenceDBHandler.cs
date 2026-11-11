using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Infrastructure.Messaging;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using NServiceBus.Logging;
using System.Text.Json;
using CustomerCorrespondence.Domain.Interfaces;

namespace ProcessRequestEndPoint.Handlers
{
    public class UpdateCorrespondenceDBHandler : IHandleMessages<UpdateCorrespondenceDBHandlerMessage>
    {
        static ILog log = LogManager.GetLogger(typeof(UpdateCorrespondenceDBHandler));
        private readonly ICorrespondenceRequestRepository _requestRepo;
        private readonly ICorrespondenceRequestStatusHistoryRepository _historyRepo;

        // Track partial processing state for rollback
        private readonly record struct ProcessingState(
            bool CorrespondenceRequestFetched,
            bool StatusDetermined,
            bool SequenceNumberFetched,
            bool StatusHistoryCreated,
            Guid? CorrespondenceId,
            Guid? StatusHistoryId,
            int? SequenceNumber,
            string? StatusCode);

        public UpdateCorrespondenceDBHandler(
            ICorrespondenceRequestRepository requestRepo,
            ICorrespondenceRequestStatusHistoryRepository historyRepo)
        {
            _requestRepo = requestRepo ?? throw new ArgumentNullException(nameof(requestRepo));
            _historyRepo = historyRepo ?? throw new ArgumentNullException(nameof(historyRepo));
        }

        public async Task Handle(UpdateCorrespondenceDBHandlerMessage message, IMessageHandlerContext context)
        {
            Console.WriteLine("**** UPDATE CORRESPONDENCE DB HANDLER STARTED ****");

            var processingState = new ProcessingState(
                CorrespondenceRequestFetched: false,
                StatusDetermined: false,
                SequenceNumberFetched: false,
                StatusHistoryCreated: false,
                CorrespondenceId: null,
                StatusHistoryId: null,
                SequenceNumber: null,
                StatusCode: null);

            try
            {
                log.Info("Updating CorrespondenceRequestStatusHistory table based on VentureFusion response...");
                Console.WriteLine($"Processing status update for DocId: {message.Process.DocId}");

                // Validate DocId is present
                if (string.IsNullOrWhiteSpace(message.Process.DocId))
                {
                    var error = "DocId is missing on the message. Cannot update status history.";
                    log.Error(error);
                    Console.WriteLine($"ERROR: {error}");
                    throw new ValidationException("DocId cannot be null or empty.",
                        new Dictionary<string, List<string>> { ["DocId"] = new List<string> { "DocId is required for status update" } });
                }

                // Step 1: Get correspondence request by DocId
                CorrespondenceRequest? correspondenceRequest = null;
                try
                {
                    var correspondenceRequests = await _requestRepo.GetCorrespondenceRequestByDocIdAsync(message.Process.DocId);
                    correspondenceRequest = correspondenceRequests.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    var error = $"Failed to retrieve correspondence request for DocId: {message.Process.DocId}";
                    log.Error(error, ex);
                    Console.WriteLine($"ERROR: {error}");
                    var notFoundEx = new NotFoundException("Correspondence request not found for the provided DocId.", "CORRESPONDENCE_NOT_FOUND");
                    throw notFoundEx;
                }

                if (correspondenceRequest == null)
                {
                    var error = $"No correspondence request found for DocId: {message.Process.DocId}";
                    log.Error(error);
                    Console.WriteLine($"ERROR: {error}");
                    var notFoundEx = new NotFoundException("Correspondence request not found for the provided DocId.", "CORRESPONDENCE_NOT_FOUND");
                    
                    throw notFoundEx;
                }

                var correspondenceId = correspondenceRequest.CorrespondenceId;
                processingState = processingState with
                {
                    CorrespondenceRequestFetched = true,
                    CorrespondenceId = correspondenceId
                };
                Console.WriteLine($"Found CorrespondenceId: {correspondenceId} for DocId: {message.Process.DocId}");

                // Save VFResponseId from VentureFusion response (only if successful)
                await SaveVFResponseIdAsync(correspondenceRequest, message.Process.VentureFusionResponse);

                // Step 2: Determine status based on VentureFusion response
                var (statusCode, statusDescription) = DetermineStatusFromVentureFusionResponse(message.Process.VentureFusionResponse);
                processingState = processingState with { StatusDetermined = true };

                Console.WriteLine($"VentureFusion Response Status: {statusDescription}");
                log.Info($"Setting status to '{statusCode}' based on VentureFusion response: {statusDescription}");

                // Step 3: Get next sequence number
                int maxSeq;
                try
                {
                    maxSeq = await _historyRepo.GetMaxSequenceNumber(correspondenceId);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to get max sequence number for CorrespondenceId: {correspondenceId}", ex);
                    throw new DatabaseException("Failed to get max sequence number", "DB_QUERY_ERROR", "Query", "CorrespondenceRequestStatusHistory", ex);
                }

                processingState = processingState with
                {
                    SequenceNumberFetched = true,
                    SequenceNumber = maxSeq + 1
                };

                // Step 4: Conditional status code assignment
                string finalStatusCode;
                try
                {
                    finalStatusCode = message.Process.VentureFusionResponse?.IsSuccessful == true
                        ? await _historyRepo.GetStatusCodeByStatus("AwaitingImaging")
                        : statusCode;
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to get status code for 'AwaitingImaging'", ex);
                    throw new DatabaseException("Failed to get status code", "DB_QUERY_ERROR", "Query", "CorrespondenceStatusCode", ex);
                }

                processingState = processingState with { StatusCode = finalStatusCode };

                Console.WriteLine($"Final status code: {finalStatusCode} (VF Success: {message.Process.VentureFusionResponse?.IsSuccessful})");

                // Step 5: Create status history record
                var statusHistoryId = Guid.NewGuid();
                var statusHistory = new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = statusHistoryId,
                    CorrespondenceId = correspondenceId,
                    HistorySequenceNum = maxSeq + 1,
                    StatusCode = finalStatusCode,
                    StatusUpdatedBy = "System",
                    StatusUpdateTS = DateTime.UtcNow
                };

                await _historyRepo.AddCorrespondenceRequestStatusHistoryAsync(statusHistory);
                
                processingState = processingState with
                {
                    StatusHistoryCreated = true,
                    StatusHistoryId = statusHistoryId
                };

                log.Info($"Successfully added status history record. Status: {finalStatusCode}, Sequence: {statusHistory.HistorySequenceNum}");
                Console.WriteLine($"Status history updated: {finalStatusCode} (Sequence: {statusHistory.HistorySequenceNum})");

                Console.WriteLine("**** UPDATE CORRESPONDENCE DB HANDLER - Business logic completed ****");

                // Step 6: Forward to next step in routing slip
                await RoutingSlipForwarder.ForwardToNextStepIfAny(context, message);
                Console.WriteLine("**** UPDATE CORRESPONDENCE DB HANDLER - Routing slip forwarding completed ****");
            }
            catch (ValidationException)
            {
                throw;
            }
            catch (NotFoundException)
            {
                throw;
            }
            catch (DatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error("An error occurred while processing the message in UpdateCorrespondenceDBHandler.", ex);
                Console.WriteLine($"Error in UpdateCorrespondenceDBHandler: {ex.Message}");
                
                // Attempt rollback of partial processing
                await RollbackPartialProcessingAsync(message, processingState, ex, context);

                // Re-throw as DatabaseException if it's a generic exception
                throw new DatabaseException("Failed to update correspondence status history", "DB_INSERT_ERROR", "Insert", "CorrespondenceRequestStatusHistory", ex);
            }
        }

        /// <summary>
        /// Attempts to rollback any partial processing that occurred before the failure.
        /// </summary>
        private async Task RollbackPartialProcessingAsync(
            UpdateCorrespondenceDBHandlerMessage message,
            ProcessingState state,
            Exception originalException,
            IMessageHandlerContext context)
        {
            log.Warn($"Initiating rollback for DocId: {message.Process.DocId}. " +
                     $"CorrespondenceRequestFetched: {state.CorrespondenceRequestFetched}, " +
                     $"StatusHistoryCreated: {state.StatusHistoryCreated}");
            Console.WriteLine($"**** ROLLBACK INITIATED for UpdateCorrespondenceDBHandler ****");

            var rollbackErrors = new List<string>();

            try
            {
                // Step 1: Delete the status history record if it was created
                if (state.StatusHistoryCreated && state.CorrespondenceId.HasValue)
                {
                    await DeleteStatusHistoryRecordAsync(state.CorrespondenceId.Value, state.SequenceNumber);
                    log.Info($"Rolled back: Deleted status history record for CorrespondenceId: {state.CorrespondenceId}");
                    Console.WriteLine($"Rollback: Status history record deleted for CorrespondenceId: {state.CorrespondenceId}");
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to delete status history record: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            try
            {
                // Step 2: Publish compensation event for audit and downstream notification
                await PublishCompensationEventAsync(message.Process, state, originalException, context);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to publish compensation event: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            try
            {
                // Step 3: Log failure for audit trail
                await LogFailureForAuditAsync(message.Process, state, originalException);
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
        /// Deletes the status history record that was created during partial processing.
        /// </summary>
        private async Task DeleteStatusHistoryRecordAsync(Guid correspondenceId, int? sequenceNumber)
        {
            if (!sequenceNumber.HasValue)
            {
                log.Warn("Cannot delete status history record - sequence number is null");
                return;
            }

            try
            {
                // Get the specific record to verify it exists before deletion
                var record = await _historyRepo.GetStatusHistoryByCorrIdSeqNumAsync(correspondenceId, sequenceNumber.Value);

                if (record != null)
                {
                    await _historyRepo.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId);
                    log.Info($"Deleted status history record: CorrespondenceId={correspondenceId}, Sequence={sequenceNumber}");
                }
                else
                {
                    log.Warn($"Status history record not found for deletion: CorrespondenceId={correspondenceId}, Sequence={sequenceNumber}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Failed to delete status history record: {ex.Message}", ex);
                throw new DatabaseException($"Failed to delete status history record", "DB_DELETE_ERROR", "Delete", "CorrespondenceRequestStatusHistory", ex);
            }
        }

        /// <summary>
        /// Publishes a compensation event to notify downstream systems of the failure.
        /// </summary>
        private async Task PublishCompensationEventAsync(
            SequentialProcess process,
            ProcessingState state,
            Exception originalException,
            IMessageHandlerContext context)
        {
            var compensationEvent = new UpdateCorrespondenceDBCompensationEvent
            {
                DocId = process.DocId,
                CorrespondenceId = state.CorrespondenceId,
                StatusHistoryId = state.StatusHistoryId,
                SequenceNumber = state.SequenceNumber,
                AttemptedStatusCode = state.StatusCode,
                CompensationReason = originalException.Message,
                CompensationTimestamp = DateTime.UtcNow,
                HandlerName = nameof(UpdateCorrespondenceDBHandler),
                WasStatusHistoryCreated = state.StatusHistoryCreated,
                VentureFusionWasSuccessful = process.VentureFusionResponse?.IsSuccessful,
                PolicyNumber = process.ECCOTrackingPolicyNumber,
                ClaimNumber = process.ECCOTrackingClaimNumber
            };

            await context.Publish(compensationEvent);
            log.Info($"Published compensation event for DocId: {process.DocId}");
            Console.WriteLine($"Compensation event published for DocId: {process.DocId}");
        }

        /// <summary>
        /// Logs detailed failure information for audit purposes.
        /// </summary>
        private Task LogFailureForAuditAsync(SequentialProcess process, ProcessingState state, Exception originalException)
        {
            var auditInfo = new
            {
                DocId = process.DocId,
                PolicyNumber = process.ECCOTrackingPolicyNumber,
                ClaimNumber = process.ECCOTrackingClaimNumber,
                HandlerName = nameof(UpdateCorrespondenceDBHandler),
                FailureTimestamp = DateTime.UtcNow,
                ProcessingState = new
                {
                    state.CorrespondenceRequestFetched,
                    state.StatusDetermined,
                    state.SequenceNumberFetched,
                    state.StatusHistoryCreated,
                    state.CorrespondenceId,
                    state.StatusHistoryId,
                    state.SequenceNumber,
                    state.StatusCode
                },
                VentureFusionResponse = process.VentureFusionResponse != null ? new
                {
                    process.VentureFusionResponse.IsSuccessful,
                    process.VentureFusionResponse.StatusCode,
                    process.VentureFusionResponse.ErrorMessage
                } : null,
                ErrorMessage = originalException.Message,
                StackTrace = originalException.StackTrace
            };

            var auditJson = JsonSerializer.Serialize(auditInfo, new JsonSerializerOptions { WriteIndented = true });
            log.Error($"UpdateCorrespondenceDBHandler failure audit:\n{auditJson}");
            Console.WriteLine($"Failure audit logged:\n{auditJson}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Determines the status code and description based on VentureFusion API response
        /// </summary>
        private (string StatusCode, string StatusDescription) DetermineStatusFromVentureFusionResponse(VentureFusionApiResponse? vfResponse)
        {
            if (vfResponse == null)
            {
                log.Warn("VentureFusion response is null - setting status to VFRequestFailed");
                return ("VFRequestFailed", "VentureFusion response was null");
            }

            if (vfResponse.IsSuccessful)
            {
                var statusDescription = $"VentureFusion API succeeded with status {vfResponse.StatusCode} at {vfResponse.ResponseTimestamp:yyyy-MM-dd HH:mm:ss}";

                var statusCode = vfResponse.StatusCode switch
                {
                    System.Net.HttpStatusCode.Created => "VFComposed", // 201 - Record created successfully
                    System.Net.HttpStatusCode.OK => "VFProcessed",     // 200 - Request processed successfully  
                    System.Net.HttpStatusCode.Accepted => "VFQueued", // 202 - Request accepted for processing
                    _ => "VFSuccess" // Other successful status codes
                };

                log.Info($"VentureFusion success: {statusDescription}");
                return (statusCode, statusDescription);
            }
            else
            {
                var errorDetails = !string.IsNullOrEmpty(vfResponse.ErrorMessage)
                    ? vfResponse.ErrorMessage
                    : $"HTTP {vfResponse.StatusCode}";

                var statusDescription = $"VentureFusion API failed: {errorDetails} at {vfResponse.ResponseTimestamp:yyyy-MM-dd HH:mm:ss}";

                log.Error($"VentureFusion failure: {statusDescription}");
                return ("VFRequestFailed", statusDescription);
            }
        }

        /// <summary>
        /// Event published when a status history update needs compensation due to handler failure.
        /// </summary>
        public class UpdateCorrespondenceDBCompensationEvent : IEvent
        {
            public string? DocId { get; set; }
            public Guid? CorrespondenceId { get; set; }
            public Guid? StatusHistoryId { get; set; }
            public int? SequenceNumber { get; set; }
            public string? AttemptedStatusCode { get; set; }
            public string? CompensationReason { get; set; }
            public DateTime CompensationTimestamp { get; set; }
            public string? HandlerName { get; set; }
            public bool WasStatusHistoryCreated { get; set; }
            public bool? VentureFusionWasSuccessful { get; set; }
            public string? PolicyNumber { get; set; }
            public string? ClaimNumber { get; set; }
        }

        private async Task SaveVFResponseIdAsync(CorrespondenceRequest correspondenceRequest, VentureFusionApiResponse? vfResponse)
        {
            // Only try to parse and save VFResponseId if the response was successful
            if (vfResponse == null || !vfResponse.IsSuccessful || string.IsNullOrEmpty(vfResponse.ResponseContent))
            {
                log.Debug($"Skipping VFResponseId parsing - Response: {(vfResponse == null ? "null" : "failed or empty")}");
                return;
            }

            try
            {
                string? valueToSave = null;

                using var jsonDocument = JsonDocument.Parse(vfResponse.ResponseContent);
                var rootElement = jsonDocument.RootElement;

                if (rootElement.ValueKind == JsonValueKind.Array && rootElement.GetArrayLength() > 0)
                    rootElement = rootElement[0];

                if (rootElement.TryGetProperty("id", out var idElement) && idElement.ValueKind != JsonValueKind.Null)
                    valueToSave = idElement.GetString();

                if (!string.IsNullOrEmpty(valueToSave))
                {
                    correspondenceRequest.VFResponseId = valueToSave;
                    await _requestRepo.UpdateCorrespondenceRequestAsync(correspondenceRequest);
                    log.Info($"Saved VFResponseId '{valueToSave}' for DocId: {correspondenceRequest.DocId}");
                }
            }
            catch (JsonException ex)
            {
                // Log but don't throw - JSON parsing failure for successful responses shouldn't stop the handler
                log.Warn($"Failed to parse VFResponse JSON for DocId {correspondenceRequest.DocId}: {ex.Message}. This is expected for non-JSON responses.", ex);
                // Don't throw - continue processing
            }
            catch (Exception ex)
            {
                log.Error($"Failed to save VFResponseId for DocId {correspondenceRequest.DocId}: {ex.Message}", ex);
                throw new DatabaseException($"Failed to save VFResponseId", "DB_UPDATE_ERROR", "Update", "CorrespondenceRequest", ex);
            }
        }
    }
}
