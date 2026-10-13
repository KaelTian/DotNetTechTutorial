using System.Net.Sockets;

namespace ModbusRtuMasterDemo
{
    /// <summary>
    /// 纯 Modbus TCP 主站：MBAP 头 + PDU，无 CRC，靠事务ID匹配（支持并发请求）
    /// 对应服务器：HSL 虚拟服务器【不勾】RTU 报文选项（默认模式）
    /// </summary>
    public class ModbusTcpMaster : IDisposable
    {
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private readonly List<byte> _rxBuffer = new List<byte>();
        private readonly object _rxLock = new object();
        private int _tidCounter;
        // ★ 与 RTU 版最大的区别：用 Dictionary 按事务ID挂起多个请求，不再需要 SemaphoreSlim
        private readonly Dictionary<ushort, TaskCompletionSource<byte[]>> _pending = new();

        public int TimeoutMs { get; set; } = 1000;
        public int RetryCount { get; set; } = 2;

        public void Connect(string ip, int port)
        {
            _tcp = new TcpClient();
            _tcp.Connect(ip, port);
            _stream = _tcp.GetStream();
            _ = Task.Run(ReceiveLoop);
        }
        /// <summary>
        /// ============ 0x03 读保持寄存器 ============
        /// </summary>
        /// <param name="unitId"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort start, ushort count, CancellationToken ct = default)
        {
            var pdu = new byte[5];
            pdu[0] = 0x03;
            pdu[1] = (byte)(start >> 8);
            pdu[2] = (byte)start;
            pdu[3] = (byte)(count >> 8);
            pdu[4] = (byte)count;

            byte[] respPdu = await TransactAsync(unitId, pdu, ct);
            if ((respPdu[0] & 0x80) != 0)
                throw new Exception($"从站异常：异常码 0x{respPdu[1]:X2}");

            int byteCount = respPdu[1];
            var values = new ushort[byteCount / 2];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ushort)((respPdu[2 + i * 2] << 8) | respPdu[3 + i * 2]);

            return values;
        }

        public async Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, CancellationToken ct = default)
        {
            var pdu = new byte[5];
            pdu[0] = 0x06;
            pdu[1] = (byte)(address >> 8);
            pdu[2] = (byte)address;
            pdu[3] = (byte)(value >> 8);
            pdu[4] = (byte)value;

            byte[] respPdu = await TransactAsync(unitId, pdu, ct);
            for (int i = 0; i < 5; i++)
                if (respPdu[i] != pdu[i]) throw new InvalidOperationException("写响应回显与请求不一致");
        }
        /// <summary>
        /// ============ 事务：组 MBAP 帧 → 按事务ID挂起 → 超时重试 ============
        /// </summary>
        /// <param name="unitId"></param>
        /// <param name="pdu"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<byte[]> TransactAsync(byte unitId, byte[] pdu, CancellationToken ct)
        {
            ushort tid = (ushort)Interlocked.Increment(ref _tidCounter);
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_rxLock) _pending[tid] = tcs;

            // MBAP: 事务ID(2) + 协议ID(2,=0) + 长度(2) + 单元标识(1)，长度 = 单元标识+PDU
            var frame = new byte[7 + pdu.Length];
            frame[0] = (byte)(tid >> 8);
            frame[1] = (byte)tid;
            frame[4] = (byte)((pdu.Length + 1) >> 8);
            frame[5] = (byte)(pdu.Length + 1);
            frame[6] = unitId;
            Array.Copy(pdu, 0, frame, 7, pdu.Length);

            try
            {
                Exception? last = null;
                for (int attempt = 0; attempt <= RetryCount; attempt++)
                {
                    try { await _stream!.WriteAsync(frame, 0, frame.Length, ct); }
                    catch (IOException ex) { throw new IOException("连接已断开，重试无意义", ex); }

                    Console.WriteLine($"[TX] {BitConverter.ToString(frame)}  (第{attempt + 1}次)");
                    var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeoutMs, ct));
                    if (done == tcs.Task) return await tcs.Task;
                    last = new TimeoutException($"超时({TimeoutMs}ms)，第{attempt + 1}次");
                    Console.WriteLine($"[WARN] {last.Message}");
                }
                throw last!;
            }
            finally { lock (_rxLock) _pending.Remove(tid); }
        }
        /// <summary>
        /// ============ 收字节 + 拆包：长度字段驱动，比 RTU 版简单得多 ============
        /// </summary>
        private void ReceiveLoop()
        {
            var buf = new byte[4096];
            try
            {
                while (true)
                {
                    int n = _stream!.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    lock (_rxLock)
                    {
                        _rxBuffer.AddRange(buf.AsSpan(0, n).ToArray());
                        TryExtractFrames();
                    }
                }
            }
            catch (IOException) { /* 连接断开 */}

            lock (_rxLock)
            {
                foreach (var kv in _pending) kv.Value.TrySetException(new IOException("连接已断开"));
                _pending.Clear();
            }
        }

        private void TryExtractFrames()
        {
            while (true)
            {
                if (_rxBuffer.Count < 7) return;

                // ★ 协议ID 必须是 0，不是则说明错位（TCP 流中混入垃圾），逐字节重新对齐
                if (_rxBuffer[2] != 0 || _rxBuffer[3] != 0)
                {
                    _rxBuffer.RemoveAt(0);
                    continue;
                }

                int length = (_rxBuffer[4] << 8) | _rxBuffer[5]; // ★ 长度字段直接给出帧长
                int total = 6 + length;
                if (_rxBuffer.Count < total) return; // 拆包： 等下一波数据

                var frame = _rxBuffer.GetRange(0, total).ToArray();
                _rxBuffer.RemoveRange(0, total);

                ushort tid = (ushort)((frame[0] << 8) | frame[1]);
                var pdu = new byte[frame.Length - 7];
                Array.Copy(frame, 7, pdu, 0, pdu.Length);
                Console.WriteLine($"[RX] {BitConverter.ToString(frame)}");

                if (_pending.TryGetValue(tid, out var tcs))
                    tcs.TrySetResult(pdu); // 按ID匹配
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _tcp?.Close();
        }
    }
}
