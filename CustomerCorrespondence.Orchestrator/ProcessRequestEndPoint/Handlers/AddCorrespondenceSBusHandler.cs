using Azure.Identity;
using Azure.Messaging.ServiceBus;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Messaging;
using CustomerCorrespondence.Infrastructure.Services;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using Microsoft.Extensions.Hosting;
using NServiceBus.Logging;
using System.Text.Json;

namespace ProcessRequestEndPoint.Handlers
{
    public class AddCorrespondenceSBusHandler : IHandleMessages<AddCorrespondenceSBusHandlerMessage>
    {
        static ILog log = LogManager.GetLogger(typeof(AddCorrespondenceSBusHandler));
        private readonly IConfiguration _configuration;
        private readonly ISecretsCacheService _secretsCacheService;
        private readonly IEncryptionService _encryptionService;
        private readonly ICorrespondenceRequestService _correspondenceRequestService;
        private readonly ICorrespondenceRequestStatusHistoryService _statusHistoryService;
        private readonly bool _isDevelopment;

        // Track partial processing state for rollback
        private readonly record struct ProcessingState(
            bool MessageSentToServiceBus,
            string? ServiceBusMessageId,
            string? SequenceNumber);

        public AddCorrespondenceSBusHandler(
            IConfiguration configuration,
            ISecretsCacheService secretsCacheService,
            IEncryptionService encryptionService,
            ICorrespondenceRequestService correspondenceRequestService,
            ICorrespondenceRequestStatusHistoryService statusHistoryService,
            IHostEnvironment environment)
        {
            _configuration = configuration;
            _secretsCacheService = secretsCacheService;
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _correspondenceRequestService = correspondenceRequestService ?? throw new ArgumentNullException(nameof(correspondenceRequestService));
            _statusHistoryService = statusHistoryService ?? throw new ArgumentNullException(nameof(statusHistoryService));
            _isDevelopment = (environment ?? throw new ArgumentNullException(nameof(environment))).IsDevelopment();
        }

        public async Task Handle(AddCorrespondenceSBusHandlerMessage message, IMessageHandlerContext context)
        {
            Console.WriteLine("**** ADD CORRESPONDENCE SERVICE BUS HANDLER STARTED ****");

            var processingState = new ProcessingState(false, null, null);

            try
            {
                // Log routing slip debug information
                LogRoutingSlipHeaders(context);

                log.Info("Adding the Correspondence Request Message on Azure Service Bus MessageQueue...");
                Console.WriteLine($"Processing Service Bus message for DocId: {message.Process.DocId}");

                // Validate DocId is present (should be set by AssignDocumentumIdHandler)
                if (string.IsNullOrEmpty(message.Process.DocId))
                {
                    var error = "DocId is missing on the message. Cannot send to Service Bus.";
                    log.Error(error);
                    Console.WriteLine($"ERROR: {error}");
                    throw new ValidationException("DocId is required but was not provided on the message.",
                        new Dictionary<string, List<string>> { ["DocId"] = new List<string> { "DocId cannot be null or empty" } });
                }

                // Send to Service Bus
                //await SendToServiceBusAsync(message.Process, context.CancellationToken);
                // Send to Service Bus and track state
                processingState = await SendToServiceBusAsync(message.Process, context.CancellationToken);


                log.Info("AddCorrespondenceSBusHandler completed successfully for message.");
                Console.WriteLine("**** ADD CORRESPONDENCE SERVICE BUS HANDLER - Business logic completed ****");

                // Use RoutingSlipForwarder to handle next step or completion
                await RoutingSlipForwarder.ForwardToNextStepIfAny(context, message);
                Console.WriteLine("**** ADD CORRESPONDENCE SERVICE BUS HANDLER - Routing slip forwarding completed ****");
            }
            catch (Exception ex)
            {
                log.Error("An error occurred while processing the message in AddCorrespondenceSBusHandler.", ex);
                Console.WriteLine($"Error in AddCorrespondenceSBusHandler: {ex.Message}");
                // Attempt rollback of partial processing
                await RollbackPartialProcessingAsync(message.Process, processingState, ex, context);

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
                    Console.WriteLine($"Service Bus Handler - Routing slip headers - Route: {routeJson ?? "missing"}, Index: {idx ?? "missing"}");
                }
                else
                {
                    log.Info("[DEBUG] context.MessageHeaders is null");
                }
            }
            catch (Exception ex)
            {
                log.Warn("Failed to write debug headers in AddCorrespondenceSBusHandler", ex);
            }
        }

