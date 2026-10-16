namespace Cache_Lab
{
    /// <summary>
    /// 缓存雪崩
    /// </summary>
    public static class AvalancheDemo
    {
        /// <summary>
        /// ---------- 无防护：1000 个 key 同时过期，1000 并发同时回源 ----------
        /// </summary>
        /// <param name="db"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        public static async Task RunUnsafeAsync(SimulateDb db,SimCache cache)
        {
            Console.WriteLine("【攻击】管理员批量预热了 1000 个商品，统一设置 10ms 后过期，到期瞬间 1000 个并发请求同时查询");
            db.Reset();
            for (var i = 1; i <= 1000; i++)
                cache.Set($"a:{i}", $"data-of-{i}", TimeSpan.FromMilliseconds(10)); // 统一 TTL
            await Task.Delay(50); // 等全部过期

            var tasks = Enumerable.Range(1, 1000).Select(async i =>
            {
                if (cache.TryGet($"a:{i}", out var v)) return v;
                var data = await db.GetAsync(i);
                cache.Set($"a:{i}", data, TimeSpan.FromMilliseconds(10)); // 又统一过期，恶性循环
                return data;
            });
            await Task.WhenAll(tasks);
            Report.Print("无防护(统一TTL)", 1000, db.QueryCount);
        }
        /// <summary>
        /// ---------- 防护：过期时间加随机抖动 ----------
        /// </summary>
        /// <param name="db"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        public static async Task RunJitterAsync(SimulateDb db,SimCache cache)
        {
            Console.WriteLine("【防护】TTL 加随机抖动(10ms + 0~2s 随机)，到期瞬间 1000 个并发请求同时查询");
            db.Reset();
            var jitterCache = new SimCache(jitter: true); // ⭐ 开启抖动
            for (var i = 1; i <= 1000; i++)
                jitterCache.Set($"a:{i}", $"data-of-{i}", TimeSpan.FromMilliseconds(10));
            await Task.Delay(50);

            var tasks = Enumerable.Range(1, 1000).Select(async i =>
            {
                if (jitterCache.TryGet($"a:{i}", out var v)) return v;
                var data = await db.GetAsync(i);
                jitterCache.Set($"a:{i}", data, TimeSpan.FromMinutes(30)); // 重建后不再抖动，回归长 TTL
                return data;
            });
            await Task.WhenAll(tasks);
            Report.Print("TTL随机抖动", 1000, db.QueryCount);
            Console.WriteLine("  └─ 抖动让 1000 个 key 的过期时刻错开了，同一时刻只有少数 key 失效，DB 有缓冲时间逐个重建");
        }
    }
}
