using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace CustomerCorrespondence.Infrastructure.Services
{
    /// <summary>
    /// A memory cache implementation that supports clearing all cached entries.
    /// </summary>
    public class ClearableMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _innerCache;
        private readonly ConcurrentDictionary<object, bool> _keys = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ClearableMemoryCache"/> class.
        /// </summary>
        /// <param name="optionsAccessor">The memory cache options.</param>
        public ClearableMemoryCache(IOptions<MemoryCacheOptions> optionsAccessor)
        {
            _innerCache = new MemoryCache(optionsAccessor);
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="ClearableMemoryCache"/> class with logging support.
        /// </summary>
        /// <param name="optionsAccessor">The memory cache options.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        public ClearableMemoryCache(IOptions<MemoryCacheOptions> optionsAccessor, ILoggerFactory loggerFactory)
        {
            _innerCache = new MemoryCache(optionsAccessor, loggerFactory);
        }
        /// <summary>
        /// CreateEntry
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public ICacheEntry CreateEntry(object key)
        {
            _keys.TryAdd(key, true);
            var entry = _innerCache.CreateEntry(key);
            return new CacheEntryWrapper(entry, () => {
            });
        }
        /// <summary>
        /// TryGetValue
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetValue(object key, out object? value)
        {
            return _innerCache.TryGetValue(key, out value);
        }
        /// <summary>
        /// Remove
        /// </summary>
        /// <param name="key"></param>
        public void Remove(object key)
        {
            _keys.TryRemove(key, out _);
            _innerCache.Remove(key);
        }
        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            _innerCache?.Dispose();
        }
        /// <summary>
        /// GetKeys
        /// </summary>
        /// <returns></returns>
        public IEnumerable<object> GetKeys()
        {
            return _keys.Keys.ToList();
        }
        /// <summary>
        /// Clear
        /// </summary>
        public void Clear()
        {
            var keys = _keys.Keys.ToList();
            foreach (var key in keys)
            {
                Remove(key);
            }
        }
        /// <summary>
        /// Gets the number of entries currently in the cache.
        /// </summary>
        public int Count => _keys.Count;
        /// <summary>
        /// CacheEntryWrapper
        /// </summary>
        private class CacheEntryWrapper : ICacheEntry
        {
            private readonly ICacheEntry _entry;
            private readonly Action _onDispose;
            private bool _disposed = false;

            public CacheEntryWrapper(ICacheEntry entry, Action onDispose)
            {
                _entry = entry;
                _onDispose = onDispose;
            }

            public object Key => _entry.Key;
            public object? Value { get => _entry.Value; set => _entry.Value = value; }
            public DateTimeOffset? AbsoluteExpiration { get => _entry.AbsoluteExpiration; set => _entry.AbsoluteExpiration = value; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get => _entry.AbsoluteExpirationRelativeToNow; set => _entry.AbsoluteExpirationRelativeToNow = value; }
            public TimeSpan? SlidingExpiration { get => _entry.SlidingExpiration; set => _entry.SlidingExpiration = value; }
            public IList<IChangeToken> ExpirationTokens => _entry.ExpirationTokens;
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => _entry.PostEvictionCallbacks;
            public CacheItemPriority Priority { get => _entry.Priority; set => _entry.Priority = value; }
            public long? Size { get => _entry.Size; set => _entry.Size = value; }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _onDispose();
                    _entry.Dispose();
                }
            }
        }
    }

    //// Extension methods to add Set functionality
    //public static class ClearableMemoryCacheExtensions
    //{
    //    public static ICacheEntry Set(this IMemoryCache cache, object key, object value, TimeSpan absoluteExpirationRelativeToNow)
    //    {
    //        using var entry = cache.CreateEntry(key);
    //        entry.Value = value;
    //        entry.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
    //        Console.WriteLine($"[ClearableMemoryCache] SET: Added key '{key}' with TimeSpan expiration ({absoluteExpirationRelativeToNow.TotalMinutes} minutes)");
    //        return entry;
    //    }

    //    public static ICacheEntry Set(this IMemoryCache cache, object key, object value, MemoryCacheEntryOptions options)
    //    {
    //        using var entry = cache.CreateEntry(key);
    //        entry.Value = value;
    //        if (options != null)
    //        {
    //            entry.AbsoluteExpiration = options.AbsoluteExpiration;
    //            entry.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;
    //            entry.SlidingExpiration = options.SlidingExpiration;
    //            entry.Priority = options.Priority;
    //            entry.Size = options.Size;

    //            foreach (var token in options.ExpirationTokens)
    //            {
    //                entry.ExpirationTokens.Add(token);
    //            }

    //            foreach (var callback in options.PostEvictionCallbacks)
    //            {
    //                entry.PostEvictionCallbacks.Add(callback);
    //            }
    //        }
    //        Console.WriteLine($"[ClearableMemoryCache] SET: Added key '{key}' with MemoryCacheEntryOptions");
    //        return entry;
    //    }
    //}
}
