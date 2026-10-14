namespace MiniTracking
{
    /// <summary>
    /// 一张表 = 实体类型 + 所有列 + 主键列
    /// </summary>
    public class TableInfo
    {
        public Type? EntityType;
        public List<ColumnInfo> Columns = new();
        public List<ColumnInfo> Primarys = new();
    }
}
