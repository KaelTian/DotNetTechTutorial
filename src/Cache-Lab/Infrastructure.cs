using System.Collections.Concurrent;

// ================= 基础设施：模拟数据库 + 模拟缓存 =================
namespace Cache_Lab
{
    /// <summary>
    /// 模拟数据库：查询有 20ms 延迟，并统计查询次数（观察 DB 压力的关键指标）
    /// </summary>
    public class SimulateDb
    {
        private readonly HashSet<int> _existingIds;
        public int QueryCount; // DB 被查询的次数

        public SimulateDb(int maxId) => _existingIds = Enumerable.Range(1, maxId).ToHashSet();

        public async Task<string?> GetAsync(int id)
        {
            Interlocked.Increment(ref QueryCount);
            await Task.Delay(20); // 模拟数据库查询耗时 20ms
            return _existingIds.Contains(id) ? $"data-of-{id}" : null;
        }

        public void Reset() => QueryCount = 0;
    }
    /// <summary>
    /// 模拟分布式缓存：ConcurrentDictionary + TTL。
    /// value 为 null 表示"空值缓存"（用于穿透防护）。
    /// </summary>
    public class SimCache
    {
        private sealed record Entry(object? Value, DateTime ExpireAt);

        private readonly ConcurrentDictionary<string, Entry> _store = new();
        private readonly bool _jitter; // 是否开启过期时间随机抖动

        public SimCache(bool jitter = false) => _jitter = jitter;

        public bool TryGet(string key, out object? value)
        {
            value = null;
            if (!_store.TryGetValue(key, out var e)) return false;
            if (DateTime.UtcNow > e.ExpireAt) { _store.TryRemove(key, out _); return false; } // 过期
            value = e.Value;
            return true;
        }

        public void Set(string key, object? value, TimeSpan ttl)
        {
            if (_jitter) ttl += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 2000)); // 雪崩防护：抖动
            _store[key] = new Entry(value, DateTime.UtcNow + ttl);
        }

        public void Remove(string key) => _store.TryRemove(key, out _);
        public void Clear() => _store.Clear();
    }

    public static class Report
    {
        public static void Print(string title, int requests, int dbQueries)
            => Console.WriteLine($"  ├─ 模拟请求数: {requests,5}   DB 实际查询次数: {dbQueries,5}   缓存拦截率: {(1 - (double)dbQueries / requests) * 100:N1}%");
    }
}
