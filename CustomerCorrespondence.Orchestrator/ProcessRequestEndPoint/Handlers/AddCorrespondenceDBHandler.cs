using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Infrastructure.Messaging;
using CustomerCorrespondence.Infrastructure.Persistence;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using Microsoft.EntityFrameworkCore;
using NServiceBus.Logging;
using System.Text;
using System.Text.Json;

namespace ProcessRequestEndPoint.Handlers
{
    public class AddCorrespondenceDBHandler : IHandleMessages<AddCorrespondenceDBHandlerMessage>
    {
        static ILog log = LogManager.GetLogger(typeof(AddCorrespondenceDBHandler));
        private readonly ICorrespondenceRequestService _requestService;
        private readonly ICorrespondenceRequestStatusHistoryService _historyService;
        private readonly IEncryptionService _encryptionService;
        private readonly CorrespondenceDBContext _dbContext;

        public AddCorrespondenceDBHandler(
            ICorrespondenceRequestService requestService,
            ICorrespondenceRequestStatusHistoryService historyService,
            IEncryptionService encryptionService,
            CorrespondenceDBContext dbContext)
        {
            _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
            _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task Handle(AddCorrespondenceDBHandlerMessage message, IMessageHandlerContext context)
        {
            Console.WriteLine("**** ADD CORRESPONDENCE DB HANDLER STARTED ****");

            // Use a transaction scope for atomicity
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(context.CancellationToken);

            try
            {
                // Log routing slip debug information
                LogRoutingSlipHeaders(context);

                log.Info("Adding the Correspondence Request Message to Azure SQL Server DB Table...");
                Console.WriteLine($"Processing request for DocId: {message.Process.DocId}");

                // Validate DocId is present (should be set by AssignDocumentumIdHandler)
                if (string.IsNullOrEmpty(message.Process.DocId))
                {
                    var error = "DocId is missing on the message. Cannot write to Azure SQL Server DB.";
                    log.Error(error);
                    Console.WriteLine($"ERROR: {error}");
                    throw new ValidationException("DocId is required but was not provided on the message.",
                        new Dictionary<string, List<string>> { ["DocId"] = new List<string> { "DocId cannot be null or empty" } });
                }

                // Write to Database
                var corrId = Guid.NewGuid();
                var now = DateTime.UtcNow;

                // Add CorrespondenceRequest
                await AddCorrespondenceRequestAsync(message.Process, corrId, now);

                // Add CorrespondenceRequestStatusHistory
                await AddCorrespondenceRequestStatusHistoryAsync(corrId, now);

                await _dbContext.SaveChangesAsync(context.CancellationToken);

                // Commit transaction BEFORE forwarding
                await transaction.CommitAsync(context.CancellationToken);

                log.Info("AddCorrespondenceDBHandler completed successfully for message.");
                Console.WriteLine("**** ADD CORRESPONDENCE DB HANDLER - Business logic completed ****");

                // Use RoutingSlipForwarder to handle next step or completion
                await RoutingSlipForwarder.ForwardToNextStepIfAny(context, message);
                Console.WriteLine("**** ADD CORRESPONDENCE DB HANDLER - Routing slip forwarding completed ****");
            }
            catch (Exception ex)
            {
                log.Error("An error occurred while processing the message in AddCorrespondenceDBHandler.", ex);
                Console.WriteLine($"Error in AddCorrespondenceDBHandler: {ex.Message}");
                await transaction.RollbackAsync(context.CancellationToken);
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
                    Console.WriteLine($"Routing slip headers - Route: {routeJson ?? "missing"}, Index: {idx ?? "missing"}");
                }
                else
                {
                    log.Info("[DEBUG] context.MessageHeaders is null");
                }
            }
            catch (Exception ex)
            {
                log.Warn("Failed to write debug headers in AddCorrespondenceDBHandler", ex);
            }
        }

