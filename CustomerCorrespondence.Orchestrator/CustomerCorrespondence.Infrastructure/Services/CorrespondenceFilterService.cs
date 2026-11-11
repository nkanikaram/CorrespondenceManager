using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.DTOs;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Infrastructure.Services
{
    /// <summary>
    /// Service implementation for filtering correspondence data based on search criteria.
    /// Optimized to minimize database calls by batching status history and status description lookups.
    /// </summary>
    public class CorrespondenceFilterService : ICorrespondenceFilterService
    {
        private readonly CorrespondenceDBContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly ICorrespondenceRequestStatusHistoryRepository _statusHistoryRepo;
        private readonly ILogger<CorrespondenceFilterService> _logger;

        /// <summary>
        /// Initializes a new instance of the CorrespondenceFilterService.
        /// </summary>
        /// <param name="context">Database context for data access.</param>
        /// <param name="encryptionService">Service for hashing policy numbers.</param>
        /// <param name="statusHistoryRepo">Repository for status history data.</param>
        /// <param name="logger">Logger for service operations.</param>
        public CorrespondenceFilterService(
            CorrespondenceDBContext context,
            IEncryptionService encryptionService,
            ICorrespondenceRequestStatusHistoryRepository statusHistoryRepo,
            ILogger<CorrespondenceFilterService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _statusHistoryRepo = statusHistoryRepo ?? throw new ArgumentNullException(nameof(statusHistoryRepo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves filtered correspondence data based on the provided search criteria.
        /// </summary>
        /// <param name="request">The filter criteria including system name, document type, delivery method, status code, date range, and optional policy number.</param>
        /// <returns>A collection of filtered correspondence records with current status information.</returns>
        public async Task<IEnumerable<CorrespondenceFilterResponse>> GetFilteredCorrespondenceAsync(CorrespondenceFilterRequest request)
        {
            try
            {
                _logger.LogInformation("Getting filtered correspondence data");

                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                try
                {
                    request.RequestingSystemName = request.RequestingSystemName?.Trim() ?? string.Empty;
                    request.DocumentDescription = request.DocumentDescription?.Trim();
                    request.DeliveryMethod = request.DeliveryMethod?.Trim();
                    request.CorrespondenceRequestStatusCode = request.CorrespondenceRequestStatusCode?.Trim();
                    request.CreateBeginDate = request.CreateBeginDate?.Trim() ?? string.Empty;
                    request.CreateEndDate = request.CreateEndDate?.Trim();
                    request.PolicyNumber = request.PolicyNumber?.Trim();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error trimming request fields");
                    throw;
                }

                if (!DateTime.TryParse(request.CreateBeginDate, out var beginDate))
                {
                    throw new ArgumentException("Invalid CreateBeginDate format. Expected format: MM/dd/yyyy");
                }

                // Convert CST dates to UTC for database comparison
                TimeZoneInfo cstZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                beginDate = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(beginDate, DateTimeKind.Unspecified), cstZone);

                if (beginDate > DateTime.UtcNow)
                {
                    throw new ArgumentException("CreateBeginDate cannot be a future date");
                }

                DateTime endDate;
                if (string.IsNullOrWhiteSpace(request.CreateEndDate))
                {
                    // End of current day in CST
                    endDate = DateTime.Now.AddDays(1).Date; // Start of next day at midnight CST
                }
                else if (!DateTime.TryParse(request.CreateEndDate, out endDate))
                {
                    throw new ArgumentException("Invalid CreateEndDate format. Expected format: MM/dd/yyyy");
                }
                else
                {
                    // End date should be the start of the next day to include the full day in CST
                    endDate = endDate.AddDays(1).Date;
                }

                // Convert CST end date to UTC
                endDate = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified), cstZone);

                byte[]? policyNumberHash = null;
                if (!string.IsNullOrWhiteSpace(request.PolicyNumber))
                {
                    policyNumberHash = _encryptionService.GenerateSearchHash(request.PolicyNumber);
                }

                var query = from cr in _context.CorrespondenceRequests
                            join cdm in _context.CorrespondenceDeliveryMethods on cr.DeliveryMethodCode equals cdm.DeliveryMethodCode
                            join crs in _context.CorrespondenceRequestStatusHistories on cr.CorrespondenceId equals crs.CorrespondenceId
                            where cr.RequestingDomainName == request.RequestingSystemName
                                  && cr.RequestCreationTS >= beginDate
                                  && cr.RequestCreationTS < endDate
                                  && (string.IsNullOrWhiteSpace(request.DocumentDescription) || cr.ArchiveDocumentTypeCode == request.DocumentDescription)
                                  && (string.IsNullOrWhiteSpace(request.DeliveryMethod) || cdm.DeliveryMethodName == request.DeliveryMethod)
                                  && (string.IsNullOrWhiteSpace(request.CorrespondenceRequestStatusCode) || crs.StatusCode == request.CorrespondenceRequestStatusCode)
                                  && (policyNumberHash == null || cr.PolicyNumberHash != null && cr.PolicyNumberHash.SequenceEqual(policyNumberHash))
                            orderby cr.DocId, cr.CorrespondenceId, crs.HistorySequenceNum
                            select new
                            {
                                cr.DocId,
                                cr.CorrespondenceId,
                                cr.RequestCreationTS,
                                cdm.DeliveryMethodName,
                                cr.ArchiveDocumentTypeCode,
                                cr.RequestingDomainName
                            };

                var results = await query.ToListAsync();

                if (!results.Any())
                {
                    _logger.LogInformation("No filtered correspondence records found");
                    return Enumerable.Empty<CorrespondenceFilterResponse>();
                }

                var correspondenceIds = results.Select(r => r.CorrespondenceId).Distinct().ToList();

                var allStatusHistories = await _context.CorrespondenceRequestStatusHistories
                    .Where(csh => correspondenceIds.Contains(csh.CorrespondenceId))
                    .ToListAsync();

                var statusHistoriesByCorrespondenceId = allStatusHistories
                    .GroupBy(csh => csh.CorrespondenceId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.StatusUpdateTS).FirstOrDefault());

                var statusCodes = statusHistoriesByCorrespondenceId.Values
                    .Where(sh => sh != null)
                    .Select(sh => sh!.StatusCode)
                    .Distinct()
                    .Where(sc => !string.IsNullOrEmpty(sc))
                    .Cast<string>()
                    .ToList();

                var statusDescriptions = new Dictionary<string, string>();
                foreach (string statusCode in statusCodes)
                {
                    try
                    {
                        var description = await _statusHistoryRepo.GetStatusDescriptionByStatusCode(statusCode);
                        statusDescriptions[statusCode] = description ?? $"Status: {statusCode}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get status description for status code: {StatusCode}", statusCode);
                        statusDescriptions[statusCode] = $"Status: {statusCode}";
                    }
                }

                var responses = new List<CorrespondenceFilterResponse>(results.Count);

                foreach (var result in results)
                {
                    var currentStatusHistory = statusHistoriesByCorrespondenceId.GetValueOrDefault(result.CorrespondenceId);
                    var statusCode = currentStatusHistory?.StatusCode ?? string.Empty;
                    var statusDescription = statusDescriptions.GetValueOrDefault(statusCode, "Unknown Status");

                    // Convert UTC from database to CDT for response
                    DateTime cstTime = TimeZoneInfo.ConvertTimeFromUtc(result.RequestCreationTS, cstZone);
                    TimeSpan cstOffset = cstZone.GetUtcOffset(cstTime);
                    var cstDateTimeOffset = new DateTimeOffset(cstTime, cstOffset);

                    var response = new CorrespondenceFilterResponse
                    {
                        DocId = result.DocId,
                        CorrespondenceId = result.CorrespondenceId,
                        RequestCreationTS = cstDateTimeOffset,
                        DeliveryMethodName = result.DeliveryMethodName,
                        CurrentStatusCode = statusCode,
                        CurrentStatusDescription = statusDescription,
                        DocumentDescription = result.ArchiveDocumentTypeCode,
                        RequestingSystemName = result.RequestingDomainName
                    };

                    responses.Add(response);
                }

                _logger.LogInformation("Retrieved {Count} filtered correspondence records", responses.Count);

                return responses;
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "Invalid null argument provided");
                throw;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid argument provided: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving filtered correspondence data");
                throw;
            }
        }
    }
}
