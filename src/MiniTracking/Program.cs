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




/// <summary>
/// 模拟数据库：内存表 + 实体
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Age { get; set; }

    public string Email { get; set; } = string.Empty;

    public override string ToString() => $"User{{Id={Id},Name={Name},Age={Age},Email={Email}}}";
}