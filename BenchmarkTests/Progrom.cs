using BenchmarkDotNet.Running;

namespace BenchmarkTests;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<SerializeBenchmark>();
    }
}
