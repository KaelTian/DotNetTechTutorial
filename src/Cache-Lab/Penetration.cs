using System.Collections.Concurrent;

namespace Cache_Lab
{
    // ================= 场景一：缓存穿透 =================
    public static class PenetrationDemo
    {
        /// <summary>
        /// ---------- 无防护：黑客拿不存在的 id 狂刷 ----------
        /// </summary>
        /// <param name="db"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        public static async Task RunUnsafeAsync(SimulateDb db, SimCache cache)
        {
            Console.WriteLine("【攻击】黑客用随机不存在的 id 刷 500 次请求（id 范围 2000~3000，DB 中不存在）");
            db.Reset();
            var tasks = Enumerable.Range(0, 500).Select(async _ =>
            {
                var id = Random.Shared.Next(2000, 3000);
                if (cache.TryGet($"p:{id}", out var v)) return v; // 永远不命中
                var data = await db.GetAsync(id);
                if (data is not null) cache.Set($"p:{id}", data, TimeSpan.FromMinutes(30));
                return data;
            });
            await Task.WhenAll(tasks);
            Report.Print("无防护", 500, db.QueryCount);
        }
        /// <summary>
        /// ---------- 防护 A：缓存空值 + 按 key 加锁去重 ----------
        /// 优化点：500 个请求齐发时，若只靠"先查缓存"，同一毫秒内大量相同 id
        /// 会同时发现缓存没值、同时回源。给每个 id 配一把锁，同一 id 只放行
        /// 一个线程查 DB，其余等待后 double check 直接命中缓存（null 也是合法值）。
        /// </summary>
        public static async Task RunNullCacheAsync(SimulateDb db, SimCache cache)
        {
            Console.WriteLine("【防护A】缓存空值 + 按 id 加锁去重：500 个请求同一毫秒齐发");
            db.Reset();
            // ⭐ 每个 id 一把锁（真实项目用 Redis 分布式锁或内存 keyed lock 库）
            var keyLocks = new ConcurrentDictionary<int, SemaphoreSlim>();

            var tasks = Enumerable.Range(0, 500).Select(async _ =>
            {
                var id = Random.Shared.Next(2000, 2050);
                if (cache.TryGet($"p:{id}", out var v)) return v; // 命中 (含 null 空值)

                var gate = keyLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync();
                try
                {
                    if (cache.TryGet($"p:{id}", out v)) return v; // double check：等锁期间已被写入
                    var data = await db.GetAsync(id);
                    cache.Set($"p:{id}", data, TimeSpan.FromSeconds(60)); // null 也写入
                    return data;
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);
            Report.Print("空值缓存+按id去重", 500, db.QueryCount);
        }
        /// <summary>
        /// ---------- 防护 B：布隆过滤器 ----------
        /// 性质：说"不存在"→一定不存在；说"可能存在"→有可能误判，但不会漏
        /// </summary>
        public class BloomFilter
        {
            private readonly bool[] _bits;
            private readonly int _size;
            public BloomFilter(int size) { _size = size; _bits = new bool[size]; }

            public void Add(int id)
            {
                foreach (var h in Hashes(id))
                {
                    _bits[h] = true;
                }
            }

            public bool MightExists(int id) => Hashes(id).All(h => _bits[h]); // 全为 true 才放行

            private IEnumerable<int> Hashes(int id)
            {
                yield return Math.Abs(id * 31 + 7) % _size;
                yield return Math.Abs(id * 131 + 11) % _size;
                yield return Math.Abs(id * 7919 + 13) % _size;
            }
        }

        public static async Task RunBloomAsync(SimulateDb db,SimCache cache)
        {
            Console.WriteLine("【防护B】布隆过滤器：先把 DB 中所有合法 id(1~1000) 加入过滤器，同样 500 次随机请求");
            db.Reset();
            var bloom = new BloomFilter(100_000);
            for (var i = 1; i <= 1000; i++) bloom.Add(i);

            var tasks = Enumerable.Range(0, 500).Select(async _ =>
            {
                var id = Random.Shared.Next(500, 1500);
                if (!bloom.MightExists(id)) return (object?)null; // 被过滤器直接拦截，连缓存都不查
                if (cache.TryGet($"p:{id}", out var v)) return v;
                var data = await db.GetAsync(id);
                if (data is not null) cache.Set($"p:{id}", data, TimeSpan.FromMinutes(30));
                return data;
            });
            await Task.WhenAll(tasks);
            Report.Print("布隆过滤器", 500, db.QueryCount);
        }
    }
}
