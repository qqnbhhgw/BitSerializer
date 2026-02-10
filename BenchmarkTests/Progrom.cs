using BenchmarkDotNet.Running;

namespace BitSerializer.BenchmarkTests;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<SerializeBenchmark>();
    }
}
