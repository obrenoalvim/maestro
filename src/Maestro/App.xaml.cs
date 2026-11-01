using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Maestro.Fps;
using Maestro.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro;

public partial class App : Application
{
    private const int FpsHotkey = 1;
    private const uint KeyF = 0x46;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);

    private readonly DispatcherTimer signals = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private Tray? tray;
    private SettingsWindow? window;
    private HwndSource? hotkeys;
    private IDisposable? languageWatch;

    public IServiceProvider Services { get; init; } = null!;
    public WaitHandle Quit { get; init; } = null!;
    public WaitHandle Greet { get; init; } = null!;
    public bool FirstRun { get; init; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = Services.GetRequiredService<Settings>();
        Loc.Instance.Set(settings.Current.Language);
        languageWatch = settings.OnChange(options => Dispatcher.BeginInvoke(() => Loc.Instance.Set(options.Language)));

        var fps = Services.GetRequiredService<FpsSwitch>();
        tray = new Tray(Services.GetRequiredService<Conductor>(), Services.GetRequiredService<ConfigGuard>(), fps,
            Services.GetRequiredService<UpdateNotifier>(), OpenSettings, Shutdown);
        RegisterHotkey(fps);

        signals.Tick += (_, _) =>
        {
            if (Quit.WaitOne(0)) Shutdown();
            else if (Greet.WaitOne(0)) OpenSettings();
        };
        signals.Start();
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = UpdateInterval;
            if (await Services.GetRequiredService<UpdateNotifier>().CheckAsync()) tray.AnnounceUpdate();
        };
        updateTimer.Start();

        _ = fps.ResumeAsync();
        if (FirstRun) OpenSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        signals.Stop();
        updateTimer.Stop();
        languageWatch?.Dispose();
        if (hotkeys is not null)
        {
            Native.UnregisterHotKey(hotkeys.Handle, FpsHotkey);
            hotkeys.Dispose();
        }
        Services.GetRequiredService<FpsSwitch>().Close();
        tray?.Dispose();
        base.OnExit(e);
    }

    private void OpenSettings()
    {
        if (window is null)
        {
            window = new SettingsWindow(ActivatorUtilities.CreateInstance<SettingsViewModel>(Services));
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private void RegisterHotkey(FpsSwitch fps)
    {
        hotkeys = new HwndSource(new HwndSourceParameters("MaestroHotkeys") { ParentWindow = new nint(-3) });
        hotkeys.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message != Native.HotkeyMessage || wParam != FpsHotkey) return 0;
            _ = fps.ToggleAsync();
            handled = true;
            return 0;
        });
        Native.RegisterHotKey(hotkeys.Handle, FpsHotkey, Native.ModControl | Native.ModAlt | Native.ModNoRepeat, KeyF);
    }
}
