using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerCorrespondence.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for managing correspondence request data operations.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public class CorrespondenceRequestRepository(CorrespondenceDBContext dbContext) : ICorrespondenceRequestRepository
    {
        /// <summary>
        /// AddCorrespondenceRequestAsync
        /// </summary>
        /// <param name="correspondenceRequest"></param>
        /// <returns></returns>
        public async Task AddCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest)
        {
            try
            {
                await dbContext.CorrespondenceRequests.AddAsync(correspondenceRequest);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log error here if you have a logger, or rethrow
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error adding CorrespondenceRequest: " + exmsg);
                throw;
            }
        }

        /// <summary>
        /// DeleteCorrespondenceRequestForCorrIdAsync
        /// </summary>
        /// <param name="correspondenceId"></param>
        /// <returns></returns>
        public async Task DeleteCorrespondenceRequestForCorrIdAsync(Guid correspondenceId)
        {
            try
            {
                var correspondenceRequest = await dbContext.CorrespondenceRequests.FindAsync(correspondenceId);
                if (correspondenceRequest != null)
                {
                    dbContext.CorrespondenceRequests.Remove(correspondenceRequest);
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                string? safeCorrId = System.Net.WebUtility.HtmlEncode(correspondenceId.ToString() ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error deleting CorrespondenceRequest by CorrId " + safeCorrId + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// DeleteCorrespondenceRequestForDocIdAsync
        /// </summary>
        /// <param name="docId"></param>
        /// <returns></returns>
        public async Task DeleteCorrespondenceRequestForDocIdAsync(string docId)
        {
            try
            {
                var correspondenceRequest = await dbContext.CorrespondenceRequests
                    .FirstOrDefaultAsync(cr => cr.DocId == docId);
                if (correspondenceRequest != null)
                {
                    dbContext.CorrespondenceRequests.Remove(correspondenceRequest);
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // Sanitize docId for log output to prevent XSS in log viewers that render HTML
                string? safeDocId = System.Net.WebUtility.HtmlEncode(docId ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error deleting CorrespondenceRequest by DocId " + safeDocId + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceRequestByCorrIdAsync
        /// </summary>
        /// <param name="correspondenceId"></param>
        /// <returns></returns>
        public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByCorrIdAsync(Guid correspondenceId)
        {
            try
            {
                return await dbContext.CorrespondenceRequests
                    .Where(requests => requests.CorrespondenceId == correspondenceId)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                // Sanitize docId for log output to prevent XSS in log viewers that render HTML
                string? safeCorrId = System.Net.WebUtility.HtmlEncode(correspondenceId.ToString() ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching CorrespondenceRequest by CorrId " + safeCorrId + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceRequestByDocIdAsync
        /// </summary>
        /// <param name="docId"></param>
        /// <returns></returns>
        public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByDocIdAsync(string docId)
        {
            try
            {
                // Only use sanitized docId for logging, not for querying
                return await dbContext.CorrespondenceRequests
                    .Where(requests => requests.DocId != null && requests.DocId.Equals(docId))
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                // Sanitize docId for log output to prevent XSS in log viewers that render HTML
                string? safeDocId = System.Net.WebUtility.HtmlEncode(docId ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching CorrespondenceRequest by DocId " + safeDocId + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceIdByDocIdAsync
        /// </summary>
        /// <param name="docId"></param>
        /// <returns></returns>
        public async Task<Guid> GetCorrespondenceIdByDocIdAsync(string docId)
        {
            try
            {
                Guid correspondenceId = await dbContext.CorrespondenceRequests
                    .Where(h => h.DocId == docId)
                    .Select(h => h.CorrespondenceId)
                    .FirstOrDefaultAsync();

                // Guid is a value type and cannot be null, so check for Guid.Empty
                return correspondenceId != Guid.Empty ? correspondenceId : Guid.Empty;
            }
            catch (Exception ex)
            {
                // Sanitize docId for log output to prevent XSS in log viewers that render HTML
                string? safeDocId = System.Net.WebUtility.HtmlEncode(docId ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching CorrespondenceId for DocId " + safeDocId + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// UpdateCorrespondenceRequestAsync
        /// </summary>
        /// <param name="correspondenceRequest"></param>
        /// <returns></returns>
        public async Task UpdateCorrespondenceRequestAsync(CorrespondenceRequest correspondenceRequest)
        {
            try
            {
                correspondenceRequest.Validate();
                dbContext.CorrespondenceRequests.Update(correspondenceRequest);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Sanitize correspondenceId for log output to prevent XSS in log viewers that render HTML
                string? safeCorrId = System.Net.WebUtility.HtmlEncode(correspondenceRequest.CorrespondenceId.ToString());
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error updating CorrespondenceRequest with CorrId " + safeCorrId + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceRequestByPolicyNumberHashAsync
        /// </summary>
        /// <param name="policyNumberHash"></param>
        /// <returns></returns>
        public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestByPolicyNumberHashAsync(byte[] policyNumberHash)
        {
            try
            {
                return await dbContext.CorrespondenceRequests
                    .Where(requests => requests.PolicyNumberHash != null && requests.PolicyNumberHash.SequenceEqual(policyNumberHash))
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                // Sanitize policyNumberHash for log output to prevent XSS in log viewers that render HTML
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching CorrespondenceRequest by PolicyNumberHash: " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceRequestByClaimNumberAsync
        /// </summary>
        /// <param name="claimNumber"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public async Task<(IEnumerable<CorrespondenceRequest> Items, int TotalCount)> GetCorrespondenceRequestByClaimNumberAsync(string claimNumber, int pageNumber, int pageSize)
        {
            try
            {
                var query = dbContext.CorrespondenceRequests
                    .AsNoTracking()
                    .Where(requests => requests.ClaimNumber != null && requests.ClaimNumber.Equals(claimNumber));

                var totalCount = await query.CountAsync();

                var items = await query
                    .OrderByDescending(r => r.RequestCreationTS)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return (items, totalCount);
            }
            catch (Exception ex)
            {
                string? safeClaimNumber = System.Net.WebUtility.HtmlEncode(claimNumber ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching CorrespondenceRequest by ClaimNumber " + safeClaimNumber + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// Gets correspondence requests filtered by multiple document types with pagination support.
        /// Uses partial matching - matches if DocumentType contains any of the search terms.
        /// </summary>
        public async Task<(IEnumerable<CorrespondenceRequest> Items, int TotalCount)> GetCorrespondenceRequestsByDocumentTypesAsync(IEnumerable<string> documentTypes, int pageNumber, int pageSize)
        {
            try
            {
                var documentTypeList = documentTypes.ToList();

                // Build a base query joining CorrespondenceRequests with CorrespondenceDocumentTypes
                var baseQuery = from cr in dbContext.CorrespondenceRequests.AsNoTracking()
                                join dt in dbContext.CorrespondenceDocumentTypes
                                    on cr.ArchiveDocumentTypeCode equals dt.DocumentType
                                select new { cr, dt };

                // Build OR predicate: dt.DocumentType LIKE '%term1%' OR dt.DocumentType LIKE '%term2%' ...
                IQueryable<CorrespondenceRequest>? combinedQuery = null;
                foreach (var searchTerm in documentTypeList)
                {
                    var termQuery = baseQuery
                        .Where(x => EF.Functions.Like(x.dt.DocumentType, $"%{searchTerm}%"))
                        .Select(x => x.cr);

                    combinedQuery = combinedQuery == null
                        ? termQuery
                        : combinedQuery.Union(termQuery);
                }

                if (combinedQuery == null)
                {
                    return (Enumerable.Empty<CorrespondenceRequest>(), 0);
                }

                var orderedQuery = combinedQuery.OrderByDescending(cr => cr.RequestCreationTS);

                var totalCount = await orderedQuery.CountAsync();
                var items = await orderedQuery
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return (items, totalCount);
            }
            catch (Exception ex)
            {
                string? safeDocumentTypes = System.Net.WebUtility.HtmlEncode(string.Join(", ", documentTypes ?? Enumerable.Empty<string>()));
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching paginated CorrespondenceRequest by DocumentTypes " + safeDocumentTypes + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// Gets correspondence requests filtered by document type with pagination support.
        /// Uses partial matching - matches if DocumentType contains the search term.
        /// </summary>
        public async Task<(IEnumerable<CorrespondenceRequest> Items, int TotalCount)> GetCorrespondenceRequestsByDocumentTypeAsync(string documentType, int pageNumber, int pageSize)
        {
            try
            {
                var query = from cr in dbContext.CorrespondenceRequests.AsNoTracking()
                            join dt in dbContext.CorrespondenceDocumentTypes
                                on cr.ArchiveDocumentTypeCode equals dt.DocumentType
                            where EF.Functions.Like(dt.DocumentType, $"%{documentType}%")
                            orderby cr.RequestCreationTS descending
                            select cr;

                var totalCount = await query.CountAsync();
                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return (items, totalCount);
            }
            catch (Exception ex)
            {
                string? safeDocumentType = System.Net.WebUtility.HtmlEncode(documentType ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching paginated CorrespondenceRequest by DocumentType " + safeDocumentType + ": " + exmsg);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceRequestsByDocumentTypeAsync
        /// Uses partial matching - matches if DocumentType contains the search term.
        /// </summary>
        public async Task<IEnumerable<CorrespondenceRequest>> GetCorrespondenceRequestsByDocumentTypeAsync(string documentType)
        {
            try
            {
                return await (from cr in dbContext.CorrespondenceRequests
                              join dt in dbContext.CorrespondenceDocumentTypes
                                  on cr.ArchiveDocumentTypeCode equals dt.DocumentType
                              where EF.Functions.Like(dt.DocumentType, $"%{documentType}%")
                              select cr)
                              .ToListAsync();
            }
            catch (Exception ex)
            {
                string? safeDocumentType = System.Net.WebUtility.HtmlEncode(documentType ?? string.Empty);
                string? exmsg = System.Net.WebUtility.HtmlEncode(ex.Message);
                Console.Error.WriteLine($"Error fetching CorrespondenceRequest by DocumentType " + safeDocumentType + ": " + exmsg);
                throw;
            }
        }
    }
}
