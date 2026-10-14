using MiniTracking;


// 元数据注册 (FreeSql 里是 CodeFirst 扫描特性/FluentApi 得到)
var userTable = new TableInfo { EntityType = typeof(User) };
foreach (var p in typeof(User).GetProperties())
    userTable.Columns.Add(ColumnInfo.FromProp(p));
userTable.Primarys.Add(userTable.Columns.First(c => c.CsName == "Id"));

// ============ 内存数据库 seed ============
var db = new List<User>
{
    new(){Id=1,Name="张三",Age=30,Email="zs@x.com"},
    new(){Id=2,Name="李四",Age=25,Email="ls@x.com"}
};
var set = new DbSet<User>(userTable, db);
var SqlLog = new List<string>();
void Log(string s) => SqlLog.Add(s);

Console.WriteLine("== 场景1：跟踪查询，改属性后 Update，只 SET 变更列 ==");
var u1 = set.TrackFromStore(db.First(a => a.Id == 1)); // 等价 ToList + TrackToList
Console.WriteLine($"    查出来的实体: {u1}");
u1.Name = "张三丰";
u1.Age = 31;
set.Update(u1); // 入队：捕获 {张三丰,31}
// 入队后再改一次，验证 diff 基于“入队时的捕获值”而不是最终值
u1.Age = 32;
set.Update(u1); // 再入队：捕获 {张三丰,32}
set.SaveChanges(Log); // 第1条 SQL: SET Name,Age=31；第2条: SET Age=32
SqlLog.ForEach(Console.WriteLine);
SqlLog.Clear();
Console.WriteLine($"    SaveChanges 后内存库: {db.First(a => a.Id == 1)}");
Console.WriteLine($"    状态字典里有 {set.TrackedCount} 条跟踪状态\n");

Console.WriteLine("== 场景2：一个属性都没改，Update 应该是空操作 ==");
var u2 = set.TrackFromStore(db.First(a => a.Id == 2));
u2.Name = "李四";     // 赋了相同值，不算变化
set.Update(u2);
set.SaveChanges(Log);
SqlLog.ForEach(Console.WriteLine);
SqlLog.Clear();
Console.WriteLine();

Console.WriteLine("== 场景3：AsNoTracking 查询，UpdateFull 全量更新所有列 ==");
var u3 = set.NoTrackingFromStore(db.First(a => a.Id == 1));
Console.WriteLine($"    查出来的实体: {u3}");
u3.Email = "zhangsf@x.com";   // 只改了这一个
set.UpdateFull(u3);           // SQL 里却是全部 4 列
set.SaveChanges(Log);
SqlLog.ForEach(Console.WriteLine);
SqlLog.Clear();
Console.WriteLine($"    SaveChanges 后内存库: {db.First(a => a.Id == 1)}\n");

Console.WriteLine("== 场景4：Attach 不查询直接更新（同样 diff）==");
// 经过场景1和场景3，库里现在是 {张三丰, 32, zhangsf@x.com}
// 构造一个手工实体只改 Name，Age/Email 与库里一致
var u4 = new User { Id = 1, Name = "张三丰·改名", Age = 32, Email = "zhangsf@x.com" };
set.Attach(db.First(a => a.Id == 1));  // 以库里当前值作为快照
set.Update(u4);                        // diff 结果：只有 Name 变了
set.SaveChanges(Log);
SqlLog.ForEach(Console.WriteLine);
Console.WriteLine($"    SaveChanges 后内存库: {db.First(a => a.Id == 1)}");

Console.WriteLine("\n完成。接 SQLite：把 EmitUpdateSql 改成用 Microsoft.Data.Sqlite 执行即可，");
Console.WriteLine("跟踪层一个字都不用动。");


/// <summary>
/// 模拟数据库：内存表 + 实体
/// </summary>
public class User
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public int Age { get; set; }

    public string? Email { get; set; }

    public override string ToString() => $"User{{Id={Id},Name={Name},Age={Age},Email={Email}}}";
}