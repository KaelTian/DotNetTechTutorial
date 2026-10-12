using BenchmarkDotNet.Running;
using BoxAndUnboxBench;

var summary = BenchmarkRunner.Run<ListVsArrayListBenchmark>();