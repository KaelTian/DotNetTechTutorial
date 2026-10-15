using Microsoft.Data.Sqlite;

namespace MiniTracking
{
    public class SqliteUserStore : IUserStore<User>, IDisposable
    {
        private readonly SqliteConnection _conn;

        public SqliteUserStore(string dbPath = "mini.db")
        {
            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();
            InitSchema();
        }

        private void InitSchema()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""User""(
            Id INTEGER PRIMARY KEY,
            Name TEXT,
            Age INTEGER,
            Email TEXT)";
            cmd.ExecuteNonQuery();
        }

        public void Seed(IEnumerable<User> users)
        {
            foreach (var u in users)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"INSERT OR REPLACE INTO ""User""(Id,Name,Age,Email)
                                    VALUES(@Id,@Name,@Age,@Email)";
                AddParams(cmd, u, "Id", "Name", "Age", "Email");
                cmd.ExecuteNonQuery();
            }
        }

        public User Find(string key)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"SELECT Id,Name,Age,Email FROM ""User"" WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", int.Parse(key.Split('|')[0]));
            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) throw new ArgumentNullException(nameof(User));
            return new User
            {
                Id = rd.GetInt32(0),
                Name = rd.IsDBNull(1) ? null : rd.GetString(1),
                Age = rd.GetInt32(2),
                Email = rd.IsDBNull(3) ? null : rd.GetString(3)
            };
        }

        public int Update(User entity, List<string> updateCols, Action<string> log)
        {
            using var cmd = _conn.CreateCommand();
            // 参数化 SQL：SET 只包含变更列，WHERE 主键
            var sets = string.Join(", ", updateCols.Select(c => $"\"{c}\"=@{c}"));
            cmd.CommandText = $"UPDATE \"User\" SET {sets} WHERE \"Id\"=@Id";
            AddParams(cmd, entity, updateCols.Concat(new[] { "Id" }).Distinct().ToArray());
            log($"    [SQL] {cmd.CommandText}");
            log($"          params: {string.Join(", ", cmd.Parameters.Cast<SqliteParameter>().Select(p => $"{p.ParameterName}={Fmt(p.Value!)}"))}");

            var affrows = cmd.ExecuteNonQuery();
            log($"          -> {affrows} row(s) affected");
            return affrows;
        }

        public void Dispose() => _conn.Dispose();

        private static void AddParams(SqliteCommand cmd, User u, params string[] cols)
        {
            foreach (var c in cols)
                cmd.Parameters.AddWithValue("@" + c,
                    c switch
                    {
                        "Id" => u.Id,
                        "Name" => u.Name,
                        "Age" => u.Age,
                        "Email" => u.Email,
                        _ => null
                    });
        }

        private static string Fmt(object v) => v == null || v is DBNull ? "NULL" : (v is string ? $"'{v}'" : v.ToString()!);
    }
}
