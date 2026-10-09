using MiddlewareDemo.Filters;
using MiddlewareDemo.Middleware;

var builder = WebApplication.CreateBuilder(args);


// ★ 新增：注册 MVC + 全局过滤器
builder.Services.AddControllers(options =>
{
    options.Filters.Add<SimpleActionFilter>(); // 全局注册过滤器>
});
builder.Services.AddScoped<ScopedActionFilter>(); // 供 [ServiceFilter] 使用

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<RequestLoggingMiddleware>();

app.UseMiddleware<ExceptionHandlingMiddleware>();


app.UseRateLimiting(opts => builder.Configuration.GetSection("RateLimit").Bind(opts));

// 全局中间件 M1：打印请求路径，并展示"此时终结点已选好"（minimal hosting 下中间件跑在 UseRouting 之后）
app.Use(async (ctx, next) =>
{
    var endpoint = ctx.GetEndpoint()?.DisplayName ?? "(未匹配任何终结点)";
    Console.WriteLine($"[M1 全局中间件] before  path={ctx.Request.Path}  已选终结点={endpoint}");
    await next();
    Console.WriteLine($"[M1 全局中间件] after   path={ctx.Request.Path}");
});


// ====== Map 分支：/admin 走一条独立的子管道 ======
app.Map("/admin", admin =>
{
    admin.Use(async (ctx, next) =>
    {
        Console.WriteLine("[Map /admin] 进入 admin 子管道");
        await next();
    });
    admin.Run(async ctx =>
        await ctx.Response.WriteAsync("Admin area (share rate limit with main)"));
});


// 主管道是 WebApplication，先转成 IApplicationBuilder 再 New()
var subPipeline = ((IApplicationBuilder)app).New();
subPipeline.Use(async (ctx, next) =>
{
    Console.WriteLine("[New 子管道] before");
    await next();
    Console.WriteLine("[New 子管道] after");
});
// 子管道末尾必须有终端写响应，否则走到管道末尾会抛 "reached the end of the pipeline without executing the endpoint"
subPipeline.Run(async ctx => await ctx.Response.WriteAsync("Hello from sub pipeline!"));
var branch = ((IApplicationBuilder)subPipeline).Build();
// 用 PathString 参数 + Action 重载挂载子管道，避免被解析成 IEndpointRouteBuilder.Map（那是注册端点，不是分支）
app.Map(new PathString("/sub"), sub => sub.Run(branch));
//app.Map("/sub", branch);

app.MapGet("/boom", () => { 
    throw new InvalidOperationException("Something went wrong!"); 
});

// ★ 新增：MVC 终结点注册（过滤器藏在它的执行过程内部）
app.MapControllers();

// 主管道兜底（用 MapFallback，不是 app.Run —— 上次踩过的坑）
app.MapFallback(async ctx =>
    await ctx.Response.WriteAsync($"[MapFallback] 没有命中任何终结点: {ctx.Request.Path}"));

app.Run();
