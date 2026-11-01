using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Maestro.Views;
using Forms = System.Windows.Forms;

namespace Maestro;

internal sealed class Tray : IDisposable
{
    private const int TooltipLimit = 127;

    private readonly Conductor conductor;
    private readonly ConfigGuard config;
    private readonly FpsSwitch fps;
    private readonly UpdateNotifier updates;
    private readonly Action openSettings;
    private readonly Action exit;
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon normalIcon = LoadIcon("maestro.ico");
    private readonly System.Drawing.Icon gamingIcon = LoadIcon("maestro-jogo.ico");
    private readonly System.Drawing.Icon pausedIcon = LoadIcon("maestro-pausado.ico");
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private (string Headline, bool Paused, bool Gaming)? shown;

    public Tray(Conductor conductor, ConfigGuard config, FpsSwitch fps, UpdateNotifier updates, Action openSettings, Action exit)
    {
        this.conductor = conductor;
        this.config = config;
        this.fps = fps;
        this.updates = updates;
        this.openSettings = openSettings;
        this.exit = exit;
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, args) =>
        {
            Fill(menu);
            args.Cancel = false;
        };
        icon = new Forms.NotifyIcon { Icon = normalIcon, Text = "Maestro", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) openSettings();
        };
        icon.BalloonTipClicked += (_, _) => Open(Updates.Page);
        refresh.Tick += (_, _) => Show(conductor.Status);
        refresh.Start();
    }

    public void AnnounceUpdate()
    {
        if (updates.Available is { } version) icon.ShowBalloonTip(8000, "Maestro", Loc.F("updateNotice", version.ToString(3)), Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        refresh.Stop();
        icon.Visible = false;
        icon.Dispose();
    }

    private void Show(Status status)
    {
        var current = (status.Headline, status.Paused, status.Gaming);
        if (current == shown) return;
        shown = current;
        icon.Icon = status.Paused ? pausedIcon : status.Gaming ? gamingIcon : normalIcon;
        icon.Text = status.Headline.Length > TooltipLimit ? status.Headline[..TooltipLimit] : status.Headline;
    }

    private void Fill(Forms.ContextMenuStrip menu)
    {
        var status = conductor.Status;
        menu.Items.Clear();
        menu.Items.Add(new Forms.ToolStripMenuItem(status.Headline) { Enabled = false });
        if (config.Fault is not null) menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("trayConfigFault"), null, (_, _) => openSettings()) { ToolTipText = config.Fault });
        if (updates.Available is { } version) menu.Items.Add(new Forms.ToolStripMenuItem(Loc.F("updateButton", version.ToString(3)), null, (_, _) => Open(Updates.Page)));
        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("trayOpen"), null, (_, _) => openSettings()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });

        var history = new Forms.ToolStripMenuItem(Loc.T("trayHistory"));
        if (status.Recent.Count == 0) history.DropDownItems.Add(new Forms.ToolStripMenuItem(Loc.T("trayNothing")) { Enabled = false });
        foreach (var line in status.Recent) history.DropDownItems.Add(new Forms.ToolStripMenuItem(line));
        menu.Items.Add(history);
        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("trayFps"), null, (_, _) => _ = fps.ToggleAsync()) { Checked = fps.On });
        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("trayPause"), null, (_, _) => conductor.Paused = !conductor.Paused) { Checked = conductor.Paused });
        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("trayStartup"), null, (_, _) => ToggleAutostart()) { Checked = Autostart.IsEnabled });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("trayExit"), null, (_, _) => exit()));
    }

    private static void ToggleAutostart()
    {
        if (Autostart.IsEnabled) Autostart.Disable();
        else Autostart.Enable();
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    private static System.Drawing.Icon LoadIcon(string name)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))
            ?? throw new InvalidOperationException($"Icon {name} is not embedded");
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream);
    }
}
