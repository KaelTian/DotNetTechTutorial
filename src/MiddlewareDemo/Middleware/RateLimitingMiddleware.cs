using System.Collections.Concurrent;

namespace MiddlewareDemo.Middleware
{
    public class RateLimitingOptions
    {
        public int Limit { get; set; } = 5;
        public int WindowSeconds { get; set; } = 10;
        public bool Enabled { get; set; } = true;
        public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
    }
    /// <summary>
    /// 简单限流：按客户端 IP 做固定窗口计数，超阈值返回 429 并短路（不调 next）。
    /// 注意：ConcurrentDictionary 的 read-modify-write 不是原子操作，
    /// 高并发（wrk -c100）下窗口边界可能有少量计数误差 —— 实验报告可当作讨论点。
    /// </summary>
    public class RateLimitingMiddleware
    {
        private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> Buckets = new ConcurrentDictionary<string, (int Count, DateTime WindowStart)>();

        private readonly RequestDelegate _next;

        private readonly RateLimitingOptions _options;

        public RateLimitingMiddleware(RequestDelegate next, RateLimitingOptions options)
        {
            _next = next;
            _options = options;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_options.Enabled)
            {
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var now = DateTime.UtcNow;
            var bucket = Buckets.GetOrAdd(ip, _ => (0, now));

            if (now - bucket.WindowStart > _options.Window)
            {
                Buckets[ip] = (1, now);
            }
            else if (bucket.Count >= _options.Limit)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = _options.WindowSeconds.ToString();
                await context.Response.WriteAsJsonAsync(new { error = "Too Many Requests", limit = _options.Limit, window = $"{_options.WindowSeconds}s" });
                return;
            }
            else
            {
                Buckets[ip] = (bucket.Count + 1, bucket.WindowStart);
            }

            await _next(context);
        }
    }

    public static class RateLimitingExtensions
    {
        public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app, Action<RateLimitingOptions>? configure = null)
        {
            var options = new RateLimitingOptions();
            configure?.Invoke(options);
            return app.UseMiddleware<RateLimitingMiddleware>(options);
        }
    }
}