        private async Task AddCorrespondenceRequestAsync(SequentialProcess message, Guid corrId, DateTime now)
        {
            try
            {
                Console.WriteLine("**** Starting encryption for database storage ****");

                // ENCRYPT POLICY NUMBER WITH PGP
                byte[] encryptedPolicyNumberBytes;
                byte[] policyNumberHashBytes;
                try
                {
                    if (string.IsNullOrEmpty(message.ECCOTrackingPolicyNumber))
                        throw new ValidationException("ECCOTrackingPolicyNumber is required but was not provided.",
                            new Dictionary<string, List<string>> { ["ECCOTrackingPolicyNumber"] = new List<string> { "Policy number is required" } });

                    var encryptedPolicyNumber = await _encryptionService.EncryptAsync(message.ECCOTrackingPolicyNumber);
                    // Convert to bytes for varbinary storage
                    encryptedPolicyNumberBytes = Encoding.UTF8.GetBytes(encryptedPolicyNumber);
                    // Generate hash for searches
                    policyNumberHashBytes = _encryptionService.GenerateSearchHash(message.ECCOTrackingPolicyNumber);

                    Console.WriteLine($"Encrypted PolicyNumber length: {encryptedPolicyNumber.Length} characters ({encryptedPolicyNumberBytes.Length} bytes)");
                    Console.WriteLine($"PolicyNumber hash length: {policyNumberHashBytes.Length} bytes");
                    log.Info($"PolicyNumber encrypted for database storage: {encryptedPolicyNumberBytes.Length} bytes");
                }
                catch (ValidationException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to encrypt PolicyNumber: {ex.Message}", ex);
                    Console.WriteLine($"ERROR: Failed to encrypt PolicyNumber: {ex.Message}");
                    throw new EncryptionException("Failed to encrypt PolicyNumber for database storage", "ENCRYPTION_ERROR", "PGP", "Encrypt", ex);
                }

                // ENCRYPT REQUEST PAYLOAD
                byte[] encryptedRequestPayload;
                try
                {
                    var requestPayloadJson = JsonSerializer.Serialize(message);
                    Console.WriteLine($"Serialized RequestPayload size: {requestPayloadJson.Length} characters");

                    var encryptedPayloadString = await _encryptionService.EncryptAsync(requestPayloadJson);
                    encryptedRequestPayload = Encoding.UTF8.GetBytes(encryptedPayloadString);

                    Console.WriteLine($"Encrypted RequestPayload size: {encryptedRequestPayload.Length} bytes");
                    log.Info("RequestPayload encrypted for database storage");
                }
                catch (JsonException ex)
                {
                    log.Error($"Failed to serialize RequestPayload: {ex.Message}", ex);
                    Console.WriteLine($"ERROR: Failed to serialize RequestPayload: {ex.Message}");
                    throw new SerializationException("Failed to serialize RequestPayload", "SERIALIZATION_ERROR", typeof(SequentialProcess), "Serialize", ex);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to encrypt RequestPayload: {ex.Message}", ex);
                    Console.WriteLine($"ERROR: Failed to encrypt RequestPayload: {ex.Message}");
                    throw new EncryptionException("Failed to encrypt RequestPayload for database storage", "ENCRYPTION_ERROR", "PGP", "Encrypt", ex);
                }

                var deliveryMethodCode = await MapDeliveryMethodNameToCodeAsync(message.DeliveryMethod);

                // CREATE CORRESPONDENCE REQUEST WITH BOTH ENCRYPTED DATA AND HASH
                var request = new CorrespondenceRequest
                {
                    CorrespondenceId = corrId,
                    DocId = message.DocId,
                    RequestingDomainName = message.RequestingSystemName,
                    DeliveryMethodCode = deliveryMethodCode,
                    VersionCode = message.CompositionInfo.VersionCode,
                    ArchiveDocumentTypeCode = message.DocumentDescription,
                    PolicyNumber = encryptedPolicyNumberBytes, // PGP encrypted for security
                    PolicyNumberHash = policyNumberHashBytes, // SHA-256 hash for searches
                    ClaimNumber = message.ECCOTrackingClaimNumber,
                    CustomerId = message.ECCOTrackingCustomerId,
                    RequestPayload = encryptedRequestPayload,
                    RequestCreationTS = now,
                };

                await _requestService.AddCorrespondenceRequestAsync(request);

                log.Info($"Successfully added CorrespondenceRequest with encrypted data. ID: {corrId}");
                Console.WriteLine($"Successfully saved to database with varbinary encryption. CorrespondenceId: {corrId}");
            }
            catch (ValidationException)
            {
                throw;
            }
            catch (EncryptionException)
            {
                throw;
            }
            catch (SerializationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error("Failed to add CorrespondenceRequest to the database.", ex);
                throw new DatabaseException("Failed to add CorrespondenceRequest to the database", "DB_INSERT_ERROR", "Insert", "CorrespondenceRequest", ex);
            }
        }

        private async Task AddCorrespondenceRequestStatusHistoryAsync(Guid corrId, DateTime now)
        {
            try
            {
                var statusHistory = new CorrespondenceRequestStatusHistory
                {
                    CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                    CorrespondenceId = corrId,
                    HistorySequenceNum = 1,
                    StatusCode = "01", // Initial status
                    StatusUpdatedBy = "System",
                    StatusUpdateTS = now
                };

                await _historyService.AddCorrespondenceRequestStatusHistoryAsync(statusHistory);
                log.Info($"Successfully added CorrespondenceRequestStatusHistory for CorrespondenceId: {corrId}");
            }
            catch (Exception ex)
            {
                log.Error("Failed to add CorrespondenceRequestStatusHistory to the database.", ex);
                throw new DatabaseException("Failed to add CorrespondenceRequestStatusHistory to the database", "DB_INSERT_ERROR", "Insert", "CorrespondenceRequestStatusHistory", ex);
            }
        }

        // Helper method for mapping
        private async Task<string> MapDeliveryMethodNameToCodeAsync(string deliveryMethodName)
        {
            if (string.IsNullOrWhiteSpace(deliveryMethodName))
                throw new ValidationException("Delivery method name is required.",
                    new Dictionary<string, List<string>> { ["DeliveryMethod"] = new List<string> { "Delivery method cannot be null or empty" } });

            var normalized = deliveryMethodName.Trim().ToLowerInvariant();

            try
            {
                var method = await _dbContext.Set<CorrespondenceDeliveryMethod>()
                    .FirstOrDefaultAsync(m => m.DeliveryMethodName.ToLower() == normalized);

                if (method == null)
                    throw new ValidationException($"Unknown delivery method: {deliveryMethodName}",
                        new Dictionary<string, List<string>> { ["DeliveryMethod"] = new List<string> { $"'{deliveryMethodName}' is not a valid delivery method" } });

                return method.DeliveryMethodCode;
            }
            catch (ValidationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error($"Database error while mapping delivery method '{deliveryMethodName}': {ex.Message}", ex);
                throw new DatabaseException($"Failed to map delivery method '{deliveryMethodName}'", "DB_QUERY_ERROR", "Query", "CorrespondenceDeliveryMethod", ex);
            }
        }
    }
}
