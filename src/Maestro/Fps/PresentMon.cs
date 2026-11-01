using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace Maestro.Fps;

public enum Permission
{
    Missing,
    Pending,
    Granted,
}

public sealed class PresentMon(AppPaths paths, FrameMeter meter, ILogger<PresentMon> logger) : IDisposable
{
    private const string Version = "2.6.0";
    private const string Sha256 = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
    private const string PerformanceLogUsers = "S-1-5-32-559";
    private const string SessionName = "Maestro";
    private const int CanceledByUser = 1223;
    private static readonly string DownloadUrl = $"https://github.com/GameTechDev/PresentMon/releases/download/v{Version}/PresentMon-{Version}-x64.exe";
    private static readonly string[] CommonArguments =
        ["--no_console_stats", "--stop_existing_session", "--session_name", SessionName, "--no_track_gpu", "--no_track_input", "--exclude", "dwm.exe"];

    private readonly HttpClient http = CreateClient();
    private Process? direct;
    private CancellationTokenSource? tail;

    public string? Error { get; private set; }

    public bool Busy { get; private set; }

    public bool Running => direct is { HasExited: false } || tail is { IsCancellationRequested: false };

    private string Exe => Path.Combine(paths.Tools, $"PresentMon-{Version}-x64.exe");

    public Permission Permission
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (principal.IsInRole(WindowsBuiltInRole.Administrator) || identity.Groups?.Any(group => group.Value == PerformanceLogUsers) == true)
                return Permission.Granted;
            return File.Exists(paths.PermissionMarker) ? Permission.Pending : Permission.Missing;
        }
    }

    public async Task<bool> GrantAsync()
    {
        var member = WindowsIdentity.GetCurrent().Name.Replace("'", "''");
        var script = $"try {{ Add-LocalGroupMember -SID '{PerformanceLogUsers}' -Member '{member}' -ErrorAction Stop }} catch [Microsoft.PowerShell.Commands.MemberExistsException] {{ }}";
        var exit = await RunElevatedAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]);
        if (exit != 0) return false;
        Directory.CreateDirectory(paths.Local);
        await File.WriteAllTextAsync(paths.PermissionMarker, DateTime.Now.ToString("O"));
        return true;
    }

    public async Task<bool> StartAsync(int targetPid, CancellationToken cancellation)
    {
        Error = null;
        if (Running) return true;
        if (!await EnsureBinaryAsync(cancellation)) return false;
        return Permission == Permission.Granted ? StartDirect() : await StartElevatedAsync(targetPid, cancellation);
    }

    public void Stop()
    {
        tail?.Cancel();
        tail = null;
        if (direct is null) return;
        try
        {
            if (!direct.HasExited) direct.Kill();
        }
        catch (InvalidOperationException)
        {
        }
        direct.Dispose();
        direct = null;
        using var cleanup = Process.Start(new ProcessStartInfo(Exe, ["--terminate_existing_session", "--session_name", SessionName]) { CreateNoWindow = true, UseShellExecute = false });
    }

    public void Dispose()
    {
        Stop();
        http.Dispose();
    }

    private async Task<bool> EnsureBinaryAsync(CancellationToken cancellation)
    {
        if (File.Exists(Exe) && await HashMatchesAsync(Exe, cancellation)) return true;
        Busy = true;
        try
        {
            Directory.CreateDirectory(paths.Tools);
            var temporary = Exe + ".download";
            await using (var source = await http.GetStreamAsync(DownloadUrl, cancellation))
            await using (var target = File.Create(temporary))
                await source.CopyToAsync(target, cancellation);
            if (!await HashMatchesAsync(temporary, cancellation))
            {
                File.Delete(temporary);
                Error = Loc.F("fpsDownloadFailed", "SHA-256");
                return false;
            }
            File.Move(temporary, Exe, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
        {
            logger.LogWarning("PresentMon download failed: {Message}", error.Message);
            Error = Loc.F("fpsDownloadFailed", error.Message);
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    private bool StartDirect()
    {
        var parser = new PresentMonCsv();
        var start = new ProcessStartInfo(Exe, [.. CommonArguments, "--output_stdout"])
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        direct = new Process { StartInfo = start, EnableRaisingEvents = true };
        direct.OutputDataReceived += (_, line) =>
        {
            if (line.Data is not null && parser.Read(line.Data) is { } frame) meter.Add(frame);
        };
        direct.ErrorDataReceived += (_, line) =>
        {
            if (!string.IsNullOrWhiteSpace(line.Data)) logger.LogDebug("PresentMon: {Line}", line.Data);
        };
        direct.Start();
        direct.BeginOutputReadLine();
        direct.BeginErrorReadLine();
        return true;
    }

    private async Task<bool> StartElevatedAsync(int targetPid, CancellationToken cancellation)
    {
        File.Delete(paths.ElevatedFrames);
        string[] arguments = [.. CommonArguments, "--process_id", targetPid.ToString(), "--terminate_on_proc_exit", "--output_file", paths.ElevatedFrames];
        try
        {
            using var started = Process.Start(new ProcessStartInfo(Exe, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch (Win32Exception error) when (error.NativeErrorCode == CanceledByUser)
        {
            Error = Loc.T("fpsRefused");
            return false;
        }
        tail = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _ = TailAsync(tail.Token);
        return true;
    }

    private async Task TailAsync(CancellationToken cancellation)
    {
        var parser = new PresentMonCsv();
        var pending = "";
        try
        {
            while (!File.Exists(paths.ElevatedFrames)) await Task.Delay(200, cancellation);
            await using var stream = new FileStream(paths.ElevatedFrames, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (!cancellation.IsCancellationRequested)
            {
                var chunk = await reader.ReadToEndAsync(cancellation);
                if (chunk.Length == 0)
                {
                    await Task.Delay(100, cancellation);
                    continue;
                }
                var lines = (pending + chunk).Split('\n');
                pending = lines[^1];
                foreach (var line in lines[..^1])
                {
                    if (parser.Read(line.TrimEnd('\r')) is { } frame) meter.Add(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException error)
        {
            logger.LogWarning("Could not read PresentMon output: {Message}", error.Message);
        }
    }

    private static async Task<int> RunElevatedAsync(string file, string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (process is null) return -1;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == CanceledByUser)
        {
            return -1;
        }
    }

    private static async Task<bool> HashMatchesAsync(string path, CancellationToken cancellation)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation)) == Sha256;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Maestro");
        return client;
    }
}
