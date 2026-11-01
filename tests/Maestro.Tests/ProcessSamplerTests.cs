using System.Diagnostics;

namespace Maestro.Tests;

public sealed class ProcessSamplerTests
{
    [Fact]
    public void ReadsTheCurrentProcessLikeTheDotnetApiDoes()
    {
        using var current = Process.GetCurrentProcess();
        var spin = Stopwatch.StartNew();
        while (spin.ElapsedMilliseconds < 200) { }

        var sample = Assert.Single(ProcessSampler.Take(), sample => sample.Key.Pid == current.Id);

        Assert.Equal(current.ProcessName, sample.Name);
        Assert.Equal(current.SessionId, sample.SessionId);
        Assert.Equal(current.StartTime.ToFileTimeUtc(), sample.Key.CreateTime);
        Assert.InRange(sample.CpuTime, TimeSpan.FromMilliseconds(150).Ticks, current.TotalProcessorTime.Ticks + TimeSpan.FromSeconds(1).Ticks);
    }

    [Fact]
    public void CountsBytesTheProcessWrites()
    {
        const int Bytes = 8 * 1024 * 1024;
        var pid = Environment.ProcessId;
        var before = ProcessSampler.Take().Single(sample => sample.Key.Pid == pid).IoBytes;
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, new byte[Bytes]);
        }
        finally
        {
            File.Delete(file);
        }

        var after = ProcessSampler.Take().Single(sample => sample.Key.Pid == pid).IoBytes;

        Assert.True(after - before >= Bytes, $"contou {after - before} bytes");
    }

    [Fact]
    public void UsageIsCpuShareAndBytesPerSecondBetweenSamples()
    {
        var key = new ProcessKey(42, 1);
        var before = new[] { new ProcessSample(key, "app", 1, 0, 0) };
        var after = new[] { new ProcessSample(key, "app", 1, TimeSpan.FromSeconds(2).Ticks, 10_000_000) };

        var usage = Assert.Single(ProcessSampler.Usage(before, after, TimeSpan.FromSeconds(2), 4));

        Assert.Equal(25, usage.CpuPercent, 3);
        Assert.Equal(5_000_000, usage.IoBytesPerSecond, 3);
    }
}
