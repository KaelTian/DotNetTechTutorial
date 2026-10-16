// ================= 入口：依次演示三个场景 =================
using Cache_Lab;

var db = new SimulateDb(maxId: 1000); // DB 中只有 id 1~1000
var cache = new SimCache();

Console.WriteLine("╔════════════════════════════════════════════╗");
Console.WriteLine("║   场景一：缓存穿透 Penetration              ║");
Console.WriteLine("╚════════════════════════════════════════════╝");
await PenetrationDemo.RunUnsafeAsync(db, cache);
await PenetrationDemo.RunNullCacheAsync(db, cache);
await PenetrationDemo.RunBloomAsync(db, cache);

Console.WriteLine();
Console.WriteLine("╔════════════════════════════════════════════╗");
Console.WriteLine("║   场景二：缓存击穿 Breakdown                ║");
Console.WriteLine("╚════════════════════════════════════════════╝");
await BreakdownDemo.RunUnsafeAsync(db, cache);
await BreakdownDemo.RunMutexAsync(db, cache);
await BreakdownDemo.RunLogicalExpireAsync(db, cache);

Console.WriteLine();
Console.WriteLine("╔════════════════════════════════════════════╗");
Console.WriteLine("║   场景三：缓存雪崩 Avalanche                ║");
Console.WriteLine("╚════════════════════════════════════════════╝");
await AvalancheDemo.RunUnsafeAsync(db, cache);
await AvalancheDemo.RunJitterAsync(db, cache);


Console.WriteLine();
Console.WriteLine("敲代码时重点体会：");
Console.WriteLine("  1. 穿透 —— null 也是合法缓存值；布隆过滤器是'拦截'不是'放行'");
Console.WriteLine("  2. 击穿 —— 互斥锁里的 double check；逻辑过期的'先返回旧数据'");
Console.WriteLine("  3. 雪崩 —— 统一 TTL 是定时炸弹，抖动让失效时间错开");