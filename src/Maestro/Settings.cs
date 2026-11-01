using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Maestro;

public sealed class Settings(IOptionsMonitor<MaestroOptions> monitor, AppPaths paths)
{
    public MaestroOptions Current => monitor.CurrentValue;

    public IDisposable? OnChange(Action<MaestroOptions> listener) => monitor.OnChange(options => listener(options));

    public void Update(Action<MaestroOptions> change)
    {
        var next = Current.Copy();
        change(next);
        next.Save(paths.Config);
    }
}

public static class Updates
{
    public const string Page = "https://github.com/obrenoalvim/maestro/releases/latest";
    public const string Repository = "https://github.com/obrenoalvim/maestro";
    private const string Api = "https://api.github.com/repos/obrenoalvim/maestro/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    public static Version Current => Plain(Assembly.GetEntryAssembly()?.GetName().Version) ?? new Version(0, 0, 0);

    public static async Task<Version?> LatestAsync()
    {
        try
        {
            using var document = JsonDocument.Parse(await Http.GetStringAsync(Api));
            return document.RootElement.TryGetProperty("tag_name", out var tag) ? Parse(tag.GetString()) : null;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public static Version? Parse(string? tag) =>
        tag is { Length: > 0 } && Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var version) ? Plain(version) : null;

    private static Version? Plain(Version? version) => version is null ? null : new Version(version.Major, version.Minor, Math.Max(0, version.Build));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Maestro");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
