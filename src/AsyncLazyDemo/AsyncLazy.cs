namespace AsyncLazyDemo
{
    public sealed class AsyncLazy<T>
    {
        private readonly object _sync = new();
        private readonly Func<CancellationToken, Task<T>> _factory;
        private readonly AsyncLazyRetryPolicy _retryPolicy;
        private Task<T>? _task;

        public AsyncLazy(Func<CancellationToken, Task<T>> factory,
            AsyncLazyRetryPolicy retryPolicy = AsyncLazyRetryPolicy.CacheException)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _retryPolicy = retryPolicy;
        }

        public Task<T> GetValueAsync(CancellationToken ct = default)
        {
            Task<T>? t = Volatile.Read(ref _task);
            if (t is null)
            {
                lock (_sync)
                {
                    t = _task;
                    if (t is null)
                    {
                        t = RunInitAsync();
                        Volatile.Write(ref _task, t);
                    }
                }
            }
            return AwaitWithCancellationAsync(t, ct);
        }

        private async Task<T> RunInitAsync()
        {
            try
            {
                // ConfigureAwait(false)：不捕获调用方 SynchronizationContext，避免 UI/ASP.NET 场景死锁
                return await _factory(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                if (_retryPolicy == AsyncLazyRetryPolicy.AllowRetry)
                    lock (_sync) { _task = null; } // 允许重试：下次重新初始化
                throw; // 本次失败仍广播给所有当前等待者
            }
        }

        private async Task<T> AwaitWithCancellationAsync(Task<T> init, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
                return await init.ConfigureAwait(false);

            var cancelWait = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (ct.Register(() => cancelWait.TrySetResult(true)).ConfigureAwait(false))
            {
                var done = await Task.WhenAny(init, cancelWait.Task).ConfigureAwait(false);
                if (done != init)
                    ct.ThrowIfCancellationRequested(); // 等待者被取消；初始化本身不受影响地继续

                return await init.ConfigureAwait(false);
            }
        }

        public bool IsValueCreated
        {
            get
            {
                var t = Volatile.Read(ref _task);
                return t is { IsCompletedSuccessfully: true };
            }
        }
    }
}
