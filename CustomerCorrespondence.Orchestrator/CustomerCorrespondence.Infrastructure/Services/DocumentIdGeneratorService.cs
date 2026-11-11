using CustomerCorrespondence.Application.Interfaces;
using CustomerCorrespondence.Domain.Entities;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;


namespace CustomerCorrespondence.Infrastructure.Services
{
    public class DocumentIdGeneratorService : IDocumentIdGeneratorService
    {
        private readonly HttpClient _httpClient;
        private readonly ISecretsCacheService _secretsCache;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private const string bearerTokenCacheKey = "DocIDBearerToken";

        public DocumentIdGeneratorService(
            HttpClient httpClient,
            ISecretsCacheService secretsCache,
            IConfiguration configuration,
            IServiceProvider serviceProvider)
        {
            _httpClient = httpClient;
            _secretsCache = secretsCache;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public async Task<string?> GenerateDocIdAsync()
        {
            string? docIDReturned = string.Empty;

            var bearerToken = await GetBearerTokenAsync();

            var endpointURL = _configuration["SecretNames:DocIDApiEndpointUrl"];
            if (string.IsNullOrEmpty(endpointURL))
                throw new InvalidOperationException("SecretNames:DocIDApiEndpointUrl is not configured");
            var url = await _secretsCache.GetSecretAsync(endpointURL);

            // SSRF mitigation: Validate the URL before making the request
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttps && uriResult.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("The endpoint URL is not a valid absolute HTTP/HTTPS URL.");
            }

            // Additional SSRF mitigation: Only allow URLs to trusted hosts
            var allowedHosts = new[] { "dev-apimanager.S9uareRootLLC.com", "demo-apimanager.S9uareRootLLC.com", "apimanager.S9uareRootLLC.com" };
            if (!allowedHosts.Contains(uriResult.Host, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The endpoint host is not allowed.");
            }

            var docSoapAction = _configuration["SecretNames:MultiDocSoapAction"];
            if (string.IsNullOrEmpty(docSoapAction))
                throw new InvalidOperationException("SecretNames:MultiDocSoapAction is not configured");
            var soapAction = await _secretsCache.GetSecretAsync(docSoapAction);

            var envlpSoap = _configuration["SecretNames:MultiDocSoapEnvelope"];
            if (string.IsNullOrEmpty(envlpSoap))
                throw new InvalidOperationException("SecretNames:MultiDocSoapEnvelope is not configured");
            var soapEnvelope = await _secretsCache.GetSecretAsync(envlpSoap);


            var docIdCount = _configuration["SecretNames:DocIdCount"];
            if (string.IsNullOrEmpty(docIdCount))
                throw new InvalidOperationException("SecretNames:DocIdCount is not configured");
            var countDocIDRetrieved = await _secretsCache.GetSecretAsync(docIdCount);

            // Replace the intNumberOfIds value with your desired count
            if (!int.TryParse(countDocIDRetrieved, out var countDocIDRetrievedInt))
                throw new InvalidOperationException("DocIdCount secret is not a valid integer.");
            soapEnvelope = ReplaceSoapEnvelopeIdCount(soapEnvelope, countDocIDRetrievedInt);

            var subKey = _configuration["SecretNames:DocSubscriptionKey"];
            if (string.IsNullOrEmpty(subKey))
                throw new InvalidOperationException("SecretNames:DocSubscriptionKey is not configured");
            var subscriptionKey = await _secretsCache.GetSecretAsync(subKey);

            var request = CreateDocIdRequest(url, soapEnvelope, soapAction, subscriptionKey, bearerToken);

            try
            {
                var response = await _httpClient.SendAsync(request);

                // If unauthorized, refresh the token and retry once
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _secretsCache.RemoveSecret(bearerTokenCacheKey);
                    bearerToken = await GetBearerTokenAsync();

                    // Recreate the request since HttpRequestMessage can only be sent once
                    request = CreateDocIdRequest(url, soapEnvelope, soapAction, subscriptionKey, bearerToken);

                    response = await _httpClient.SendAsync(request);
                }

                response.EnsureSuccessStatusCode();

                var xmlResponse = await response.Content.ReadAsStringAsync();

                var xmlDocument = XDocument.Parse(xmlResponse);
                XNamespace tempuriNamespace = "http://tempuri.org/";

                XNamespace soapNs = "http://schemas.xmlsoap.org/soap/envelope/";

                // Find all <string> elements under <GenerateIDsResult>
                var idStrings = xmlDocument
                    .Descendants(soapNs + "Body")
                    .Descendants(tempuriNamespace + "GenerateIDsResult")
                    .Elements(tempuriNamespace + "string")
                    .Select(e => e.Value)
                    .ToList();

                // Insert the document IDs into the database
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<CorrespondenceDBContext>();
                        foreach (var docId in idStrings)
                        {
                            var entity = new DocumentIdEntity { DocIds = docId, DocIdStatus = "New", RequestCreationTS = DateTime.UtcNow };
                            dbContext.DocumentIds.Add(entity);
                        }
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) // 208 = Invalid object name (table does not exist)
                {
                    // Log or handle the error as needed
                    // Optionally: return null or throw a custom exception
                    throw new InvalidOperationException($"Table does not exist: {ex.Message}", ex);

                }

                // Return the first document ID (or null if none)
                docIDReturned = idStrings.FirstOrDefault() ?? string.Empty;
                

                return docIDReturned;

            }
            catch (HttpRequestException ex)
            {
                // Handle network errors, DNS failures, connection refused, etc.
                throw new InvalidOperationException($"Failed to communicate with DocID API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // Handle request timeout
                throw new InvalidOperationException("DocID API request timed out.", ex);
            }


        }

