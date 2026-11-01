using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Maestro.Views;

public partial class SettingsWindow : Window
{
    private const double CompactWidth = 760;
    private static readonly GridLength SideNav = new(200);

    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public SettingsWindow(SettingsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        refresh.Tick += (_, _) => model.Refresh();
        refresh.Start();
        Closed += (_, _) => refresh.Stop();
        SourceInitialized += (_, _) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        SizeChanged += (_, _) => Arrange(ActualWidth < CompactWidth);
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close();
        };
    }

    public void ShowPage(string page)
    {
        foreach (var item in Nav.Children.OfType<RadioButton>()) item.IsChecked = (string)item.Tag == page;
    }

    private void Arrange(bool compact)
    {
        Nav.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
        Grid.SetRow(Nav, compact ? 0 : 1);
        Grid.SetColumnSpan(Nav, compact ? 2 : 1);
        Grid.SetColumn(Pages, compact ? 0 : 1);
        Grid.SetColumnSpan(Pages, compact ? 2 : 1);
        NavColumn.Width = compact ? new GridLength(0) : SideNav;
        NavRow.Height = compact ? GridLength.Auto : new GridLength(0);
        Nav.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 14, 0);
    }

    private void OnNav(object sender, RoutedEventArgs args)
    {
        if (Pages is null) return;
        var page = (string)((RadioButton)sender).Tag;
        foreach (var panel in new FrameworkElement[] { HomePage, FpsPage, GamesPage, AppsPage, GeneralPage })
            panel.Visibility = panel.Name == page ? Visibility.Visible : Visibility.Collapsed;
        Pages.ScrollToTop();
    }
}
