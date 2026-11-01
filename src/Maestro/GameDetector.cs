using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace Maestro;

public sealed record GameSession(IReadOnlyList<ProcessSample> Processes, string Name);

public sealed class GameDetector(IOptionsMonitor<MaestroOptions> options, string storeKey = GameDetector.GameConfigStore)
{
    public const string GameConfigStore = @"System\GameConfigStore\Children";
    private const string PresenceWriter = "GameBarPresenceWriter";
    private static readonly TimeSpan StoreRefresh = TimeSpan.FromSeconds(30);

    private HashSet<string> knownGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> markedGames = new(StringComparer.OrdinalIgnoreCase);
    private DateTime storeReadAt = DateTime.MinValue;

    public GameSession? Detect(IReadOnlyList<ProcessSample> processes, int sessionId)
    {
        if (DateTime.UtcNow - storeReadAt > StoreRefresh) ReadStore();
        var writerUp = processes.Any(process => process.Name.Equals(PresenceWriter, StringComparison.OrdinalIgnoreCase));
        var settings = options.CurrentValue;
        var extra = new HashSet<string>(settings.ExtraGames, StringComparer.OrdinalIgnoreCase);
        var games = processes
            .Where(process => process.SessionId == sessionId && IsGame(process.Name, writerUp, extra, settings.NeverRestrain))
            .ToList();
        if (games.Count == 0 && !writerUp) return null;
        return new GameSession(games, games.Count > 0 ? games[0].Name : Loc.T("unknownGame"));
    }

    private bool IsGame(string name, bool writerUp, HashSet<string> extra, string[] neverRestrain) =>
        (extra.Contains(name) || markedGames.Contains(name) || (writerUp && knownGames.Contains(name)))
        && !Balancer.Untouchable.Contains(name)
        && !neverRestrain.Contains(name, StringComparer.OrdinalIgnoreCase);

    private void ReadStore()
    {
        storeReadAt = DateTime.UtcNow;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var marked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.CurrentUser.OpenSubKey(storeKey);
        foreach (var child in root?.GetSubKeyNames() ?? [])
        {
            using var game = root!.OpenSubKey(child);
            if (game?.GetValue("MatchedExeFullPath") is not string path || path.Length == 0) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            known.Add(name);
            if (game.GetValue("TitleId") is null) marked.Add(name);
        }
        knownGames = known;
        markedGames = marked;
    }
}
