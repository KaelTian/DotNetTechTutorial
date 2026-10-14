using System;
using System.Collections.Generic;
using System.Text;

namespace MiniTracking
{
    /// <summary>
    /// 3. 实体状态：快照副本 + key + 时间 （对应 DbSet.cs 内部类 EntityState）
    /// </summary>
    public class EntityState
    {
        public string? Key;
        public object? Snapshot; // 快照副本，不是原实体引用!
        public DateTime Time = DateTime.Now;
    }
}
