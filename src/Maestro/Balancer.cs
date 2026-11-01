namespace Maestro;

public enum Tune
{
    Restrain,
    Release,
    Boost,
    Unboost,
}

public sealed record Decision(ProcessKey Key, string Name, Tune Tune, LocText Reason);

public sealed record BalanceContext(
    int SessionId,
    int ForegroundPid,
    IReadOnlySet<int> GamePids,
    IReadOnlySet<string> NeverRestrain,
    IReadOnlySet<string> SlowDuringGames)
{
    public bool Gaming { get; init; }
}

public sealed class Balancer
{
    public const double DesktopCpuPercent = 15;
    public const double GamingCpuPercent = 8;
    public const double TransferBytesPerSecond = 20 * 1024 * 1024;
    private const int BusySamplesToRestrain = 3;
    private const int CalmSamplesToRelease = 5;
    private const int MovingSamplesToBoost = 2;
    private const int StillSamplesToUnboost = 5;
    private const double BytesPerMegabyte = 1024 * 1024;

    public static readonly IReadOnlySet<string> Untouchable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso",
        "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon", "conhost", "taskhostw", "RuntimeBroker", "dllhost", "audiodg",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost", "GameBarPresenceWriter",
        "vgc", "vgtray", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEDaisy", "EAAntiCheat.GameService",
        "FACEITService", "FACEITClient", "faceit",
    };

    private enum State
    {
        Normal,
        Restrained,
        Boosted,
    }

    private sealed class Tracker
    {
        public string Name = "";
        public State State;
        public int Busy;
        public int Calm;
        public int Moving;
        public int Still;
    }

    private readonly Dictionary<ProcessKey, Tracker> trackers = [];
    private readonly HashSet<ProcessKey> refused = [];

    public void Refuse(ProcessKey key)
    {
        refused.Add(key);
        trackers.Remove(key);
    }

    public void Reset() => trackers.Clear();

    public IReadOnlyList<Decision> ReleaseForeground(int pid) =>
        [.. trackers
            .Where(pair => pair.Key.Pid == pid && pair.Value.State == State.Restrained)
            .Select(pair => Move(pair.Value, pair.Key, Tune.Release, State.Normal, new LocText("whyFront")))];

    public IReadOnlyList<Decision> Next(IEnumerable<ProcessUsage> usages, BalanceContext context)
    {
        var decisions = new List<Decision>();
        var alive = new HashSet<ProcessKey>();
        foreach (var usage in usages)
        {
            alive.Add(usage.Key);
            var decision = Decide(usage, context);
            if (decision is not null) decisions.Add(decision);
        }
        trackers.Keys.Where(key => !alive.Contains(key)).ToList().ForEach(key => trackers.Remove(key));
        refused.RemoveWhere(key => !alive.Contains(key));
        return decisions;
    }

    private Decision? Decide(ProcessUsage usage, BalanceContext context)
    {
        if (!IsManageable(usage, context)) return LetGo(usage, new LocText("whyGame"));
        var tracker = trackers.TryGetValue(usage.Key, out var existing) ? existing : trackers[usage.Key] = new Tracker { Name = usage.Name };
        Count(tracker, usage, context);
        var foreground = usage.Key.Pid == context.ForegroundPid;
        var slowForGame = context.Gaming && context.SlowDuringGames.Contains(usage.Name);
        var restrainable = !context.NeverRestrain.Contains(usage.Name);
        return tracker.State switch
        {
            State.Boosted when context.Gaming => Move(tracker, usage.Key, Tune.Unboost, State.Normal, new LocText("whyGameOpened"), usage.Name),
            State.Boosted when tracker.Still >= StillSamplesToUnboost => Move(tracker, usage.Key, Tune.Unboost, State.Normal, new LocText("whyTransferDone"), usage.Name),
            State.Restrained when foreground => Move(tracker, usage.Key, Tune.Release, State.Normal, new LocText("whyFront"), usage.Name),
            State.Restrained when !slowForGame && tracker.Calm >= CalmSamplesToRelease => Move(tracker, usage.Key, Tune.Release, State.Normal, new LocText("whyCalm"), usage.Name),
            State.Normal when slowForGame && !foreground => Move(tracker, usage.Key, Tune.Restrain, State.Restrained, new LocText("whyGameRunning"), usage.Name),
            State.Normal when !context.Gaming && tracker.Moving >= MovingSamplesToBoost =>
                Move(tracker, usage.Key, Tune.Boost, State.Boosted, new LocText("whyTransfer", usage.IoBytesPerSecond / BytesPerMegabyte), usage.Name),
            State.Normal when restrainable && !foreground && tracker.Busy >= BusySamplesToRestrain =>
                Move(tracker, usage.Key, Tune.Restrain, State.Restrained, new LocText("whyCpu", usage.CpuPercent), usage.Name),
            _ => null,
        };
    }

    private bool IsManageable(ProcessUsage usage, BalanceContext context) =>
        usage.SessionId == context.SessionId
        && usage.Key.Pid != Environment.ProcessId
        && !Untouchable.Contains(usage.Name)
        && !context.GamePids.Contains(usage.Key.Pid)
        && !refused.Contains(usage.Key);

    private Decision? LetGo(ProcessUsage usage, LocText reason)
    {
        if (!trackers.Remove(usage.Key, out var tracker)) return null;
        return tracker.State switch
        {
            State.Restrained => new Decision(usage.Key, usage.Name, Tune.Release, reason),
            State.Boosted => new Decision(usage.Key, usage.Name, Tune.Unboost, reason),
            _ => null,
        };
    }

    private static void Count(Tracker tracker, ProcessUsage usage, BalanceContext context)
    {
        var limit = context.Gaming ? GamingCpuPercent : DesktopCpuPercent;
        tracker.Busy = usage.CpuPercent > limit ? tracker.Busy + 1 : 0;
        tracker.Calm = usage.CpuPercent < limit / 2 ? tracker.Calm + 1 : 0;
        tracker.Moving = usage.IoBytesPerSecond >= TransferBytesPerSecond ? tracker.Moving + 1 : 0;
        tracker.Still = usage.IoBytesPerSecond < TransferBytesPerSecond / 10 ? tracker.Still + 1 : 0;
    }

    private static Decision Move(Tracker tracker, ProcessKey key, Tune tune, State state, LocText reason, string? name = null)
    {
        tracker.State = state;
        tracker.Busy = tracker.Calm = tracker.Moving = tracker.Still = 0;
        return new Decision(key, name ?? tracker.Name, tune, reason);
    }
}
