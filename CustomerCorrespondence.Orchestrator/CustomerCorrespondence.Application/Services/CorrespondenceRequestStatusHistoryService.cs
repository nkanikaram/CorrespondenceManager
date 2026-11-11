using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NServiceBus.Transport;

namespace CustomerCorrespondence.Application.Services
{
    public class CorrespondenceRequestStatusHistoryService : ICorrespondenceRequestStatusHistoryService
    {
        private readonly ICorrespondenceRequestStatusHistoryRepository _correspondenceRequestStatusHistoryRepository;
        private readonly ILogger<CorrespondenceRequestStatusHistoryService> _logger;

        public CorrespondenceRequestStatusHistoryService(ICorrespondenceRequestStatusHistoryRepository correspondenceRequestStatusHistoryRepository,
                                                                    ILogger<CorrespondenceRequestStatusHistoryService> logger)
        {
            _correspondenceRequestStatusHistoryRepository = correspondenceRequestStatusHistoryRepository;
            _logger = logger;
        }

        public async Task AddCorrespondenceRequestStatusHistoryAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory)
        {
            try
            {
                _logger.LogInformation("Adding CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceRequestStatusHistory.CorrespondenceId);
                await _correspondenceRequestStatusHistoryRepository.AddCorrespondenceRequestStatusHistoryAsync(correspondenceRequestStatusHistory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceRequestStatusHistory.CorrespondenceId);
                throw;
            }
        }

        public async Task DeleteCorrespondenceRequestStatusHistoryAsync(Guid correspondenceId)
        {
            try
            {
                _logger.LogInformation("Deleting CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceId);
                await _correspondenceRequestStatusHistoryRepository.DeleteCorrespondenceRequestStatusHistoryAsync(correspondenceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceId);
                throw;
            }
        }

        public async Task<IEnumerable<CorrespondenceRequestStatusHistory?>> GetAllByCorrespondenceIdAsync(Guid correspondenceId)
        {
            try
            {
                _logger.LogInformation("Fetching all CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceId);
                return await _correspondenceRequestStatusHistoryRepository.GetAllByCorrespondenceIdAsync(correspondenceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceId);
                throw;
            }
        }

        public async Task<CorrespondenceRequestStatusHistory?> GetByHistorySequenceNumAsync(Guid correspondenceId, int historySequenceNum)
        {
            try
            {
                _logger.LogInformation("Fetching CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId} and HistorySequenceNum: {HistorySequenceNum}", correspondenceId, historySequenceNum);
                return await _correspondenceRequestStatusHistoryRepository.GetStatusHistoryByCorrIdSeqNumAsync(correspondenceId, historySequenceNum);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId} and HistorySequenceNum: {HistorySequenceNum}", correspondenceId, historySequenceNum);
                throw;
            }
        }

        public async Task UpdateCorrespondenceRequesStatusHistorytAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory)
        {
            try
            {
                _logger.LogInformation("Updating CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceRequestStatusHistory.CorrespondenceId);
                await _correspondenceRequestStatusHistoryRepository.UpdateCorrespondenceRequesStatusHistorytAsync(correspondenceRequestStatusHistory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating CorrespondenceRequestStatusHistory with CorrespondenceId: {CorrespondenceId}", correspondenceRequestStatusHistory.CorrespondenceId);
                throw;
            }
        }

        public Task LogNonRecoverableErrorAsync(IncomingMessage messageId, string errorMessage, string stackTrace)
        {
            try
            {
                _logger.LogInformation("Logging non-recoverable error for MessageId: {MessageId}", messageId.MessageId);
                return _correspondenceRequestStatusHistoryRepository.LogNonRecoverableErrorAsync(messageId, errorMessage, stackTrace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging non-recoverable error for MessageId: {MessageId}", messageId.MessageId);
                throw;
            }
        }
    }
}
