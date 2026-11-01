using Maestro.Fps;
using Maestro.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Maestro;

internal static class Program
{
    private const string QuitSignal = @"Local\Maestro.Sair";
    private const string GreetSignal = @"Local\Maestro.Mostrar";

    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\Maestro", out var first);
        if (!first)
        {
            var signal = args.Contains("--sair") ? QuitSignal : GreetSignal;
            if (EventWaitHandle.TryOpenExisting(signal, out var running))
            {
                using (running) running.Set();
            }
            return;
        }
        if (args.Contains("--sair")) return;
        using var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitSignal);
        using var greet = new EventWaitHandle(false, EventResetMode.AutoReset, GreetSignal);

        System.Windows.Forms.Application.EnableVisualStyles();
        var paths = AppPaths.ForCurrentUser();
        var firstRun = MaestroOptions.EnsureFile(paths.Config);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddJsonFile(source =>
        {
            source.Path = paths.Config;
            source.Optional = false;
            source.ReloadOnChange = true;
            source.OnLoadException = failure => failure.Ignore = true;
            source.ResolveFileProvider();
        });
        builder.Services.AddSingleton<ConfigGuard>();
        builder.Services.AddOptions<MaestroOptions>()
            .Bind(builder.Configuration)
            .PostConfigure<ConfigGuard>((options, guard) => guard.Check(options));
        builder.Services.AddSerilog(log => log
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.Logs, "maestro-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7));
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(2) });
        builder.Services.AddSingleton<ProcessTuner>();
        builder.Services.AddSingleton<Balancer>();
        builder.Services.AddSingleton<GameDetector>();
        builder.Services.AddSingleton<Medal>();
        builder.Services.AddSingleton<QBittorrent>();
        builder.Services.AddSingleton<Conductor>();
        builder.Services.AddHostedService(services => services.GetRequiredService<Conductor>());
        builder.Services.AddSingleton<Settings>();
        builder.Services.AddSingleton<FrameMeter>();
        builder.Services.AddSingleton<PresentMon>();
        builder.Services.AddSingleton<Benchmark>();
        builder.Services.AddSingleton<FpsSwitch>();
        builder.Services.AddSingleton<UpdateNotifier>();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<App>>();
        host.Start();
        if (firstRun) Autostart.Enable();
        StartMenu.Ensure();
        logger.LogInformation("Maestro started");

        var app = new App { Services = host.Services, Quit = quit, Greet = greet, FirstRun = firstRun };
        app.DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled error in the window");
            args.Handled = true;
        };
        app.InitializeComponent();
        app.Run();
        host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        logger.LogInformation("Maestro stopped, everything back to normal");
    }
}
