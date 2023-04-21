using FolderWatcher.BackgroundServices;
using FolderWatcher.Storage;
using FolderWatcher.Utils;
using FolderWatcher.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<FileWatcherConfiguration>(context.Configuration);
        services.AddSingleton<FileWatcherStorage>();
        services.AddSingleton<ConfigurationValidator>();
        services.AddHostedService<GlobalExceptionHandlerService>();
        services.AddHostedService<FileWatcherService>();
    })
    .Build();

await host.RunAsync();