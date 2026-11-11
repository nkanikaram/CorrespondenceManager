using CustomerCorrespondence.Domain.Entities;
using NServiceBus.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Interfaces
{
    /// <summary>
    /// Repository interface for managing correspondence request status history data operations.
    /// </summary>
    public interface ICorrespondenceRequestStatusHistoryRepository
    {
        /// <summary>
        /// Gets all status history entries for a given correspondence ID.
        /// </summary>
        /// <param name="correspondenceId">The correspondence ID.</param>
        /// <returns>A collection of status history entries.</returns>
        Task<IEnumerable<CorrespondenceRequestStatusHistory>> GetAllByCorrespondenceIdAsync(Guid correspondenceId);

        /// <summary>
        /// Gets a specific status history entry by correspondence ID and sequence number.
        /// </summary>
        /// <param name="correspondenceId">The correspondence ID.</param>
        /// <param name="historySequenceNum">The history sequence number.</param>
        /// <returns>The matching status history entry, or null if not found.</returns>
        Task<CorrespondenceRequestStatusHistory?> GetStatusHistoryByCorrIdSeqNumAsync(Guid correspondenceId, int historySequenceNum);

        /// <summary>
        /// Gets the maximum sequence number for a given correspondence ID.
        /// </summary>
        /// <param name="correspondenceId">The correspondence ID.</param>
        /// <returns>The maximum sequence number.</returns>
        Task<int> GetMaxSequenceNumber(Guid correspondenceId);

        /// <summary>
        /// Gets the status code associated with a status name.
        /// </summary>
        /// <param name="statusName">The status name to look up.</param>
        /// <returns>The corresponding status code.</returns>
        Task<string> GetStatusCodeByStatus(string statusName);

        /// <summary>
        /// Adds a new correspondence request status history entry.
        /// </summary>
        /// <param name="correspondenceRequestStatusHistory">The status history entry to add.</param>
        Task AddCorrespondenceRequestStatusHistoryAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory);

        /// <summary>
        /// Updates an existing correspondence request status history entry.
        /// </summary>
        /// <param name="correspondenceRequestStatusHistory">The status history entry to update.</param>
        Task UpdateCorrespondenceRequesStatusHistorytAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory);

        /// <summary>
        /// Deletes all status history entries for a given correspondence ID.
        /// </summary>
        /// <param name="correspondenceId">The correspondence ID.</param>
        Task DeleteCorrespondenceRequestStatusHistoryAsync(Guid correspondenceId);

        /// <summary>
        /// Gets the status description for a given status code.
        /// </summary>
        /// <param name="statusCode">The status code to look up.</param>
        /// <returns>The status description, or null if not found.</returns>
        Task<string?> GetStatusDescriptionByStatusCode(string statusCode);

        /// <summary>
        /// Gets status descriptions for multiple status codes.
        /// </summary>
        /// <param name="statusCodes">The status codes to look up.</param>
        /// <returns>A dictionary mapping status codes to their descriptions.</returns>
        Task<Dictionary<string, string>> GetStatusDescriptionsByStatusCodesAsync(IEnumerable<string> statusCodes);

        /// <summary>
        /// Logs a non-recoverable error for a message.
        /// </summary>
        /// <param name="messageId">The incoming message associated with the error.</param>
        /// <param name="errorMessage">The error message.</param>
        /// <param name="stackTrace">The stack trace of the error.</param>
        Task LogNonRecoverableErrorAsync(IncomingMessage messageId, string errorMessage, string stackTrace);
        /// <summary>
        /// Gets all status history entries for multiple correspondence IDs in a single query.
        /// </summary>
        /// <param name="correspondenceIds">The correspondence IDs to look up.</param>
        /// <returns>A dictionary mapping each correspondence ID to its status history entries.</returns>
        Task<Dictionary<Guid, List<CorrespondenceRequestStatusHistory>>> GetAllByCorrespondenceIdsAsync(IEnumerable<Guid> correspondenceIds);
    }
}
