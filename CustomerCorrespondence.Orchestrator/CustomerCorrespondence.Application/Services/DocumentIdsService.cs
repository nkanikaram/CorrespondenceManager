using AutoMapper;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Application.Services
{
    public class DocumentIdsService : IDocumentIdsService
    {
        private readonly IDocumentIdsRepository _documentIdRepository;
        private readonly ILogger<DocumentIdsService> _logger;

        public DocumentIdsService(IDocumentIdsRepository documentIdRepository, ILogger<DocumentIdsService> logger)
        {
            _documentIdRepository = documentIdRepository;
            _logger = logger;
        }
        public async Task AddDocumentIdAsync(string docId)
        {
            try
            {
                _logger.LogInformation("Adding DocumentId with DocId: {DocId}", docId);
                var documentId = new DocumentIdEntity { DocIds = docId };
                await _documentIdRepository.AddDocumentIdAsync(docId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding DocumentId with DocId: {DocId}", docId);
                throw;
            }
        }

        public async Task<string?> GetAndUpdateDocumentIdAsync()
        {
            try
            {
                _logger.LogInformation("Atomically claiming next available DocumentId");
                return await _documentIdRepository.GetAndUpdateDocumentIdAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error claiming next DocumentId");
                throw;
            }
        }

        public async Task<int> GetNewDocumentIdCountAsync()
        {
            try
            {
                _logger.LogInformation("Fetching new DocumentId count");
                return await _documentIdRepository.GetNewDocumentIdCountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching new DocumentId count");
                throw;
            }
        }

        public async Task RevertDocumentIdAsync(string docId)
        {
            try
            {
                _logger.LogInformation("Reverting DocumentId with DocId: {DocId}", docId);
                await _documentIdRepository.RevertDocumentIdAsync(docId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reverting DocumentId with DocId: {DocId}", docId);
                throw;
            }
        }

    }
}
