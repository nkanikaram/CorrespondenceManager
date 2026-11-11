using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using NServiceBus.Logging;
using NServiceBus.MessageRouting.RoutingSlips;

namespace ProcessRequestEndPoint.Handlers
{
    public class UpdateArchivedStatusDBHandler : IHandleMessages<SequentialProcess>
    {
        static ILog log = LogManager.GetLogger(typeof(UpdateArchivedStatusDBHandler));
        private readonly ICorrespondenceRequestRepository _requestRepo;
        private readonly ICorrespondenceRequestStatusHistoryRepository _historyRepo;

        public UpdateArchivedStatusDBHandler(ICorrespondenceRequestRepository requestRepo, ICorrespondenceRequestStatusHistoryRepository historyRepo)
        {
            _requestRepo = requestRepo;
            _historyRepo = historyRepo;
        }
        public async Task Handle(SequentialProcess message, IMessageHandlerContext context)
        {
            var routingSlip = context.Extensions.Get<RoutingSlip>();
            log.Info("Add a record to CorrespondenceRequestStatusHistory table with information about CompositionSend Request to Venture...");

            // Ensure DocId is not null before parsing
            if (string.IsNullOrWhiteSpace(message.DocId))
            {
                throw new ArgumentNullException(nameof(message.DocId), "DocId cannot be null or empty.");
            }
            var maxSeq = await _historyRepo.GetMaxSequenceNumber(Guid.Parse(message.DocId));
            var statusHistory = new CorrespondenceRequestStatusHistory
            {
                CorrespondenceRequestStatusHistoryId = Guid.NewGuid(),
                CorrespondenceId = Guid.Parse(routingSlip.Attachments["CorrespondenceId"]),//need to add code to get CorrespondenceId or need to change the DB schema to add DocId to CorrespondenceRequestStatusHistory table
                HistorySequenceNum = maxSeq + 1,
                StatusCode = "RequestArchived",
                StatusUpdatedBy = "System",
                StatusUpdateTS = DateTime.UtcNow
            };
            await _historyRepo.AddCorrespondenceRequestStatusHistoryAsync(statusHistory);
            routingSlip.Attachments["UpdateArchivedStatusDBHandler"] = "Success";
            await Task.CompletedTask;
        }
    }
}
