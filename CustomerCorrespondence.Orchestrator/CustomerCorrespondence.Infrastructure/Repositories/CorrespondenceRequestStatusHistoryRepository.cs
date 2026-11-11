using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NServiceBus.Transport;

namespace CustomerCorrespondence.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for managing correspondence request status history data operations.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public class CorrespondenceRequestStatusHistoryRepository(CorrespondenceDBContext dbContext) : ICorrespondenceRequestStatusHistoryRepository
    {
        /// <summary>
        /// AddCorrespondenceRequestStatusHistoryAsync
        /// </summary>
        /// <param name="correspondenceRequestStatusHistory"></param>
        /// <returns></returns>
        public async Task AddCorrespondenceRequestStatusHistoryAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory)
        {
            try
            {
                await dbContext.CorrespondenceRequestStatusHistories.AddAsync(correspondenceRequestStatusHistory);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error adding CorrespondenceRequestStatusHistory for CorrespondenceId {correspondenceRequestStatusHistory.CorrespondenceId}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// DeleteCorrespondenceRequestStatusHistoryAsync
        /// </summary>
        /// <param name="correspondenceId"></param>
        /// <returns></returns>
        public async Task DeleteCorrespondenceRequestStatusHistoryAsync(Guid correspondenceId)
        {
            try
            {
                var histories = await dbContext.CorrespondenceRequestStatusHistories
                    .Where(h => h.CorrespondenceId == correspondenceId)
                    .ToListAsync();

                if (histories.Any())
                {
                    dbContext.CorrespondenceRequestStatusHistories.RemoveRange(histories);
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error deleting CorrespondenceRequestStatusHistory for CorrespondenceId {correspondenceId}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// GetAllByCorrespondenceIdAsync
        /// </summary>
        /// <param name="correspondenceId"></param>
        /// <returns></returns>
        public async Task<IEnumerable<CorrespondenceRequestStatusHistory>> GetAllByCorrespondenceIdAsync(Guid correspondenceId)
        {
            try
            {
                return await dbContext.CorrespondenceRequestStatusHistories
                    .Where(h => h.CorrespondenceId == correspondenceId)
                    .OrderBy(h => h.HistorySequenceNum)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fetching all CorrespondenceRequestStatusHistory for CorrespondenceId {correspondenceId}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// GetMaxSequenceNumber
        /// </summary>
        /// <param name="correspondenceId"></param>
        /// <returns></returns>
        public async Task<int> GetMaxSequenceNumber(Guid correspondenceId)
        {
            try
            {
                var maxSeq = await dbContext.CorrespondenceRequestStatusHistories
                    .Where(h => h.CorrespondenceId == correspondenceId)
                    .MaxAsync(h => (int?)h.HistorySequenceNum);
                //if (maxSeq == 1)
                //{
                //    maxSeq = 0;
                //}
                return maxSeq ?? 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fetching max sequence number for CorrespondenceId {correspondenceId}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// GetStatusCodeByStatus
        /// </summary>
        /// <param name="statusName"></param>
        /// <returns></returns>
        public async Task<string> GetStatusCodeByStatus(string statusName)
        {
            try
            {
                var statusCode = await dbContext.CorrespondenceStatusCodes
                    .Where(h => h.StatusDescription == statusName)
                    .Select(h => h.StatusCode)
                    .FirstOrDefaultAsync();

                return statusCode ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fetching status code for status name {statusName}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// GetStatusHistoryByCorrIdSeqNumAsync
        /// </summary>
        /// <param name="correspondenceId"></param>
        /// <param name="historySequenceNum"></param>
        /// <returns></returns>
        public async Task<CorrespondenceRequestStatusHistory?> GetStatusHistoryByCorrIdSeqNumAsync(Guid correspondenceId, int historySequenceNum)
        {
            try
            {
                return await dbContext.CorrespondenceRequestStatusHistories
                     .FirstOrDefaultAsync(h => h.CorrespondenceId == correspondenceId && h.HistorySequenceNum == historySequenceNum);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fetching CorrespondenceRequestStatusHistory for CorrespondenceId {correspondenceId} and HistorySequenceNum {historySequenceNum}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// UpdateCorrespondenceRequesStatusHistorytAsync
        /// </summary>
        /// <param name="correspondenceRequestStatusHistory"></param>
        /// <returns></returns>
        public async Task UpdateCorrespondenceRequesStatusHistorytAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory)
        {
            try
            {
                dbContext.CorrespondenceRequestStatusHistories.Update(correspondenceRequestStatusHistory);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error updating CorrespondenceRequestStatusHistory for CorrespondenceId {correspondenceRequestStatusHistory.CorrespondenceId}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// Gets the status description for a given status code from the CorrespondenceStatusCode table
        /// </summary>
        /// <param name="statusCode">The status code to look up</param>
        /// <returns>The status description or null if not found</returns>
        public async Task<string?> GetStatusDescriptionByStatusCode(string statusCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(statusCode))
                    return null;

                var statusDescription = await dbContext.CorrespondenceStatusCodes
                    .Where(sc => sc.StatusCode == statusCode)
                    .Select(sc => sc.StatusDescription)
                    .FirstOrDefaultAsync();

                return statusDescription;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fetching status description for status code {statusCode}: {ex}");
                throw;
            }
        }
        /// <summary>
        /// GetStatusDescriptionsByStatusCodesAsync
        /// </summary>
        /// <param name="statusCodes"></param>
        /// <returns></returns>
        public async Task<Dictionary<string, string>> GetStatusDescriptionsByStatusCodesAsync(IEnumerable<string> statusCodes)
        {
            var codes = statusCodes.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            if (!codes.Any())
                return new Dictionary<string, string>();

            try
            {
                return await dbContext.CorrespondenceStatusCodes
                    .AsNoTracking()
                    .Where(s => codes.Contains(s.StatusCode))
                    .ToDictionaryAsync(s => s.StatusCode, s => s.StatusDescription ?? $"Status: {s.StatusCode}");
            }
            catch (Exception ex)
            {
                //_logger.LogWarning(ex, "Failed to batch-fetch status descriptions, falling back to per-code lookup");
                return new Dictionary<string, string>();
            }
        }
        /// <summary>
        /// GetAllByCorrespondenceIdsAsync
        /// </summary>
        /// <param name="correspondenceIds"></param>
        /// <returns></returns>
        public async Task<Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>> GetAllByCorrespondenceIdsAsync(IEnumerable<Guid> correspondenceIds)
        {
            var ids = correspondenceIds.ToList();
            if (!ids.Any())
                return new Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>();

            try
            {
                var histories = await dbContext.CorrespondenceRequestStatusHistories
                    .AsNoTracking()
                    .Where(h => ids.Contains(h.CorrespondenceId))
                    .OrderBy(h => h.CorrespondenceId)
                    .ThenBy(h => h.HistorySequenceNum)
                    .ToListAsync();

                return histories
                    .GroupBy(h => h.CorrespondenceId)
                    .ToDictionary(g => g.Key, g => g.ToList());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error fetching batch CorrespondenceRequestStatusHistory for {ids.Count} IDs: {ex}");
                throw;
            }
        }
        /// <summary>
        /// LogNonRecoverableErrorAsync
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="errorMessage"></param>
        /// <param name="stackTrace"></param>
        /// <returns></returns>
        public Task LogNonRecoverableErrorAsync(IncomingMessage messageId, string errorMessage, string stackTrace)
        {
            try
            {
                var logEntry = new NonRecoverableErrorLog
                {
                    NonRecoverableErrorLogId = messageId.MessageId,
                    ErrorMessage = errorMessage,
                    StackTrace = stackTrace,
                    ErrorTimestamp = DateTime.UtcNow,
                };
                dbContext.NonRecoverableErrorLogs.Add(logEntry);
                return dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error logging non-recoverable error for MessageId {messageId.MessageId}: {ex}");
                throw;
            }
        }
    }
}
