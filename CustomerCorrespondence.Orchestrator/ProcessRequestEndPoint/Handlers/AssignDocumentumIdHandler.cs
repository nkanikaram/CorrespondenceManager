using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Entities.Messages;
using CustomerCorrespondence.Infrastructure.Messaging;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using NServiceBus.Logging;

namespace ProcessRequestEndPoint.Handlers
{
    public class AssignDocumentumIdHandler : IHandleMessages<AssignDocumentumIdHandlerMessage>
    {
        static ILog log = LogManager.GetLogger(typeof(AssignDocumentumIdHandler));
        private readonly IDocumentIdGeneratorService _documentIdGeneratorService;
        private readonly IDocumentIdsService _documentIdsService;

        // Track partial processing state for rollback
        private readonly record struct ProcessingState(
            bool DocIdAssigned,
            bool DocIdStatusUpdated,
            string? AssignedDocId,
            bool WasNewlyGenerated);

        public AssignDocumentumIdHandler(
            IDocumentIdGeneratorService documentIdGeneratorService,
            IDocumentIdsService documentIdsService)
        {
            Console.WriteLine("Assign Documentum Id End Point constructor");
            _documentIdGeneratorService = documentIdGeneratorService ?? throw new ArgumentNullException(nameof(documentIdGeneratorService));
            _documentIdsService = documentIdsService ?? throw new ArgumentNullException(nameof(documentIdsService));
        }

        public async Task Handle(AssignDocumentumIdHandlerMessage message, IMessageHandlerContext context)
        {
            Console.WriteLine("**** ASSIGN DOCUMENTUM ID HANDLER STARTED ****");

            var processingState = new ProcessingState(false, false, null, false);
            var originalDocId = message.Process.DocId;

            try
            {
                // Generate DocId if not already present
                if (string.IsNullOrEmpty(message.Process.DocId))
                {
                    processingState = await AssignDocIdAsync(message, processingState);
                }
                else
                {
                    log.Info($"Message already contains DocId '{message.Process.DocId}'.");
                    Console.WriteLine($"DocId already exists: {message.Process.DocId}");
                }

                log.Info("AssignDocumentumIdHandler completed successfully for message.");
                Console.WriteLine("**** ASSIGN DOCUMENTUM ID HANDLER - Business logic completed ****");

                // Use RoutingSlipForwarder to handle next step or completion
                await RoutingSlipForwarder.ForwardToNextStepIfAny(context, message);
                Console.WriteLine("**** ASSIGN DOCUMENTUM ID HANDLER - Routing slip forwarding completed ****");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to process message in AssignDocumentumIdHandler: {ex.Message}", ex);
                Console.WriteLine($"Error in AssignDocumentumIdHandler: {ex.Message}");

                // Attempt rollback of partial processing
                await RollbackPartialProcessingAsync(message, originalDocId, processingState, ex, context);

                throw;
            }
        }

        /// <summary>
        /// Assigns a DocId to the message, tracking state for potential rollback.
        /// </summary>
        private async Task<ProcessingState> AssignDocIdAsync(
            AssignDocumentumIdHandlerMessage message,
            ProcessingState state)
        {
            string? docId = null;
            bool wasNewlyGenerated = false;

            try
            {
                // Step 1: Attempt to get DocId from DocumentIds table first
                docId = await _documentIdsService.GetAndUpdateDocumentIdAsync();

                if (docId == null)
                {
                    // Step 2: If not found in DocumentIds table, generate a new batch and get one
                    docId = await _documentIdGeneratorService.GenerateDocIdAsync();
                    wasNewlyGenerated = true;
                    Console.WriteLine($"Generated new DocId batch, using: {docId}");
                }
                else
                {
                    Console.WriteLine($"Retrieved existing DocId from pool: {docId}");
                }

                if (docId == null)
                {
                    log.Error("Generated DocId is null.");
                    Console.WriteLine("Error: Generated DocId is null.");
                    throw new DocumentIdGenerationException("Generated DocId cannot be null.", "DOCID_GENERATION_FAILED");
                }

                // Step 3: Assign DocId to message
                message.Process.DocId = docId;
                state = state with 
                { 
                    DocIdAssigned = true, 
                    DocIdStatusUpdated = true,  // ✅ FIX: Set this to true so rollback works
                    AssignedDocId = docId, 
                    WasNewlyGenerated = wasNewlyGenerated 
                };

                log.Info($"Assigned DocId '{docId}' to message.");
                Console.WriteLine($"Assigned DocId: {docId}");

                log.Info($"Updated DocId '{docId}' status to 'Used'.");
                Console.WriteLine($"DocId status updated to 'Used': {docId}");

                return state;
            }
            catch (DocumentIdGenerationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new DocumentIdGenerationException($"Failed to assign DocId: {ex.Message}", "DOCID_ASSIGNMENT_ERROR", ex);
            }
        }

