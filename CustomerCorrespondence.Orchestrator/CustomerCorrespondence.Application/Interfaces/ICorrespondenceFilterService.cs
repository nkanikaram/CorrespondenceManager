using CustomerCorrespondence.Domain.DTOs;

namespace CustomerCorrespondence.Application.Interfaces
{
    /// <summary>
    /// Service interface for filtering correspondence data based on search criteria.
    /// </summary>
    public interface ICorrespondenceFilterService
    {
        /// <summary>
        /// Retrieves filtered correspondence data based on the provided search criteria.
        /// </summary>
        /// <param name="request">The filter criteria including system name, document type, delivery method, status code, date range, and optional policy number.</param>
        /// <returns>A collection of filtered correspondence records with current status information.</returns>
        Task<IEnumerable<CorrespondenceFilterResponse>> GetFilteredCorrespondenceAsync(CorrespondenceFilterRequest request);
    }
}
