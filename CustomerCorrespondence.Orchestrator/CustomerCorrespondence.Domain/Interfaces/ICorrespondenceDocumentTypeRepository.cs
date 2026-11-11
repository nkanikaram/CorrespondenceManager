using CustomerCorrespondence.Domain.Entities;

namespace CustomerCorrespondence.Domain.Interfaces
{
    public interface ICorrespondenceDocumentTypeRepository
    {
        Task<CorrespondenceDocumentType?> GetByDocumentTypeAsync(string documentType);
        Task<IEnumerable<CorrespondenceDocumentType>> GetAllAsync();
        Task<CorrespondenceDocumentType?> GetByIdAsync(int id);
    }
}
