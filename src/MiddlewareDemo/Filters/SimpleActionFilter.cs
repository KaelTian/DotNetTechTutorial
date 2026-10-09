using Microsoft.AspNetCore.Mvc.Filters;

namespace MiddlewareDemo.Filters
{
    /// <summary>
    /// 全局注册：所有 controller/action 都经过（演示"看得见 action 参数和结果"）
    /// </summary>
    public class SimpleActionFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var action = context.ActionDescriptor.RouteValues["action"];
            var args = string.Join(", ", context.ActionArguments.Select(kv => $"{kv.Key}={kv.Value}"));
            Console.WriteLine($"[ActionFilter 全局] before -> 即将执行 {action} ({args})");

            var executed = await next(); // 执行 action，返回后续的 ActionExecutedContext

            Console.WriteLine($"[ActionFilter 全局] after <- {action} 执行完毕，结果: {executed.Result?.GetType()?.Name ?? "null"}");
        }
    }
}
