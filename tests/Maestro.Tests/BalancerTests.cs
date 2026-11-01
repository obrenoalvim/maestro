namespace Maestro.Tests;

public sealed class BalancerTests
{
    private const int Session = 1;
    private static readonly ProcessKey Busy = new(100, 1);
    private static readonly ProcessKey Copier = new(200, 1);
    private static readonly ProcessKey Torrent = new(300, 1);

    private static BalanceContext Desktop(int foreground = 0) =>
        new(Session, foreground, new HashSet<int>(), new HashSet<string> { "explorer" }, new HashSet<string> { "qbittorrent" });

    private static BalanceContext Gaming(params int[] gamePids) => Desktop() with { GamePids = gamePids.ToHashSet(), Gaming = true };

    private static ProcessUsage Usage(ProcessKey key, string name, double cpu = 0, double io = 0) => new(key, name, Session, cpu, io);

    [Fact]
    public void RestrainsABackgroundHogOnlyAfterItStaysBusy()
    {
        var balancer = new Balancer();

        Assert.Empty(balancer.Next([Usage(Busy, "render", cpu: 40)], Desktop()));
        Assert.Empty(balancer.Next([Usage(Busy, "render", cpu: 40)], Desktop()));
        var decision = Assert.Single(balancer.Next([Usage(Busy, "render", cpu: 40)], Desktop()));

        Assert.Equal(Tune.Restrain, decision.Tune);
    }

    [Fact]
    public void NeverRestrainsTheWindowInFrontAndReleasesItWhenItComesForward()
    {
        var balancer = new Balancer();
        for (var i = 0; i < 3; i++) Assert.Empty(balancer.Next([Usage(Busy, "render", cpu: 40)], Desktop(foreground: Busy.Pid)));

        for (var i = 0; i < 3; i++) balancer.Next([Usage(Busy, "render", cpu: 40)], Desktop());
        var release = Assert.Single(balancer.Next([Usage(Busy, "render", cpu: 40)], Desktop(foreground: Busy.Pid)));

        Assert.Equal(Tune.Release, release.Tune);
    }

    [Fact]
    public void ClickingARestrainedWindowReleasesItAtOnce()
    {
        var balancer = new Balancer();
        for (var i = 0; i < 3; i++) balancer.Next([Usage(Busy, "render", cpu: 40)], Desktop());

        var release = Assert.Single(balancer.ReleaseForeground(Busy.Pid));

        Assert.Equal((Tune.Release, "render"), (release.Tune, release.Name));
        Assert.Empty(balancer.ReleaseForeground(Busy.Pid));
    }

    [Fact]
    public void ProtectedAndSystemProcessesAreLeftAlone()
    {
        var balancer = new Balancer();

        for (var i = 0; i < 5; i++)
        {
            Assert.Empty(balancer.Next([Usage(Busy, "explorer", cpu: 90), Usage(new ProcessKey(4, 1), "csrss", cpu: 90)], Desktop()));
        }
    }

    [Fact]
    public void BoostsATransferAndHandsItBackWhenAGameStarts()
    {
        var balancer = new Balancer();
        balancer.Next([Usage(Copier, "robocopy", io: 50 * 1024 * 1024)], Desktop());

        var boost = Assert.Single(balancer.Next([Usage(Copier, "robocopy", io: 50 * 1024 * 1024)], Desktop()));
        var unboost = Assert.Single(balancer.Next([Usage(Copier, "robocopy", io: 50 * 1024 * 1024)], Gaming()));

        Assert.Equal(Tune.Boost, boost.Tune);
        Assert.Equal(Tune.Unboost, unboost.Tune);
    }

    [Fact]
    public void SlowsTorrentsDuringAGameAndReleasesThemAfter()
    {
        var balancer = new Balancer();

        var slow = Assert.Single(balancer.Next([Usage(Torrent, "qbittorrent")], Gaming()));
        var released = Enumerable.Range(0, 5).SelectMany(_ => balancer.Next([Usage(Torrent, "qbittorrent")], Desktop())).ToList();

        Assert.Equal(Tune.Restrain, slow.Tune);
        Assert.Equal(Tune.Release, Assert.Single(released).Tune);
    }

    [Fact]
    public void AProcessThatBecomesTheGameIsLetGo()
    {
        var balancer = new Balancer();
        for (var i = 0; i < 3; i++) balancer.Next([Usage(Busy, "eldenring", cpu: 40)], Desktop());

        var letGo = Assert.Single(balancer.Next([Usage(Busy, "eldenring", cpu: 40)], Gaming(Busy.Pid)));

        Assert.Equal(Tune.Release, letGo.Tune);
    }
}
