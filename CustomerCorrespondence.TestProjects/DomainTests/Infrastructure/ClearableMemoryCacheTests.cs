using CustomerCorrespondence.Infrastructure.Services;
using Xunit;

namespace CustomerCorrespondenceTests.Infrastructure
{
    [TestClass]
    public class ClearableMemoryCacheTests : IDisposable
    {
        private readonly ClearableMemoryCache _cache;

        public ClearableMemoryCacheTests()
        {
            // Pass required arguments to the ClearableMemoryCache constructor.
            // Assuming MemoryCacheOptions is required, as is typical for MemoryCache.
            _cache = new ClearableMemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        }

        [TestMethod]
        public void CreateEntry_StoresAndRetrievesValue()
        {
            var key = "test-key";
            var value = "test-value";

            using (var entry = _cache.CreateEntry(key))
            {
                entry.Value = value;
            }

            Assert.IsTrue(_cache.TryGetValue(key, out var cachedValue));
            Assert.AreEqual(value, cachedValue);
        }

        [TestMethod]
        public void Remove_RemovesEntry()
        {
            var key = "test-key";
            using (var entry = _cache.CreateEntry(key))
            {
                entry.Value = "value";
            }

            _cache.Remove(key);

            Assert.IsFalse(_cache.TryGetValue(key, out _));
        }

        [TestMethod]
        public void Clear_RemovesAllEntries()
        {
            _cache.CreateEntry("key1").Dispose();
            _cache.CreateEntry("key2").Dispose();

            _cache.Clear();

            Assert.IsFalse(_cache.TryGetValue("key1", out _));
            Assert.IsFalse(_cache.TryGetValue("key2", out _));
        }

        //[TestMethod]
        //public void GetAllEntries_ReturnsAllCurrentEntries()
        //{
        //    var key1 = "key1";
        //    var key2 = "key2";
        //    var value1 = "value1";
        //    var value2 = "value2";

        //    using (var entry1 = _cache.CreateEntry(key1)) { entry1.Value = value1; }
        //    using (var entry2 = _cache.CreateEntry(key2)) { entry2.Value = value2; }

        //    var entries = new Dictionary<object, object>();
        //    foreach (var kvp in _cache.GetAllEntries())
        //    {
        //        entries[kvp.Key] = kvp.Value;
        //    }

        //    Assert.AreEqual(2, entries.Count);
        //    Assert.AreEqual(value1, entries[key1]);
        //    Assert.AreEqual(value2, entries[key2]);
        //}

        [TestMethod]
        public void Dispose_DisposesUnderlyingCache()
        {
            _cache.Dispose();
            // No exception should be thrown
        }

        public void Dispose()
        {
            _cache.Dispose();
        }
    }
}