using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Interface for a service that manages document IDs for customer correspondence. This service is responsible for tracking the status of document IDs (e.g., new, used) and providing functionality to add new IDs, retrieve and update existing IDs, and count available new IDs. It also includes a method to revert a document ID's status in case of rollbacks, ensuring that the system can maintain accurate tracking of document ID usage.
    /// </summary>
    public interface IDocumentIdsService
    {
        /// <summary>
        /// Adds a new document ID to the system.
        /// </summary>
        /// <param name="documentId">The document ID to add.</param>    
        Task AddDocumentIdAsync(string documentId);

        /// <summary>
        /// Gets a new document ID from the system and updates its status to 'Used'. This method retrieves an available document ID that is currently marked as 'New', updates its status to 'Used' to indicate that it has been assigned, and returns the document ID for use in customer correspondence. If no new document IDs are available, it may return null or throw an exception based on the implementation.
        /// </summary>
        /// <returns>The document ID that was retrieved and updated, or null if none are available.</returns>
        Task<string?> GetAndUpdateDocumentIdAsync();

        /// <summary>
        /// Gets the count of new document IDs available in the system.
        /// </summary>
        /// <returns>The count of new document IDs.</returns>
        Task<int> GetNewDocumentIdCountAsync();

        /// <summary>
        /// Reverts a DocId status from 'Used' back to 'New' for rollback scenarios.
        /// </summary>
        Task RevertDocumentIdAsync(string documentId);
    }
}
