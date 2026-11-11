using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.HealthChecks
{
    public class KeyVaultHealthCheck : IHealthCheck
    {
        private readonly ISecretsCacheService _secretsCache;
        private readonly IConfiguration _configuration;

        public KeyVaultHealthCheck(ISecretsCacheService secretsCache, IConfiguration configuration)
        {
            _secretsCache = secretsCache;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Changed from "NServicebuslicense" to "NServicebusLicense"  
                //var secret = await _secretsCache.GetSecretAsync("NServicebusLicense");
                // Get the name of the secret from configuration to match existing pattern and avoid CVE 259 from Veracode
                var nserviceBusLicenseName = _configuration["SecretNames:NServiceBusLicense"];
                if (string.IsNullOrEmpty(nserviceBusLicenseName))
                    throw new InvalidOperationException("SecretNames:NServiceBusLicense is not configured");
                var secret = await _secretsCache.GetSecretAsync(nserviceBusLicenseName);
                if (string.IsNullOrWhiteSpace(secret))
                    return HealthCheckResult.Unhealthy("KeyVault secret 'NServicebusLicense' missing or empty.");

                return HealthCheckResult.Healthy("KeyVault accessible, NServiceBus license retrieved");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy($"KeyVault unreachable: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
}
