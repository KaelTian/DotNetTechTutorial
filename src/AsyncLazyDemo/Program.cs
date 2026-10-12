

using AsyncLazyDemo;

await Demo_ConcurrencyDeduplication();
await Demo_ExceptionCache();
await Demo_Retry();
Console.WriteLine("Press enter to end...");
Console.ReadLine();

async Task Demo_ConcurrencyDeduplication()
{
    int callCount = 0;
    var lazy = new AsyncLazy<string>(async ct =>
    {
        Interlocked.Increment(ref callCount);
        await Task.Delay(500); // 模拟昂贵的异步初始化
        return "db-connection";
    });

    // N 个并发 await: 全部成功，但初始化只执行一次
    var results = await Task.WhenAll(
        Enumerable.Range(0, 20).Select(_ => lazy.GetValueAsync()));

    Console.WriteLine($"[去重] 并发 20个，初始化执行次数 = {callCount}，结果一致 = {results.All(r => r == "db-connection")}");
    // 预期：callCount = 1，结果一致 = True
}

async Task Demo_ExceptionCache()
{
    int attempts = 0;
    var lazy = new AsyncLazy<int>(_ =>
    {
        Interlocked.Increment(ref attempts);
        throw new InvalidOperationException("booms");
    });

    for (int i = 0; i < 3; i++)
    {
        try { await lazy.GetValueAsync(); }
        catch (InvalidOperationException)
        {
            /* 同一个缓存的异常 */
        }
    }
    Console.WriteLine($"[异常缓存] 尝试次数 = {attempts} （应为 1，异常被缓存不重试）");
}

async Task Demo_Retry()
{
    int attempts = 0;
    var lazy = new AsyncLazy<int>(async ct =>
    {
        var n = Interlocked.Increment(ref attempts);
        await Task.Delay(10);
        if (n < 3)
        {
            throw new InvalidOperationException($"第 {n} 次失败");
        }
        return n; // 第 3 次成功
    }, AsyncLazyRetryPolicy.AllowRetry);

    var lastEx = (Exception?)null;
    for (int i = 0; i < 5; i++)
    {
        try
        {
            var v = await lazy.GetValueAsync();
            Console.WriteLine($"[重试] 成功，值 = {v}，总尝试 = {attempts}");
            break;
        }
        catch (Exception ex)
        {
            lastEx = ex;
        }
    }
    Console.WriteLine($"[重试] 最终异常 = {lastEx?.Message}（前两次失败后第三次成功）");
}
