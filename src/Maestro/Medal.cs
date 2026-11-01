using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maestro;

public sealed class Medal(IOptionsMonitor<MaestroOptions> options, ILogger<Medal> logger)
{
    private static readonly string[] ProcessNames = ["Medal", "MedalEncoder"];

    public static bool IsMedal(string name) => ProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public bool Start()
    {
        var path = Environment.ExpandEnvironmentVariables(options.CurrentValue.Medal.Path);
        if (!File.Exists(path))
        {
            logger.LogWarning("Medal não encontrado em {Path}", path);
            return false;
        }
        Process.Start(new ProcessStartInfo(path, "--hidden") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();
        return true;
    }

    public void Stop()
    {
        foreach (var process in ProcessNames.SelectMany(Process.GetProcessesByName))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception)
                {
                    logger.LogDebug(error, "Medal ({Pid}) já tinha saído ou recusou fechar", process.Id);
                }
            }
        }
    }
}
