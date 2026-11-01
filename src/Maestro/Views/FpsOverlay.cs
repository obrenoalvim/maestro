using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Maestro.Fps;
using Forms = System.Windows.Forms;

namespace Maestro.Views;

internal sealed class FpsOverlay : Window
{
    private const int EdgePixels = 18;

    private readonly FrameMeter meter;
    private readonly Benchmark benchmark;
    private readonly TextBlock fps = new() { FontSize = 26, FontWeight = FontWeights.Bold };
    private readonly TextBlock unit = new() { Text = " FPS", FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
    private readonly TextBlock detail = new() { FontSize = 11 };
    private readonly TextBlock phase = new() { FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private nint handle;

    public FpsOverlay(FrameMeter meter, Benchmark benchmark)
    {
        this.meter = meter;
        this.benchmark = benchmark;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        var ink = (Brush)Application.Current.FindResource("Ink");
        var muted = (Brush)Application.Current.FindResource("Muted");
        var mono = (FontFamily)Application.Current.FindResource("Mono");
        foreach (var text in new[] { fps, unit, detail, phase }) text.FontFamily = mono;
        fps.Foreground = ink;
        unit.Foreground = muted;
        detail.Foreground = muted;
        phase.Foreground = (Brush)Application.Current.FindResource("Accent");
        var number = new StackPanel { Orientation = Orientation.Horizontal, Children = { fps, unit } };
        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 14, 9),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x0D, 0x0E, 0x13)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { number, detail, phase } },
        };
        SourceInitialized += (_, _) => MakeClickThrough();
        tick.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) tick.Start();
            else tick.Stop();
        };
    }

    private void MakeClickThrough()
    {
        handle = new WindowInteropHelper(this).Handle;
        var style = (long)Native.GetWindowLongPtr(handle, Native.ExStyleIndex);
        Native.SetWindowLongPtr(handle, Native.ExStyleIndex, (nint)(style | Native.ExTransparent | Native.ExToolWindow | Native.ExLayered | Native.ExNoActivate));
    }

    private void Refresh()
    {
        var window = Native.GetForegroundWindow();
        var live = meter.Live(Native.WindowPid(window));
        fps.Text = live is null ? "--" : live.Fps.ToString("0", Loc.Instance.Culture);
        detail.Text = live is null ? Loc.T("overlayNoFrames") : Loc.F("overlayLow", live.OnePercentLowFps);
        phase.Text = benchmark.Phase ?? "";
        phase.Visibility = benchmark.Running ? Visibility.Visible : Visibility.Collapsed;
        var screen = Forms.Screen.FromHandle(window).Bounds;
        if (handle != 0) Native.PinOnTop(handle, screen.Left + EdgePixels, screen.Top + EdgePixels);
    }
}
