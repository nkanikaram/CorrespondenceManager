using CustomerCorrespondence.Domain.Entities;
using NServiceBus.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Domain.Interfaces
{
    public interface IDocumentIdsRepository
    {
        Task AddDocumentIdAsync(string documentId);
        Task<string?> GetAndUpdateDocumentIdAsync();
        Task<int> GetNewDocumentIdCountAsync();
        Task RevertDocumentIdAsync(string documentId);

    }
}
