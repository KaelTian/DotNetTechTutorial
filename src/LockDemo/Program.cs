
using LockDemo;

Console.WriteLine("========== Start LockDemo ==========");
ILockRunner lockRunner=new NormalRunner();
lockRunner.Run();
Console.WriteLine("Press Enter to End...");
Console.ReadLine();