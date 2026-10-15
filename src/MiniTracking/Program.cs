using MiniTracking;


// 元数据注册
var userTable = new TableInfo { EntityType = typeof(User) };
foreach (var p in typeof(User).GetProperties())
    userTable.Columns.Add(ColumnInfo.FromProp(p));
userTable.Primarys.Add(userTable.Columns.First(c => c.CsName == "Id"));

using var store = new SqliteUserStore("mini.db");
store.Seed(new[]
{
            new User { Id = 1, Name = "张三", Age = 30, Email = "zs@x.com" },
            new User { Id = 2, Name = "李四", Age = 25, Email = "ls@x.com" },
});

var set = new DbSet<User>(userTable, store);
var SqlLog = new List<string>();
void Log(string s) => SqlLog.Add(s);
void Verify(int id, string tag)
{
    Console.WriteLine($"    {tag} 从 SQLite 读回: {store.Find(id.ToString())}");
}

Console.WriteLine("== 场景1：跟踪查询，改属性后 Update，只 UPDATE 变更列 ==");
var u1 = set.TrackFromStore(store.Find("1"));   // SELECT → 物化 → 进 _states
Console.WriteLine($"    查出来的实体: {u1}");
u1.Name = "张三丰";
u1.Age = 31;
set.Update(u1);       // 入队：冻结 {张三丰,31}
u1.Age = 32;
set.Update(u1);       // 再入队：冻结 {张三丰,32}
set.SaveChanges(Log);
SqlLog.ForEach(Console.WriteLine);
SqlLog.Clear();
Verify(1, "SaveChanges 后");
Console.WriteLine($"    状态字典里有 {set.TrackedCount} 条跟踪状态\n");

Console.WriteLine("== 场景2：一个属性都没改，Update 是空操作 ==");
var u2 = set.TrackFromStore(store.Find("2"));
u2.Name = "李四";     // 相同值，不算变化
set.Update(u2);
set.SaveChanges(Log);
SqlLog.ForEach(Console.WriteLine);
SqlLog.Clear();
Verify(2, "SaveChanges 后");
Console.WriteLine();

Console.WriteLine("== 场景3：AsNoTracking 查询，UpdateFull 全量 UPDATE ==");
var u3 = set.NoTrackingFromStore(store.Find("1"));
Console.WriteLine($"    查出来的实体: {u3}");
u3.Email = "zhangsf@x.com";   // 只改了这一个
set.UpdateFull(u3);           // SQL 里是全部列
set.SaveChanges(Log);
SqlLog.ForEach(Console.WriteLine);
SqlLog.Clear();
Verify(1, "SaveChanges 后");
Console.WriteLine();

Console.WriteLine("== 场景4：Attach 不查询直接更新（同样 diff）==");
var dbVal = store.Find("1");   // 库里此时 {张三丰, 32, zhangsf@x.com}
var u4 = new User { Id = 1, Name = "张三丰·改名", Age = 32, Email = "zhangsf@x.com" };
set.Attach(dbVal);   // 以库里当前值作为快照
set.Update(u4);      // diff：只有 Name 变了
set.SaveChanges(Log);
SqlLog.ForEach(Console.WriteLine);
Verify(1, "SaveChanges 后");

Console.WriteLine("\n全部落库完成，数据库文件: mini.db");

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