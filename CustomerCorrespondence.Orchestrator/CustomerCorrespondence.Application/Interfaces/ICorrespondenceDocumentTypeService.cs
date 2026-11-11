using CustomerCorrespondence.Domain.Entities;

namespace CustomerCorrespondence.Application.Interfaces
{
    public interface ICorrespondenceDocumentTypeService
    {
        /// <summary>
        /// GetDocumentTypeInfoAsync retrieves detailed information about a specific correspondence document type based on the provided document type identifier. 
        /// This method allows users to obtain metadata and configuration details associated with a particular document type, 
        /// which can be essential for processing and handling correspondences correctly within the system.
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        Task<CorrespondenceDocumentType?> GetDocumentTypeInfoAsync(string documentType);
        /// <summary>
        /// GetAllDocumentTypesAsync retrieves a list of all correspondence document types available in the system. 
        /// Each item in the list includes details such as the document type, sub-type, and associated cabinet name 
        /// if applicable.
        /// </summary>
        /// <returns></returns>
        Task<IEnumerable<CorrespondenceDocumentType>> GetAllDocumentTypesAsync();
        /// <summary>
        /// Checks if any document type contains the specified search term (case-insensitive partial match).
        /// </summary>
        /// <param name="searchTerm">The search term to match against document types.</param>
        /// <returns>True if any document type contains the search term; otherwise false.</returns>
        Task<bool> DocumentTypeExistsPartialAsync(string searchTerm);
    }
}
