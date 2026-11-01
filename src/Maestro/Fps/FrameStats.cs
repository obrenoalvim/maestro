namespace Maestro.Fps;

public sealed record FrameStats(int Frames, double AverageFps, double OnePercentLowFps, double P99FrameTimeMs)
{
    private const int MinimumFrames = 10;
    private const double SlowestPlausibleFrameMs = 1000;

    public static FrameStats? From(IEnumerable<double> frameTimesMs)
    {
        var times = frameTimesMs.Where(ms => ms is > 0 and < SlowestPlausibleFrameMs).Order().ToArray();
        if (times.Length < MinimumFrames) return null;
        var p99 = times[(int)Math.Ceiling(times.Length * 0.99) - 1];
        var slowest = times[^Math.Max(1, times.Length / 100)..];
        return new FrameStats(times.Length, times.Length * 1000 / times.Sum(), slowest.Length * 1000 / slowest.Sum(), p99);
    }
}

public sealed record Frame(int Pid, double MsBetweenPresents);

public sealed class PresentMonCsv
{
    private int pidColumn = -1;
    private int frameTimeColumn = -1;

    public Frame? Read(string line)
    {
        var cells = line.Split(',');
        if (cells.Length > 0 && cells[0] == "Application")
        {
            pidColumn = Array.IndexOf(cells, "ProcessID");
            frameTimeColumn = Array.IndexOf(cells, "MsBetweenPresents");
            return null;
        }
        if (pidColumn < 0 || frameTimeColumn < 0 || cells.Length <= Math.Max(pidColumn, frameTimeColumn)) return null;
        if (!int.TryParse(cells[pidColumn], out var pid)) return null;
        if (!double.TryParse(cells[frameTimeColumn], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms)) return null;
        return new Frame(pid, ms);
    }
}
