using ModbusRtuMasterDemo;

//#region Rtu 串口报文 版
//{
//    // Rtu 串口报文 版
//    using var master = new ModbusRtuMaster("COM5", 9600); // COM5 对应方案B；方案A 用 TCP 转串口工具映射
//    master.Open();
//}
//#endregion

//#region Rtu 串口报文 但是通过 TCP协议转发
//{
//    //Rtu 串口报文 但是通过 TCP协议转发
//    using var master = new ModbusRtuOverTcpMaster("127.0.0.1", 502);

//    // 1. 正常读取
//    ushort[] values = await master.ReadHoldingRegistersAsync(1, 0, 3);
//    Console.WriteLine($"读到 {values.Length} 个寄存器：{string.Join("，", values)}");

//    // 2. 写入
//    await master.WriteSingleRegisterAsync(1, 0, 1234);
//    Console.WriteLine("写入成功");

//    // 3. 故意读不存在的站号 → 触发超时 + 重试
//    try { await master.ReadHoldingRegistersAsync(99, 0, 1); }
//    catch (TimeoutException ex) { Console.WriteLine($"预期中的超时: {ex.Message}"); }

//}
//#endregion

#region Modbus TCP 协议实现 （当前主流）
var master = new ModbusTcpMaster();
master.Connect("127.0.0.1", 503); // HSL 虚拟服务器保持默认模式（不勾 RTU 报文）
var v = await master.ReadHoldingRegistersAsync(1, 0, 3);
Console.WriteLine(string.Join(", ", v));   // 100, 200, 300
await master.WriteSingleRegisterAsync(1, 3, 8763);
#endregion