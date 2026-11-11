using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.DTOs;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerCorrespondence.Application.Services
{
    public class CorrespondenceStatusService : ICorrespondenceStatusService
    {
        private readonly ICorrespondenceRequestRepository _requestRepo;
        private readonly ICorrespondenceRequestStatusHistoryRepository _historyRepo;
        private readonly ICorrespondenceDocumentTypeService _documentTypeService;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<CorrespondenceStatusService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        /// <summary>
        /// CorrespondenceStatusService Constructor with dependency injection for repositories, services, and logging.
        /// </summary>
        /// <param name="requestRepo"></param>
        /// <param name="historyRepo"></param>
        /// <param name="documentTypeService"></param>
        /// <param name="encryptionService"></param>
        /// <param name="logger"></param>
        /// <param name="scopeFactory"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public CorrespondenceStatusService(
            ICorrespondenceRequestRepository requestRepo,
            ICorrespondenceRequestStatusHistoryRepository historyRepo,
            ICorrespondenceDocumentTypeService documentTypeService,
            IEncryptionService encryptionService,
            ILogger<CorrespondenceStatusService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _requestRepo = requestRepo ?? throw new ArgumentNullException(nameof(requestRepo));
            _historyRepo = historyRepo ?? throw new ArgumentNullException(nameof(historyRepo));
            _documentTypeService = documentTypeService ?? throw new ArgumentNullException(nameof(documentTypeService));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }
        /// <summary>
        /// GetCorrespondenceStatusByDocIdAsync - Retrieves the correspondence status based on the provided document ID. 
        /// It fetches the corresponding request, its status history, and related details to build a comprehensive response. 
        /// Logs relevant information and handles exceptions gracefully.
        /// </summary>
        /// <param name="docId">The document ID to look up</param>
        /// <returns>A task that represents the asynchronous operation. 
        /// The task result contains the correspondence status response or null if not found.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when the docId is null</exception>
        public async Task<CorrespondenceStatusResponse?> GetCorrespondenceStatusByDocIdAsync(string docId)
        {
            if (docId == null)
                throw new ArgumentNullException(nameof(docId));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var safeDocId = System.Net.WebUtility.HtmlEncode(docId.Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogInformation("BEGIN GetCorrespondenceStatusByDocIdAsync for DocId: {DocId}", safeDocId);

                var correspondenceRequests = await _requestRepo.GetCorrespondenceRequestByDocIdAsync(docId);
                var correspondenceRequest = correspondenceRequests.FirstOrDefault();

                if (correspondenceRequest == null)
                {
                    _logger.LogWarning("No correspondence request found for DocId: {DocId}", safeDocId);
                    return null;
                }

                var result = await BuildSingleStatusResponseAsync(correspondenceRequest);
                stopwatch.Stop();
                _logger.LogInformation("END GetCorrespondenceStatusByDocIdAsync for DocId: {DocId} — completed in {ElapsedMs}ms", safeDocId, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var safeDocId = docId.Replace("\r", string.Empty).Replace("\n", string.Empty);
                _logger.LogError(ex, "FAILED GetCorrespondenceStatusByDocIdAsync for DocId: {DocId} — failed after {ElapsedMs}ms", safeDocId, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceStatusByCorrespondenceIdAsync - Retrieves the correspondence status based on the provided correspondence ID.
        /// Logs relevant information and handles exceptions gracefully.
        /// </summary>
        /// <param name="correspondenceId">The correspondence ID to look up</param>
        /// <returns>A task that represents the asynchronous operation. 
        /// The task result contains the correspondence status response or null if not found.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when the correspondenceId is null</exception>
        public async Task<CorrespondenceStatusResponse?> GetCorrespondenceStatusByCorrespondenceIdAsync(Guid correspondenceId)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var safeCorrespondenceId = System.Net.WebUtility.HtmlEncode(correspondenceId.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogInformation("BEGIN GetCorrespondenceStatusByCorrespondenceIdAsync for CorrespondenceId: {CorrespondenceId}", safeCorrespondenceId);

                var correspondenceRequests = await _requestRepo.GetCorrespondenceRequestByCorrIdAsync(correspondenceId);
                var correspondenceRequest = correspondenceRequests.FirstOrDefault();

                if (correspondenceRequest == null)
                {
                    _logger.LogWarning("No correspondence request found for CorrespondenceId: {CorrespondenceId}", safeCorrespondenceId);
                    return null;
                }

                var result = await BuildSingleStatusResponseAsync(correspondenceRequest);
                stopwatch.Stop();
                _logger.LogInformation("END GetCorrespondenceStatusByCorrespondenceIdAsync for CorrespondenceId: {CorrespondenceId} — completed in {ElapsedMs}ms", safeCorrespondenceId, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var safeCorrespondenceId = System.Net.WebUtility.HtmlEncode(correspondenceId.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogError(ex, "FAILED GetCorrespondenceStatusByCorrespondenceIdAsync for CorrespondenceId: {CorrespondenceId} — failed after {ElapsedMs}ms", safeCorrespondenceId, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceStatusByPolicyNumberAsync - Retrieves the correspondence status for all requests associated with the provided policy number.
        /// Logs relevant information and handles exceptions gracefully.
        /// </summary>
        /// <param name="policyNumber">The policy number to look up</param>
        /// <returns>A task that represents the asynchronous operation. 
        /// The task result contains a collection of correspondence status responses or an empty collection if none are found.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when the policyNumber is null</exception>
        public async Task<IEnumerable<CorrespondenceStatusResponse>> GetCorrespondenceStatusByPolicyNumberAsync(string policyNumber)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var safePolicyNumber = System.Net.WebUtility.HtmlEncode(policyNumber.Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogInformation("BEGIN GetCorrespondenceStatusByPolicyNumberAsync for PolicyNumber: {PolicyNumber}", safePolicyNumber);

                var policyNumberHash = _encryptionService.GenerateSearchHash(policyNumber);
                var correspondenceRequests = await _requestRepo.GetCorrespondenceRequestByPolicyNumberHashAsync(policyNumberHash);
                var requestList = correspondenceRequests.ToList();

                if (!requestList.Any())
                {
                    stopwatch.Stop();
                    _logger.LogInformation("END GetCorrespondenceStatusByPolicyNumberAsync for PolicyNumber: {PolicyNumber} — no results found, completed in {ElapsedMs}ms", safePolicyNumber, stopwatch.ElapsedMilliseconds);
                    return Enumerable.Empty<CorrespondenceStatusResponse>();
                }

                // Batch 1: Fetch all histories (single query)
                var correspondenceIds = requestList.Select(r => r.CorrespondenceId);
                var historiesByCorrespondenceId = await _historyRepo.GetAllByCorrespondenceIdsAsync(correspondenceIds);

                // Batch 2: Fetch all status descriptions (single query)
                var allStatusCodes = historiesByCorrespondenceId.Values
                    .SelectMany(h => h)
                    .Select(h => h.StatusCode)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct();
                var statusDescriptions = await _historyRepo.GetStatusDescriptionsByStatusCodesAsync(allStatusCodes!);

                // Batch 3: Pre-fetch all unique document types
                var documentTypeCache = await PreFetchDocumentTypesAsync(
                    requestList.Select(r => r.ArchiveDocumentTypeCode));

                var results = new List<CorrespondenceStatusResponse>();
                foreach (var request in requestList)
                {
                    var histories = historiesByCorrespondenceId.TryGetValue(request.CorrespondenceId, out var h)
                        ? h
                        : new List<CorrespondenceRequestStatusHistory>();

                    var response = await BuildBatchedStatusResponseAsync(request, histories, statusDescriptions, documentTypeCache);
                    if (response != null)
                        results.Add(response);
                }

                stopwatch.Stop();
                _logger.LogInformation(
                    "END GetCorrespondenceStatusByPolicyNumberAsync for PolicyNumber: {PolicyNumber} — " +
                    "returned {Count} results, {HistoryCount} total history entries, " +
                    "{DocTypeCount} unique document types, completed in {ElapsedMs}ms",
                    safePolicyNumber, results.Count, historiesByCorrespondenceId.Values.Sum(v => v.Count),
                    documentTypeCache.Count, stopwatch.ElapsedMilliseconds);
                return results;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var safePolicyNumber = System.Net.WebUtility.HtmlEncode(policyNumber.Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogError(ex, "FAILED GetCorrespondenceStatusByPolicyNumberAsync for PolicyNumber: {PolicyNumber} — failed after {ElapsedMs}ms", safePolicyNumber, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceStatusByClaimNumberAsync - Retrieves a paginated list of correspondence statuses for all requests associated with the provided claim number.
        /// Logs relevant information and handles exceptions gracefully.
        /// </summary>
        /// <param name="claimNumber">The claim number to look up</param>
        /// <param name="pageNumber">The page number for pagination</param>
        /// <param name="pageSize">The page size for pagination</param>
        /// <returns>A task that represents the asynchronous operation. 
        /// The task result contains a paginated response of correspondence status responses or an empty collection if none are found.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when the claimNumber is null</exception>
        public async Task<PaginatedResponse<CorrespondenceStatusResponse>> GetCorrespondenceStatusByClaimNumberAsync(string claimNumber, int pageNumber, int pageSize)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var safeClaimNumber = System.Net.WebUtility.HtmlEncode(claimNumber.Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogInformation("BEGIN GetCorrespondenceStatusByClaimNumberAsync for ClaimNumber: {ClaimNumber}, Page: {PageNumber}, Size: {PageSize}", safeClaimNumber, pageNumber, pageSize);

                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 10;
                if (pageSize > 50) pageSize = 50;

                var (correspondenceRequests, totalCount) = await _requestRepo.GetCorrespondenceRequestByClaimNumberAsync(claimNumber, pageNumber, pageSize);
                var requestList = correspondenceRequests.ToList();

                if (!requestList.Any())
                {
                    stopwatch.Stop();
                    _logger.LogInformation("END GetCorrespondenceStatusByClaimNumberAsync for ClaimNumber: {ClaimNumber} — no results found, completed in {ElapsedMs}ms", safeClaimNumber, stopwatch.ElapsedMilliseconds);
                    return new PaginatedResponse<CorrespondenceStatusResponse>
                    {
                        Data = Enumerable.Empty<CorrespondenceStatusResponse>(),
                        TotalCount = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                // Batch 1: Fetch all histories (single query)
                var correspondenceIds = requestList.Select(r => r.CorrespondenceId);
                var historiesByCorrespondenceId = await _historyRepo.GetAllByCorrespondenceIdsAsync(correspondenceIds);

                // Batch 2: Fetch all status descriptions (single query)
                var allStatusCodes = historiesByCorrespondenceId.Values
                    .SelectMany(h => h)
                    .Select(h => h.StatusCode)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct();
                var statusDescriptions = await _historyRepo.GetStatusDescriptionsByStatusCodesAsync(allStatusCodes!);

                // Batch 3: Pre-fetch all unique document types (few queries — typically 2-3 distinct types)
                var documentTypeCache = await PreFetchDocumentTypesAsync(
                    requestList.Select(r => r.ArchiveDocumentTypeCode));

                // Build responses — only decryption remains per-item (CPU-bound, no DB)
                var results = new List<CorrespondenceStatusResponse?>();
                foreach (var request in requestList)
                {
                    var histories = historiesByCorrespondenceId.TryGetValue(request.CorrespondenceId, out var h)
                        ? h
                        : new List<CorrespondenceRequestStatusHistory>();

                    var response = await BuildBatchedStatusResponseAsync(request, histories, statusDescriptions, documentTypeCache);
                    // Redact PolicyNumber for claim number search results
                    if (response != null)
                    {
                        response.PolicyNumber = "[REDACTED]";
                    }
                    results.Add(response);
                }

                var validResults = results.Where(r => r != null).ToList();
                stopwatch.Stop();
                _logger.LogInformation(
                    "END GetCorrespondenceStatusByClaimNumberAsync for ClaimNumber: {ClaimNumber} — " +
                    "Page {PageNumber}/{TotalPages}, returned {Count}/{TotalCount} results, " +
                    "{HistoryCount} total history entries, {StatusCodeCount} unique status codes, " +
                    "{DocTypeCount} unique document types, completed in {ElapsedMs}ms",
                    safeClaimNumber,
                    pageNumber,
                    pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0,
                    validResults.Count,
                    totalCount,
                    historiesByCorrespondenceId.Values.Sum(v => v.Count),
                    statusDescriptions.Count,
                    documentTypeCache.Count,
                    stopwatch.ElapsedMilliseconds);

                return new PaginatedResponse<CorrespondenceStatusResponse>
                {
                    Data = validResults.Select(r => r!),
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var safeClaimNumber = System.Net.WebUtility.HtmlEncode(claimNumber.Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogError(ex, "FAILED GetCorrespondenceStatusByClaimNumberAsync for ClaimNumber: {ClaimNumber} — failed after {ElapsedMs}ms", safeClaimNumber, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
        /// <summary>
        /// GetCorrespondenceStatusByDocumentTypesAsync - Retrieves a paginated list of correspondence statuses for all requests matching the provided document types.
        /// </summary>
        public async Task<PaginatedResponse<CorrespondenceStatusResponse>> GetCorrespondenceStatusByDocumentTypesAsync(IEnumerable<string> documentTypes, int pageNumber, int pageSize)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var documentTypeList = documentTypes.ToList();
                var safeDocumentTypes = System.Net.WebUtility.HtmlEncode(string.Join(", ", documentTypeList).Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogInformation("BEGIN GetCorrespondenceStatusByDocumentTypesAsync for DocumentTypes: [{DocumentTypes}], Page: {PageNumber}, Size: {PageSize}", safeDocumentTypes, pageNumber, pageSize);

                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 10;
                if (pageSize > 50) pageSize = 50;

                var (correspondenceRequests, totalCount) = await _requestRepo.GetCorrespondenceRequestsByDocumentTypesAsync(documentTypeList, pageNumber, pageSize);
                var requestList = correspondenceRequests.ToList();

                if (!requestList.Any())
                {
                    stopwatch.Stop();
                    _logger.LogInformation("END GetCorrespondenceStatusByDocumentTypesAsync for DocumentTypes: [{DocumentTypes}] — no results found, completed in {ElapsedMs}ms", safeDocumentTypes, stopwatch.ElapsedMilliseconds);
                    return new PaginatedResponse<CorrespondenceStatusResponse>
                    {
                        Data = Enumerable.Empty<CorrespondenceStatusResponse>(),
                        TotalCount = 0,
                        PageNumber = pageNumber,
                        PageSize = pageSize
                    };
                }

                // Batch 1: Fetch all histories (single query)
                var correspondenceIds = requestList.Select(r => r.CorrespondenceId);
                var historiesByCorrespondenceId = await _historyRepo.GetAllByCorrespondenceIdsAsync(correspondenceIds);

                // Batch 2: Fetch all status descriptions (single query)
                var allStatusCodes = historiesByCorrespondenceId.Values
                    .SelectMany(h => h)
                    .Select(h => h.StatusCode)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct();
                var statusDescriptions = await _historyRepo.GetStatusDescriptionsByStatusCodesAsync(allStatusCodes!);

                // Batch 3: Pre-fetch all unique document types
                var documentTypeCache = await PreFetchDocumentTypesAsync(
                    requestList.Select(r => r.ArchiveDocumentTypeCode));

                // Build responses
                var results = new List<CorrespondenceStatusResponse?>();
                foreach (var request in requestList)
                {
                    var histories = historiesByCorrespondenceId.TryGetValue(request.CorrespondenceId, out var h)
                        ? h
                        : new List<CorrespondenceRequestStatusHistory>();

                    var response = await BuildBatchedStatusResponseAsync(request, histories, statusDescriptions, documentTypeCache);

                    // Return PolicyNumberHash value as PolicyNumber in document type search results
                    if (response != null && request.PolicyNumberHash != null && request.PolicyNumberHash.Length > 0)
                    {
                        response.PolicyNumber = "[REDACTED]";
                    }

                    results.Add(response);
                }

                var validResults = results.Where(r => r != null).ToList();
                stopwatch.Stop();
                _logger.LogInformation(
                    "END GetCorrespondenceStatusByDocumentTypesAsync for DocumentTypes: [{DocumentTypes}] — " +
                    "Page {PageNumber}/{TotalPages}, returned {Count}/{TotalCount} results, " +
                    "{HistoryCount} total history entries, {StatusCodeCount} unique status codes, " +
                    "{DocTypeCount} unique document types, completed in {ElapsedMs}ms",
                    safeDocumentTypes,
                    pageNumber,
                    pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0,
                    validResults.Count,
                    totalCount,
                    historiesByCorrespondenceId.Values.Sum(v => v.Count),
                    statusDescriptions.Count,
                    documentTypeCache.Count,
                    stopwatch.ElapsedMilliseconds);

                return new PaginatedResponse<CorrespondenceStatusResponse>
                {
                    Data = validResults.Select(r => r!),
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var safeDocumentTypes = System.Net.WebUtility.HtmlEncode(string.Join(", ", documentTypes).Replace("\r", string.Empty).Replace("\n", string.Empty));
                _logger.LogError(ex, "FAILED GetCorrespondenceStatusByDocumentTypesAsync for DocumentTypes: [{DocumentTypes}] — failed after {ElapsedMs}ms", safeDocumentTypes, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
        /// <summary>
        /// Builds a status response using pre-fetched batch data. No additional DB queries for histories or status descriptions.
        /// </summary>
        /// <param name="request">The correspondence request</param>
        /// <param name="statusHistories">The status histories for the correspondence request</param>
        /// <param name="statusDescriptionCache">A cache of status descriptions keyed by status code</param>
        /// <param name="documentTypeCache">A cache of document type info keyed by document type code</param>
        /// <returns>A task that represents the asynchronous operation. 
        /// The task result contains a correspondence status response or null if none is found.
        /// </returns>
        private async Task<CorrespondenceStatusResponse?> BuildBatchedStatusResponseAsync(
           CorrespondenceRequest request,
           IEnumerable<CorrespondenceRequestStatusHistory> statusHistories,
           Dictionary<string, string> statusDescriptionCache,
           Dictionary<string, CorrespondenceDocumentType?> documentTypeCache)
        {
            var orderedHistory = statusHistories.OrderByDescending(h => h.StatusUpdateTS).ToList();
            var currentStatusHistory = orderedHistory.FirstOrDefault();

            //// Decrypt policy number - Commenting out the following logic for now since it adds exposes the policy number being returned 
            ////in the response for claim number or document type searches. We can revisit this if we decide to include the decrypted 
            ////policy number in those search results in the future.
            //string? decryptedPolicyNumber = null;
            //if (request.PolicyNumber != null && request.PolicyNumber.Length > 0)
            //{
            //    try
            //    {
            //        var encryptedPolicyString = Encoding.UTF8.GetString(request.PolicyNumber);
            //        decryptedPolicyNumber = await _encryptionService.DecryptAsync(encryptedPolicyString);
            //    }
            //    catch (Exception ex)
            //    {
            //        var safeId = System.Net.WebUtility.HtmlEncode(request.CorrespondenceId.ToString());
            //        _logger.LogWarning(ex, "Failed to decrypt policy number for CorrespondenceId: " + safeId);
            //    }
            //}

            // Decrypt payload
            ProcessingInfo? processingInfo = null;
            if (request.RequestPayload != null && request.RequestPayload.Length > 0)
            {
                try
                {
                    var payloadString = Encoding.UTF8.GetString(request.RequestPayload);
                    var decryptedPayload = await _encryptionService.DecryptAsync(payloadString);
                    var sequentialProcess = System.Text.Json.JsonSerializer.Deserialize<SequentialProcess>(decryptedPayload);
                    if (sequentialProcess?.VentureFusionResponse != null)
                    {
                        processingInfo = new ProcessingInfo
                        {
                            VentureFusionProcessed = sequentialProcess.VentureFusionResponse.IsSuccessful,
                            VentureFusionStatus = sequentialProcess.VentureFusionResponse.StatusCode.ToString(),
                            VentureFusionTimestamp = sequentialProcess.VentureFusionResponse.ResponseTimestamp
                        };
                    }
                }
                catch (Exception ex)
                {
                    var safeId = System.Net.WebUtility.HtmlEncode(request.CorrespondenceId.ToString());
                    _logger.LogWarning(ex, "Failed to deserialize request payload for CorrespondenceId: " + safeId);
                }
            }

            // Use pre-fetched document type (no DB call)
            DocumentTypeInfo? documentTypeDetails = null;
            if (!string.IsNullOrWhiteSpace(request.ArchiveDocumentTypeCode) &&
                documentTypeCache.TryGetValue(request.ArchiveDocumentTypeCode, out var cachedDocType) &&
                cachedDocType != null)
            {
                documentTypeDetails = new DocumentTypeInfo
                {
                    DocumentType = cachedDocType.DocumentType,
                    DocumentSubType = cachedDocType.DocumentSubType,
                    CabinetName = cachedDocType.CabinetName
                };
            }

            var response = new CorrespondenceStatusResponse
            {
                DocId = request.DocId,
                CorrespondenceId = request.CorrespondenceId,
                RequestingSystemName = request.RequestingDomainName,
                PolicyNumber = "[REDACTED]",
                ClaimNumber = request.ClaimNumber,
                CustomerId = request.CustomerId,
                DeliveryMethod = request.DeliveryMethodCode,
                DocumentType = request.ArchiveDocumentTypeCode,
                RequestCreationDate = request.RequestCreationTS,
                ProcessingInfo = processingInfo,
                DocumentTypeDetails = documentTypeDetails
            };

            if (currentStatusHistory != null)
            {
                response.CurrentStatus = new CurrentStatusInfo
                {
                    StatusCode = currentStatusHistory.StatusCode ?? string.Empty,
                    StatusDescription = GetStatusDescriptionFromCache(currentStatusHistory.StatusCode, statusDescriptionCache),
                    LastUpdated = currentStatusHistory.StatusUpdateTS,
                    UpdatedBy = currentStatusHistory.StatusUpdatedBy ?? string.Empty
                };
            }

            response.StatusHistory = orderedHistory.Select(h => new StatusHistoryInfo
            {
                SequenceNumber = h.HistorySequenceNum,
                StatusCode = h.StatusCode ?? string.Empty,
                StatusDescription = GetStatusDescriptionFromCache(h.StatusCode, statusDescriptionCache),
                UpdatedDate = h.StatusUpdateTS,
                UpdatedBy = h.StatusUpdatedBy ?? string.Empty
            }).ToList();

            return response;
        }

        /// <summary>
        /// Builds a status response for a single item (used by DocId and CorrespondenceId lookups).
        /// Acceptable to have individual queries here since it's always a single record.
        /// </summary>
        /// <param name="request">The correspondence request</param>
        /// <returns>A task that represents the asynchronous operation. 
        /// The task result contains a correspondence status response or null if none is found.
        /// </returns>
        private async Task<CorrespondenceStatusResponse> BuildSingleStatusResponseAsync(CorrespondenceRequest request)
        {
            var statusHistories = await _historyRepo.GetAllByCorrespondenceIdAsync(request.CorrespondenceId);
            var orderedHistory = statusHistories.OrderByDescending(h => h.StatusUpdateTS).ToList();
            var currentStatusHistory = orderedHistory.FirstOrDefault();

            // Batch fetch status descriptions for this single request's history
            var statusCodes = orderedHistory
                .Select(h => h.StatusCode)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct();
            var statusDescriptions = await _historyRepo.GetStatusDescriptionsByStatusCodesAsync(statusCodes!);

            //// Decrypt policy number - Commenting out the following logic for now since it adds exposes the policy number being returned 
            ////in the response for claim number or document type searches. We can revisit this if we decide to include the decrypted 
            ////policy number in those search results in the future.
            //string? decryptedPolicyNumber = null;
            //if (request.PolicyNumber != null && request.PolicyNumber.Length > 0)
            //{
            //    try
            //    {
            //        var encryptedPolicyString = Encoding.UTF8.GetString(request.PolicyNumber);
            //        decryptedPolicyNumber = await _encryptionService.DecryptAsync(encryptedPolicyString);
            //    }
            //    catch (Exception ex)
            //    {
            //        var safeCorrespondenceId = System.Net.WebUtility.HtmlEncode(request.CorrespondenceId.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty));
            //        _logger.LogWarning(ex, "Failed to decrypt policy number for CorrespondenceId: " + safeCorrespondenceId);
            //        decryptedPolicyNumber = "[Encrypted]";
            //    }
            //}

            var response = new CorrespondenceStatusResponse
            {
                DocId = request.DocId,
                CorrespondenceId = request.CorrespondenceId,
                RequestingSystemName = request.RequestingDomainName,
                PolicyNumber = "[REDACTED]",
                ClaimNumber = request.ClaimNumber,
                CustomerId = request.CustomerId,
                DeliveryMethod = request.DeliveryMethodCode,
                DocumentType = request.ArchiveDocumentTypeCode,
                RequestCreationDate = request.RequestCreationTS
            };

            if (currentStatusHistory != null)
            {
                response.CurrentStatus = new CurrentStatusInfo
                {
                    StatusCode = currentStatusHistory.StatusCode ?? string.Empty,
                    StatusDescription = GetStatusDescriptionFromCache(currentStatusHistory.StatusCode, statusDescriptions),
                    LastUpdated = currentStatusHistory.StatusUpdateTS,
                    UpdatedBy = currentStatusHistory.StatusUpdatedBy ?? string.Empty
                };
            }

            response.StatusHistory = orderedHistory.Select(h => new StatusHistoryInfo
            {
                SequenceNumber = h.HistorySequenceNum,
                StatusCode = h.StatusCode ?? string.Empty,
                StatusDescription = GetStatusDescriptionFromCache(h.StatusCode, statusDescriptions),
                UpdatedDate = h.StatusUpdateTS,
                UpdatedBy = h.StatusUpdatedBy ?? string.Empty
            }).ToList();

            // Extract processing info
            if (request.RequestPayload != null && request.RequestPayload.Length > 0)
            {
                try
                {
                    var payloadString = Encoding.UTF8.GetString(request.RequestPayload);
                    var decryptedPayload = await _encryptionService.DecryptAsync(payloadString);
                    var sequentialProcess = System.Text.Json.JsonSerializer.Deserialize<SequentialProcess>(decryptedPayload);

                    if (sequentialProcess?.VentureFusionResponse != null)
                    {
                        response.ProcessingInfo = new ProcessingInfo
                        {
                            VentureFusionProcessed = sequentialProcess.VentureFusionResponse.IsSuccessful,
                            VentureFusionStatus = sequentialProcess.VentureFusionResponse.StatusCode.ToString(),
                            VentureFusionTimestamp = sequentialProcess.VentureFusionResponse.ResponseTimestamp
                        };
                    }
                }
                catch (Exception ex)
                {
                    var safeCorrespondenceId = System.Net.WebUtility.HtmlEncode(request.CorrespondenceId.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty));
                    _logger.LogWarning(ex, "Failed to deserialize request payload for CorrespondenceId: " + safeCorrespondenceId);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.ArchiveDocumentTypeCode))
            {
                try
                {
                    var documentTypeInfo = await _documentTypeService.GetDocumentTypeInfoAsync(request.ArchiveDocumentTypeCode);
                    if (documentTypeInfo != null)
                    {
                        response.DocumentTypeDetails = new DocumentTypeInfo
                        {
                            DocumentType = documentTypeInfo.DocumentType,
                            DocumentSubType = documentTypeInfo.DocumentSubType,
                            CabinetName = documentTypeInfo.CabinetName
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to lookup DocumentType details for: {DocumentType}", request.ArchiveDocumentTypeCode);
                }
            }

            return response;
        }
        /// <summary>
        /// Pre-fetches document type info for all unique document type codes in a batch.
        /// </summary>
        private async Task<Dictionary<string, CorrespondenceDocumentType?>> PreFetchDocumentTypesAsync(IEnumerable<string?> documentTypeCodes)
        {
            var uniqueCodes = documentTypeCodes
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList();

            var cache = new Dictionary<string, CorrespondenceDocumentType?>();
            foreach (var code in uniqueCodes)
            {
                try
                {
                    cache[code!] = await _documentTypeService.GetDocumentTypeInfoAsync(code!);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to pre-fetch DocumentType for: {DocumentType}", code);
                    cache[code!] = null;
                }
            }
            return cache;
        }
        /// <summary>
        /// GetStatusDescriptionFromCache - Helper method to retrieve status description from a provided cache.
        /// </summary>
        /// <param name="statusCode">The status code to look up</param>
        /// <param name="cache">A cache of status descriptions keyed by status code</param>
        /// <returns>The status description if found; otherwise, a default message indicating the status is unknown</returns>
        private static string GetStatusDescriptionFromCache(string? statusCode, Dictionary<string, string> cache)
        {
            if (string.IsNullOrEmpty(statusCode)) return "Unknown Status";
            return cache.TryGetValue(statusCode, out var desc) ? desc : $"Status: {statusCode}";
        }
    }
}