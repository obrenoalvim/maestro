using System.Diagnostics;
using System.Text.Json;

namespace Maestro.Fps;

public sealed record Comparison(DateTime At, string Game, int Seconds, bool Load, FrameStats Without, FrameStats With)
{
    public double AverageDelta => With.AverageFps - Without.AverageFps;
    public double AveragePercent => Without.AverageFps > 0 ? AverageDelta * 100 / Without.AverageFps : 0;
    public double LowDelta => With.OnePercentLowFps - Without.OnePercentLowFps;
}

public sealed class Benchmark(PresentMon presentMon, FrameMeter meter, Conductor conductor, AppPaths paths)
{
    private const int CountdownSeconds = 5;
    private const int HistoryLimit = 10;
    private static readonly TimeSpan SettleWithout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SettleWith = TimeSpan.FromSeconds(8);

    private volatile string? phase;

    public string? Phase => phase;

    public bool Running => phase is not null;

    public IReadOnlyList<Comparison> History()
    {
        if (!File.Exists(paths.Comparisons)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Comparison>>(File.ReadAllText(paths.Comparisons)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<(Comparison? Result, string? Error)> RunAsync(int seconds, bool load, CancellationToken cancellation)
    {
        try
        {
            for (var left = CountdownSeconds; left > 0; left--)
            {
                phase = Loc.F("phaseCountdown", left);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
            }
            var target = Native.ForegroundPid();
            var game = NameOf(target);
            if (!await presentMon.StartAsync(target, cancellation)) return (null, presentMon.Error);

            var wasPaused = conductor.Paused;
            using var heavy = load ? BackgroundLoad.Start() : null;
            try
            {
                conductor.Paused = true;
                var without = await MeasureAsync(Loc.T("phaseWithout"), target, seconds, SettleWithout, cancellation);
                conductor.Paused = false;
                var with = await MeasureAsync(Loc.T("phaseWith"), target, seconds, SettleWith, cancellation);
                if (without is null || with is null) return (null, Loc.F("compareNoFrames", game));
                var result = new Comparison(DateTime.Now, game, seconds, load, without, with);
                Save(result);
                return (result, null);
            }
            finally
            {
                conductor.Paused = wasPaused;
            }
        }
        catch (OperationCanceledException)
        {
            return (null, Loc.T("compareCanceled"));
        }
        finally
        {
            phase = null;
        }
    }

    private async Task<FrameStats?> MeasureAsync(string label, int target, int seconds, TimeSpan settle, CancellationToken cancellation)
    {
        phase = Loc.F("phaseSettle", label);
        await Task.Delay(settle, cancellation);
        using var recording = meter.Record(target);
        for (var left = seconds; left > 0; left--)
        {
            phase = Loc.F("phaseMeasure", label, left);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
        }
        return recording.Stats();
    }

    private void Save(Comparison result)
    {
        var history = History().Prepend(result).Take(HistoryLimit).ToList();
        File.WriteAllText(paths.Comparisons, JsonSerializer.Serialize(history));
    }

    private static string NameOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return Loc.T("unknownGame");
        }
    }
}

public sealed class BackgroundLoad : IDisposable
{
    private const string Spin = "$n = [Math]::Max(1, [Environment]::ProcessorCount / 2); Add-Type -TypeDefinition 'public static class Spin { public static void Run(int n) { for (int i = 0; i < n; i++) new System.Threading.Thread(() => { while (true) { } }) { IsBackground = false }.Start(); } }'; [Spin]::Run($n); while ($true) { Start-Sleep 60 }";

    private readonly Process process;

    private BackgroundLoad(Process process) => this.process = process;

    public static BackgroundLoad Start() =>
        new(Process.Start(new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", Spin]) { CreateNoWindow = true, UseShellExecute = false })!);

    public void Dispose()
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        process.Dispose();
    }
}
