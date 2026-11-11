using CustomerCorrespondence.Domain.DTOs;
using CustomerCorrespondence.Domain.Entities;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for managing correspondence status information, including retrieval of current status based on various identifiers such as document ID, correspondence ID, policy number, and claim number. This service allows tracking the current state of correspondences and provides insights into their lifecycle and delivery status.
    /// </summary>
    public interface ICorrespondenceStatusService
    {
        /// <summary>
        /// Gets the current status of a correspondence based on the provided document ID. This method retrieves the latest status information for the correspondence associated with the specified document ID, allowing users to track the progress and delivery status of their correspondences effectively.
        /// </summary>
        /// <param name="docId">The unique identifier of the document.</param>
        /// <returns>A <see cref="CorrespondenceStatusResponse"/> containing the current status of the correspondence, or null if not found.</returns>
        Task<CorrespondenceStatusResponse?> GetCorrespondenceStatusByDocIdAsync(string docId);

        /// <summary>
        /// Gets the current status of a correspondence based on the provided correspondence ID.
        /// </summary>
        /// <param name="correspondenceId">The unique identifier of the correspondence.</param>
        /// <returns>A <see cref="CorrespondenceStatusResponse"/> containing the current status of the correspondence, or null if not found.</returns>  
        Task<CorrespondenceStatusResponse?> GetCorrespondenceStatusByCorrespondenceIdAsync(Guid correspondenceId);

        /// <summary>
        /// Gets the current status of correspondences based on the provided policy number. This method retrieves a list of correspondences associated with the specified policy number, allowing users to track the progress and delivery status of all correspondences related to that policy effectively.
        /// </summary>
        /// <param name="policyNumber">The policy number associated with the correspondences.</param>
        /// <returns>A list of <see cref="CorrespondenceStatusResponse"/> containing the current status of the correspondences, or an empty list if none are found.</returns>
        Task<IEnumerable<CorrespondenceStatusResponse>> GetCorrespondenceStatusByPolicyNumberAsync(string policyNumber);

        /// <summary>
        /// Gets the current status of correspondences based on the provided claim number. This method retrieves a list of correspondences associated with the specified claim number, allowing users to track the progress and delivery status of all correspondences related to that claim effectively.
        /// </summary>
        /// <param name="claimNumber">The claim number associated with the correspondences.</param>
        /// <param name="pageNumber">The page number for pagination.</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <returns>A <see cref="PaginatedResponse{T}"/> of <see cref="CorrespondenceStatusResponse"/> containing the current status of the correspondences.</returns>
        //Task<IEnumerable<CorrespondenceStatusResponse>> GetCorrespondenceStatusByClaimNumberAsync(string claimNumber);
        Task<PaginatedResponse<CorrespondenceStatusResponse>> GetCorrespondenceStatusByClaimNumberAsync(string claimNumber, int pageNumber, int pageSize);
        /// <summary>
        /// Gets the current status of correspondences based on the provided document types (paginated).
        /// </summary>
        /// <param name="documentTypes">The document types to filter by.</param>
        /// <param name="pageNumber">The page number for pagination.</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <returns>A <see cref="PaginatedResponse{T}"/> of <see cref="CorrespondenceStatusResponse"/> containing the current status of the correspondences.</returns>
        Task<PaginatedResponse<CorrespondenceStatusResponse>> GetCorrespondenceStatusByDocumentTypesAsync(IEnumerable<string> documentTypes, int pageNumber, int pageSize);
    }
}
