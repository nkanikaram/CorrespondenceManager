using CustomerCorrespondence.Domain.Entities;
using NServiceBus.Transport;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for managing the history of correspondence request statuses, including retrieval, addition, update, and deletion of status history records. This service allows tracking the changes in the status of correspondence requests over time, providing insights into the lifecycle of each request and enabling error logging for non-recoverable issues.
    /// </summary>
    public interface ICorrespondenceRequestStatusHistoryService
    {
        /// <summary>
        /// Gets all status history records for a specific correspondence request.
        /// </summary>
        /// <param name="correspondenceId">The unique identifier of the correspondence request.</param>
        /// <returns>A list of status history records associated with the specified correspondence request.</returns>
        Task<IEnumerable<CorrespondenceRequestStatusHistory?>> GetAllByCorrespondenceIdAsync(Guid correspondenceId);

        /// <summary>
        /// Gets a specific status history record for a correspondence request based on the history sequence number.
        /// </summary>
        /// <param name="correspondenceId">The unique identifier of the correspondence request.</param>
        /// <param name="historySequenceNum">The sequence number of the history record.</param>
        /// <returns>The status history record associated with the specified correspondence request and sequence number.</returns>
        Task<CorrespondenceRequestStatusHistory?> GetByHistorySequenceNumAsync(Guid correspondenceId, int historySequenceNum);

        /// <summary>
        /// Adds a new status history record for a correspondence request.
        /// </summary>
        /// <param name="correspondenceRequestStatusHistory">The status history record to add.</param>      
        Task AddCorrespondenceRequestStatusHistoryAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory);
        /// <summary>
        /// Updates an existing status history record for a correspondence request. This method allows modifying the details of a status history record, such as the status code, updated by, and update timestamp, providing flexibility in managing the status history of correspondence requests.    
        /// </summary>
        /// <param name="correspondenceRequestStatusHistory"></param>
        /// <returns></returns>
        Task UpdateCorrespondenceRequesStatusHistorytAsync(CorrespondenceRequestStatusHistory correspondenceRequestStatusHistory);
        /// <summary>
        /// Deletes a status history record for a correspondence request.
        /// </summary>
        /// <param name="correspondenceId">The unique identifier of the correspondence request.</param> 
        Task DeleteCorrespondenceRequestStatusHistoryAsync(Guid correspondenceId);

        /// <summary>
        /// Logs a non-recoverable error for a correspondence request. This method allows recording errors that cannot be recovered, providing details such as the message ID, error message, and stack trace.
        /// </summary>
        /// <param name="messageId">The unique identifier of the incoming message.</param>
        /// <param name="errorMessage">The error message describing the non-recoverable error.</param>
        /// <param name="stackTrace">The stack trace of the error.</param>
        Task LogNonRecoverableErrorAsync(IncomingMessage messageId, string errorMessage, string stackTrace);
    }
}
