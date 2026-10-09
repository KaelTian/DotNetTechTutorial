using System.Diagnostics;

namespace MiddlewareDemo.Middleware
{
    /// <summary>
    /// 请求日志：请求阶段打入口日志，响应阶段（next 之后）打耗时。
    /// 注意：注册在异常处理之前，包裹住异常处理中间件，可以看到即使下游抛异常，也能记录完整的请求日志。
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public RequestLoggingMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine($"[日志] --> {context.Request.Method} {context.Request.Path} from {context.Connection.RemoteIpAddress}");

            await _next(context);   // ★ 异步续体：next 返回后，响应正在往回走

            sw.Stop();
            Console.WriteLine($"[日志] <-- {context.Response.StatusCode} 耗时 {sw.ElapsedMilliseconds}ms");
        }
    }
}
