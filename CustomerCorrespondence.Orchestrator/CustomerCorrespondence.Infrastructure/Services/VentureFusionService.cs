using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;

namespace CustomerCorrespondence.Infrastructure.Services
{
    public class VentureFusionService : IVentureFusionService
    {
        private HttpClient _tokenClient = new HttpClient();
        private HttpClient _ventureFusionClient = new HttpClient();
        private readonly VentureFusionServiceCredentials _options;
        private DateTime _tokenExpiration = DateTime.Now;
        private VentureFusionBearerToken _bearerToken = new VentureFusionBearerToken();
        private readonly ISecretsCacheService _cache;
        private const string VFBearerTokenCacheKey = "VFBearerToken";

        //Coming from Program.cs DI
        private readonly Solutions _solutionOptions;

        public VentureFusionService(
            IOptions<VentureFusionServiceCredentials> options, 
            IOptions<Solutions> solutionOptions, 
            ISecretsCacheService cache)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _solutionOptions = solutionOptions?.Value ?? throw new ArgumentNullException(nameof(solutionOptions));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            // VALIDATE REQUIRED PROPERTIES AT CONSTRUCTION
            if (string.IsNullOrEmpty(_options.TokenEndpoint))
                throw new ArgumentNullException(nameof(_options.TokenEndpoint), "VentureFusionTokenEndpoint is required but not configured");

            if (_options.Uri == null)
                throw new ArgumentNullException(nameof(_options.Uri), "VentureFusionUri is required but not configured");

            if (string.IsNullOrEmpty(_options.ClientId))
                throw new ArgumentNullException(nameof(_options.ClientId), "VentureFusionClientId is required but not configured");
        }

        public async Task<HttpResponseMessage> CheckHealth()
        {
            try
            {
                var token = await GetBearerToken();

                if (token != null)
                {
                    SetBearerTokenOnVentureFusionClient(token);
                }
                else
                {
                    throw new Exception("Failed to retrieve Venture Fusion bearer token.");
                }

                var response = await _ventureFusionClient.GetAsync("FusionAPI/Health");
                response.EnsureSuccessStatusCode();

                return response;
            }
            catch (HttpRequestException ex)
            {
                throw new PermanentFailureException("Error reaching Venture Fusion API check health endpoint", ex, NonRetryableExceptionType.ExternalServiceError);
            }
            catch (Exception ex)
            {
                throw new PermanentFailureException(
                    "Unexpected error in Venture Fusion API check health endpoint",
                    ex as HttpRequestException ?? new HttpRequestException("Unknown HttpRequestException", ex),
                    NonRetryableExceptionType.Unknown
                );
            }
        }

