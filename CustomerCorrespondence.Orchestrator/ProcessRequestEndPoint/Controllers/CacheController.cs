using CustomerCorrespondence.Domain.Interfaces;
using CustomerCorrespondence.Infrastructure.Services;
using CustomerCorrespondence.Exceptions;
using CustomerCorrespondence.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace ProcessRequestEndPoint.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "ECCOUsersOrDevelopers")]
    public class CacheController : ControllerBase
    {
        private readonly IMemoryCache _cache;
        private readonly ISecretsCacheService _secretsCache;

        public CacheController(IMemoryCache cache, ISecretsCacheService secretsCache)
        {
            _cache = cache;
            _secretsCache = secretsCache;
        }

        [HttpPost("clear-cache")]
        public IActionResult ClearCache()
        {
            try
            {
                if (_cache is ClearableMemoryCache clearable)
                {
                    var keysBefore = clearable.GetKeys().ToList();
                    clearable.Clear();
                    return Ok(new
                    {
                        success = true,
                        message = "Cache cleared successfully",
                        entriesCleared = keysBefore.Count,
                        clearedKeys = keysBefore.Take(10)
                    });
                }

                // Fallback to SecretsCacheService
                _secretsCache.ClearCache();
                return Ok(new
                {
                    success = true,
                    message = "Cache cleared using SecretsCacheService"
                });
            }
            catch (CorrespondenceException ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Failed to clear cache",
                    ErrorCode = ex.ErrorCode ?? "CACHE_CLEAR_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_OPERATION_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("all-entries")]
        public ActionResult<object> GetAllEntries()
        {
            try
            {
                if (_cache is ClearableMemoryCache clearable)
                {
                    var keys = clearable.GetKeys().ToList();
                    var entries = keys.Select(key =>
                    {
                        object? value = null;
                        var hasValue = _cache.TryGetValue(key, out value);
                        return new
                        {
                            Key = key?.ToString() ?? "null",
                            HasValue = hasValue,
                            ValueType = value?.GetType().Name ?? "null",
                            ValueLength = value is string str ? str.Length : (value?.ToString()?.Length ?? 0)
                        };
                    }).ToList();

                    return Ok(new
                    {
                        totalEntries = entries.Count,
                        entries = entries,
                        cacheType = _cache.GetType().Name
                    });
                }

                return Ok(new
                {
                    message = "Cache inspection not supported with current cache implementation",
                    cacheType = _cache.GetType().Name,
                    entries = new object[0]
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_INSPECTION_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("secrets")]
        public ActionResult<object> GetCachedSecrets()
        {
            try
            {
                var secretNames = _secretsCache.GetCachedSecretNames().ToList();

                return Ok(new
                {
                    totalSecrets = secretNames.Count,
                    secrets = secretNames.Select(name => new
                    {
                        secretName = name,
                        cacheKey = $"secret:{name}",
                        isCached = _cache.TryGetValue($"secret:{name}", out var value),
                        valueLength = value is string str ? str.Length : 0
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_SECRETS_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("monitor")]
        public ActionResult<object> MonitorCache()
        {
            try
            {
                var cacheInfo = new
                {
                    cacheType = _cache.GetType().Name,
                    timestamp = DateTime.UtcNow,
                    isClearableCache = _cache is ClearableMemoryCache
                };

                if (_cache is ClearableMemoryCache clearable)
                {
                    var keys = clearable.GetKeys().ToList();
                    var detailedEntries = keys.Select(key =>
                    {
                        var hasValue = _cache.TryGetValue(key, out var value);
                        return new
                        {
                            key = key?.ToString(),
                            hasValue = hasValue,
                            valueType = value?.GetType().Name,
                            valueLength = value is string str ? str.Length : (value?.ToString()?.Length ?? 0),
                            isSecret = key?.ToString()?.StartsWith("secret:") == true,
                            secretName = key is string s && s.StartsWith("secret:") ? s["secret:".Length..] : null
                        };
                    }).ToList();

                    return Ok(new
                    {
                        cacheInfo.cacheType,
                        cacheInfo.timestamp,
                        cacheInfo.isClearableCache,
                        totalKeys = keys.Count,
                        secretKeys = detailedEntries.Count(e => e.isSecret),
                        otherKeys = detailedEntries.Count(e => !e.isSecret),
                        entries = detailedEntries
                    });
                }

                return Ok(cacheInfo);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_MONITOR_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpDelete("secret/{secretName}")]
        public IActionResult RemoveSecret(string secretName)
        {
            try
            {
                _secretsCache.RemoveSecret(secretName);
                return Ok(new
                {
                    success = true,
                    message = $"Secret '{secretName}' removed from cache"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_REMOVE_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("secret/{configSecretName}")]
        public async Task<ActionResult<object>> GetSecretByName(string configSecretName)
        {
            try
            {
                Console.WriteLine($"[CacheController] === GET SECRET BY CONFIG NAME: {configSecretName} ===");

                // Get the actual Key Vault secret name from configuration
                var configKey = $"SecretNames:{configSecretName}";
                var actualSecretName = HttpContext.RequestServices.GetRequiredService<IConfiguration>()[configKey];

                if (string.IsNullOrEmpty(actualSecretName))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = $"Configuration key '{configKey}' not found or empty",
                        ErrorCode = "CONFIG_NOT_FOUND",
                        Details = System.Text.Json.JsonSerializer.Serialize(new { configSecretName }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                var cacheKey = $"secret:{actualSecretName}";
                var timestamp = DateTime.UtcNow;

                Console.WriteLine($"[CacheController] Config name: {configSecretName}");
                Console.WriteLine($"[CacheController] Actual secret name: {actualSecretName}");
                Console.WriteLine($"[CacheController] Cache key: {cacheKey}");

                // Check if secret is in cache using the correct ke
                var isInCache = _cache.TryGetValue(cacheKey, out var cachedValue);

                if (isInCache)
                {
                    Console.WriteLine($"[CacheController] Secret '{actualSecretName}' found in cache");
                    return Ok(new
                    {
                        success = true,
                        configSecretName = configSecretName,
                        actualSecretName = actualSecretName,
                        cacheKey = cacheKey,
                        source = "cache",
                        isInCache = true,
                        valueLength = cachedValue is string str ? str.Length : 0,
                        timestamp = timestamp,
                        hasValue = cachedValue != null
                    });
                }
                else
                {
                    Console.WriteLine($"[CacheController] Secret '{actualSecretName}' not found in cache, fetching from KeyVault...");

                    // Get from KeyVault via SecretsCacheService (this will cache it with the correct key)
                    var startTime = DateTime.UtcNow;
                    var secret = await _secretsCache.GetSecretAsync(actualSecretName);
                    var duration = DateTime.UtcNow - startTime;

                    return Ok(new
                    {
                        success = true,
                        configSecretName = configSecretName,
                        actualSecretName = actualSecretName,
                        cacheKey = cacheKey,
                        source = "keyvault",
                        isInCache = false,
                        valueLength = secret?.Length ?? 0,
                        retrievalDuration = duration.TotalMilliseconds,
                        timestamp = timestamp,
                        hasValue = !string.IsNullOrEmpty(secret),
                        nowCached = true
                    });
                }
            }
            catch (SecretNotFoundException ex)
            {
                return NotFound(new ErrorResponse
                {
                    Message = $"Secret not found: {configSecretName}",
                    ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { configSecretName, secretName = ex.SecretName }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (CorrespondenceException ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    Message = "Error retrieving secret",
                    ErrorCode = ex.ErrorCode ?? "SECRET_RETRIEVAL_ERROR",
                    Details = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "SECRET_OPERATION_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("secret-by-keyvault-name/{actualSecretName}")]
        public async Task<ActionResult<object>> GetSecretByKeyvaultName(string actualSecretName)
        {
            try
            {
                Console.WriteLine($"[CacheController] === GET SECRET BY KEYVAULT NAME: {actualSecretName} ===");

                var cacheKey = $"secret:{actualSecretName}";
                var timestamp = DateTime.UtcNow;

                Console.WriteLine($"[CacheController] Actual secret name: {actualSecretName}");
                Console.WriteLine($"[CacheController] Cache key: {cacheKey}");

                // Check if secret is in cache
                var isInCache = _cache.TryGetValue(cacheKey, out var cachedValue);

                if (isInCache)
                {
                    Console.WriteLine($"[CacheController] Secret '{actualSecretName}' found in cache");
                    return Ok(new
                    {
                        success = true,
                        actualSecretName = actualSecretName,
                        cacheKey = cacheKey,
                        source = "cache",
                        isInCache = true,
                        valueLength = cachedValue is string str ? str.Length : 0,
                        timestamp = timestamp,
                        hasValue = cachedValue != null
                    });
                }
                else
                {
                    Console.WriteLine($"[CacheController] Secret '{actualSecretName}' not found in cache, fetching from KeyVault...");

                    var startTime = DateTime.UtcNow;
                    var secret = await _secretsCache.GetSecretAsync(actualSecretName);
                    var duration = DateTime.UtcNow - startTime;

                    return Ok(new
                    {
                        success = true,
                        actualSecretName = actualSecretName,
                        cacheKey = cacheKey,
                        source = "keyvault",
                        isInCache = false,
                        valueLength = secret?.Length ?? 0,
                        retrievalDuration = duration.TotalMilliseconds,
                        timestamp = timestamp,
                        hasValue = !string.IsNullOrEmpty(secret),
                        nowCached = true
                    });
                }
            }
            catch (SecretNotFoundException ex)
            {
                return NotFound(new ErrorResponse
                {
                    Message = $"Secret not found: {actualSecretName}",
                    ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { secretName = ex.SecretName }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "SECRET_OPERATION_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("secret-mappings")]
        public ActionResult<object> GetSecretMappings()
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();

                var secretConfigs = new[]
                {
                    "AzureServiceBusFQURL",
                    "ManagedIdentityKey",
                    "AzureDbConnectionString",
                    "VFClientID",
                    "VFClientSecret",
                    "VFSubscriptionKey",
                    "VFTenantID",
                    "VFUri",
                    "VFScopes",
                    "VFTokenEndpoint",
                    "VFSolutionID",
                    "VFCustomerID",
                    "VFUsageID",
                    "NServicebusLicense"
                };

                var mappings = secretConfigs.Select(configName =>
                {
                    var configKey = $"SecretNames:{configName}";
                    var actualSecretName = configuration[configKey];
                    var cacheKey = $"secret:{actualSecretName}";
                    object? value = null;
                    var isInCache = !string.IsNullOrEmpty(actualSecretName) && _cache.TryGetValue(cacheKey, out value);

                    return new
                    {
                        configName = configName,
                        configKey = configKey,
                        actualSecretName = actualSecretName ?? "[NOT CONFIGURED]",
                        cacheKey = cacheKey,
                        isConfigured = !string.IsNullOrEmpty(actualSecretName),
                        isInCache = isInCache,
                        valueLength = isInCache && value is string str ? str.Length : 0
                    };
                }).ToArray();

                return Ok(new
                {
                    timestamp = DateTime.UtcNow,
                    totalConfigs = mappings.Length,
                    configuredSecrets = mappings.Count(m => m.isConfigured),
                    cachedSecrets = mappings.Count(m => m.isInCache),
                    mappings = mappings
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "SECRET_MAPPINGS_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("secret/{secretName}/exists")]
        public ActionResult<object> CheckSecretInCache(string secretName)
        {
            try
            {
                var cacheKey = $"secret:{secretName}";
                var isInCache = _cache.TryGetValue(cacheKey, out var cachedValue);

                Console.WriteLine($"[CacheController] Checking if secret '{secretName}' exists in cache: {isInCache}");

                return Ok(new
                {
                    secretName = secretName,
                    cacheKey = cacheKey,
                    isInCache = isInCache,
                    hasValue = cachedValue != null,
                    valueLength = cachedValue is string str ? str.Length : 0,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_CHECK_ERROR",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { secretName }),
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("secret/{secretName}/force-refresh")]
        public async Task<ActionResult<object>> ForceRefreshSecret(string secretName)
        {
            try
            {
                Console.WriteLine($"[CacheController] === FORCE REFRESH SECRET: {secretName} ===");

                // Remove from cache first
                _secretsCache.RemoveSecret(secretName);
                Console.WriteLine($"[CacheController] Removed '{secretName}' from cache");

                // Get fresh from KeyVault (this will cache it again)
                var startTime = DateTime.UtcNow;
                var secret = await _secretsCache.GetSecretAsync(secretName);
                var duration = DateTime.UtcNow - startTime;

                return Ok(new
                {
                    success = true,
                    secretName = secretName,
                    action = "force-refresh",
                    valueLength = secret?.Length ?? 0,
                    retrievalDuration = duration.TotalMilliseconds,
                    hasValue = !string.IsNullOrEmpty(secret),
                    timestamp = DateTime.UtcNow,
                    fromKeyVault = true,
                    nowCached = true
                });
            }
            catch (SecretNotFoundException ex)
            {
                return NotFound(new ErrorResponse
                {
                    Message = $"Secret not found: {secretName}",
                    ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { secretName = ex.SecretName }),
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "FORCE_REFRESH_ERROR",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { secretName }),
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("secret/{secretName}/info")]
        public ActionResult<object> GetSecretInfo(string secretName)
        {
            try
            {
                var cacheKey = $"secret:{secretName}";
                var isInCache = _cache.TryGetValue(cacheKey, out var cachedValue);

                var info = new
                {
                    secretName = secretName,
                    cacheKey = cacheKey,
                    isInCache = isInCache,
                    hasValue = cachedValue != null,
                    valueType = cachedValue?.GetType().Name,
                    valueLength = cachedValue is string str ? str.Length : (cachedValue?.ToString()?.Length ?? 0),
                    timestamp = DateTime.UtcNow
                };

                // Add cache statistics if available
                if (_cache is ClearableMemoryCache clearable)
                {
                    var allKeys = clearable.GetKeys().ToList();
                    var secretKeys = allKeys.Where(k => k?.ToString()?.StartsWith("secret:") == true).ToList();

                    return Ok(new
                    {
                        info.secretName,
                        info.cacheKey,
                        info.isInCache,
                        info.hasValue,
                        info.valueType,
                        info.valueLength,
                        info.timestamp,
                        // Replace this block inside GetSecretInfo method:
                        cacheStats = new
                        {
                            totalCacheEntries = allKeys.Count,
                            totalSecretEntries = secretKeys.Count,
                            allSecretNames = secretKeys
                                .Where(k => k != null && k.ToString() != null && k.ToString()!.StartsWith("secret:"))
                                .Select(k => k!.ToString()!["secret:".Length..])
                                .ToArray()
                        }
                    });
                }

                return Ok(info);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "SECRET_INFO_ERROR",
                    Details = System.Text.Json.JsonSerializer.Serialize(new { secretName }),
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("stats")]
        public ActionResult<object> GetCacheStats()
        {
            try
            {
                var stats = new
                {
                    cacheType = _cache.GetType().Name,
                    timestamp = DateTime.UtcNow,
                    totalEntries = _cache is ClearableMemoryCache clearable ? clearable.GetKeys().Count() : 0,
                    secretEntries = _secretsCache.GetCachedSecretNames().Count(),
                    supportsClearableOperations = _cache is ClearableMemoryCache
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_STATS_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("test")]
        public ActionResult<object> TestCache()
        {
            try
            {
                var testKey = $"test-{DateTime.UtcNow:HHmmss}";
                var testValue = $"TestValue-{DateTime.UtcNow:HHmmss}";

                Console.WriteLine($"[CacheController] === TESTING CACHE ===");
                Console.WriteLine($"[CacheController] Test key: {testKey}");
                Console.WriteLine($"[CacheController] Test value: {testValue}");
                Console.WriteLine($"[CacheController] Cache type: {_cache.GetType().Name}");

                // Use standard IMemoryCache.Set method
                _cache.Set(testKey, testValue, TimeSpan.FromMinutes(5));
                Console.WriteLine($"[CacheController] Used standard IMemoryCache.Set");

                // Test immediate retrieval
                var retrieved = _cache.Get<string>(testKey);
                Console.WriteLine($"[CacheController] Retrieved value: {retrieved}");

                var result = new
                {
                    success = true,
                    testKey = testKey,
                    testValue = testValue,
                    retrieved = retrieved,
                    match = retrieved == testValue,
                    cacheType = _cache.GetType().Name,
                    isClearableCache = _cache is ClearableMemoryCache
                };

                if (_cache is ClearableMemoryCache clearable2)
                {
                    var keys = clearable2.GetKeys().ToList();
                    return Ok(new
                    {
                        result.success,
                        result.testKey,
                        result.testValue,
                        result.retrieved,
                        result.match,
                        result.cacheType,
                        result.isClearableCache,
                        totalKeys = keys.Count,
                        allKeys = keys.Select(k => k?.ToString()).ToList()
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "CACHE_TEST_ERROR",
                    Details = ex.StackTrace,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("test-secret")]
        public async Task<ActionResult<object>> TestSecretCaching()
        {
            try
            {
                Console.WriteLine($"[CacheController] === TESTING SECRET CACHING ===");

                // Use configuration name but get the actual secret name
                // Get the name of the secret from configuration to match existing pattern and avoid CVE 259 from Veracode
                //var configSecretName = "VFSolutionID"; Moved to line below to get from config
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var configSecretName = configuration["SecretNames:VFSolutionID"];
                var actualSecretName = configuration[$"SecretNames:{configSecretName}"];

                if (string.IsNullOrEmpty(actualSecretName))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = $"Configuration 'SecretNames:{configSecretName}' not found",
                        ErrorCode = "CONFIG_NOT_FOUND",
                        Details = System.Text.Json.JsonSerializer.Serialize(new { configSecretName }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                Console.WriteLine($"[CacheController] Testing config secret: {configSecretName}");
                Console.WriteLine($"[CacheController] Actual secret name: {actualSecretName}");

                // Clear cache first
                if (_cache is ClearableMemoryCache clearable)
                {
                    clearable.Clear();
                    Console.WriteLine($"[CacheController] Cleared cache before test");
                }

                // First call - should go to KeyVault
                Console.WriteLine($"[CacheController] First call (should hit KeyVault)...");
                var startTime1 = DateTime.UtcNow;
                var secret1 = await _secretsCache.GetSecretAsync(actualSecretName);
                var duration1 = DateTime.UtcNow - startTime1;

                // Second call - should hit cache
                Console.WriteLine($"[CacheController] Second call (should hit cache)...");
                var startTime2 = DateTime.UtcNow;
                var secret2 = await _secretsCache.GetSecretAsync(actualSecretName);
                var duration2 = DateTime.UtcNow - startTime2;

                // Check cache directly
                var cacheKey = $"secret:{actualSecretName}";
                var isInCache = _cache.TryGetValue(cacheKey, out var cachedValue);

                var result = new
                {
                    success = true,
                    configSecretName = configSecretName,
                    actualSecretName = actualSecretName,
                    cacheKey = cacheKey,
                    firstCallDuration = duration1.TotalMilliseconds,
                    secondCallDuration = duration2.TotalMilliseconds,
                    resultsMatch = secret1 == secret2,
                    secretLength = secret1?.Length ?? 0,
                    cacheFaster = duration2 < duration1,
                    speedImprovement = duration1.TotalMilliseconds > 0 ? (duration1.TotalMilliseconds - duration2.TotalMilliseconds) / duration1.TotalMilliseconds * 100 : 0,
                    isInCacheAfterTest = isInCache,
                    cachedValueLength = cachedValue is string str ? str.Length : 0
                };

                if (_cache is ClearableMemoryCache clearable2)
                {
                    var keys = clearable2.GetKeys().ToList();
                    return Ok(new
                    {
                        result.success,
                        result.configSecretName,
                        result.actualSecretName,
                        result.cacheKey,
                        result.firstCallDuration,
                        result.secondCallDuration,
                        result.resultsMatch,
                        result.secretLength,
                        result.cacheFaster,
                        result.speedImprovement,
                        result.isInCacheAfterTest,
                        result.cachedValueLength,
                        totalKeysAfterTest = keys.Count,
                        allKeysAfterTest = keys.Select(k => k?.ToString()).ToList()
                    });
                }

                return Ok(result);
            }
            catch (SecretNotFoundException ex)
            {
                return NotFound(new ErrorResponse
                {
                    Message = "Secret not found",
                    ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
                    Details = ex.StackTrace,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "TEST_SECRET_ERROR",
                    Details = ex.StackTrace,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

#if DEBUG
        [HttpGet("debug/secret-values/{configSecretName}")]
        public async Task<ActionResult<object>> DebugGetSecretValue(string configSecretName)
        {
            try
            {
                if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "This endpoint is only available in Development environment",
                        ErrorCode = "FORBIDDEN_IN_PRODUCTION",
                        Timestamp = DateTime.UtcNow
                    });
                }

                Console.WriteLine($"[CacheController] DEBUG: Getting secret value for {configSecretName}");

                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var configKey = $"SecretNames:{configSecretName}";
                var actualSecretName = configuration[configKey];

                if (string.IsNullOrEmpty(actualSecretName))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = $"Configuration key '{configKey}' not found or empty",
                        ErrorCode = "CONFIG_NOT_FOUND",
                        Details = System.Text.Json.JsonSerializer.Serialize(new { configSecretName }),
                        Timestamp = DateTime.UtcNow
                    });
                }

                var cacheKey = $"secret:{actualSecretName}";

                // Check cache first
                var isInCache = _cache.TryGetValue(cacheKey, out var cachedValue);
                // Get from service
                var secret = await _secretsCache.GetSecretAsync(actualSecretName);
                // Check cache again after service call
                var isInCacheAfter = _cache.TryGetValue(cacheKey, out var cachedValueAfter);

                return Ok(new
                {
                    success = true,
                    configSecretName = configSecretName,
                    actualSecretName = actualSecretName,
                    cacheKey = cacheKey,
                    wasInCacheBefore = isInCache,
                    isInCacheAfter = isInCacheAfter,
                    secretFromService = secret,
                    cachedValueBefore = cachedValue?.ToString(),
                    cachedValueAfter = cachedValueAfter?.ToString(),
                    lengthFromService = secret?.Length ?? 0,
                    lengthFromCache = cachedValueAfter?.ToString()?.Length ?? 0,
                    valuesMatch = secret == cachedValueAfter?.ToString()
                });
            }
            catch (SecretNotFoundException ex)
            {
                return NotFound(new ErrorResponse
                {
                    Message = "Secret not found",
                    ErrorCode = ex.ErrorCode ?? "SECRET_NOT_FOUND",
                    Details = ex.StackTrace,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "DEBUG_SECRET_ERROR",
                    Details = ex.StackTrace,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("debug/cache-inspection")]
        public ActionResult<object> DebugCacheInspection()
        {
            try
            {
                if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "This endpoint is only available in Development environment",
                        ErrorCode = "FORBIDDEN_IN_PRODUCTION",
                        Timestamp = DateTime.UtcNow
                    });
                }

                var result = new
                {
                    cacheType = _cache.GetType().Name,
                    timestamp = DateTime.UtcNow
                };

                if (_cache is ClearableMemoryCache clearable)
                {
                    var keys = clearable.GetKeys().ToList();
                    var detailedEntries = keys.Select(key =>
                    {
                        var hasValue = _cache.TryGetValue(key, out var value);
                        return new
                        {
                            key = key?.ToString(),
                            hasValue = hasValue,
                            valueType = value?.GetType().Name,
                            actualValue = value?.ToString(),
                            valueLength = value?.ToString()?.Length ?? 0,
                            isSecret = key?.ToString()?.StartsWith("secret:") == true
                        };
                    }).ToList();

                    return Ok(new
                    {
                        result.cacheType,
                        result.timestamp,
                        totalEntries = keys.Count,
                        secretEntries = detailedEntries.Count(e => e.isSecret),
                        entries = detailedEntries
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    ErrorCode = "DEBUG_INSPECTION_ERROR",
                    Timestamp = DateTime.UtcNow
                });
            }
        }
#endif
    }
}
