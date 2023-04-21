using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FolderWatcher.BackgroundServices;

public sealed class GlobalExceptionHandlerService : IHostedService
{
    private readonly ILogger<GlobalExceptionHandlerService> _logger;

    public GlobalExceptionHandlerService(ILogger<GlobalExceptionHandlerService> logger) => 
        _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        return Task.CompletedTask;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = (Exception)e.ExceptionObject;
        _logger.LogCritical(ex, "Критическая ошибка, остановка приложения...");
        
        Environment.Exit(1);
    }
}