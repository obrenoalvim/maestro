using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Maestro.Fps;

namespace Maestro.Views;

public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly char[] LineBreaks = ['\r', '\n'];

    private readonly Conductor conductor;
    private readonly Settings settings;
    private readonly PresentMon presentMon;
    private readonly Benchmark benchmark;
    private readonly FpsSwitch fps;
    private readonly QBittorrent qbittorrent;
    private readonly UpdateNotifier updates;
    private readonly AppPaths paths;
    private CancellationTokenSource? comparing;
    private Comparison? last;
    private string? compareMessage;
    private string language;

    public SettingsViewModel(Conductor conductor, Settings settings, PresentMon presentMon, Benchmark benchmark, FpsSwitch fps,
        QBittorrent qbittorrent, UpdateNotifier updates, AppPaths paths)
    {
        this.conductor = conductor;
        this.settings = settings;
        this.presentMon = presentMon;
        this.benchmark = benchmark;
        this.fps = fps;
        this.qbittorrent = qbittorrent;
        this.updates = updates;
        this.paths = paths;
        last = benchmark.History().FirstOrDefault();
        language = settings.Current.Language;
        fps.Changed += () => OnPropertyChanged(nameof(ShowFps));
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Refresh();
        RebuildPrevious();
    }

    public ObservableCollection<string> Recent { get; } = [];

    public ObservableCollection<string> Previous { get; } = [];

    public int[] Durations { get; } = [30, 60, 120];

    public string Headline => conductor.Status.Headline;

    public bool HasNoHistory => Recent.Count == 0;

    public bool Running
    {
        get => !conductor.Paused;
        set
        {
            conductor.Paused = !value;
            OnPropertyChanged();
        }
    }

    public bool ShowFps
    {
        get => fps.On;
        set => _ = fps.SetAsync(value);
    }

    public string? FpsStatus => fps.Status;

    public Permission Permission => presentMon.Permission;

    public string PermissionText => Permission switch
    {
        Permission.Granted => Loc.T("permReady"),
        Permission.Pending => Loc.T("permPending"),
        _ => Loc.T("permMissing"),
    };

    public bool CanGrant => Permission == Permission.Missing;

    [ObservableProperty]
    private int duration = 60;

    [ObservableProperty]
    private bool simulateLoad;

    public bool Comparing => comparing is not null;

    public string? Phase => benchmark.Phase ?? compareMessage;

    public bool HasResult => last is not null;

    public string WithoutAverage => last is null ? "" : Number(last.Without.AverageFps);
    public string WithoutLow => last is null ? "" : Number(last.Without.OnePercentLowFps);
    public string WithoutWorst => last is null ? "" : Number(last.Without.P99FrameTimeMs) + " ms";
    public string WithAverage => last is null ? "" : Number(last.With.AverageFps);
    public string WithLow => last is null ? "" : Number(last.With.OnePercentLowFps);
    public string WithWorst => last is null ? "" : Number(last.With.P99FrameTimeMs) + " ms";
    public string Delta => last is null ? "" : DeltaOf(last);

    public string ExtraGames
    {
        get => Lines(settings.Current.ExtraGames);
        set => settings.Update(options => options.ExtraGames = Names(value));
    }

    public string NeverRestrain
    {
        get => Lines(settings.Current.NeverRestrain);
        set => settings.Update(options => options.NeverRestrain = Names(value));
    }

    public string SlowDuringGames
    {
        get => Lines(settings.Current.SlowDuringGames);
        set => settings.Update(options => options.SlowDuringGames = Names(value));
    }

    public bool MedalManage
    {
        get => settings.Current.Medal.Manage;
        set => settings.Update(options => options.Medal.Manage = value);
    }

    public string MedalPath
    {
        get => settings.Current.Medal.Path;
        set => settings.Update(options => options.Medal.Path = value.Trim());
    }

    public string MedalCloseAfter
    {
        get => settings.Current.Medal.CloseAfterMinutes.ToString();
        set
        {
            if (int.TryParse(value, out var minutes) && minutes is > 0 and <= 120) settings.Update(options => options.Medal.CloseAfterMinutes = minutes);
            else OnPropertyChanged();
        }
    }

    public string QbitUrl
    {
        get => settings.Current.QBittorrentWebUi;
        set => settings.Update(options => options.QBittorrentWebUi = value.Trim());
    }

    [ObservableProperty]
    private string? qbitResult;

    public bool LanguageAuto
    {
        get => language is not (Loc.English or Loc.Portuguese);
        set { if (value) SetLanguage(Loc.Automatic); }
    }

    public bool LanguagePortuguese
    {
        get => language == Loc.Portuguese;
        set { if (value) SetLanguage(Loc.Portuguese); }
    }

    public bool LanguageEnglish
    {
        get => language == Loc.English;
        set { if (value) SetLanguage(Loc.English); }
    }

    public bool StartWithWindows
    {
        get => Autostart.IsEnabled;
        set
        {
            if (value) Autostart.Enable();
            else Autostart.Disable();
            OnPropertyChanged();
        }
    }

    public bool CheckUpdates
    {
        get => settings.Current.CheckUpdates;
        set => settings.Update(options => options.CheckUpdates = value);
    }

    public string VersionText => Loc.F("version", Updates.Current.ToString(3));

    public string? UpdateText => updates.Available is { } version ? Loc.F("updateButton", version.ToString(3)) : null;

    public void Refresh()
    {
        var status = conductor.Status;
        if (!Recent.SequenceEqual(status.Recent))
        {
            Recent.Clear();
            foreach (var line in status.Recent) Recent.Add(line);
            OnPropertyChanged(nameof(HasNoHistory));
        }
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Running));
        OnPropertyChanged(nameof(FpsStatus));
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(Permission));
        OnPropertyChanged(nameof(PermissionText));
        OnPropertyChanged(nameof(CanGrant));
        OnPropertyChanged(nameof(UpdateText));
    }

    [RelayCommand]
    private async Task GrantAsync()
    {
        var granted = await presentMon.GrantAsync();
        compareMessage = granted ? null : Loc.T("permFailed");
        Refresh();
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (comparing is not null) return;
        comparing = new CancellationTokenSource();
        compareMessage = null;
        OnPropertyChanged(nameof(Comparing));
        fps.ShowDuringBenchmark();
        try
        {
            var (result, error) = await benchmark.RunAsync(Duration, SimulateLoad, comparing.Token);
            compareMessage = error;
            if (result is not null) last = result;
        }
        finally
        {
            fps.AfterBenchmark();
            comparing.Dispose();
            comparing = null;
            OnPropertyChanged(string.Empty);
            Refresh();
            RebuildPrevious();
        }
    }

    [RelayCommand]
    private void CancelCompare() => comparing?.Cancel();

    [RelayCommand]
    private async Task TestQbitAsync()
    {
        var slow = await qbittorrent.ProbeAsync(CancellationToken.None);
        QbitResult = slow is null ? Loc.T("qbitFail") : Loc.F("qbitOk", Loc.T(slow.Value ? "on" : "off"));
    }

    [RelayCommand]
    private void OpenConfig() => Open(paths.Config);

    [RelayCommand]
    private void OpenLogs() => Open(paths.Logs);

    [RelayCommand]
    private void OpenRepository() => Open(Updates.Repository);

    [RelayCommand]
    private void DownloadUpdate() => Open(Updates.Page);

    private void SetLanguage(string choice)
    {
        language = choice;
        settings.Update(options => options.Language = choice);
        Loc.Instance.Set(choice);
        OnPropertyChanged(nameof(LanguageAuto));
        OnPropertyChanged(nameof(LanguagePortuguese));
        OnPropertyChanged(nameof(LanguageEnglish));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(string.Empty);
        Previous.Clear();
        RebuildPrevious();
    }

    private void RebuildPrevious()
    {
        var lines = benchmark.History().Select(comparison =>
            Loc.F("resultLine", comparison.At, comparison.Game, DeltaOf(comparison)) + (comparison.Load ? " · " + Loc.T("loadTag") : "")).ToList();
        if (Previous.SequenceEqual(lines)) return;
        Previous.Clear();
        foreach (var line in lines) Previous.Add(line);
    }

    private static string DeltaOf(Comparison comparison) =>
        Loc.F("resultDelta", comparison.AverageDelta, comparison.AveragePercent, comparison.LowDelta);

    private static string Number(double value) => value.ToString("0.0", Loc.Instance.Culture);

    private static string Lines(IEnumerable<string> names) => string.Join(Environment.NewLine, names);

    private static string[] Names(string text) =>
        [.. text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static void Open(string target)
    {
        if (!target.StartsWith("http", StringComparison.Ordinal)) Directory.CreateDirectory(Path.GetExtension(target).Length > 0 ? Path.GetDirectoryName(target)! : target);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }
}
