using System.Text.Json;

namespace Maestro;

public sealed class MaestroOptions
{
    public string[] ExtraGames { get; set; } = [];
    public string[] NeverRestrain { get; set; } = [];
    public string[] SlowDuringGames { get; set; } = [];
    public MedalOptions Medal { get; set; } = new();
    public string QBittorrentWebUi { get; set; } = "";
    public string Language { get; set; } = Loc.Automatic;
    public bool CheckUpdates { get; set; }
    public bool ShowFps { get; set; }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static MaestroOptions Defaults => new()
    {
        NeverRestrain =
        [
            "explorer", "Maestro", "WinDock", "Flow.Launcher", "Medal", "MedalEncoder", "obs64", "Discord",
            "deskflow", "deskflow-core", "deskflow-daemon", "lghub", "lghub_agent", "lghub_system_tray", "openlogi-agent",
            "WindowsTerminal", "OpenConsole",
        ],
        SlowDuringGames = ["qbittorrent"],
        Medal = new MedalOptions { Manage = true, Path = @"%LOCALAPPDATA%\Medal\current\Medal.exe", CloseAfterMinutes = 3 },
        QBittorrentWebUi = "http://127.0.0.1:8080",
        Language = Loc.Automatic,
        CheckUpdates = true,
    };

    public static bool EnsureFile(string path)
    {
        if (File.Exists(path)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Defaults.Save(path);
        return true;
    }

    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Indented));
        File.Move(temporary, path, overwrite: true);
    }

    public MaestroOptions Copy() => JsonSerializer.Deserialize<MaestroOptions>(JsonSerializer.Serialize(this))!;

    public void CopyTo(MaestroOptions target)
    {
        foreach (var property in typeof(MaestroOptions).GetProperties().Where(property => property.CanWrite))
            property.SetValue(target, property.GetValue(this));
    }
}

public sealed class MedalOptions
{
    public bool Manage { get; set; }
    public string Path { get; set; } = "";
    public int CloseAfterMinutes { get; set; }
}
