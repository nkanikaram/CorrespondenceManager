using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for a service that generates unique document IDs for customer correspondence. This allows for consistent ID generation across the application and can be implemented using various strategies (e.g., GUIDs, sequential numbers, etc.) depending on requirements.
    /// </summary>
    public interface IDocumentIdGeneratorService
    {
        /// <summary>
        /// Generates a unique document ID for customer correspondence.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. The task result contains the generated document ID, or null if the generation fails.</returns>
        Task<string?> GenerateDocIdAsync();
    }
}
