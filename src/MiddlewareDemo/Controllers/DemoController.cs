using Microsoft.AspNetCore.Mvc;
using MiddlewareDemo.Filters;

namespace MiddlewareDemo.Controllers
{
    [ApiController]
    [Route("api/demo")]
    public class DemoController : ControllerBase
    {
        // GET api/demo/42
        [HttpGet("{id:int}")]
        public IActionResult Get(int id)
        {
            Console.WriteLine($"    >>> DemoController.Get({id}) ← action 方法真正执行");
            return Ok(new { id, msg = "hello from action" });
        }

        [HttpGet("skip")]
        [ServiceFilter(typeof(ScopedActionFilter))]
        public IActionResult Skip()
        {
            Console.WriteLine("     >>> 这行不会打印");
            return Ok("这也不会返回");
        }

        [HttpGet("nodi-filter")]
        [TypeFilter(typeof(NoDIActionFilter), Arguments = new object[] { "不需要DI容器的过滤器", 110 })]
        public IActionResult NoDI()
        {
            Console.WriteLine("     >>> 这行会打印");
            return new JsonResult(new
            {
                msg = "nodi-filter 会返回"
            });
        }
    }
}