        private async Task<string> GetBearerTokenAsync()
        {
            // Check cache for bearer token
            if (_secretsCache.IsSecretCached(bearerTokenCacheKey))
            {
                var cachedToken = _secretsCache.GetCachedSecret(bearerTokenCacheKey);
                if (!string.IsNullOrEmpty(cachedToken))
                    return cachedToken;
            }

            var bearerEndpoint = _configuration["SecretNames:BearerTokenEndpoint"];
            if (string.IsNullOrEmpty(bearerEndpoint))
                throw new InvalidOperationException("SecretNames:BearerTokenEndpoint is not configured");
            var tokenEndpoint = await _secretsCache.GetSecretAsync(bearerEndpoint);

            var apiClientID = _configuration["SecretNames:BearerServiceAccountClientId"];
            if (string.IsNullOrEmpty(apiClientID))
                throw new InvalidOperationException("SecretNames:BearerServiceAccountClientId is not configured");
            var clientId = await _secretsCache.GetSecretAsync(apiClientID);

            var bearerScope = _configuration["SecretNames:BearerTokenScope"];
            if (string.IsNullOrEmpty(bearerScope))
                throw new InvalidOperationException("SecretNames:BearerTokenScope is not configured");
            var scope = await _secretsCache.GetSecretAsync(bearerScope);

            var subKey = _configuration["SecretNames:DocSubscriptionKey"];
            if (string.IsNullOrEmpty(subKey))
                throw new InvalidOperationException("SecretNames:DocSubscriptionKey is not configured");
            var subscriptionKey = await _secretsCache.GetSecretAsync(subKey);

            var saSecret = _configuration["SecretNames:SAECCOM"];
            if (string.IsNullOrEmpty(saSecret))
                throw new InvalidOperationException("SecretNames:SAECCOM is not configured");
            var secret = await _secretsCache.GetSecretAsync(saSecret);

            // SSRF mitigation: Validate the token endpoint URL
            if (!Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out var uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttps && uriResult.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("The token endpoint URL is not a valid absolute HTTP/HTTPS URL.");
            }

            // Only allow URLs to trusted hosts
            var allowedHosts = new[] { "dev-apimanager.S9uareRootLLC.com", "demo-apimanager.S9uareRootLLC.com", "apimanager.S9uareRootLLC.com" }; // Add your trusted token hosts
            if (!allowedHosts.Contains(uriResult.Host, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The token endpoint host is not allowed.");
            }

            var url = $"{tokenEndpoint}?subscription-key={subscriptionKey}";
            var keyValues = new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("scope", scope),
                new KeyValuePair<string, string>("username", clientId),
                new KeyValuePair<string, string>("password", secret)
            };
            var requestBody = new FormUrlEncodedContent(keyValues);

            var response = await _httpClient.PostAsync(url, requestBody);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();

            using var jsonDocument = JsonDocument.Parse(responseContent);
            if (!jsonDocument.RootElement.TryGetProperty("access_token", out var accessTokenElement) || accessTokenElement.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("access_token not found in token response.");

            var accessToken = accessTokenElement.GetString()!;

            // Store bearer token in cache
            _secretsCache.RemoveSecret(bearerTokenCacheKey); // Remove if exists to refresh

            //Store the token with an expiration time slightly less than the actual token lifetime to ensure we refresh before it expires
            _secretsCache.SetSecret(bearerTokenCacheKey, accessToken, TimeSpan.FromMinutes(29));

            return accessToken;

        }
        private HttpRequestMessage CreateDocIdRequest(string url, string soapEnvelope, string soapAction, string subscriptionKey, string bearerToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml")
            };
            request.Headers.Add("SOAPAction", soapAction);
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            return request;
        }

        private string ReplaceSoapEnvelopeIdCount(string soapEnvelope, int newIdCount)
        {
            var xmlDocument = XDocument.Parse(soapEnvelope);
            XNamespace tempuriNamespace = "http://tempuri.org/";
            
            var intNumberOfIdsElement = xmlDocument
                .Descendants(tempuriNamespace + "intNumberOfIds")
                .FirstOrDefault();
            
            if (intNumberOfIdsElement != null)
            {
                intNumberOfIdsElement.Value = newIdCount.ToString();
            }
            
            return xmlDocument.ToString(SaveOptions.DisableFormatting);
        }
    }
}
