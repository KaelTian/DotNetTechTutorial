namespace Cache_Lab
{
    /// <summary>
    /// ================= 场景二：缓存击穿 =================
    /// </summary>
    public static class BreakdownDemo
    {
        /// <summary>
        /// ---------- 无防护：热点 key 过期瞬间，200 并发同时回源 ----------
        /// </summary>
        /// <param name="db"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        public static async Task RunUnsafeAsync(SimulateDb db, SimCache cache)
        {
            Console.WriteLine("【攻击】热点商品 id=1 的缓存刚好过期，此时 200 个并发请求同时来查");
            // 先让缓存有值，然后人为删除模拟"刚好过期"
            cache.Set("hot:1", await db.GetAsync(1), TimeSpan.FromMinutes(30));
            cache.Remove("hot:1"); // 模拟过期瞬间
            db.Reset();          // ⭐ 重置计数器：只统计本次冲击对 DB 的压力

            var tasks = Enumerable.Range(0, 200).Select(async _ =>
            {
                if (cache.TryGet("hot:1", out var v)) return v; // 全部失效 -> 全部打到 DB
                var data = await db.GetAsync(1);
                cache.Set("hot:1", data, TimeSpan.FromMinutes(30)); // 200 个线程同时创建
                return data;
            });
            await Task.WhenAll(tasks);
            Report.Print("无防护", 200, db.QueryCount);
        }
        /// <summary>
        /// ---------- 防护 A：互斥锁（一个线程回源，其余等待） ----------
        /// </summary>
        private static readonly SemaphoreSlim LockObj = new(1, 1);

        public static async Task RunMutexAsync(SimulateDb db, SimCache cache)
        {
            Console.WriteLine("【防护A】互斥锁：缓存失效时只放 1 个线程回源，其余等待后重试缓存，200 并发");
            var key = "got:1";
            cache.Set(key, await db.GetAsync(1), TimeSpan.FromMinutes(30));
            cache.Remove(key);
            db.Reset(); //  重新计数，只看这次冲击

            var tasks = Enumerable.Range(0, 200).Select(async _ =>
            {
                if (cache.TryGet(key, out var v)) return v;

                await LockObj.WaitAsync();
                try
                {
                    if (cache.TryGet(key, out v)) return v; // double check：可能已被重建
                    var data = await db.GetAsync(1); // 只有抢到锁的线程会走到这里
                    cache.Set(key, data, TimeSpan.FromMinutes(30));
                    return data;
                }
                finally
                {
                    LockObj.Release();
                }
            });
            await Task.WhenAll(tasks);
            Report.Print("互斥锁", 200, db.QueryCount);
        }

        /// <summary>
        /// ---------- 防护 B：逻辑过期（永不过期，异步重建，返回旧数据） ----------
        /// </summary>
        /// <param name="Value"></param>
        /// <param name="ExpireAt"></param>
        private sealed record CacheItem(object? Value, DateTime ExpireAt);

        public static async Task RunLogicalExpireAsync(SimulateDb db,SimCache cache)
        {
            Console.WriteLine("【防护B】逻辑过期：缓存永不过期，失效后异步重建、并发请求返回旧数据，200 并发");
            // 写入"1 毫秒前已过期"的数据，模拟热点数据刚过期
            cache.Set("hot:1", new CacheItem("旧数据-of-1", DateTime.UtcNow.AddMilliseconds(-1)), TimeSpan.FromDays(3650));
            db.Reset();

            var rebuildLock = new SemaphoreSlim(1, 1);

            async Task<object?> QueryAsync()
            {
                if (!cache.TryGet("hot:1", out var raw)) throw new Exception("不应发生");
                var item = (CacheItem)raw!;

                if (item.ExpireAt > DateTime.UtcNow) return item.Value; // 未过期，直接返回

                // 已过期：第一个线程异步重建，本线程立即返回旧数据（不阻塞）
                if (rebuildLock.Wait(0))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var fresh = await db.GetAsync(1); // DB 查询约 20ms
                            cache.Set("hot:1", new CacheItem(fresh, DateTime.UtcNow.AddMinutes(30)),
                                      TimeSpan.FromDays(3650));
                        }
                        finally { rebuildLock.Release(); }
                    });
                }
                return item.Value; // ⭐ 无阻塞
            }

            // 第一波 50 个：重建完成前到达（DB 查询还没返回，只能拿旧数据）
            var wave1 = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => QueryAsync()));

            await Task.Delay(120); // 等异步重建完成

            // 第二波 150 个：重建完成后到达（缓存里已是新数据）
            var wave2 = await Task.WhenAll(Enumerable.Range(0, 150).Select(_ => QueryAsync()));

            var oldCount = wave1.Count(r => r?.ToString()?.Contains("旧数据") == true)
                         + wave2.Count(r => r?.ToString()?.Contains("旧数据") == true);
            Report.Print("逻辑过期", 200, db.QueryCount);
            Console.WriteLine($"  └─ 第一波 50 个请求(重建完成前到达)：全部立即返回旧数据，零等待");
            Console.WriteLine($"     第二波 150 个请求(重建完成后到达)：全部拿到新数据");
            Console.WriteLine($"     全程 DB 只被异步重建线程查询了 1 次，旧数据返回 {oldCount}/200 次");
        }
    }
}
