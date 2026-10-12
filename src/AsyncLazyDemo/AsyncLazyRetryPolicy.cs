namespace AsyncLazyDemo
{
    /// <summary>
    /// Lazy<T> 的三种线程安全模式回顾：
    ///  1. LazyThreadSafetyMode.None               —— 多线程下不保证，可能多次执行、返回不同实例
    ///  2. PublicationOnly                          —— 多个线程可同时执行初始化，但只"发布"一个结果（可能浪费执行）
    ///  3. ExecutionAndPublication（默认）           —— 只执行一次，其余线程阻塞等待（可能死锁风险）
    /// AsyncLazy<T> 的目标：像 ExecutionAndPublication 一样"只执行一次"，
    /// 但等待方式是 await（非阻塞），且不持有锁执行异步代码（避免线程池占用与死锁）。
    /// </summary>
    public enum AsyncLazyRetryPolicy
    {
        /// <summary>
        /// 初始化失败后缓存异常，后续 await 都拿到同一个异常（类比 Lazy&lt;T&gt; 默认行为）。
        /// </summary>
        CacheException,
        /// <summary>
        /// 初始化失败后不缓存异常，后续 await 会重新执行初始化（类比 Lazy&lt;T&gt; PublicationOnly 行为）。
        /// </summary>
        AllowRetry
    }
}
