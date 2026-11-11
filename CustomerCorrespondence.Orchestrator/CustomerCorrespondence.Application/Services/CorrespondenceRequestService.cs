using AutoMapper;
using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Application.Services
{
    public class CorrespondenceRequestService : ICorrespondenceRequestService
    {
        private readonly ICorrespondenceRequestRepository _correspondenceRequestRepository;
        private readonly ILogger<CorrespondenceRequestService> _logger;

        public CorrespondenceRequestService(ICorrespondenceRequestRepository correspondenceRequestRepository,ILogger<CorrespondenceRequestService> logger)
        {
            _correspondenceRequestRepository = correspondenceRequestRepository;
            _logger = logger;
        }
        public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid corrId)
        {
            try
            {
                _logger.LogInformation("Fetching CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", corrId);
                return await _correspondenceRequestRepository.GetCorrespondenceRequestByCorrIdAsync(corrId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", corrId);
                throw;
            }
        }
        public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByDocIdAsync(string docId)
        {
            try
            {
                _logger.LogInformation("Fetching CorrespondenceRequest with DocId: {DocId}", docId);
                return await _correspondenceRequestRepository.GetCorrespondenceRequestByDocIdAsync(docId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching CorrespondenceRequest with DocId: {DocId}", docId);
                throw;
            }
        }

        public async Task AddCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest)
        {
            try
            {
                _logger.LogInformation("Adding new CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", correspondenceRequest.CorrespondenceId);
                await _correspondenceRequestRepository.AddCorrespondenceRequestAsync(correspondenceRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", correspondenceRequest.CorrespondenceId);
                throw;
            }
        }

        public async Task UpdateCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest)
        {
            try
            {
                _logger.LogInformation("Updating CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", correspondenceRequest.CorrespondenceId);
                await _correspondenceRequestRepository.UpdateCorrespondenceRequestAsync(correspondenceRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", correspondenceRequest.CorrespondenceId);
                throw;
            }
        }

        public async Task DeleteCorrespondenceRequestAsync(Guid correspondenceId)
        {
            try
            {
                _logger.LogInformation("Deleting CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", correspondenceId);
                await _correspondenceRequestRepository.DeleteCorrespondenceRequestForCorrIdAsync(correspondenceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting CorrespondenceRequest with CorrespondenceId: {CorrespondenceId}", correspondenceId);
                throw;
            }
        }

        public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestsByDocumentTypeAsync(string documentType)
        {
            try
            {
                _logger.LogInformation("Fetching CorrespondenceRequest with DocumentType: {DocumentType}", documentType);
                return await _correspondenceRequestRepository.GetCorrespondenceRequestsByDocumentTypeAsync(documentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching CorrespondenceRequest with DocumentType: {DocumentType}", documentType);
                throw;
            }
        }
    }
}
