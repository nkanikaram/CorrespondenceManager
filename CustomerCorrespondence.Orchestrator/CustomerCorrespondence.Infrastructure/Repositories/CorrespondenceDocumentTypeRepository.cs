using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for managing correspondence document type data operations.
    /// </summary>
    public class CorrespondenceDocumentTypeRepository : ICorrespondenceDocumentTypeRepository
    {
        private readonly CorrespondenceDBContext _dbContext;
        private readonly ILogger<CorrespondenceDocumentTypeRepository> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrespondenceDocumentTypeRepository"/> class.
        /// </summary>
        /// <param name="dbContext">The database context.</param>
        /// <param name="logger">The logger instance.</param>
        public CorrespondenceDocumentTypeRepository(
            CorrespondenceDBContext dbContext,
            ILogger<CorrespondenceDocumentTypeRepository> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets a correspondence document type by its document type name.
        /// </summary>
        /// <param name="documentType">The document type name to search for.</param>
        /// <returns>The matching correspondence document type, or null if not found.</returns>
        public async Task<CorrespondenceDocumentType?> GetByDocumentTypeAsync(string documentType)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(documentType))
                {
                    _logger.LogWarning("GetByDocumentTypeAsync called with null or empty documentType");
                    return null;
                }

                _logger.LogDebug("Retrieving CorrespondenceDocumentType for DocumentType: {DocumentType}", documentType);

                var result = await _dbContext.CorrespondenceDocumentTypes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(dt => dt.DocumentType == documentType);

                if (result == null)
                {
                    _logger.LogDebug("No CorrespondenceDocumentType found for DocumentType: {DocumentType}", documentType);
                }
                else
                {
                    _logger.LogDebug("Successfully retrieved CorrespondenceDocumentType for DocumentType: {DocumentType}", documentType);
                }

                return result;
            }
            catch (Exception ex)
            {
                // Sanitize documentType for log output to prevent XSS in log viewers that render HTML
                string? safeDocumentType = System.Net.WebUtility.HtmlEncode(documentType ?? string.Empty);
                _logger.LogError(ex, "Error retrieving CorrespondenceDocumentType for DocumentType: {SafeDocumentType}", safeDocumentType);
                throw;
            }
        }

        /// <summary>
        /// Gets all correspondence document types.
        /// </summary>
        /// <returns>A collection of all correspondence document types.</returns>
        public async Task<IEnumerable<CorrespondenceDocumentType>> GetAllAsync()
        {
            try
            {
                _logger.LogDebug("Retrieving all CorrespondenceDocumentTypes");

                var results = await _dbContext.CorrespondenceDocumentTypes
                    .AsNoTracking()
                    .ToListAsync();

                _logger.LogInformation("Successfully retrieved {Count} CorrespondenceDocumentTypes", results.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all CorrespondenceDocumentTypes");
                throw;
            }
        }

        /// <summary>
        /// Gets a correspondence document type by its identifier.
        /// </summary>
        /// <param name="id">The correspondence document type identifier.</param>
        /// <returns>The matching correspondence document type, or null if not found.</returns>
        public async Task<CorrespondenceDocumentType?> GetByIdAsync(int id)
        {
            try
            {
                _logger.LogDebug("Retrieving CorrespondenceDocumentType for Id: {Id}", id);

                var result = await _dbContext.CorrespondenceDocumentTypes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(dt => dt.CorrespondenceDocumentTypeId == id);

                if (result == null)
                {
                    _logger.LogDebug("No CorrespondenceDocumentType found for Id: {Id}", id);
                }
                else
                {
                    _logger.LogDebug("Successfully retrieved CorrespondenceDocumentType for Id: {Id}", id);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving CorrespondenceDocumentType for Id: {Id}", id);
                throw;
            }
        }
    }
}