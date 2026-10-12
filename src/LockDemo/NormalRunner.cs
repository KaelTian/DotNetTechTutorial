using System.Diagnostics;

namespace LockDemo
{
    internal class NormalRunner : ILockRunner
    {
        private int _counter = 0;
        public void Run()
        {
            Console.WriteLine("========== 测试 1：无锁，演示竞争失败 ==========");
            _counter = 0;
            // 8个线程各执行 10万次 "读→让出CPU→写"，理论共 +800000
            Parallel.For(0, 8, _ =>
            {
                for (int i = 0; i < 100_000; i++)
                {
                    int temp = _counter; // 读
                    Thread.Yield();      // 主动让出CPU，另一个线程读了同一个旧值
                    _counter = temp + 1; // 写：基于旧值，更新被覆盖
                }
            });
            Console.WriteLine($"理论应加总: {8 * 100_000}");
            Console.WriteLine($"实际结果  : {_counter}   ← 差值就是被竞争吃掉的更新");

            Console.WriteLine();
            Console.WriteLine("========== 测试 2：使用 SimpleLock ==========");
            _counter = 0;
            var theLock = new SimpleLock();
            var sw = Stopwatch.StartNew();
            Parallel.For(0, 8, _ =>
            {
                for (int i = 0; i < 100_000; i++)
                {
                    theLock.Enter();
                    try
                    {
                        _counter++; // 读+写，原子操作
                    }
                    finally
                    {
                        theLock.Exit();
                    }
                }
            });
            sw.Stop();
            Console.WriteLine($"实际结果  : {_counter}   (应为 800000)");
            Console.WriteLine($"使用 SimpleLock 耗时: {sw.ElapsedMilliseconds} ms");

            Console.WriteLine();
            Console.WriteLine("========== 测试 3：保护『检查-修改』复合操作 ==========");
            var account = new BankAccount();
            Parallel.For(0, 10, _ =>
            {
                for (int i = 0; i < 1000; i++) account.Deposit(1);
            });
            Parallel.For(0, 10, _ =>
            {
                for (int i = 0; i < 500; i++) account.Withdraw(1);
            });
            Console.WriteLine($"账户余额  : {account.Balance}  (存10000 取5000，应为 5000)");

            Console.WriteLine();
            Console.WriteLine("========== 测试 4：重入说明 ==========");
            Console.WriteLine("SimpleLock 不可重入：同线程二次 Enter 会死锁（自己等自己释放）。");
            Console.WriteLine("对比：lock(Monitor) / Mutex 支持重入，同线程可嵌套进入。");
            Console.WriteLine("本测试故意不演示死锁，避免程序卡死。");
        }

        class BankAccount
        {
            private int _balance = 0;
            private readonly SimpleLock _lock = new SimpleLock();

            public int Balance
            {
                get
                {
                    _lock.Enter();
                    try
                    {
                        return _balance;
                    }
                    finally
                    {
                        _lock.Exit();
                    }
                }
            }

            public void Deposit(int amount)
            {
                _lock.Enter();
                try
                {
                    _balance += amount;
                }
                finally
                {
                    _lock.Exit();
                }
            }

            public bool Withdraw(int amount)
            {
                _lock.Enter();
                try
                {
                    if (_balance >= amount)
                    {
                        _balance -= amount;
                        return true;
                    }
                    return false;
                }
                finally
                {
                    _lock.Exit();
                }
            }
        }
    }
}
