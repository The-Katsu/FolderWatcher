using Microsoft.Extensions.Hosting;

namespace FolderWatcher;

public sealed class GlobalExceptionHandlerService : IHostedService
{
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
        Console.WriteLine("Ошибка:");
        Console.WriteLine(ex.Message);
        Console.WriteLine(ex.StackTrace);
        
        Environment.Exit(1);
    }
}