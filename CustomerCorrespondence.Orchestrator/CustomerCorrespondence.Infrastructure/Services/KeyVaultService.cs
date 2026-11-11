using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using CustomerCorrespondence.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Microsoft.Extensions.Logging;
using CustomerCorrespondence.Domain.Interfaces;

namespace CustomerCorrespondence.Infrastructure.Services
{
    public class KeyVaultService : IKeyVaultService
    {
        private readonly SecretClient _secretClient;
        private readonly DefaultAzureCredential _credential;
        private readonly ILogger<KeyVaultService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        public KeyVaultService(IConfiguration configuration, ILogger<KeyVaultService> logger)
        {
            // Validate parameters first before accessing them
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var keyVaultUrl = configuration["KeyVault:Url"];
            var managedIdentityClientId = configuration["KeyVault:ManagedIdentityClientId"]
                ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");

            if (string.IsNullOrWhiteSpace(keyVaultUrl))
            {
                _logger.LogError("KeyVault:Url configuration value is required and cannot be null or empty");
                throw new CorrespondenceException(
                    "KeyVault:Url configuration value is required and cannot be null or empty.",
                    "KEYVAULT_CONFIG_MISSING");
            }

            try
            {
                // Use DefaultAzureCredential with ManagedIdentityClientId if provided
                var credentialOptions = new DefaultAzureCredentialOptions();
                if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
                {
                    credentialOptions.ManagedIdentityClientId = managedIdentityClientId;
                    _logger.LogInformation("KeyVaultService initialized with Managed Identity Client ID: {ClientId}", managedIdentityClientId);
                }

                _credential = new DefaultAzureCredential(credentialOptions);
                _secretClient = new SecretClient(new Uri(keyVaultUrl), _credential);

                // Configure retry policy for transient failures (throttling, service unavailable, timeout)
                _retryPolicy = Policy
                    .Handle<Azure.RequestFailedException>(ex => IsTransientError(ex))
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (exception, timeSpan, retry, ctx) =>
                        {
                            _logger.LogWarning(
                                exception,
                                "Key Vault retry {Retry}/3 after {Delay}s delay due to: {Message}",
                                retry,
                                timeSpan.TotalSeconds,
                                exception.Message);
                        });

                _logger.LogInformation("KeyVaultService initialized successfully for Key Vault: {KeyVaultUrl}", keyVaultUrl);
            }
            catch (Exception ex) when (ex is not CorrespondenceException)
            {
                _logger.LogError(ex, "Failed to initialize KeyVaultService");
                throw new CorrespondenceException("Failed to initialize KeyVaultService", "KEYVAULT_INIT_FAILED", ex);
            }
        }

        /// <summary>
        /// Retrieves a secret value from Azure Key Vault by its name.
        /// </summary>
        /// <param name="secretName">The name of the secret to retrieve.</param>
        /// <returns>The value of the secret, or null if not found.</returns>
        /// <exception cref="SecretNotFoundException">Thrown when secret is not found in Key Vault</exception>
        /// <exception cref="CorrespondenceException">Thrown when access is denied or other errors occur</exception>
        public async Task<string?> GetSecretAsync(string secretName)
        {
            if (string.IsNullOrWhiteSpace(secretName))
            {
                _logger.LogWarning("GetSecretAsync called with null or empty secret name");
                throw new ArgumentNullException(nameof(secretName));
            }

            try
            {
                _logger.LogDebug("Retrieving secret '{SecretName}' from Key Vault", secretName);

                var secret = await _retryPolicy.ExecuteAsync(async () =>
                    await _secretClient.GetSecretAsync(secretName));

                _logger.LogDebug("Successfully retrieved secret '{SecretName}'", secretName);
                return secret.Value.Value;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Secret '{SecretName}' not found in Key Vault", secretName);
                throw new SecretNotFoundException(secretName, ex);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 403)
            {
                _logger.LogError(ex, "Access denied to secret '{SecretName}' in Key Vault. Check Key Vault permissions and managed identity configuration", secretName);
                throw new CorrespondenceException(
                    $"Access denied to secret '{secretName}' in Key Vault. Check Key Vault permissions and managed identity configuration",
                    "KEYVAULT_ACCESS_DENIED",
                    ex);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 429)
            {
                _logger.LogError(ex, "Key Vault throttling limit exceeded for secret '{SecretName}'", secretName);
                throw new CorrespondenceException(
                    $"Key Vault throttling limit exceeded for secret '{secretName}'",
                    "KEYVAULT_THROTTLED",
                    ex);
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to retrieve secret '{SecretName}' from Key Vault. Status: {Status}", secretName, ex.Status);
                throw new CorrespondenceException(
                    $"Failed to retrieve secret '{secretName}' from Key Vault. Status: {ex.Status}",
                    "KEYVAULT_REQUEST_FAILED",
                    ex);
            }
            catch (Exception ex) when (ex is not SecretNotFoundException && ex is not CorrespondenceException)
            {
                _logger.LogError(ex, "Unexpected error retrieving secret '{SecretName}' from Key Vault", secretName);
                throw new CorrespondenceException(
                    $"Unexpected error retrieving secret '{secretName}' from Key Vault",
                    "KEYVAULT_UNEXPECTED_ERROR",
                    ex);
            }
        }

        /// <summary>
        /// Determines if an Azure RequestFailedException represents a transient error that should be retried.
        /// </summary>
        private static bool IsTransientError(Azure.RequestFailedException ex)
        {
            // Retry on throttling (429), service unavailable (503), timeout (408), or gateway timeout (504)
            return ex.Status == 429 || ex.Status == 503 || ex.Status == 408 || ex.Status == 504;
        }
    }
}