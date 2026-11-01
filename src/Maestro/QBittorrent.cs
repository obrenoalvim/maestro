using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maestro;

public sealed class QBittorrent(HttpClient http, IOptionsMonitor<MaestroOptions> options, AppPaths paths, ILogger<QBittorrent> logger)
{
    private const string ModePath = "/api/v2/transfer/speedLimitsMode";
    private const string TogglePath = "/api/v2/transfer/toggleSpeedLimitsMode";

    private string BaseUrl => options.CurrentValue.QBittorrentWebUi.TrimEnd('/');

    public async Task<bool> SlowDownAsync(CancellationToken cancellation)
    {
        if (File.Exists(paths.QBittorrentMarker)) return false;
        var slow = await IsSlowAsync(cancellation);
        if (slow is not false) return false;
        if (!await ToggleAsync(cancellation)) return false;
        await File.WriteAllTextAsync(paths.QBittorrentMarker, DateTime.Now.ToString("O"), cancellation);
        return true;
    }

    public async Task<bool> RestoreAsync(CancellationToken cancellation)
    {
        if (!File.Exists(paths.QBittorrentMarker)) return false;
        var slow = await IsSlowAsync(cancellation);
        if (slow is null) return false;
        if (slow == true && !await ToggleAsync(cancellation)) return false;
        File.Delete(paths.QBittorrentMarker);
        return true;
    }

    public Task<bool?> ProbeAsync(CancellationToken cancellation) => IsSlowAsync(cancellation);

    private async Task<bool?> IsSlowAsync(CancellationToken cancellation)
    {
        if (BaseUrl.Length == 0) return null;
        try
        {
            var mode = await http.GetStringAsync(BaseUrl + ModePath, cancellation);
            return mode.Trim() == "1";
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug("Interface Web do qBittorrent fora do ar: {Message}", error.Message);
            return null;
        }
    }

    private async Task<bool> ToggleAsync(CancellationToken cancellation)
    {
        try
        {
            using var response = await http.PostAsync(BaseUrl + TogglePath, null, cancellation);
            return response.IsSuccessStatusCode;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            logger.LogDebug("qBittorrent não aceitou trocar o limite: {Message}", error.Message);
            return false;
        }
    }
}
