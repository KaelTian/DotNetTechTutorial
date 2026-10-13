using System.IO.Ports;
using System.Net.Sockets;

internal class Program
{
    private static void Main(string[] args)
    {
        var com = new SerialPort("COM5", 9600, Parity.None, 8, StopBits.One);
        com.Open();
        var tcp = new TcpClient();
        tcp.Connect("127.0.0.1", 502);
        var net = tcp.GetStream();
        Console.WriteLine("桥已建立：COM5 <-> 127.0.0.1:502,Ctrl+C 退出");
        Task.Run(() => Forward(com.BaseStream, net));
        Forward(net, com.BaseStream);



        void Forward(Stream src, Stream dst)
        {
            var buf = new byte[4096];
            while (true)
            {
                int n = src.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                dst.Write(buf, 0, n);
                Console.WriteLine($"转发 {n} 字节");
            }
        }
    }
}