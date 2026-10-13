using System.Net.Sockets;

namespace ModbusRtuMasterDemo
{
    /// <summary>
    /// RTU over TCP 主站：报文格式与串口版完全相同（地址+功能码+数据+CRC16），
    /// 只是传输层从 SerialPort 换成 TcpClient。
    /// </summary>
    public class ModbusRtuOverTcpMaster : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly List<byte> _rxBuffer = new List<byte>();
        private readonly object _rxLock = new object();
        private readonly SemaphoreSlim _txLock = new SemaphoreSlim(1, 1);
        private PendingRequest? _pending;

        public int TimeoutMs { get; set; } = 1000;
        public int RetryCount { get; set; } = 2;

        public ModbusRtuOverTcpMaster(string ip, int port)
        {
            _tcp = new TcpClient();
            _tcp.Connect(ip, port);          // ★ 唯一的"连接"差异：IP+端口
            _stream = _tcp.GetStream();
            _ = Task.Run(ReceiveLoop);       // ★ 用后台读循环替代 DataReceived 事件
        }

        // ============ 收字节：NetworkStream 读循环（替代串口的 DataReceived）============
        private void ReceiveLoop()
        {
            var buf = new byte[4096];
            try
            {
                while (true)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;         // 服务器断开
                    lock (_rxLock)
                    {
                        _rxBuffer.AddRange(buf.AsSpan(0, n).ToArray());
                        TryExtractFrames();    // ★ 拆包/CRC/事务匹配 —— 与串口版一模一样
                    }
                }
            }
            catch (IOException) { /* 连接断开 */ }
        }

        // ============ 以下与 ModbusRtuMaster 完全相同（略）============
        public static ushort ComputeCrc(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[offset + i];
                for (int b = 0; b < 8; b++)
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }

        public async Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort start, ushort count, CancellationToken ct = default)
        {
            byte[] req = new byte[8];
            req[0] = slaveId; req[1] = 0x03;
            req[2] = (byte)(start >> 8); req[3] = (byte)start;
            req[4] = (byte)(count >> 8); req[5] = (byte)count;
            ushort crc = ComputeCrc(req, 0, 6);
            req[6] = (byte)(crc & 0xFF); req[7] = (byte)(crc >> 8);

            byte[] resp = await TransactAsync(req, slaveId, 0x03, ct);
            int byteCount = resp[2];
            var values = new ushort[byteCount / 2];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ushort)((resp[3 + i * 2] << 8) | resp[4 + i * 2]);
            return values;
        }

        /// <summary>
        /// 3. 写单寄存器 (0x06)，响应是请求回显
        /// </summary>
        /// <param name="slaveId"></param>
        /// <param name="address"></param>
        /// <param name="value"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken ct = default)
        {
            byte[] req = new byte[8];
            req[0] = slaveId;
            req[1] = 0x06;
            req[2] = (byte)(address >> 8);
            req[3] = (byte)address;
            req[4] = (byte)(value >> 8);
            req[5] = (byte)value;
            AppendCrc(req);
            byte[] resp = await TransactAsync(req, slaveId, 0x06, ct);
            // 简单校验回显
            if (resp[3] != req[3] || resp[4] != req[4] || resp[5] != req[5])
                throw new InvalidOperationException("写响应回显与请求不一致");
        }

        private static void AppendCrc(byte[] frame)
        {
            ushort crc = ComputeCrc(frame, 0, frame.Length - 2);
            frame[^2] = (byte)(crc & 0x00FF); // 低字节在前
            frame[^1] = (byte)(crc >> 8);
        }
        private async Task<byte[]> TransactAsync(byte[] request, byte slaveId, byte func, CancellationToken ct)
        {
            await _txLock.WaitAsync(ct);
            try
            {
                Exception? lastError = null;
                for (int attempt = 0; attempt <= RetryCount; attempt++)
                {
                    var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pending = new PendingRequest(slaveId, func, tcs);
                    lock (_rxLock) _rxBuffer.Clear();

                    await _stream.WriteAsync(request, 0, request.Length, ct); // ★ 发给 Socket
                    Console.WriteLine($"[TX] {BitConverter.ToString(request)}");

                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeoutMs, ct));
                    if (completed == tcs.Task) { _pending = null; return await tcs.Task; }
                    lastError = new TimeoutException($"超时，第{attempt + 1}次");
                    _pending = null;
                }
                throw lastError!;
            }
            finally { _txLock.Release(); }
        }

        /// <summary>
        /// 循环从缓冲区中抠完整帧：长度够 -> 裁帧 -> CRC 校验 -> 事务匹配
        /// </summary>
        private void TryExtractFrames()
        {
            while (_pending != null && _rxBuffer.Count >= 5) // RTU 最短帧 （异常帧）= 5 字节
            {
                // (a) 帧头对齐：找当前请求对应的从站地址，前面不属于本事务的字节直接丢弃
                int start = _rxBuffer.IndexOf(_pending.SlaveId);
                if (start < 0) { _rxBuffer.Clear(); return; }
                if (start > 0) _rxBuffer.RemoveRange(0, start);

                byte func = _rxBuffer[1];
                int frameLen;

                // (b) 先按"异常帧"判断：功能码最高位置1，固定5字节
                if ((func & 0x80) != 0)
                {
                    if (func != (byte)(_pending.Func | 0x80))
                    {
                        _rxBuffer.RemoveAt(0);
                        continue;
                    } // 不是等我们的，丢
                    frameLen = 5;
                }
                else if (func == _pending.Func)
                {
                    // (c) 正常帧：按功能码的帧长规则计算
                    frameLen = func switch
                    {
                        0x03 => 3 + _rxBuffer[2] + 2,// 地址+功能+字节数+数据+CRC；[2]已知才够，故需 Count>=3
                        0x06 => 8, // 写单寄存器 = 请求回显
                        _ => 5
                    };
                }
                else { _rxBuffer.RemoveAt(0); continue; } // 功能码不匹配，丢

                if (_rxBuffer.Count < frameLen) return; // ★ 拆包：数据还不够，等下一次回调

                // (d) 截取完整帧并 CRC 校验
                var frame = _rxBuffer.GetRange(0, frameLen).ToArray();
                _rxBuffer.RemoveRange(0, frameLen);
                ushort recvCrc = (ushort)(frame[^2] | (frame[^1] << 8));
                if (recvCrc != ComputeCrc(frame, 0, frame.Length - 2))
                {
                    Console.WriteLine("[ERR] CRC 校验失败，丢弃该帧");
                    continue; // ★ 粘包/干扰导致错位，继续尝试抠下一帧
                }

                Console.WriteLine($"[RX] {BitConverter.ToString(frame)}");
                if ((frame[1] & 0x80) != 0)
                    _pending.Tcs.TrySetException(new Exception($"从站异常：异常码0x{frame[2]:X2}"));
                else
                    _pending.Tcs.TrySetResult(frame); // ★ 事务匹配成功
            }
        }


        public void Dispose()
        {
            _stream?.Dispose(); _tcp?.Close();
        }

        private sealed class PendingRequest
        {
            public byte SlaveId; public byte Func; public TaskCompletionSource<byte[]> Tcs;
            public PendingRequest(byte s, byte f, TaskCompletionSource<byte[]> t) { SlaveId = s; Func = f; Tcs = t; }
        }
    }
}
