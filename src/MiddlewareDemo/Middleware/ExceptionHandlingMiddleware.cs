namespace MiddlewareDemo.Middleware
{
    /// <summary>
    /// 全局异常处理: try/catch 包住 next，统一返回 500 JSON。
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;

        public ExceptionHandlingMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[异常] {ex.GetType().Name}: {ex.Message}");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Internal Server Error" });
            }
        }
    }
}
