using BenchmarkDotNet.Attributes;
using System.Collections;

namespace BoxAndUnboxBench
{
    [MemoryDiagnoser]
    public class ListVsArrayListBenchmark
    {
        private const int N = 100000;

        // 基准测试 1: 泛型 List<int> 的添加与读取
        [Benchmark]
        public int ListInt_Add_Read()
        {
            var list = new List<int>();
            for (int i = 0; i < N; i++)
            {
                list.Add(i);
            }
            int sum = 0;
            for (int i = 0; i < N; i++)
            {
                sum += list[i];
            }
            return sum;
        }

        // 基准测试 2: 非泛型 ArrayList 的添加与读取
        [Benchmark]
        public int ArrayList_Add_Read()
        {
            var arrayList = new System.Collections.ArrayList();
            for (int i = 0; i < N; i++)
            {
                arrayList.Add(i); // 装箱
            }
            int sum = 0;
            for (int i = 0; i < N; i++)
            {
                sum += (int)arrayList[i]!; // 拆箱
            }
            return sum;
        }

        // ---- string 相关 ----
        [Benchmark]
        public int ListString_Add_Read()
        {
            var list = new List<string>(N);
            for (int i = 0; i < N; i++)
                list.Add(i.ToString());   // 注意：ToString 本身有开销
            int len = 0;
            for (int i = 0; i < N; i++)
                len += list[i].Length;
            return len;
        }

        [Benchmark]
        public int ArrayListString_Add_Read()
        {
            var arrayList = new ArrayList(N);
            for (int i = 0; i < N; i++)
                arrayList.Add(i.ToString());
            int len = 0;
            for (int i = 0; i < N; i++)
                len += ((string)arrayList[i]!).Length;
            return len;
        }
    }
}
