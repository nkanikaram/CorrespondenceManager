using CustomerCorrespondence.Domain.Entities;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for managing correspondence requests, including retrieval, addition, update, and deletion of correspondence request records.
    /// </summary>
    public interface ICorrespondenceRequestService
    {
        /// <summary>
        /// Gets a list of correspondence requests based on the provided document ID. This method allows retrieval of all correspondence requests associated with a specific document, identified by its unique identifier (docId).
        /// </summary>
        /// <param name="docId">The unique identifier of the document.</param>
        /// <returns>A list of correspondence requests associated with the specified document ID.</returns>
        Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid docId);

        /// <summary>
        /// Gets a list of correspondence requests based on the provided document ID. This method allows retrieval of all correspondence requests associated with a specific document, identified by its unique identifier (docId).
        /// </summary>
        /// <param name="docId">The unique identifier of the document.</param>
        /// <returns>A list of correspondence requests associated with the specified document ID.</returns>
        Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByDocIdAsync(string docId);

        /// <summary>
        /// Adds a new correspondence request to the system.
        /// </summary>
        /// <param name="correspondenceRequest">The correspondence request to add.</param>  
        Task AddCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest);

        /// <summary>
        /// Updates an existing correspondence request in the system. This method allows modification of the details of an existing correspondence request, such as its delivery method, version code, or other relevant information. The update operation typically requires the unique identifier of the correspondence request to ensure that the correct record is modified.
        /// </summary>
        /// <param name="correspondenceRequest">The correspondence request to update.</param>
        /// <returns>A task representing the asynchronous update operation.</returns>
        Task UpdateCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest);

        /// <summary>
        /// Deletes an existing correspondence request from the system. This method allows removal of a correspondence request based on its unique identifier.
        /// </summary>
        /// <param name="correspondenceId">The unique identifier of the correspondence request to delete.</param>
        /// <returns>A task representing the asynchronous delete operation.</returns>   
        Task DeleteCorrespondenceRequestAsync(Guid correspondenceId);

        /// <summary>
        /// Gets a list of correspondence requests filtered by document type. Retrieves all correspondence requests 
        /// where ArchiveDocumentTypeCode matches the specified document type from the CorrespondenceDocumentType master table.
        /// </summary>
        /// <param name="documentType">The document type to filter by.</param>
        /// <returns>A list of correspondence requests associated with the specified document type.</returns>
        Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestsByDocumentTypeAsync(string documentType);
    }
}
