using FolderWatcher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<FileWatcherStorage>();
        services.AddSingleton<ConfigurationValidator>();
        services.AddHostedService<GlobalExceptionHandlerService>();
        services.AddHostedService<FileWatcherService>();
    })
    .Build();

await host.RunAsync();