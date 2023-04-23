using FolderWatcher.BackgroundServices;
using FolderWatcher.Configuration;
using FolderWatcher.Helpers;
using FolderWatcher.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options => 
            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ");
    })
    .ConfigureAppConfiguration((_, config) =>
    {
        config
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<FileWatcherConfiguration>(context.Configuration);
        services.AddSingleton<FileWatcherValidator>();
        services.AddSingleton<FileWatcherConfigurator>();
        services.AddSingleton<FileWatcherStorage>();
        services.AddSingleton<FileWatcherFactory>();
        services.AddHostedService<GlobalExceptionHandlerService>();
        services.AddHostedService<FileWatcherService>();
    })
    .Build();

await host.RunAsync();