        /// <summary>
        /// Attempts to rollback any partial processing that occurred before the failure.
        /// </summary>
        private async Task RollbackPartialProcessingAsync(
            AssignDocumentumIdHandlerMessage message,
            string? originalDocId,
            ProcessingState state,
            Exception originalException,
            IMessageHandlerContext context)
        {
            log.Warn($"Initiating rollback. DocIdAssigned: {state.DocIdAssigned}, " +
                     $"DocIdStatusUpdated: {state.DocIdStatusUpdated}, AssignedDocId: {state.AssignedDocId}");
            Console.WriteLine($"**** ROLLBACK INITIATED for AssignDocumentumIdHandler ****");

            var rollbackErrors = new List<string>();

            try
            {
                // Step 1: Revert DocId status back to 'New' if it was assigned
                if (state.DocIdAssigned && !string.IsNullOrEmpty(state.AssignedDocId))
                {
                    await RevertDocIdStatusAsync(state.AssignedDocId);
                    log.Info($"Reverted DocId '{state.AssignedDocId}' status back to 'New'.");
                    Console.WriteLine($"Rollback: DocId status reverted to 'New': {state.AssignedDocId}");
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to revert DocId status: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            try
            {
                // Step 2: Restore original DocId on message (likely null)
                if (state.DocIdAssigned)
                {
                    message.Process.DocId = originalDocId;
                    log.Info($"Restored original DocId on message: '{originalDocId ?? "(null)"}'.");
                    Console.WriteLine($"Rollback: Message DocId restored to: {originalDocId ?? "(null)"}");
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to restore original DocId on message: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            try
            {
                // Step 3: Publish compensation event for audit trail
                await PublishCompensationEventAsync(state, originalException, context);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to publish compensation event: {ex.Message}";
                log.Error(errorMsg, ex);
                rollbackErrors.Add(errorMsg);
            }

            // Log summary of rollback
            if (rollbackErrors.Count > 0)
            {
                log.Error($"Rollback completed with {rollbackErrors.Count} error(s): {string.Join("; ", rollbackErrors)}");
                Console.WriteLine($"**** ROLLBACK COMPLETED WITH ERRORS ****");
            }
            else
            {
                log.Info($"Rollback completed successfully for DocId: {state.AssignedDocId}");
                Console.WriteLine($"**** ROLLBACK COMPLETED SUCCESSFULLY ****");
            }
        }

        /// <summary>
        /// Reverts the DocId status from 'Used' back to 'New' so it can be reused.
        /// </summary>
        private async Task RevertDocIdStatusAsync(string docId)
        {
            try
            {
                await _documentIdsService.AddDocumentIdAsync(docId);
                log.Info($"DocId '{docId}' has been returned to the available pool.");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to revert DocId '{docId}' status: {ex.Message}", ex);
                throw new DatabaseException($"Failed to revert DocId '{docId}' status", "DB_UPDATE_ERROR", "Update", "DocumentIds", ex);
            }
        }

        /// <summary>
        /// Publishes a compensation event for audit and downstream notification.
        /// </summary>
        private async Task PublishCompensationEventAsync(
            ProcessingState state,
            Exception originalException,
            IMessageHandlerContext context)
        {
            var compensationEvent = new DocumentIdAssignmentCompensationEvent
            {
                DocId = state.AssignedDocId,
                WasNewlyGenerated = state.WasNewlyGenerated,
                CompensationReason = originalException.Message,
                CompensationTimestamp = DateTime.UtcNow,
                HandlerName = nameof(AssignDocumentumIdHandler)
            };

            await context.Publish(compensationEvent);
            log.Info($"Published compensation event for DocId: {state.AssignedDocId}");
            Console.WriteLine($"Compensation event published for DocId: {state.AssignedDocId}");
        }
    }

    /// <summary>
    /// Event published when DocId assignment needs compensation due to handler failure.
    /// </summary>
    public class DocumentIdAssignmentCompensationEvent : IEvent
    {
        public string? DocId { get; set; }
        public bool WasNewlyGenerated { get; set; }
        public string? CompensationReason { get; set; }
        public DateTime CompensationTimestamp { get; set; }
        public string? HandlerName { get; set; }
    }
}
