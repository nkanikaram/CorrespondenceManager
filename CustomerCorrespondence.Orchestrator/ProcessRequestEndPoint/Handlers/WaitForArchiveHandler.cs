using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using NServiceBus.Logging;
using NServiceBus.MessageRouting.RoutingSlips;

namespace ProcessRequestEndPoint.Handlers
{
    public class WaitForArchiveHandler : IHandleMessages<SequentialProcess>
    {
        static ILog log = LogManager.GetLogger(typeof(WaitForArchiveHandler));
        private readonly ICorrespondenceRequestStatusHistoryRepository _historyRepo;
        public WaitForArchiveHandler(ICorrespondenceRequestStatusHistoryRepository historyRepo)
        {
            _historyRepo = historyRepo;
        }
        public async Task Handle(SequentialProcess message, IMessageHandlerContext context)
        {
            var routingSlip = context.Extensions.Get<RoutingSlip>();
            log.Info("Step to Wait for Archive, need further discussion on what logic we will implement here...");

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
                StatusCode = "WaitForArchive",
                StatusUpdatedBy = "System",
                StatusUpdateTS = DateTime.UtcNow
            };
            await _historyRepo.AddCorrespondenceRequestStatusHistoryAsync(statusHistory);
            routingSlip.Attachments["WaitForArchiveHandler"] = "Success";
            await Task.CompletedTask;
        }
    }
}
