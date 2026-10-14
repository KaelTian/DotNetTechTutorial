using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace MiniTracking
{
    /* =====================================================================
     * MiniTracking —— 提炼 FreeSql 的 ChangeTracking 原理做的最小实现
     *
     * 对应关系：
     *   ColumnInfo            <-> FreeSql ColumnInfo（列元数据）
     *   TableInfo             <-> FreeSql TableInfo
     *   EntityUtil            <-> FreeSql EntityUtilExtensions
     *                             (MapEntityValue / GetEntityKeyString /
     *                              CompareEntityValueReturnColumns)
     *   DbSet<T>.EntityState  <-> DbSet.cs 里的内部类 EntityState
     *   DbSet<T>._states      <-> ConcurrentDictionary<string, EntityState>
     *   DbSet<T>.TrackToList  <-> 查询结果自动进入 _states
     *   DbContextBatchUpdate  <-> DbSetSync.cs 里 diff 后 IgnoreColumns(没变列)
     *   AsNoTracking          <-> EF/FreeSql 的无跟踪查询 => 全量列更新
     * ===================================================================== */

    // ---------------------------------------------------------------------
    // 1. 元数据：一列 = 属性 + 编译好的 get/set 委托（FreeSql 用 IL 缓存，
    //    这里用最简单的 Expression.Compile，原理相同）
    // ---------------------------------------------------------------------
    public class ColumnInfo
    {
        public string? CsName;
        public PropertyInfo? Prop;
        public Func<object, object>? Get; // compiled getter
        public Action<object, object>? Set; // compiled setter

        public static ColumnInfo FromProp(PropertyInfo p)
        {
            // obj => (object)((TEntity)obj).Prop
            var obj = Expression.Parameter(typeof(object), "obj");
            var getter = Expression.Lambda<Func<object, object>>(
                Expression.Convert(
                    Expression.Property(Expression.Convert(obj, p.DeclaringType!), p),
                    typeof(object)), obj).Compile();

            // (obj, val) => ((TEntity)obj).Prop = (PropType)val
            var val = Expression.Parameter(typeof(object), "val");
            var setter = Expression.Lambda<Action<object, object>>(
                Expression.AddAssign(
                    Expression.Property(Expression.Convert(obj, p.DeclaringType!), p),
                    Expression.Convert(val, p.PropertyType)), obj, val).Compile();

            return new ColumnInfo { CsName = p.Name, Prop = p, Get = getter, Set = setter };
        }
    }
}
