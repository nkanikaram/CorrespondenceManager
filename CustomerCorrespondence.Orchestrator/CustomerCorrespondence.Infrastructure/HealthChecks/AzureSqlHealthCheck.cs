using Azure.Core;
using Azure.Identity;
using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CustomerCorrespondence.Infrastructure.HealthChecks
{
    public class AzureSqlHealthCheck : IHealthCheck
    {
        private readonly IConfiguration _configuration;
        private readonly ISecretsCacheService _secretsCache;
        private readonly ILogger<AzureSqlHealthCheck> _logger;

        public AzureSqlHealthCheck(IConfiguration configuration, ISecretsCacheService secretsCache, ILogger<AzureSqlHealthCheck> logger)
        {
            _configuration = configuration;
            _secretsCache = secretsCache;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Starting Azure SQL Database health check");

                var dbSecretName = _configuration["SecretNames:AzureDbConnectionString"];
                if (string.IsNullOrWhiteSpace(dbSecretName))
                {
                    return HealthCheckResult.Unhealthy("Database secret name not configured");
                }

                var baseConnectionString = await _secretsCache.GetSecretAsync(dbSecretName);
                if (string.IsNullOrWhiteSpace(baseConnectionString))
                {
                    return HealthCheckResult.Unhealthy("Database connection string is empty");
                }

                // Clean the connection string for managed identity (same as main app)
                var connectionStringBuilder = new SqlConnectionStringBuilder(baseConnectionString);
                connectionStringBuilder.Remove("Authentication");
                connectionStringBuilder.Remove("User ID");
                connectionStringBuilder.Remove("Password");

                // Use the same managed identity approach as the main application
                var azureClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
                TokenCredential credential;

                if (!string.IsNullOrWhiteSpace(azureClientId))
                {
                    _logger.LogDebug("Using specific managed identity for SQL health check: {ClientId}", azureClientId?.Replace("{", "{{").Replace("}", "}}"));
                    credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                    {
                        ManagedIdentityClientId = azureClientId
                    });
                }
                else
                {
                    _logger.LogDebug("Using default managed identity for SQL health check");
                    credential = new DefaultAzureCredential();
                }

                using var connection = new SqlConnection(connectionStringBuilder.ConnectionString);

                // Acquire access token for Azure SQL using the same managed identity
                var tokenRequestContext = new TokenRequestContext(new[] { "https://database.windows.net/.default" });
                var token = await credential.GetTokenAsync(tokenRequestContext, cancellationToken);

                connection.AccessToken = token.Token;
                _logger.LogDebug("Access token acquired successfully for SQL health check");

                await connection.OpenAsync(cancellationToken);
                _logger.LogDebug("SQL connection opened successfully");

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1 AS HealthCheck";
                command.CommandTimeout = 10; // Quick timeout for health check
                var result = await command.ExecuteScalarAsync(cancellationToken);

                var isHealthy = result?.ToString() == "1";
                _logger.LogDebug("SQL health check query result: " + System.Net.WebUtility.HtmlEncode(result?.ToString() ?? "null"));

                return isHealthy
                    ? HealthCheckResult.Healthy($"Database connection successful. Server: {connectionStringBuilder.DataSource}")
                    : HealthCheckResult.Unhealthy("Database query returned unexpected result");
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL Server error during health check. Error Number: " + sqlEx.Number + ", State: " + sqlEx.State + ", Class: " + sqlEx.Class);
                return HealthCheckResult.Unhealthy($"SQL Server error: {sqlEx.Message} (Error: {sqlEx.Number})", sqlEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed with exception: " + ex.GetType().Name);
                return HealthCheckResult.Unhealthy($"Database connection failed: {ex.Message}", ex);
            }
        }
    }
}
