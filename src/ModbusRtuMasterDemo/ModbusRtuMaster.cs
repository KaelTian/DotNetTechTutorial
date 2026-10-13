using System.IO.Ports;

namespace ModbusRtuMasterDemo
{
    /// <summary>
    /// 手写 Modbus RTU 主站：组帧 / CRC16 / DataReceived 缓冲拆包 / 事务匹配 / 超时重试
    /// 用法: var master = new ModbusRtuMaster("COM5"); master.Open();
    ///        ushort[] v = await master.ReadHoldingRegistersAsync(1, 0, 3);
    /// </summary>
    public class ModbusRtuMaster : IDisposable
    {
        private readonly SerialPort _serial;
        private readonly List<byte> _rxBuffer = new List<byte>(); // 接收缓冲区
        private readonly object _rxLock = new object(); // 接收缓冲区锁
        private readonly SemaphoreSlim _txLock = new SemaphoreSlim(1, 1); // 半双工：同一时刻只有一个事务

        private PendingRequest? _pending;

        public int TimeoutMs { get; set; } = 1000; // 默认 1 秒超时
        public int RetryCount { get; set; } = 2; // 默认重试 2 次

        public ModbusRtuMaster(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {
            _serial = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            _serial.DataReceived += OnDataReceived;
        }

        public void Open()
        {
            if (!_serial.IsOpen) _serial.Open();
        }
        /// <summary>
        /// 1. CRC-16/MODBUS（初值0xFFFF，多项式0xA001，低字节在前）
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort ComputeCrc(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[offset + 1];
                for (int b = 0; b < 8; b++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        private static void AppendCrc(byte[] frame)
        {
            ushort crc = ComputeCrc(frame, 0, frame.Length - 2);
            frame[^2] = (byte)(crc & 0x00FF); // 低字节在前
            frame[^1] = (byte)(crc >> 8);
        }
        /// <summary>
        /// 2. 读保持寄存器 (0x03)
        /// </summary>
        /// <param name="slaveId"></param>
        /// <param name="start"></param>
        /// <param name="count"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveId, ushort start, ushort count, CancellationToken ct = default)
        {
            byte[] req = new byte[8];
            req[0] = slaveId;
            req[1] = 0x03;
            req[2] = (byte)(start >> 8);
            req[3] = (byte)start;
            req[4] = (byte)(count >> 8);
            req[5] = (byte)count;
            AppendCrc(req);

            return TransactAsync(req, slaveId, 0x03, ct).ContinueWith(t =>
            {
                byte[] resp = t.GetAwaiter().GetResult();
                int byteCount = resp[2];
                var values = new ushort[byteCount / 2];
                for (int i = 0; i < values.Length; i++)
                    values[i] = (ushort)((resp[3 + i * 2] << 8) | resp[4 + i * 2]); // 大端

                return values;
            }, ct);
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

        private async Task<byte[]> TransactAsync(byte[] request, byte slaveId, byte func, CancellationToken ct)
        {
            await _txLock.WaitAsync(ct); // 半双工：串上同时只能有一个请求
            try
            {
                Exception? lastError = null;
                for (int attempt = 0; attempt <= RetryCount; attempt++)
                {
                    var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pending = new PendingRequest(slaveId, func, tcs);
                    _serial.DiscardInBuffer();
                    lock (_rxLock) _rxBuffer.Clear(); // 丢弃上一轮的残骸

                    _serial.Write(request, 0, request.Length);
                    Console.WriteLine($"[TX] {BitConverter.ToString(request)}  (第{attempt + 1}次)");

                    var timeoutTask = Task.Delay(TimeoutMs, ct);
                    var completed = await Task.WhenAny(tcs.Task, timeoutTask);
                    if (completed == tcs.Task)
                    {
                        _pending = null;
                        return await tcs.Task; // 收到匹配的响应
                    }
                    lastError = new TimeoutException($"从站 {slaveId} 功能码 0x{func:X2} 超时({TimeoutMs}ms)，第{attempt + 1}次");
                    Console.WriteLine($"[WARN] {lastError.Message}");
                    _pending = null;
                }
                throw lastError!;
            }
            finally
            {
                _txLock.Release();
            }
        }
        /// <summary>
        /// 5. DataReceived：可能多次回调 → 粘包/拆包处理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int n = _serial.BytesToRead;
            if (n <= 0) return;
            var chunk = new byte[n];
            _serial.Read(chunk, 0, n);
            lock (_rxLock)
            {
                _rxBuffer.AddRange(chunk); // 先追加到缓冲区，不假设一次就是一帧
                TryExtractFrames();
            }
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

        private sealed class PendingRequest
        {
            public byte SlaveId;
            public byte Func;
            public TaskCompletionSource<byte[]> Tcs;
            public PendingRequest(byte s, byte f, TaskCompletionSource<byte[]> t)
            {
                SlaveId = s;
                Func = f;
                Tcs = t;
            }
        }

        public void Dispose()
        {
            _serial.DataReceived -= OnDataReceived;
            _serial.Close();
            _serial.Dispose();
        }
    }
}
