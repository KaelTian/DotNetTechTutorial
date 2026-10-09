using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MiddlewareDemo.Filters;

/// <summary>
/// 不需要 DI 容器的过滤器，使用 TypeFilter 注册
/// 构造函数带有参数，TypeFilter 可以传递参数给构造函数
/// </summary>
public class NoDIActionFilter : IAsyncActionFilter
{

    private readonly string _message;

    private readonly int _counter;

    public NoDIActionFilter(string message, int counter)
    {
        _message = message;
        _counter = counter;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Console.WriteLine($"NoDIActionFilter 拦截, Message: {_message},Counter: {_counter}");

        var executed = await next(); // 执行 action，返回后续的 ActionExecutedContext

        Console.WriteLine($"{context.ActionDescriptor.RouteValues["action"]} 执行完毕，结果: {(
            executed.Result is JsonResult jsonResult ? 
            jsonResult.Value : executed.Result?.ToString() ?? "null")}");
    }
}
