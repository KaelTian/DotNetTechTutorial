namespace MiniTracking
{
    public interface IUserStore<T>
    {
        T Find(string key); // 模拟 SELECT
        int Update(T entity, List<string> updateCols, Action<string> log); // 模拟 UPDATE (真实执行)
    }
}
