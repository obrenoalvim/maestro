using Maestro.Fps;

namespace Maestro.Views;

public sealed class FpsSwitch(PresentMon presentMon, FrameMeter meter, Benchmark benchmark, Settings settings)
{
    private FpsOverlay? overlay;

    public bool On { get; private set; }

    public string? Status => presentMon.Busy ? Loc.T("fpsDownloading") : presentMon.Error;

    public event Action? Changed;

    public async Task SetAsync(bool on)
    {
        if (on == On) return;
        On = on;
        settings.Update(options => options.ShowFps = on);
        Changed?.Invoke();
        if (!on)
        {
            overlay?.Hide();
            if (!benchmark.Running) presentMon.Stop();
            return;
        }
        overlay ??= new FpsOverlay(meter, benchmark);
        overlay.Show();
        await presentMon.StartAsync(Native.ForegroundPid(), CancellationToken.None);
        Changed?.Invoke();
    }

    public Task ToggleAsync() => SetAsync(!On);

    public Task ResumeAsync() =>
        settings.Current.ShowFps && presentMon.Permission == Permission.Granted ? SetAsync(true) : Task.CompletedTask;

    public void ShowDuringBenchmark()
    {
        overlay ??= new FpsOverlay(meter, benchmark);
        overlay.Show();
    }

    public void AfterBenchmark()
    {
        if (On) return;
        overlay?.Hide();
        presentMon.Stop();
    }

    public void Close()
    {
        overlay?.Close();
        presentMon.Stop();
    }
}

public sealed class UpdateNotifier(Settings settings)
{
    public Version? Available { get; private set; }

    public async Task<bool> CheckAsync()
    {
        if (!settings.Current.CheckUpdates) return false;
        var latest = await Updates.LatestAsync();
        var isNew = latest is not null && latest > Updates.Current && latest != Available;
        if (latest is not null && latest > Updates.Current) Available = latest;
        return isNew;
    }
}