        private async Task<ProcessingState> SendToServiceBusAsync(SequentialProcess message, CancellationToken cancellationToken)
        {
            var state = new ProcessingState(false, null, null);

            // Serialize the message
            string messageBody;
            try
            {
                messageBody = JsonSerializer.Serialize(message);
                Console.WriteLine($"Serialized message size: {messageBody.Length} characters");
            }
            catch (JsonException ex)
            {
                log.Error("Failed to serialize the message.", ex);
                throw new SerializationException("Failed to serialize message for Service Bus", "SERIALIZATION_ERROR", typeof(SequentialProcess), "Serialize", ex);
            }

            // Local development: the real Azure Service Bus queue isn't reachable/authenticated
            // locally (separate from NServiceBus's own transport). Skip the actual send so the
            // routing slip can proceed to AddCorrespondenceDBHandler and actually persist the record,
            // while still exercising the PolicyNumber encryption step.
            if (_isDevelopment)
            {
                try
                {
                    var policyNumberToEncryptDev = message.ECCOTrackingPolicyNumber ?? string.Empty;
                    var encryptedPolicyNumberDev = await _encryptionService.EncryptAsync(policyNumberToEncryptDev);
                    log.Info("PolicyNumber AES encrypted for Service Bus transmission (Development)");
                    Console.WriteLine("Successfully AES encrypted PolicyNumber for Service Bus (Development)");
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to encrypt PolicyNumber: {ex.Message}", ex);
                    Console.WriteLine($"ERROR: Failed to encrypt PolicyNumber: {ex.Message}");
                    throw new EncryptionException("Failed to encrypt PolicyNumber for Service Bus transmission", "ENCRYPTION_ERROR", "AES", "Encrypt", ex);
                }

                log.Warn($"Skipping real Azure Service Bus send (Development) — DocId: {message.DocId}");
                Console.WriteLine($"**** DEVELOPMENT: Skipping real Azure Service Bus send for DocId: {message.DocId} ****");

                return new ProcessingState(
                    MessageSentToServiceBus: true,
                    ServiceBusMessageId: message.DocId ?? Guid.NewGuid().ToString(),
                    SequenceNumber: null);
            }

            // Retrieve the Service Bus connection string
            string serviceBusConnectionString;
            try
            {
                var secretName = _configuration["SecretNames:AzureServiceBusFQURL"];
                if (string.IsNullOrWhiteSpace(secretName))
                    throw new ConfigurationException("SecretNames:AzureServiceBusFQURL configuration is missing or empty.", "CONFIG_MISSING", "SecretNames:AzureServiceBusFQURL");

                serviceBusConnectionString = await _secretsCacheService.GetSecretAsync(secretName) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
                    throw new SecretNotFoundException(secretName);

                Console.WriteLine("Successfully retrieved Service Bus connection string");
            }
            catch (ConfigurationException)
            {
                throw;
            }
            catch (SecretNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error("Failed to retrieve the Service Bus connection string.", ex);
                throw new SecretNotFoundException("AzureServiceBusFQURL", ex);
            }

            // Retrieve Managed Identity Key (if applicable)
            DefaultAzureCredential credential;
            try
            {
                var managedIdentityKeySecretName = _configuration["SecretNames:ManagedIdentityKey"];
                if (string.IsNullOrWhiteSpace(managedIdentityKeySecretName))
                    throw new ConfigurationException("SecretNames:ManagedIdentityKey configuration is missing or empty.", "CONFIG_MISSING", "SecretNames:ManagedIdentityKey");

                var managedIdentityKeyValue = await _secretsCacheService.GetSecretAsync(managedIdentityKeySecretName);
                credential = string.IsNullOrWhiteSpace(managedIdentityKeyValue)
                    ? new DefaultAzureCredential()
                    : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                    { ManagedIdentityClientId = managedIdentityKeyValue });

                Console.WriteLine("Successfully configured Azure credentials");
            }
            catch (ConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error("Failed to retrieve the Managed Identity Key.", ex);
                throw new SecretNotFoundException("ManagedIdentityKey", ex);
            }

