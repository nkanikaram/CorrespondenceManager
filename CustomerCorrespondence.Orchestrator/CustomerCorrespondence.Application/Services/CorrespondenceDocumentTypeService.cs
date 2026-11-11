using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Application.Services
{
    /// <summary>
    /// Service for managing correspondence document type operations.
    /// </summary>
    public class CorrespondenceDocumentTypeService : ICorrespondenceDocumentTypeService
    {
        private readonly ICorrespondenceDocumentTypeRepository _repository;
        private readonly ILogger<CorrespondenceDocumentTypeService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrespondenceDocumentTypeService"/> class.
        /// </summary>
        /// <param name="repository">The correspondence document type repository.</param>
        /// <param name="logger">The logger instance.</param>
        public CorrespondenceDocumentTypeService(
            ICorrespondenceDocumentTypeRepository repository,
            ILogger<CorrespondenceDocumentTypeService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        /// <summary>
        /// GetDocumentTypeInfoAsync
        /// </summary>
        /// <param name="documentType"></param>
        /// <returns></returns>
        public async Task<CorrespondenceDocumentType?> GetDocumentTypeInfoAsync(string documentType)
        {
            try
            {
                _logger.LogDebug("Getting document type info for: {DocumentType}", documentType);
                return await _repository.GetByDocumentTypeAsync(documentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting document type info for: {DocumentType}", documentType);
                throw;
            }
        }
        /// <summary>
        /// GetAllDocumentTypesAsync
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<CorrespondenceDocumentType>> GetAllDocumentTypesAsync()
        {
            try
            {
                _logger.LogDebug("Getting all document types");
                return await _repository.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all document types");
                throw;
            }
        }
        /// <summary>
        /// Checks if any document type contains the specified search term (case-insensitive).
        /// </summary>
        public async Task<bool> DocumentTypeExistsPartialAsync(string searchTerm)
        {
            var allTypes = await GetAllDocumentTypesAsync();
            return allTypes.Any(dt => dt.DocumentType != null &&
                dt.DocumentType.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }
    }
}