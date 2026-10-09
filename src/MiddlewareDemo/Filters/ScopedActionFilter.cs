using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MiddlewareDemo.Filters
{
    /// <summary>
    /// 用 [ServiceFilter(typeof(ScopedActionFilter))] 打在方法上
    /// 它走 DI 容器，所以构造函数里能注入 scoped 服务（比如 DbContext）——这是中间件不好直接做到的
    /// </summary> 
    public class ScopedActionFilter : IAsyncActionFilter
    {
        public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            Console.WriteLine("[ScopedActionFilter action级] before（此过滤器来自 DI 容器）");

            // 短路演示：给 Result 赋值并且【不调用 next】，action 方法体根本不会执行
            context.Result = new JsonResult(new
            {
                intercepted = true,
                msg = "action 被过滤器拦截，未执行"
            });
            return Task.CompletedTask;
        }
    }
}
