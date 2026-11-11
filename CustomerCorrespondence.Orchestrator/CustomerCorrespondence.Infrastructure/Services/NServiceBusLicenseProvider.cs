using CustomerCorrespondence.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CustomerCorrespondence.Infrastructure.Services
{
    public class NServiceBusLicenseProvider
    {
        private readonly ISecretsCacheService _secretsCacheService;
        private string? _cachedLicenseKey;
        private readonly IConfiguration _configuration;

        public NServiceBusLicenseProvider(ISecretsCacheService secretsCacheService, IConfiguration configuration)
        {
            _secretsCacheService = secretsCacheService;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }
        public async Task<string?> GetLicenseKeyAsync()
        {
            if (_cachedLicenseKey != null)
                return _cachedLicenseKey;

            // Get the name of the secret from configuration to match existing pattern and avoid CVE 259 from Veracode
            var nserviceBusLicenseName = _configuration["SecretNames:NServiceBusLicense"];
            if (string.IsNullOrEmpty(nserviceBusLicenseName))
                throw new InvalidOperationException("SecretNames:NServiceBusLicense is not configured");
            _cachedLicenseKey = await _secretsCacheService.GetSecretAsync(nserviceBusLicenseName);
            return _cachedLicenseKey;
        }
    }
}