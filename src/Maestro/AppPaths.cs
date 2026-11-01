using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Maestro;

public sealed record AppPaths(string Roaming, string Local)
{
    public string Config => Path.Combine(Roaming, "maestro.json");
    public string Logs => Path.Combine(Local, "logs");
    public string QBittorrentMarker => Path.Combine(Local, "qbittorrent-freado");
    public string Journal => Path.Combine(Local, "alteracoes.json");
    public string Tools => Path.Combine(Local, "presentmon");
    public string ElevatedFrames => Path.Combine(Local, "presentmon", "quadros.csv");
    public string PermissionMarker => Path.Combine(Local, "permissao-pedida");
    public string Comparisons => Path.Combine(Local, "comparacoes.json");

    public static AppPaths ForCurrentUser()
    {
        var paths = new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Maestro"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maestro"));
        Directory.CreateDirectory(paths.Local);
        return paths;
    }
}

public static class StartMenu
{
    private const string Description = "Ajusta o Windows ao que você está fazendo";

    public static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Maestro.lnk");

    public static void Ensure()
    {
        var target = Environment.ProcessPath!;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            var link = shell.CreateShortcut(ShortcutPath);
            if (File.Exists(ShortcutPath) && string.Equals((string)link.TargetPath, target, StringComparison.OrdinalIgnoreCase)) return;
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = Description;
            link.IconLocation = target + ",0";
            link.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Maestro";
    private const byte ApprovedOn = 0x02;
    private const byte ApprovedOff = 0x03;

    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
    }

    public static void Enable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey)) run.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        Approve(ApprovedOn, 0);
    }

    public static void Disable() => Approve(ApprovedOff, DateTime.UtcNow.ToFileTimeUtc());

    private static void Approve(byte state, long when)
    {
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        approved.SetValue(ValueName, (byte[])[state, 0, 0, 0, .. BitConverter.GetBytes(when)], RegistryValueKind.Binary);
    }
}
