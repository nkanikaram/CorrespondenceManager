using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace CustomerCorrespondence.Infrastructure.Services
{
    /// <summary>
    /// Provides caching functionality for secrets retrieved from Azure Key Vault to improve performance and reduce API calls.
    /// </summary>
    public class SecretsCacheService : ISecretsCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly IKeyVaultService _keyVaultService;  // Changed from concrete KeyVaultService to interface
        private readonly IConfiguration _configuration;
        private readonly ILogger<SecretsCacheService> _logger;
        private readonly TimeSpan _cacheDuration;
        private readonly bool _isDevelopment;

        /// <summary>
        /// Initializes a new instance of the <see cref="SecretsCacheService"/> class.
        /// </summary>
        /// <param name="cache">The memory cache instance.</param>
        /// <param name="keyVaultService">The Key Vault service.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="environment">The host environment.</param>
        public SecretsCacheService(
            IMemoryCache cache,
            IKeyVaultService keyVaultService,  // Changed from concrete KeyVaultService to interface
            IConfiguration configuration,
            ILogger<SecretsCacheService> logger,
            IHostEnvironment environment)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _keyVaultService = keyVaultService ?? throw new ArgumentNullException(nameof(keyVaultService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _isDevelopment = (environment ?? throw new ArgumentNullException(nameof(environment))).IsDevelopment();

            var durationMinutes = GetCacheDurationMinutes();
            _cacheDuration = TimeSpan.FromMinutes(durationMinutes);

            _logger.LogInformation("SecretsCacheService initialized with {DurationMinutes} minute cache duration", durationMinutes);
        }

        private int GetCacheDurationMinutes()
        {
            try
            {
                return _configuration.GetValue<int>("SecretsCache:DurationMinutes", 240);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read SecretsCache:DurationMinutes configuration, using default 240 minutes");
                return 240;
            }
        }

        /// <summary>
        /// Retrieves a secret from cache or Key Vault if not cached.
        /// </summary>
        /// <param name="secretName">The name of the secret to retrieve.</param>
        /// <returns>The secret value.</returns>
        /// <exception cref="SecretNotFoundException">Thrown when secret is not found.</exception>
        /// <exception cref="CorrespondenceException">Thrown when other errors occur.</exception>
        public async Task<string> GetSecretAsync(string secretName)
        {
            if (string.IsNullOrWhiteSpace(secretName))
                throw new ArgumentNullException(nameof(secretName));

            var cacheKey = $"secret:{secretName}";

            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out string? cachedValue))
            {
                return cachedValue ?? string.Empty;
            }

            // Local development override: skip Key Vault entirely for secrets configured under
            // LocalSecretOverrides (e.g. PGPPublicKey/PGPPrivateKey/PGPPassphrase for local testing).
            if (_isDevelopment)
            {
                var localOverride = _configuration[$"LocalSecretOverrides:{secretName}"];
                if (!string.IsNullOrEmpty(localOverride))
                {
                    _logger.LogWarning("Using LocalSecretOverrides value for secret '{SecretName}' (Development)", secretName);
                    _cache.Set(cacheKey, localOverride, _cacheDuration);
                    return localOverride;
                }
            }

            _logger.LogDebug("Cache miss for secret '{SecretName}', fetching from Key Vault", secretName);

            try
            {
                var secret = await _keyVaultService.GetSecretAsync(secretName);

                if (!string.IsNullOrEmpty(secret))
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _cacheDuration,
                        Priority = CacheItemPriority.High
                    };

                    cacheOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = (key, value, reason, state) =>
                        {
                            _logger.LogDebug("Cache eviction: Key '{Key}', Reason: {Reason}", key, reason);
                        }
                    });

                    _cache.Set(cacheKey, secret, cacheOptions);
                    _logger.LogDebug("Cached secret '{SecretName}' for {DurationMinutes} minutes", secretName, _cacheDuration.TotalMinutes);
                }
                else
                {
                    _logger.LogWarning("Key Vault returned null or empty value for secret '{SecretName}'", secretName);
                }

                return secret ?? string.Empty;
            }
            catch (SecretNotFoundException ex)
            {
                _logger.LogError(ex, "Secret '{SecretName}' not found in Key Vault", secretName);
                throw;
            }
            catch (CorrespondenceException ex)
            {
                _logger.LogError(ex, "Failed to retrieve secret '{SecretName}' from Key Vault. ErrorCode: {ErrorCode}", secretName, ex.ErrorCode);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving secret '{SecretName}'", secretName);
                throw new CorrespondenceException($"Unexpected error retrieving secret '{secretName}'", "SECRET_CACHE_ERROR", ex);
            }
        }
        /// <summary>
        /// Determines whether a secret is currently cached.
        /// </summary>
        /// <param name="secretName">The name of the secret.</param>
        /// <returns><c>true</c> if the secret is cached; otherwise, <c>false</c>.</returns>
        public bool IsSecretCached(string secretName)
        {
            if (string.IsNullOrWhiteSpace(secretName))
                throw new ArgumentNullException(nameof(secretName));

            var cacheKey = $"secret:{secretName}";
            return _cache.TryGetValue(cacheKey, out _);
        }
        /// <summary>
        /// Gets a cached secret value without fetching from Key Vault.
        /// </summary>
        /// <param name="secretName">The name of the secret.</param>
        /// <returns>The cached secret value, or <c>null</c> if not cached.</returns>
        public string? GetCachedSecret(string secretName)
        {
            if (string.IsNullOrWhiteSpace(secretName))
                throw new ArgumentNullException(nameof(secretName));

            var cacheKey = $"secret:{secretName}";
            if (_cache.TryGetValue(cacheKey, out string? value))
            {
                return value;
            }
            return null;
        }
        /// <summary>
        /// Clears all cached secrets.
        /// </summary>
        /// <exception cref="NotSupportedException">Thrown when the cache is not a <see cref="ClearableMemoryCache"/>.</exception>
        public void ClearCache()
        {
            if (_cache is ClearableMemoryCache clearableCache)
            {
                clearableCache.Clear();
                _logger.LogDebug("All secrets cleared from cache.");
            }
            else
            {
                throw new NotSupportedException("ClearCache is only supported when using ClearableMemoryCache.");
            }
        }
        /// <summary>
        /// Removes a specific secret from the cache.
        /// </summary>
        /// <param name="secretName">The name of the secret to remove.</param>
        public void RemoveSecret(string secretName)
        {
            if (string.IsNullOrWhiteSpace(secretName))
                throw new ArgumentNullException(nameof(secretName));

            var cacheKey = $"secret:{secretName}";
            _cache.Remove(cacheKey);
            _logger.LogDebug("Secret '{SecretName}' removed from cache.", secretName);
        }
        /// <summary>
        /// Gets the names of all currently cached secrets.
        /// </summary>
        /// <returns>A collection of cached secret names.</returns>
        /// <exception cref="NotSupportedException">Thrown when the cache is not a <see cref="ClearableMemoryCache"/>.</exception>
        public IEnumerable<string> GetCachedSecretNames()
        {
            if (_cache is ClearableMemoryCache clearableCache)
            {
                return clearableCache.GetKeys()
                    .Where(k => k is string s && s.StartsWith("secret:"))
                    .Select(k => ((string)k).Substring("secret:".Length))
                    .ToList();
            }
            throw new NotSupportedException("GetCachedSecretNames is only supported when using ClearableMemoryCache.");
        }
        /// <summary>
        /// Sets a secret value in the cache with a specified duration.
        /// </summary>
        /// <param name="secretName">The name of the secret.</param>
        /// <param name="value">The secret value to cache.</param>
        /// <param name="duration">The cache duration.</param>
        public void SetSecret(string secretName, string value, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(secretName))
                throw new ArgumentNullException(nameof(secretName));

            var cacheKey = $"secret:{secretName}";
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration,
                Priority = CacheItemPriority.High
            };
            cacheOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
            {
                EvictionCallback = (key, val, reason, state) =>
                {
                    _logger.LogDebug("Cache eviction: Key '{Key}' evicted. Reason: {Reason}, Time: {Time}",
                        key, reason, DateTime.UtcNow);
                }
            });
            _cache.Set(cacheKey, value, cacheOptions);
            _logger.LogDebug("Secret '{SecretName}' set in cache for {DurationMinutes} minutes.", secretName, duration.TotalMinutes);
        }
    }
}
