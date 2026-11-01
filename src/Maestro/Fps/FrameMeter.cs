namespace Maestro.Fps;

public sealed record LiveFps(double Fps, double OnePercentLowFps);

public sealed class FrameMeter
{
    private const long KeepMs = 5000;
    private const long StaleMs = 2000;
    private const double SecondMs = 1000;

    private readonly Lock gate = new();
    private readonly Dictionary<int, Queue<(long At, double Ms)>> frames = [];
    private readonly List<Recording> recordings = [];

    public void Add(Frame frame)
    {
        var now = Environment.TickCount64;
        lock (gate)
        {
            if (!frames.TryGetValue(frame.Pid, out var queue)) frames[frame.Pid] = queue = new Queue<(long, double)>();
            queue.Enqueue((now, frame.MsBetweenPresents));
            while (queue.Count > 0 && now - queue.Peek().At > KeepMs) queue.Dequeue();
            foreach (var recording in recordings.Where(recording => recording.Pid == frame.Pid)) recording.Add(frame.MsBetweenPresents);
        }
    }

    public LiveFps? Live(int pid)
    {
        double[] recent;
        lock (gate)
        {
            if (!frames.TryGetValue(pid, out var queue) || queue.Count == 0) return null;
            if (Environment.TickCount64 - queue.Last().At > StaleMs) return null;
            recent = [.. queue.Select(frame => frame.Ms)];
        }
        var lastSecond = recent.Reverse().Aggregate((Sum: 0.0, Count: 0), (total, ms) => total.Sum >= SecondMs ? total : (total.Sum + ms, total.Count + 1));
        var stats = FrameStats.From(recent);
        if (lastSecond.Sum <= 0) return null;
        return new LiveFps(lastSecond.Count * SecondMs / lastSecond.Sum, stats?.OnePercentLowFps ?? 0);
    }

    public Recording Record(int pid)
    {
        var recording = new Recording(pid, this);
        lock (gate) recordings.Add(recording);
        return recording;
    }

    internal void Finish(Recording recording)
    {
        lock (gate) recordings.Remove(recording);
    }
}

public sealed class Recording(int pid, FrameMeter meter) : IDisposable
{
    private readonly List<double> times = [];

    public int Pid => pid;

    internal void Add(double ms) => times.Add(ms);

    public FrameStats? Stats()
    {
        meter.Finish(this);
        return FrameStats.From(times);
    }

    public void Dispose() => meter.Finish(this);
}
