using ECMServicesDemo;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DocumentStatusSyncFunction.Services
{
    public class ECMServiceClientWrapper : IECMServiceClient
    {
        private readonly ECMServiceClient _client;
        private readonly ILogger<ECMServiceClientWrapper> _logger;

        public ECMServiceClientWrapper(ILogger<ECMServiceClientWrapper> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _client = new ECMServiceClient(ECMServiceClient.EndpointConfiguration.ECMServicePort);
        }

        public async Task<string> getTokenAsync(string repository, string superuser, string password)
        {
            if (string.IsNullOrWhiteSpace(repository))
                throw new ArgumentException("Repository cannot be null or empty.", nameof(repository));
            if (string.IsNullOrWhiteSpace(superuser))
                throw new ArgumentException("Superuser cannot be null or empty.", nameof(superuser));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty.", nameof(password));

            try
            {
                _logger.LogInformation("Requesting authentication token for repository: {Repository}, user: {User}", repository, superuser);
                var response = await _client.getTokenAsync(repository, superuser, password);
                _logger.LogInformation("Successfully retrieved authentication token for repository: {Repository}", repository);
                return response.@return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve authentication token for repository: {Repository}, user: {User}", repository, superuser);
                throw;
            }
        }

        public async Task<string> execDQLAsync(DocumentumSecurityToken token, string user, string repo, string query)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));
            if (string.IsNullOrWhiteSpace(user))
                throw new ArgumentException("User cannot be null or empty.", nameof(user));
            if (string.IsNullOrWhiteSpace(repo))
                throw new ArgumentException("Repository cannot be null or empty.", nameof(repo));
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query cannot be null or empty.", nameof(query));

            try
            {
                _logger.LogInformation("Executing DQL query for repository: {Repository}, user: {User}, query: {Query}", repo, user, query);
                var response = await _client.execDQLAsync(token, user, repo, query);
                _logger.LogInformation("Successfully executed DQL query for repository: {Repository}", repo);
                return response.@return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute DQL query for repository: {Repository}, user: {User}, query: {Query}", repo, user, query);
                throw;
            }
        }
    }
}