        public async Task<HttpResponseMessage> PostDataRecords(SequentialProcess request)
        {
            try
            {
                var token = await GetBearerToken();

                if (token != null)
                {
                    SetBearerTokenOnVentureFusionClient(token);
                }
                else
                {
                    throw new Exception("Failed to retrieve Venture Fusion bearer token.");
                }

                var records = MapCorrespondenceRequestToVentureFusionRequest(request);

                HttpResponseMessage response = await _ventureFusionClient.PostAsJsonAsync("FusionApi/DataRecords", records);
                
                // Check if token is expired or invalid (401 Unauthorized)
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("Received 401 Unauthorized - refreshing bearer token and retrying");

                    // Clear cached token and reset local token state
                    _cache.RemoveSecret(VFBearerTokenCacheKey);
                    _bearerToken = new VentureFusionBearerToken();
                    _tokenExpiration = DateTime.Now;

                    // Get fresh token
                    var refreshedToken = await GetBearerToken();

                    if (refreshedToken != null)
                    {
                        SetBearerTokenOnVentureFusionClient(refreshedToken);
                    }
                    else
                    {
                        throw new Exception("Failed to refresh Venture Fusion bearer token.");
                    }

                    // Retry the request with fresh token
                    response = await _ventureFusionClient.PostAsJsonAsync("FusionApi/DataRecords", records);
                }
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");

                if (ex is HttpRequestException httpEx)
                {
                    throw new PermanentFailureException("HTTP error posting data records to Venture Fusion", httpEx, NonRetryableExceptionType.ExternalServiceError);
                }
                else
                {
                    throw new PermanentFailureException(
                        "Unexpected error posting data records to Venture Fusion",
                        ex as HttpRequestException ?? new HttpRequestException("Unknown HttpRequestException", ex),
                        NonRetryableExceptionType.Unknown
                    );
                }
            }
        }

        public void SetBearerTokenOnVentureFusionClient(VentureFusionBearerToken token)
        {
            _bearerToken = token;
            _tokenExpiration = DateTime.Now.AddSeconds(token.expires_in);

            _ventureFusionClient.DefaultRequestHeaders.Clear();
            _ventureFusionClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.access_token}");
            _ventureFusionClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _ventureFusionClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _options.ApimSubscriptionKey);
            _ventureFusionClient.BaseAddress = _options.Uri;
        }

        public async Task<VentureFusionBearerToken> GetBearerToken()
        {

            Console.WriteLine ("Retrieving Venture Fusion Get Bearer Token");

            // 1. Prefer in-memory token if still valid
            if (_bearerToken != null && _tokenExpiration > DateTime.Now && !string.IsNullOrWhiteSpace(_bearerToken.access_token))
            {
                Console.WriteLine("Using in-memory Venture Fusion bearer token");
                return _bearerToken;
            }

            // 2. Fallback to cache if in-memory token is missing/expired
            if (_cache.IsSecretCached(VFBearerTokenCacheKey))
            {
                var VFcachedToken = _cache.GetCachedSecret(VFBearerTokenCacheKey);
                if (!string.IsNullOrEmpty(VFcachedToken))
                {
                    var deserializedToken = System.Text.Json.JsonSerializer.Deserialize<VentureFusionBearerToken>(VFcachedToken);
                    if (deserializedToken != null && !string.IsNullOrWhiteSpace(deserializedToken.access_token))
                    {
                        // Safe guard: if in-memory expiration isn't set, set a conservative one based on token.expires_in
                        _bearerToken = deserializedToken;
                        _tokenExpiration = DateTime.Now.AddSeconds(deserializedToken.expires_in > 60 ? deserializedToken.expires_in - 60 : deserializedToken.expires_in);
                        Console.WriteLine("Using cached Venture Fusion bearer token");
                        return deserializedToken;
                    }
                }
            }

            // 3. If we are here, we need a new token
            var requestBody = new Dictionary<string, string>
            {
                { "grant_type", _options.grant_type ?? "client_credentials"},
                { "scope", _options.Scope ?? string.Empty}
            };

            var content = new FormUrlEncodedContent(requestBody);
            _tokenClient.DefaultRequestHeaders.Clear();
            _tokenClient.DefaultRequestHeaders.Add("Authorization", $"Basic {_options.GetEncodedAuthenticationString()}");

            if (string.IsNullOrEmpty(_options.TokenEndpoint))
            {
                throw new ArgumentNullException(nameof(_options.TokenEndpoint), "VentureFusionTokenEndpoint is required but not configured");
            }
            _tokenClient.BaseAddress = new Uri(_options.TokenEndpoint);

            try
            {
                var response = await _tokenClient.PostAsync("", content);
                response.EnsureSuccessStatusCode();
                var token = await response.Content.ReadFromJsonAsync<VentureFusionBearerToken>();

                if (token != null && !string.IsNullOrWhiteSpace(token.access_token))
                {
                    // 3a. Update in-memory token with a small safety margin on expiration
                    _bearerToken = token;
                    var expiresInSeconds = token.expires_in;
                    if (expiresInSeconds > 60)
                    {
                        // expire 60 seconds before actual expiry to reduce chance of 401s
                        _tokenExpiration = DateTime.Now.AddSeconds(expiresInSeconds - 60);
                    }
                    else
                    {
                        _tokenExpiration = DateTime.Now.AddSeconds(expiresInSeconds);
                    }

                    // 3b. Cache token with same duration as our in-memory expiration window
                    var tokenString = System.Text.Json.JsonSerializer.Serialize(token);
                    var cacheDuration = _tokenExpiration - DateTime.Now;
                    _cache.SetSecret(VFBearerTokenCacheKey, tokenString, cacheDuration);

                    Console.WriteLine($"Obtained new Venture Fusion bearer token, expires in {cacheDuration.TotalMinutes} minutes");
                    return token;
                }

                throw new Exception("Received null or empty token from Venture Fusion");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get bearer token from Venture Fusion: {ex.Message}", ex);
            }
        }

        public DataRecords.DataRecord MapCorrespondenceRequestToVentureFusionRequest(SequentialProcess request)
        {
            try
            {
                Console.WriteLine("========================================");
                Console.WriteLine("MAPPING REQUEST TO VENTURE FUSION FORMAT");
                Console.WriteLine("========================================");

                DataRecords.DataRecord requestRecord = new DataRecords.DataRecord();
                requestRecord.records = new DataRecords.Record[1] { new DataRecords.Record() };

                if (request.MailDeliveryInfo == null)
                {
                    throw new ArgumentNullException(nameof(request.MailDeliveryInfo), "MailDeliveryInfo is required for VentureFusion request");
                }

                // Validate DocId is present
                if (string.IsNullOrWhiteSpace(request.DocId))
                {
                    throw new ArgumentNullException(nameof(request.DocId), "DocId is required for VentureFusion request");
                }


                // Default mapping for other document types
                requestRecord.solutionsId = request.CompositionInfo.SolutionsId;
            
                requestRecord.usageId = _solutionOptions.usageId;
                requestRecord.orderId = null;
                requestRecord.numberOfRecords = 1;

                // Record properties - DocId becomes recordHandle
                requestRecord.records[0].recordHandle = request.DocId;
                Console.WriteLine($"Set recordHandle to DocId: {request.DocId}");

                // Build additionalData with correct ordering - ALL data goes here
                requestRecord.records[0].additionalData = BuildAdditionalDataWithOrdering(request);
                requestRecord.records[0].additionalDataSchemaId = string.Empty;

                Console.WriteLine($"Mapped successfully: usageId={requestRecord.usageId}, recordHandle={requestRecord.records[0].recordHandle}");
                Console.WriteLine($"AdditionalData properties: {requestRecord.records[0].additionalData?.Count ?? 0}");
                Console.WriteLine("========================================");

                return requestRecord;
            }
            catch (ArgumentNullException argEx)
            {
                Console.WriteLine($"Validation Error: {argEx.Message}");
                throw new PermanentFailureException(
                    $"Validation failed while mapping to VentureFusion request: {argEx.ParamName}",
                    new HttpRequestException(argEx.Message, argEx),
                    NonRetryableExceptionType.Unknown
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw new PermanentFailureException(
                    "Unexpected error mapping correspondence request to VentureFusion format",
                    ex as HttpRequestException ?? new HttpRequestException("Unknown mapping error", ex),
                    NonRetryableExceptionType.Unknown
                );
            }
        }

        /// <summary>
        /// Builds additionalData with specific ordering: MailFlag, ImageFlag, VersionCode, MailDeliveryInfo, then all other fields
        /// </summary>
        private Dictionary<string, object> BuildAdditionalDataWithOrdering(SequentialProcess request)
        {
            try
            {
                Console.WriteLine("Building additionalData with ordered properties...");

                // Check if AdditionalData already has merged data
                if (request.AdditionalData != null &&
                    (request.AdditionalData.ContainsKey("MailDeliveryInfo") || request.AdditionalData.ContainsKey("JourneyDeliveryInfo")))
                {
                    Console.WriteLine("AdditionalData already contains delivery info - reordering for VentureFusion");
                    return ReorderAdditionalData(request.AdditionalData, request.DeliveryMethod, request);
                }

                Console.WriteLine("Building AdditionalData from scratch with proper ordering");

                var orderedData = new Dictionary<string, object>();

                // 1. Add fixed flags FIRST - based on DeliveryMethod
                if (request.IsMailDelivery)
                {
                    orderedData["MailFlag"] = "Y";
                    orderedData["ImageFlag"] = "Y";
                }
                else if (request.IsEPresentDelivery)
                {
                    orderedData["MailFlag"] = "N";
                    orderedData["ImageFlag"] = "Y";
                    orderedData["EPresentFlag"] = "Y";
                }

                orderedData["VersionCode"] = request.CompositionInfo?.VersionCode ?? string.Empty;

                Console.WriteLine($"DeliveryMethod: '{request.DeliveryMethod}', MailFlag: {orderedData["MailFlag"]}, ImageFlag: {orderedData["ImageFlag"]}");

                // 2. Add MailDeliveryInfo (always present per validation)
                if (request.MailDeliveryInfo != null)
                {
                    var mailDeliveryInfoDict = new Dictionary<string, object>
                    {
                        ["RecipientFullName"] = request.MailDeliveryInfo.RecipientFullName ?? string.Empty,
                        ["RecipientFirstName"] = request.MailDeliveryInfo.RecipientFirstName ?? string.Empty,
                        ["RecipientLastName"] = request.MailDeliveryInfo.RecipientLastName ?? string.Empty
                    };

                    if (request.MailDeliveryInfo.RecipientAddress != null)
                    {
                        mailDeliveryInfoDict["RecipientAddress"] = new Dictionary<string, object>
                        {
                            ["RecipientAddressLine1"] = request.MailDeliveryInfo.RecipientAddress.RecipientAddressLine1 ?? string.Empty,
                            ["RecipientAddressLine2"] = request.MailDeliveryInfo.RecipientAddress.RecipientAddressLine2 ?? string.Empty,
                            ["RecipientAddressLine3"] = request.MailDeliveryInfo.RecipientAddress.RecipientAddressLine3 ?? string.Empty,
                            ["RecipientAddressCity"] = request.MailDeliveryInfo.RecipientAddress.RecipientAddressCity ?? string.Empty,
                            ["RecipientAddressST"] = request.MailDeliveryInfo.RecipientAddress.RecipientAddressST ?? string.Empty,
                            ["RecipientAddressZIP"] = request.MailDeliveryInfo.RecipientAddress.RecipientAddressZIP ?? string.Empty,
                            ["RecipientCountry"] = request.MailDeliveryInfo.RecipientAddress.RecipientCountry ?? string.Empty
                        };
                    }

                    mailDeliveryInfoDict["NonUSAddressFlag"] = request.MailDeliveryInfo.NonUSAddressFlag ?? false;
                    mailDeliveryInfoDict["BadAddressFlag"] = request.MailDeliveryInfo.BadAddressFlag ?? false;

                    orderedData["MailDeliveryInfo"] = mailDeliveryInfoDict;
                }

                // 3. Add JourneyDeliveryInfo for ePresent
                if (request.IsEPresentDelivery && request.JourneyDeliveryInfo != null)
                {
                    orderedData["JourneyDeliveryInfo"] = new Dictionary<string, object>
                    {
                        ["AccountId"] = request.JourneyDeliveryInfo.AccountId,
                        ["MessageBody"] = new Dictionary<string, object>
                        {
                            ["ContactKey"] = request.JourneyDeliveryInfo.MessageBody.ContactKey,
                            ["EventDefinitionKey"] = request.JourneyDeliveryInfo.MessageBody.EventDefinitionKey,
                            ["Data"] = new Dictionary<string, object>
                            {
                                ["SubscriberKey"] = request.JourneyDeliveryInfo.MessageBody.Data.SubscriberKey,
                                ["EmailAddress"] = request.JourneyDeliveryInfo.MessageBody.Data.EmailAddress ?? string.Empty,
                                ["SourceSystem"] = request.JourneyDeliveryInfo.MessageBody.Data.SourceSystem ?? string.Empty,
                                ["FirstName"] = request.JourneyDeliveryInfo.MessageBody.Data.FirstName ?? string.Empty,
                                ["AttachmentName"] = request.JourneyDeliveryInfo.MessageBody.Data.AttachmentName ?? string.Empty
                            }
                        }
                    };
                    Console.WriteLine("  - Added JourneyDeliveryInfo for ePresent delivery");
                }

                // 4. Add all other properties from request.AdditionalData LAST
                if (request.AdditionalData != null)
                {
                    foreach (var kvp in request.AdditionalData)
                    {
                        if (!orderedData.ContainsKey(kvp.Key))
                        {
                            orderedData[kvp.Key] = kvp.Value ?? string.Empty;
                        }
                    }
                }

                Console.WriteLine($"Built additionalData with {orderedData.Count} properties in correct order");
                return orderedData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building additionalData: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Reorders existing AdditionalData to ensure correct property order for VentureFusion
        /// </summary>
        private Dictionary<string, object> ReorderAdditionalData(Dictionary<string, object> existingData, string? deliveryMethod, SequentialProcess request)
        {
            try
            {
                var orderedData = new Dictionary<string, object>();

                // 1. Add fixed flags FIRST based on DeliveryMethod
                if (request.IsMailDelivery)
                {
                    orderedData["MailFlag"] = "Y";
                    orderedData["ImageFlag"] = "Y";
                }
                else if (request.IsEPresentDelivery)
                {
                    orderedData["MailFlag"] = "N";
                    orderedData["ImageFlag"] = "Y";
                    orderedData["EPresentFlag"] = "Y";
                }

                Console.WriteLine($"Reordering: DeliveryMethod='{deliveryMethod}', MailFlag={orderedData["MailFlag"]}, ImageFlag={orderedData["ImageFlag"]}");

                // 2. Add VersionCode
                if (existingData.ContainsKey("VersionCode"))
                    orderedData["VersionCode"] = existingData["VersionCode"];
                else
                    orderedData["VersionCode"] = request.CompositionInfo?.VersionCode ?? string.Empty;

                // 3. Add MailDeliveryInfo
                if (existingData.ContainsKey("MailDeliveryInfo") && existingData["MailDeliveryInfo"] is Dictionary<string, object> mailDeliveryDict)
                {
                    var reorderedMailDeliveryInfo = new Dictionary<string, object>();

                    if (mailDeliveryDict.ContainsKey("RecipientFullName"))
                        reorderedMailDeliveryInfo["RecipientFullName"] = mailDeliveryDict["RecipientFullName"];
                    if (mailDeliveryDict.ContainsKey("RecipientFirstName"))
                        reorderedMailDeliveryInfo["RecipientFirstName"] = mailDeliveryDict["RecipientFirstName"];
                    if (mailDeliveryDict.ContainsKey("RecipientLastName"))
                        reorderedMailDeliveryInfo["RecipientLastName"] = mailDeliveryDict["RecipientLastName"];

                    if (mailDeliveryDict.ContainsKey("RecipientAddress"))
                        reorderedMailDeliveryInfo["RecipientAddress"] = mailDeliveryDict["RecipientAddress"];

                    if (mailDeliveryDict.ContainsKey("NonUSAddressFlag"))
                    {
                        var value = mailDeliveryDict["NonUSAddressFlag"];
                        reorderedMailDeliveryInfo["NonUSAddressFlag"] = value is bool boolValue ? boolValue :
                            (value is string strValue && strValue.Equals("True", StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        reorderedMailDeliveryInfo["NonUSAddressFlag"] = false;
                    }

                    if (mailDeliveryDict.ContainsKey("BadAddressFlag"))
                    {
                        var value = mailDeliveryDict["BadAddressFlag"];
                        reorderedMailDeliveryInfo["BadAddressFlag"] = value is bool boolValue ? boolValue :
                            (value is string strValue && strValue.Equals("True", StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        reorderedMailDeliveryInfo["BadAddressFlag"] = false;
                    }

                    orderedData["MailDeliveryInfo"] = reorderedMailDeliveryInfo;
                }

                // 4. Add JourneyDeliveryInfo for ePresent (preserve from existing data)
                if (existingData.ContainsKey("JourneyDeliveryInfo"))
                {
                    orderedData["JourneyDeliveryInfo"] = existingData["JourneyDeliveryInfo"];
                }

                // 5. Add EPresentFlag if exists in existing data
                if (existingData.ContainsKey("EPresentFlag") && !orderedData.ContainsKey("EPresentFlag"))
                {
                    orderedData["EPresentFlag"] = existingData["EPresentFlag"];
                }

                // 6. Add all remaining properties LAST
                foreach (var kvp in existingData)
                {
                    if (!orderedData.ContainsKey(kvp.Key))
                    {
                        orderedData[kvp.Key] = kvp.Value;
                    }
                }

                Console.WriteLine($"Reordered {orderedData.Count} properties for VentureFusion");
                return orderedData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reordering additionalData: {ex.Message}");
                throw new PermanentFailureException(
                    "Unexpected error reordering additional data for VentureFusion request",
                    ex as HttpRequestException ?? new HttpRequestException("Unknown error in ReorderAdditionalData", ex),
                    NonRetryableExceptionType.Unknown
                );
            }
        }
    }

    public enum NonRetryableExceptionType
    {
        ExternalServiceError,
        Unknown
    }
}
