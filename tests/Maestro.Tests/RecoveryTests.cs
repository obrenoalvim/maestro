using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace Maestro.Tests;

public sealed class RecoveryTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("maestro-").FullName;
    private AppPaths Paths => new(folder, folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void AFreshStartPutsBackWhatACrashedRunLeftChanged()
    {
        using var current = Process.GetCurrentProcess();
        var original = current.PriorityClass;
        var key = ProcessSampler.Take().Single(sample => sample.Key.Pid == current.Id).Key;
        var crashed = new ProcessTuner(Paths, NullLogger<ProcessTuner>.Instance);
        try
        {
            Assert.True(crashed.Restrain(key, current.ProcessName));
            current.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, current.PriorityClass);

            var recovered = new ProcessTuner(Paths, NullLogger<ProcessTuner>.Instance).RecoverFromJournal();

            current.Refresh();
            Assert.Equal(original, current.PriorityClass);
            Assert.Equal([current.ProcessName], recovered);
            Assert.False(File.Exists(Paths.Journal));
        }
        finally
        {
            current.PriorityClass = original;
        }
    }

    [Fact]
    public void ABrokenConfigKeepsTheLastOneThatWorked()
    {
        var guard = new ConfigGuard(Paths);
        File.WriteAllText(Paths.Config, "{ \"ExtraGames\": [\"meujogo\"] }");
        guard.Check(new MaestroOptions { ExtraGames = ["meujogo"] });

        File.WriteAllText(Paths.Config, "{ \"ExtraGames\": [\"meujogo\" ");
        var reloaded = new MaestroOptions();
        guard.Check(reloaded);

        Assert.NotNull(guard.Fault);
        Assert.Equal(["meujogo"], reloaded.ExtraGames);
    }

    [Fact]
    public void AGameMarkedByHandCountsEvenWithoutTheGameBarSignal()
    {
        const string Store = @"Software\MaestroTeste\Children";
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\MaestroTeste", throwOnMissingSubKey: false);
        using (var marked = Registry.CurrentUser.CreateSubKey(Store + @"\a")) marked.SetValue("MatchedExeFullPath", @"C:\Jogos\marcado.exe");
        using (var listed = Registry.CurrentUser.CreateSubKey(Store + @"\b"))
        {
            listed.SetValue("MatchedExeFullPath", @"C:\Jogos\conhecido.exe");
            listed.SetValue("TitleId", "1234");
        }
        try
        {
            var detector = new GameDetector(new FixedOptions(new MaestroOptions()), Store);
            ProcessSample Running(int pid, string name) => new(new ProcessKey(pid, 1), name, 1, 0, 0);

            var withoutSignal = detector.Detect([Running(10, "marcado"), Running(11, "conhecido")], 1);
            var withSignal = detector.Detect([Running(10, "marcado"), Running(11, "conhecido"), Running(12, "GameBarPresenceWriter")], 1);

            Assert.Equal(["marcado"], withoutSignal!.Processes.Select(process => process.Name));
            Assert.Equal(["marcado", "conhecido"], withSignal!.Processes.Select(process => process.Name));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MaestroTeste", throwOnMissingSubKey: false);
        }
    }

    private sealed class FixedOptions(MaestroOptions value) : IOptionsMonitor<MaestroOptions>
    {
        public MaestroOptions CurrentValue => value;

        public MaestroOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<MaestroOptions, string?> listener) => null;
    }
}
