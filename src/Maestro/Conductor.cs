using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maestro;

public sealed record Entry(DateTime At, LocText Text)
{
    public override string ToString() => $"{At:HH:mm} {Text}";
}

public sealed record Status(string? Game, IReadOnlyList<Entry> History, bool Paused, bool Started)
{
    public IReadOnlyList<string> Recent => [.. History.Select(entry => entry.ToString())];

    public bool Gaming => Game is not null;

    public string Headline =>
        !Started ? Loc.T("headlineStarting")
        : Paused ? Loc.T("headlinePaused")
        : Game is null ? Loc.T("headlineNormal")
        : Loc.F("headlineGame", Game);
}

public sealed class Conductor(
    ProcessTuner tuner,
    Balancer balancer,
    GameDetector detector,
    Medal medal,
    QBittorrent qbittorrent,
    IOptionsMonitor<MaestroOptions> options,
    ILogger<Conductor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GameGrace = TimeSpan.FromSeconds(10);
    private const int RecentLimit = 20;

    private readonly int sessionId = Process.GetCurrentProcess().SessionId;
    private readonly LinkedList<Entry> recent = new();
    private readonly Lock gate = new();
    private readonly HashSet<ProcessKey> boostedGame = [];
    private GameSession? session;
    private DateTime lastGameAt;
    private DateTime medalNeededAt = DateTime.Now;
    private bool medalStartedThisSession;
    private readonly SemaphoreSlim work = new(1, 1);
    private volatile bool paused;

    public Status Status { get; private set; } = new(null, [], false, false);

    public bool Paused
    {
        get => paused;
        set
        {
            paused = value;
            Publish();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (await qbittorrent.RestoreAsync(stopping)) Note(new LocText("noteQbitStale"));
        foreach (var name in tuner.RecoverFromJournal()) Note(new LocText("noteJournal", name));
        using var timer = new PeriodicTimer(Interval);
        var before = ProcessSampler.Take();
        var clock = Stopwatch.StartNew();
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
            {
                try
                {
                    var after = ProcessSampler.Take();
                    var usage = ProcessSampler.Usage(before, after, clock.Elapsed, Environment.ProcessorCount);
                    before = after;
                    clock.Restart();
                    await work.WaitAsync(stopping);
                    try
                    {
                        await TickAsync(after, usage, stopping);
                    }
                    finally
                    {
                        work.Release();
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    logger.LogError(error, "Falha numa rodada");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await work.WaitAsync(CancellationToken.None);
            await StandDownAsync(CancellationToken.None);
        }
    }

    public void OnForeground(int pid)
    {
        if (paused || pid == 0) return;
        _ = Task.Run(async () =>
        {
            await work.WaitAsync();
            try
            {
                foreach (var decision in balancer.ReleaseForeground(pid)) Apply(decision);
            }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao soltar a janela da frente");
            }
            finally
            {
                work.Release();
            }
        });
    }

    private async Task TickAsync(IReadOnlyList<ProcessSample> processes, IReadOnlyList<ProcessUsage> usage, CancellationToken stopping)
    {
        if (paused)
        {
            if (session is not null || tuner.HasChanges) await StandDownAsync(stopping);
            Publish();
            return;
        }

        var now = DateTime.Now;
        var foreground = Native.ForegroundPid();
        var game = detector.Detect(processes, sessionId);
        if (game is not null) await StartGameAsync(game, now, stopping);
        else if (session is not null && now - lastGameAt > GameGrace) await EndGameAsync(stopping);

        var settings = options.CurrentValue;
        var context = new BalanceContext(
            sessionId,
            foreground,
            (game?.Processes ?? []).Select(process => process.Key.Pid).ToHashSet(),
            new HashSet<string>(settings.NeverRestrain, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(settings.SlowDuringGames, StringComparer.OrdinalIgnoreCase))
        {
            Gaming = game is not null || session is not null,
        };
        foreach (var decision in balancer.Next(usage, context)) Apply(decision);
        if (game is not null) BoostGame(game);

        ManageMedal(processes, foreground, now, settings.Medal);
        Publish();
    }

    private async Task StartGameAsync(GameSession game, DateTime now, CancellationToken stopping)
    {
        lastGameAt = now;
        var first = session is null;
        session = game;
        if (!first) return;
        Note(new LocText("noteGameOpen", game.Name));
        medalStartedThisSession = false;
        if (await qbittorrent.SlowDownAsync(stopping)) Note(new LocText("noteQbitSlow"));
    }

    private void BoostGame(GameSession game)
    {
        var boosted = game.Processes
            .Where(process => boostedGame.Add(process.Key))
            .Where(process => tuner.Boost(process.Key, process.Name, ProcessPriorityClass.High))
            .GroupBy(process => process.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var group in boosted)
        {
            var count = group.Count();
            Note(count == 1 ? new LocText("noteHigh", group.Key) : new LocText("noteHighMany", group.Key, count));
        }
    }

    private async Task EndGameAsync(CancellationToken stopping)
    {
        if (session is null) return;
        Note(new LocText("noteGameClosed", session.Name));
        foreach (var key in boostedGame) tuner.Restore(key);
        boostedGame.Clear();
        session = null;
        if (await qbittorrent.RestoreAsync(stopping)) Note(new LocText("noteQbitFree"));
    }

    private async Task StandDownAsync(CancellationToken stopping)
    {
        await EndGameAsync(stopping);
        tuner.RestoreAll();
        balancer.Reset();
    }

    private void Apply(Decision decision)
    {
        var applied = decision.Tune switch
        {
            Tune.Restrain => tuner.Restrain(decision.Key, decision.Name),
            Tune.Boost => tuner.Boost(decision.Key, decision.Name, ProcessPriorityClass.AboveNormal),
            _ => Restore(decision.Key),
        };
        if (!applied)
        {
            balancer.Refuse(decision.Key);
            return;
        }
        Note(decision.Tune switch
        {
            Tune.Restrain => new LocText("noteRestrain", decision.Name, decision.Reason),
            Tune.Release => new LocText("noteRelease", decision.Name, decision.Reason),
            Tune.Boost => new LocText("noteBoost", decision.Name, decision.Reason),
            _ => new LocText("noteUnboost", decision.Name, decision.Reason),
        });
    }

    private bool Restore(ProcessKey key)
    {
        tuner.Restore(key);
        return true;
    }

    private void ManageMedal(IReadOnlyList<ProcessSample> processes, int foreground, DateTime now, MedalOptions settings)
    {
        if (!settings.Manage) return;
        var medalProcesses = processes.Where(process => process.SessionId == sessionId && Medal.IsMedal(process.Name)).ToList();
        if (session is not null || medalProcesses.Any(process => process.Key.Pid == foreground)) medalNeededAt = now;
        if (session is not null && medalProcesses.Count == 0 && !medalStartedThisSession)
        {
            medalStartedThisSession = true;
            if (medal.Start()) Note(new LocText("noteMedalOpen"));
            return;
        }
        if (session is null && medalProcesses.Count > 0 && now - medalNeededAt > TimeSpan.FromMinutes(settings.CloseAfterMinutes))
        {
            medal.Stop();
            medalNeededAt = now;
            Note(new LocText("noteMedalClose", settings.CloseAfterMinutes));
        }
    }

    private void Note(LocText text)
    {
        logger.LogInformation("{Action}", text.ToString());
        lock (gate)
        {
            recent.AddFirst(new Entry(DateTime.Now, text));
            while (recent.Count > RecentLimit) recent.RemoveLast();
        }
    }

    private void Publish()
    {
        lock (gate) Status = new Status(paused ? null : session?.Name, [.. recent], paused, true);
    }
}
