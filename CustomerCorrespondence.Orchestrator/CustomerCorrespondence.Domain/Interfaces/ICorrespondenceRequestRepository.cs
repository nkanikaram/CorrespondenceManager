using CustomerCorrespondence.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Interfaces
{
    /// <summary>
    /// Repository interface for managing correspondence request data operations.
    /// </summary>
    public interface ICorrespondenceRequestRepository
    {
        /// <summary>
        /// Gets correspondence requests by correspondence ID.
        /// </summary>
        /// <param name="CorrespondenceId">The correspondence ID to search for.</param>
        /// <returns>A collection of matching correspondence requests.</returns>
        Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid CorrespondenceId);

        /// <summary>
        /// Gets correspondence requests by document ID.
        /// </summary>
        /// <param name="docId">The document ID to search for.</param>
        /// <returns>A collection of matching correspondence requests.</returns>
        Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByDocIdAsync(string docId);

        /// <summary>
        /// Adds a new correspondence request.
        /// </summary>
        /// <param name="correspondenceRequest">The correspondence request to add.</param>
        Task AddCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest);

        /// <summary>
        /// Updates an existing correspondence request.
        /// </summary>
        /// <param name="correspondenceRequest">The correspondence request to update.</param>
        Task UpdateCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest);

        /// <summary>
        /// Deletes a correspondence request by correspondence ID.
        /// </summary>
        /// <param name="correspondenceId">The correspondence ID to delete.</param>
        Task DeleteCorrespondenceRequestForCorrIdAsync(Guid correspondenceId);

        /// <summary>
        /// Deletes a correspondence request by document ID.
        /// </summary>
        /// <param name="docId">The document ID to delete.</param>
        Task DeleteCorrespondenceRequestForDocIdAsync(string docId);

        /// <summary>
        /// Gets the correspondence ID associated with a document ID.
        /// </summary>
        /// <param name="docId">The document ID to look up.</param>
        /// <returns>The corresponding correspondence ID.</returns>
        Task<Guid> GetCorrespondenceIdByDocIdAsync(string docId);

        /// <summary>
        /// Gets correspondence requests by policy number hash.
        /// </summary>
        /// <param name="policyNumberHash">The hashed policy number to search for.</param>
        /// <returns>A collection of matching correspondence requests.</returns>
        Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByPolicyNumberHashAsync(byte[] policyNumberHash);
        //Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByClaimNumberAsync(string claimNumber);

        /// <summary>
        /// Gets correspondence requests filtered by document type from CorrespondenceDocumentType table.
        /// Only returns requests where ArchiveDocumentTypeCode matches a valid DocumentType.
        /// </summary>
        /// <param name="documentType">The document type to filter by.</param>
        /// <returns>A collection of correspondence requests matching the document type.</returns>
        Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestsByDocumentTypeAsync(string documentType);
        /// <summary>
        /// Gets correspondence requests filtered by claim number with pagination support.
        /// </summary>
        /// <param name="claimNumber">The claim number to filter by.</param>
        /// <param name="pageNumber">The page number for pagination.</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <returns>A tuple containing the collection of correspondence requests and the total count.</returns>
        Task<(IEnumerable<CorrespondenceRequest> Items, int TotalCount)> GetCorrespondenceRequestByClaimNumberAsync(string claimNumber, int pageNumber, int pageSize);
        /// <summary>
        /// Gets correspondence requests filtered by multiple document types with pagination support.
        /// </summary>
        /// <param name="documentTypes">The document types to filter by.</param>
        /// <param name="pageNumber">The page number for pagination.</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <returns>A tuple containing the collection of correspondence requests and the total count.</returns>
        Task<(IEnumerable<CorrespondenceRequest> Items, int TotalCount)> GetCorrespondenceRequestsByDocumentTypesAsync(IEnumerable<string> documentTypes, int pageNumber, int pageSize);
    }
}
