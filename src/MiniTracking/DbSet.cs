using System.Collections.Concurrent;

namespace MiniTracking
{
    /// <summary>
    /// 4. DbSet<T>：状态管理器 （对应 FreeSql DbSet<T> 的核心字段与方法）
    /// </summary>
    public class DbSet<T> where T : class, new()
    {
        public TableInfo Table;
        private readonly List<T> _store; // 模拟数据库的内存表

        // FreeSql 同款: ConcurrentDictionary<主键字符串，EntityState>
        private readonly ConcurrentDictionary<string, EntityState> _states = new();
        // 待执行的命令队列（FreeSql 里是 EnqueueToDbContext，SaveChanges 统一执行）
        private readonly List<Action<Action<string>>> _pending = new();

        public DbSet(TableInfo table, List<T> store)
        {
            Table = table;
            _store = store;
        }
        /// <summary>
        /// ---- 查询跟踪：TrackToList 的模拟版 ----
        /// 真实 ORM 里查询回来的是"数据库的值"，这里模拟：
        /// 返回 store 里的实体【拷贝】给用户改，并把该拷贝的快照放进 _states
        /// </summary>
        /// <param name="storeEntity"></param>
        /// <returns></returns>
        public T TrackFromStore(T storeEntity)
        {
            var copy = new T();
            EntityUtil.MapEntityValue(Table, storeEntity, copy);
            AddOrRefreshState(copy);
            return copy;
        }
        /// <summary>
        /// ---- AsNoTracking：查出来的东西不进 _states ----
        /// </summary>
        /// <param name="storeEntity"></param>
        /// <returns></returns>
        public T NoTrackingFromStore(T storeEntity)
        {
            var copy = new T();
            EntityUtil.MapEntityValue(Table, storeEntity, copy);
            return copy;
        }
        /// <summary>
        /// ---- Attach：不查询就更新（FreeSql 的 Attach 语义）----
        /// </summary>
        /// <param name="entity"></param>
        public void Attach(T entity) => AddOrRefreshState(entity);

        private void AddOrRefreshState(T entity)
        {
            var key = EntityUtil.GetEntityKeyString(Table, entity);
            _states.AddOrUpdate(key!,
                _ => new EntityState { Key = key, Snapshot = CopyOf(entity) },
                (_, old) =>
                {
                    old.Snapshot = CopyOf(entity);
                    old.Time = DateTime.Now;
                    return old;
                });
        }
        /// <summary>
        /// ---- 有跟踪的更新：入队时捕获当时的值，SaveChanges 时 diff ----
        /// </summary>
        /// <param name="entity"></param>
        public void Update(T entity)
        {
            var key = EntityUtil.GetEntityKeyString(Table, entity);
            if (key == null || !_states.TryGetValue(key, out var state))
                throw new Exception($"数据未被跟踪，应该先查询或 Attach：key={key}");

            // 关键：入队时捕获副本（FreeSql 的 CreateEntityState(item)），
            // 防止用户在 SaveChanges 前继续改实体导致 diff 失真
            var captured = CopyOf(entity);

            _pending.Add(log =>
            {
                // === 核心：捕获值 vs _states 快照，逐列 diff ===
                // （DbContextBatchUpdatePriv 的逻辑）
                var changed = EntityUtil.DiffColumns(Table, state.Snapshot!, captured);
                if (changed.Count == 0)
                {
                    log($"    (无变更列，跳过 UPDATE，key={state.Key})");
                    return;
                }
                EmitUpdateSql(captured, changed, log);

                // 1）把新值应用到"数据库"
                ApplyToStore(captured);
                // 2）把新值拷回快照，准备下一轮对比
                // （FreeSql SaveChanges 后同样会刷新快照）
                EntityUtil.MapEntityValue(Table, captured, (T)state.Snapshot!);
                state.Time = DateTime.Now;
            });
        }
        /// <summary>
        /// ---- AsNoTracking 实体的更新：全量列（EF 的 DbSet.Update 行为）----
        /// </summary>
        /// <param name="entity"></param>
        public void UpdateFull(T entity)
        {
            var captured = CopyOf(entity);
            _pending.Add(log =>
            {
                var all = Table.Columns.Where(c => !string.IsNullOrWhiteSpace(c.CsName))
                .Select(c => c.CsName)
                .ToList();
                EmitUpdateSql(captured, all!, log);
                ApplyToStore(captured);
            });
        }

        // SaveChanges：统一执行队列（FreeSql 的 FlushCommand）
        public int SaveChanges(Action<string> sqlLogger)
        {
            var n = 0;
            foreach (var cmd in _pending)
            {
                cmd(sqlLogger);
                n++;
            }
            _pending.Clear();
            return n;
        }

        #region 内部工具
        private T CopyOf(T entity)
        {
            var t = new T();
            EntityUtil.MapEntityValue(Table, entity, t);
            return t;
        }

        private object ColVal(string name, T e) => Table.Columns.First(c => c.CsName == name).Get!(e);

        private static string Fmt(object v) => v == null ? "NULL" : (v is string ? $"'{v}'" : v.ToString()!);

        private void EmitUpdateSql(T captured,List<string> updateCols,Action<string> log)
        {
            //var sets = string.Join(", ", updateCols.Select(c => $"{c}=@{c}({Fmt(ColVal(c, captured))})"));
            //var where = string.Join(" AND ", Table.Primarys.Select(pk => $"{pk.CsName}=@{pk.CsName}"));
            //log($" UPDATE {typeof(T).Name} SET {sets} WHERE {where}");
            var sets = string.Join(", ", updateCols.Select(c => $"{c}=@{c}"));
            var where = string.Join(" AND ", Table.Primarys.Select(pk => $"{pk.CsName}=@{pk.CsName}"));
            var ps = string.Join(", ",
                updateCols.Select(c => $"@{c}={Fmt(ColVal(c, captured))}")
                    .Concat(Table.Primarys.Select(pk => $"@{pk.CsName}={Fmt(pk.Get!(captured))}")));
            log($"    UPDATE {typeof(T).Name} SET {sets} WHERE {where}    -- params: {ps}");
        }

        private void ApplyToStore(T entity)
        {
            var key = EntityUtil.GetEntityKeyString(Table, entity);
            var row = _store.FirstOrDefault(a => EntityUtil.GetEntityKeyString(Table, a) == key);
            if (row != null) EntityUtil.MapEntityValue(Table, entity, row);
        }

        public int TrackedCount => _states.Count;
        public void FlushState() => _states.Clear(); // FreeSql 的 FlushState
        #endregion
    }
}
