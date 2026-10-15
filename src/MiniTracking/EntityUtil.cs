namespace MiniTracking
{
    /// <summary>
    /// ---------------------------------------------------------------------
    /// 2. EntityUtil：实体值拷贝 / 主键字符串 / 逐列 diff
    /// （对应 FreeSql 的 MapEntityValue / GetEntityKeyString / CompareEntityValueReturnColumns）
    /// ---------------------------------------------------------------------
    /// </summary>
    public static class EntityUtil
    {
        // 把 src 的所有列值拷贝到 dest（快照就是这么做的：
        // 新建一个空实例，MapEntityValue 拷进去）
        public static void MapEntityValue(TableInfo tb, object src, object dest)
        {
            foreach (var col in tb.Columns)
            {
                if (col.Set != null && col.Get != null)
                    col.Set(dest, col.Get(src));
            }
        }
        // 主键拼字符串，作为状态字典的 key
        public static string? GetEntityKeyString(TableInfo tb, object entity)
        {
            if (!tb.Primarys.Any()) return null;
            return string.Join("|", tb.Primarys.Where(pk => pk.Get != null).Select(pk => $"{pk.Get!(entity)}"));
        }

        // 返回【发生了变化的列名】——FreeSql 的 CompareEntityValueReturnColumns
        public static List<string> DiffColumns(TableInfo tb, object snapshot, object current)
        {
            var changed = new List<string>();
            foreach (var col in tb.Columns)
            {
                if (col.Get == null || string.IsNullOrWhiteSpace(col.CsName)) continue;
                var oldV = col.Get(snapshot);
                var newV = col.Get(current);
                if (!Equals(oldV, newV))
                    changed.Add(col.CsName);
            }
            return changed;
        }
    }
}