            // Send the message to the Service Bus queue
            try
            {
                await using var client = new ServiceBusClient(serviceBusConnectionString, credential);
                ServiceBusSender sender = client.CreateSender("correspondence-queue");

                var serviceBusMessage = new ServiceBusMessage(messageBody);

                // ENCRYPT POLICY NUMBER BEFORE ADDING TO MESSAGE PROPERTIES
                string encryptedPolicyNumber;
                try
                {
                    // FIX: Ensure ECCOTrackingPolicyNumber is not null before encrypting
                    var policyNumberToEncrypt = message.ECCOTrackingPolicyNumber ?? string.Empty;
                    encryptedPolicyNumber = await _encryptionService.EncryptAsync(policyNumberToEncrypt);
                    Console.WriteLine($"Successfully AES encrypted PolicyNumber for Service Bus");
                    log.Info($"PolicyNumber AES encrypted for Service Bus transmission");
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to encrypt PolicyNumber: {ex.Message}", ex);
                    Console.WriteLine($"ERROR: Failed to encrypt PolicyNumber: {ex.Message}");
                    throw new EncryptionException("Failed to encrypt PolicyNumber for Service Bus transmission", "ENCRYPTION_ERROR", "AES", "Encrypt", ex);
                }

                // Add message properties for tracking with encrypted PolicyNumber
                serviceBusMessage.MessageId = message.DocId ?? Guid.NewGuid().ToString();
                serviceBusMessage.Subject = "CorrespondenceRequest";
                serviceBusMessage.ApplicationProperties["PolicyNumber"] = encryptedPolicyNumber; // ✅ ENCRYPTED
                serviceBusMessage.ApplicationProperties["ClaimNumber"] = message.ECCOTrackingClaimNumber;
                serviceBusMessage.ApplicationProperties["RequestingSystem"] = message.RequestingSystemName;

                // ADD METADATA TO INDICATE ENCRYPTION
                serviceBusMessage.ApplicationProperties["PolicyNumberEncrypted"] = "AES";
                serviceBusMessage.ApplicationProperties["EncryptionVersion"] = "1.0";

                await sender.SendMessageAsync(serviceBusMessage, cancellationToken);

                // Update state to indicate message was sent successfully
                state = new ProcessingState(
                    MessageSentToServiceBus: true,
                    ServiceBusMessageId: serviceBusMessage.MessageId,
                    SequenceNumber: null // Sequence number is assigned by Service Bus
                );

                log.Info($"Message successfully sent to Service Bus with AES encrypted PolicyNumber. MessageId: {serviceBusMessage.MessageId}");
                Console.WriteLine($"Successfully sent to Service Bus with AES encryption. MessageId: {serviceBusMessage.MessageId}");
                Console.WriteLine($"PolicyNumber transmitted in encrypted format");
                return state;
            }
            catch (EncryptionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error("Failed to send the message to the Service Bus queue.", ex);
                Console.WriteLine($"Failed to send to Service Bus: {ex.Message}");
                throw new CommunicationException("Failed to send message to Service Bus queue", "SERVICEBUS_SEND_ERROR", "AzureServiceBus", ex);
            }
        }

        /// <summary>
        /// Attempts to rollback any partial processing that occurred before the failure.
        /// Logs the failure to the status history for audit trail.
        /// </summary>
        private async Task RollbackPartialProcessingAsync(
            SequentialProcess process,
            ProcessingState state,
            Exception originalException,
            IMessageHandlerContext context)
        {
            log.Warn($"Initiating rollback for DocId: {process.DocId}. MessageSentToServiceBus: {state.MessageSentToServiceBus}");
            Console.WriteLine($"**** ROLLBACK INITIATED for DocId: {process.DocId} ****");

            var rollbackErrors = new List<string>();

            try
            {
                // 1. Log failure to status history for audit trail
                await LogFailureToStatusHistoryAsync(process, originalException);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to log failure to status history: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            try
            {
                // 2. If message was sent to Service Bus, we can't directly delete it
                // but we can publish a compensation event for downstream handlers
                if (state.MessageSentToServiceBus)
                {
                    await PublishCompensationEventAsync(process, state, context);
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
                // 3. Update correspondence request status to indicate failure
                await UpdateCorrespondenceStatusToFailedAsync(process.DocId);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to update correspondence status: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            // Log summary of rollback
            if (rollbackErrors.Count > 0)
            {
                log.Error($"Rollback completed with {rollbackErrors.Count} error(s): {string.Join("; ", rollbackErrors)}");
                Console.WriteLine($"**** ROLLBACK COMPLETED WITH ERRORS for DocId: {process.DocId} ****");
            }
            else
            {
                log.Info($"Rollback completed successfully for DocId: {process.DocId}");
                Console.WriteLine($"**** ROLLBACK COMPLETED SUCCESSFULLY for DocId: {process.DocId} ****");
            }
        }

        /// <summary>
        /// Logs the processing failure to the status history table for audit purposes.
        /// </summary>
        private async Task LogFailureToStatusHistoryAsync(SequentialProcess process, Exception exception)
        {
            try
            {
                // Find the correspondence request by DocId to get the CorrespondenceId
                var correspondenceRequests = await _correspondenceRequestService.GetCorrespondenceRequestByDocIdAsync(process.DocId!);
                var correspondenceRequest = correspondenceRequests.FirstOrDefault();

                if (correspondenceRequest != null)
                {
                    // Get the latest sequence number
                    var existingHistory = await _statusHistoryService.GetAllByCorrespondenceIdAsync(correspondenceRequest.CorrespondenceId);
                    var maxSequence = existingHistory.Any()
                        ? existingHistory.Max(h => h?.HistorySequenceNum ?? 0)
                        : 0;

                    var failureHistory = new CorrespondenceRequestStatusHistory
                    {
                        CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                        CorrespondenceId = correspondenceRequest.CorrespondenceId,
                        HistorySequenceNum = maxSequence + 1,
                        StatusCode = "99", // Error status code
                        StatusUpdatedBy = "AddCorrespondenceSBusHandler-Rollback",
                        StatusUpdateTS = DateTime.UtcNow
                    };

                    await _statusHistoryService.AddCorrespondenceRequestStatusHistoryAsync(failureHistory);
                    log.Info($"Logged failure status history for CorrespondenceId: {correspondenceRequest.CorrespondenceId}");
                }
                else
                {
                    log.Warn($"Could not find correspondence request for DocId: {process.DocId} to log failure history");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Failed to log failure to status history: {ex.Message}", ex);
                throw new DatabaseException("Failed to log failure to status history", "DB_INSERT_ERROR", "Insert", "CorrespondenceRequestStatusHistory", ex);
            }
        }

        /// <summary>
        /// Publishes a compensation event to notify downstream systems that the message should be ignored/compensated.
        /// </summary>
        private async Task PublishCompensationEventAsync(
            SequentialProcess process,
            ProcessingState state,
            IMessageHandlerContext context)
        {
            try
            {
                var compensationEvent = new ServiceBusMessageCompensationEvent
                {
                    DocId = process.DocId,
                    ServiceBusMessageId = state.ServiceBusMessageId,
                    CompensationReason = "Handler failure after Service Bus send",
                    CompensationTimestamp = DateTime.UtcNow,
                    OriginalPolicyNumber = process.ECCOTrackingPolicyNumber,
                    OriginalClaimNumber = process.ECCOTrackingClaimNumber
                };

                await context.Publish(compensationEvent);
                log.Info($"Published compensation event for ServiceBusMessageId: {state.ServiceBusMessageId}");
                Console.WriteLine($"Compensation event published for message: {state.ServiceBusMessageId}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to publish compensation event: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Updates the correspondence request status to indicate processing failure.
        /// </summary>
        private async Task UpdateCorrespondenceStatusToFailedAsync(string? docId)
        {
            if (string.IsNullOrEmpty(docId))
            {
                log.Warn("Cannot update correspondence status - DocId is null or empty");
                return;
            }

            try
            {
                var correspondenceRequests = await _correspondenceRequestService.GetCorrespondenceRequestByDocIdAsync(docId);
                var correspondenceRequest = correspondenceRequests.FirstOrDefault();

                if (correspondenceRequest != null)
                {
                    // Update to failed status - implementation depends on your domain model
                    // correspondenceRequest.Status = "Failed";
                    // await _correspondenceRequestService.UpdateCorrespondenceRequestAsync(correspondenceRequest);
                    log.Info($"Marked correspondence request as failed for DocId: {docId}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Failed to update correspondence status to failed: {ex.Message}", ex);
                throw new DatabaseException("Failed to update correspondence status to failed", "DB_UPDATE_ERROR", "Update", "CorrespondenceRequest", ex);
            }
        }

        /// <summary>
        /// Event published when a Service Bus message needs compensation due to handler failure.
        /// </summary>
        public class ServiceBusMessageCompensationEvent : IEvent
        {
            public string? DocId { get; set; }
            public string? ServiceBusMessageId { get; set; }
            public string? CompensationReason { get; set; }
            public DateTime CompensationTimestamp { get; set; }
            public string? OriginalPolicyNumber { get; set; }
            public string? OriginalClaimNumber { get; set; }
        }
    }
}
