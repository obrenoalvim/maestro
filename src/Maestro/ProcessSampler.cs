using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Maestro;

public readonly record struct ProcessKey(int Pid, long CreateTime);

public sealed record ProcessSample(ProcessKey Key, string Name, int SessionId, long CpuTime, long IoBytes);

public sealed record ProcessUsage(ProcessKey Key, string Name, int SessionId, double CpuPercent, double IoBytesPerSecond);

public static class ProcessSampler
{
    private const int CreateTimeOffset = 0x20;
    private const int UserTimeOffset = 0x28;
    private const int KernelTimeOffset = 0x30;
    private const int ImageNameLengthOffset = 0x38;
    private const int ImageNameBufferOffset = 0x40;
    private const int ProcessIdOffset = 0x50;
    private const int SessionIdOffset = 0x64;
    private const int ReadTransferOffset = 0xE8;
    private const int WriteTransferOffset = 0xF0;

    public static IReadOnlyList<ProcessSample> Take()
    {
        var size = 1 << 20;
        while (true)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = Native.NtQuerySystemInformation(Native.SystemProcessInformation, buffer, size, out var needed);
                if (status == Native.StatusInfoLengthMismatch)
                {
                    size = Math.Max(size * 2, needed + (1 << 16));
                    continue;
                }
                if (status != 0) throw new Win32Exception($"NtQuerySystemInformation devolveu 0x{status:X8}");
                return Parse(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public static IReadOnlyList<ProcessUsage> Usage(IReadOnlyList<ProcessSample> before, IReadOnlyList<ProcessSample> after, TimeSpan elapsed, int processorCount)
    {
        var previous = before.ToDictionary(sample => sample.Key);
        var cpuCapacity = elapsed.Ticks * (double)processorCount;
        var usage = new List<ProcessUsage>(after.Count);
        foreach (var sample in after)
        {
            if (!previous.TryGetValue(sample.Key, out var earlier)) continue;
            usage.Add(new ProcessUsage(
                sample.Key,
                sample.Name,
                sample.SessionId,
                (sample.CpuTime - earlier.CpuTime) * 100 / cpuCapacity,
                (sample.IoBytes - earlier.IoBytes) / elapsed.TotalSeconds));
        }
        return usage;
    }

    private static List<ProcessSample> Parse(nint buffer)
    {
        var samples = new List<ProcessSample>(512);
        var entry = buffer;
        while (true)
        {
            var pid = (int)Marshal.ReadIntPtr(entry, ProcessIdOffset);
            samples.Add(new ProcessSample(
                new ProcessKey(pid, Marshal.ReadInt64(entry, CreateTimeOffset)),
                ReadName(entry, pid),
                Marshal.ReadInt32(entry, SessionIdOffset),
                Marshal.ReadInt64(entry, UserTimeOffset) + Marshal.ReadInt64(entry, KernelTimeOffset),
                Marshal.ReadInt64(entry, ReadTransferOffset) + Marshal.ReadInt64(entry, WriteTransferOffset)));
            var next = Marshal.ReadInt32(entry);
            if (next == 0) return samples;
            entry += next;
        }
    }

    private static string ReadName(nint entry, int pid)
    {
        var length = (ushort)Marshal.ReadInt16(entry, ImageNameLengthOffset);
        var text = Marshal.ReadIntPtr(entry, ImageNameBufferOffset);
        if (pid == 0 || text == 0) return "Idle";
        return Path.GetFileNameWithoutExtension(Marshal.PtrToStringUni(text, length / 2));
    }
}
