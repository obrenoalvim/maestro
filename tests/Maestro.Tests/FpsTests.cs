using System.Text.RegularExpressions;
using Maestro.Fps;

namespace Maestro.Tests;

public sealed class FpsTests
{
    [Fact]
    public void StatsGiveAverageAndTheSlowestOnePercent()
    {
        var times = Enumerable.Repeat(10.0, 99).Append(50.0);

        var stats = FrameStats.From(times)!;

        Assert.Equal(100, stats.Frames);
        Assert.Equal(100 * 1000 / 1040.0, stats.AverageFps, 3);
        Assert.Equal(20, stats.OnePercentLowFps, 3);
        Assert.Equal(10, stats.P99FrameTimeMs, 3);
    }

    [Fact]
    public void TooFewFramesGiveNoStats() => Assert.Null(FrameStats.From([16.6, 16.6, 16.6]));

    [Fact]
    public void ReadsFramesByHeaderNamesNotPositions()
    {
        var csv = new PresentMonCsv();

        Assert.Null(csv.Read("Application,ProcessID,SwapChainAddress,PresentRuntime,MsBetweenPresents,MsInPresentAPI"));
        var frame = csv.Read("eldenring.exe,4242,0x000001,DXGI,6.944,0.12");

        Assert.Equal(new Frame(4242, 6.944), frame);
        Assert.Null(csv.Read("eldenring.exe,notapid,0x000001,DXGI,6.944,0.12"));
    }

    [Fact]
    public void LiveFpsCountsTheLastSecondOfFrames()
    {
        var meter = new FrameMeter();
        for (var i = 0; i < 300; i++) meter.Add(new Frame(7, 1000 / 144.0));

        var live = meter.Live(7)!;

        Assert.Equal(144, live.Fps, 0);
        Assert.Null(meter.Live(8));
    }

    [Fact]
    public void ARecordingKeepsOnlyItsProcessFrames()
    {
        var meter = new FrameMeter();
        using var recording = meter.Record(7);
        for (var i = 0; i < 20; i++)
        {
            meter.Add(new Frame(7, 10));
            meter.Add(new Frame(8, 40));
        }

        var stats = recording.Stats()!;
        meter.Add(new Frame(7, 99));

        Assert.Equal((20, 100.0), (stats.Frames, Math.Round(stats.AverageFps)));
    }

    [Fact]
    public void EveryTextExistsInBothLanguagesWithTheSamePlaceholdersAndNoDashes()
    {
        var problems = new List<string>();
        foreach (var key in Loc.Keys)
        {
            Loc.Instance.Set(Loc.English);
            var english = Loc.T(key);
            Loc.Instance.Set(Loc.Portuguese);
            var portuguese = Loc.T(key);
            if (english.Length == 0 || portuguese.Length == 0) problems.Add($"{key}: empty");
            if ((english + portuguese).Contains('—') || (english + portuguese).Contains('–')) problems.Add($"{key}: dash");
            if (!Placeholders(english).SequenceEqual(Placeholders(portuguese))) problems.Add($"{key}: placeholders differ");
        }

        Assert.Empty(problems);
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)").Select(match => match.Groups[1].Value).Order();
}
