using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Maestro;

public enum Throttle
{
    Default,
    Efficient,
    Fast,
}

public sealed class ProcessTuner(AppPaths paths, ILogger<ProcessTuner> logger)
{
    private const int LowIoPriority = 1;

    private sealed record Original(string Name, ProcessPriorityClass Priority, int IoPriority);

    private sealed record JournalEntry(int Pid, long CreateTime, string Name, ProcessPriorityClass Priority, int IoPriority);

    private readonly Dictionary<ProcessKey, Original> touched = [];
    private readonly Lock gate = new();

    public bool HasChanges
    {
        get { lock (gate) return touched.Count > 0; }
    }

    private static readonly ProcessPriorityClass[] PriorityOrder =
    [
        ProcessPriorityClass.Idle, ProcessPriorityClass.BelowNormal, ProcessPriorityClass.Normal,
        ProcessPriorityClass.AboveNormal, ProcessPriorityClass.High, ProcessPriorityClass.RealTime,
    ];

    public bool Restrain(ProcessKey key, string name) =>
        Change(key, name, _ => ProcessPriorityClass.BelowNormal, Throttle.Efficient, LowIoPriority);

    public bool Boost(ProcessKey key, string name, ProcessPriorityClass priority) =>
        Change(key, name, current => Array.IndexOf(PriorityOrder, current) > Array.IndexOf(PriorityOrder, priority) ? current : priority, Throttle.Fast, null);

    public void Restore(ProcessKey key)
    {
        Original? original;
        lock (gate)
        {
            if (!touched.Remove(key, out original)) return;
        }
        PutBack(key, original);
        SaveJournal();
    }

    public IReadOnlyList<string> RecoverFromJournal()
    {
        if (!File.Exists(paths.Journal)) return [];
        JournalEntry[] entries;
        try
        {
            entries = JsonSerializer.Deserialize<JournalEntry[]>(File.ReadAllText(paths.Journal)) ?? [];
        }
        catch (JsonException error)
        {
            logger.LogWarning("Diário de alterações ilegível, descartado: {Message}", error.Message);
            entries = [];
        }
        var recovered = entries
            .Where(entry => PutBack(new ProcessKey(entry.Pid, entry.CreateTime), new Original(entry.Name, entry.Priority, entry.IoPriority)))
            .Select(entry => entry.Name)
            .ToList();
        File.Delete(paths.Journal);
        return recovered;
    }

    public void RestoreAll()
    {
        List<ProcessKey> keys;
        lock (gate) keys = [.. touched.Keys];
        foreach (var key in keys) Restore(key);
    }

    private bool Change(ProcessKey key, string name, Func<ProcessPriorityClass, ProcessPriorityClass> priority, Throttle throttle, int? ioPriority)
    {
        using var handle = Open(key);
        if (handle is null) return false;
        Original original;
        lock (gate)
        {
            if (!touched.TryGetValue(key, out original!))
            {
                var current = (ProcessPriorityClass)Native.GetPriorityClass(handle);
                if (current == 0) return false;
                Native.NtQueryInformationProcess(handle, Native.ProcessIoPriority, out var currentIo, sizeof(int), out _);
                touched[key] = original = new Original(name, current, currentIo);
            }
        }
        SaveJournal();
        var ok = Native.SetPriorityClass(handle, (uint)priority(original.Priority)) && SetThrottle(handle, throttle);
        if (ioPriority is int io) Native.NtSetInformationProcess(handle, Native.ProcessIoPriority, ref io, sizeof(int));
        if (ok) return true;
        logger.LogDebug("Windows recusou mudar {Name} ({Pid}): erro {Error}", name, key.Pid, Marshal.GetLastPInvokeError());
        Restore(key);
        return false;
    }

    private static bool PutBack(ProcessKey key, Original original)
    {
        using var handle = Open(key);
        if (handle is null) return false;
        Native.SetPriorityClass(handle, (uint)original.Priority);
        SetThrottle(handle, Throttle.Default);
        var io = original.IoPriority;
        Native.NtSetInformationProcess(handle, Native.ProcessIoPriority, ref io, sizeof(int));
        return true;
    }

    private void SaveJournal()
    {
        JournalEntry[] entries;
        lock (gate) entries = [.. touched.Select(pair => new JournalEntry(pair.Key.Pid, pair.Key.CreateTime, pair.Value.Name, pair.Value.Priority, pair.Value.IoPriority))];
        if (entries.Length == 0)
        {
            File.Delete(paths.Journal);
            return;
        }
        var temporary = paths.Journal + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
        File.Move(temporary, paths.Journal, overwrite: true);
    }

    private static SafeProcessHandle? Open(ProcessKey key)
    {
        var handle = Native.OpenProcess(Native.ProcessSetInformation | Native.ProcessQueryLimitedInformation, false, key.Pid);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        if (Native.GetProcessTimes(handle, out var created, out _, out _, out _) && created == key.CreateTime) return handle;
        handle.Dispose();
        return null;
    }

    private static bool SetThrottle(SafeProcessHandle handle, Throttle throttle)
    {
        var state = new Native.PowerThrottlingState
        {
            Version = 1,
            ControlMask = throttle == Throttle.Default ? 0u : 1u,
            StateMask = throttle == Throttle.Efficient ? 1u : 0u,
        };
        return Native.SetProcessInformation(handle, Native.ProcessPowerThrottling, ref state, Marshal.SizeOf<Native.PowerThrottlingState>());
    }
}
