namespace LockDemo
{
    /// <summary>
    /// 简易锁
    /// </summary>
    internal class SimpleLock
    {
        private int _locked;
        private int _waiterCount;
        private readonly SemaphoreSlim _waiters = new(0);

        public void Enter()
        {
            // 快速通道：没人拿就直接 CAS 抢
            if (Interlocked.CompareExchange(ref _locked, 1, 0) == 0)
                return;

            // 抢不到：登记排队，挂起睡觉
            Interlocked.Increment(ref _waiterCount);
            while (true)
            {
                _waiters.Wait(); // 挂起线程，不烧CPU
                if (Interlocked.CompareExchange(ref _locked, 1, 0) == 0)
                    break; // 醒来后抢到钥匙
                // 没抢到（被别人先醒拿走了）：重新排队继续睡
            }
            Interlocked.Decrement(ref _waiterCount);
        }

        public void Exit()
        {
            Volatile.Write(ref _locked, 0); // 还钥匙
            if (Volatile.Read(ref _waiterCount) > 0) // 有人在排队吗？
                _waiters.Release(); // 叫醒一个排队的线程
        }
    }
}
