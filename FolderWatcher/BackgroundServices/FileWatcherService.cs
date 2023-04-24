using FolderWatcher.Configuration;
using FolderWatcher.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FolderWatcher.BackgroundServices;

public sealed class FileWatcherService : IHostedService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly FileWatcherStorage _storage;
    private readonly FileWatcherFactory _factory;

    public readonly FileWatcherConfiguration Configuration = new();
    public bool IsRunning = false;
    public bool IsConfigurationValid = false;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        FileWatcherStorage storage, 
        FileWatcherFactory factory, 
        FileWatcherConfigurator configurator)
    {
        _logger = logger;
        _storage = storage;
        _factory = factory;
        configurator.ConfigureService(this);
    }

    /// <summary>
    /// Запускаем сервис, если данные валидны.
    /// С помощью фабрики создаём наблюдателя и ожидаем изменения.
    /// Если данные не валидны - получим ошибку на этапе загрузки конфигурации и пропустим этап создания наблюдателя.
    /// Флаг IsRunning необходим для корректного перезапуска при изменении конфигурации. 
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsRunning = true;
            if (!IsConfigurationValid) return Task.CompletedTask;
            
            _logger.LogInformation(
                "Запуск сервиса... Ожидаем рабочий интервал времени для выражения '{Cron}' ...", Configuration.Cron);
            _factory.CreateWatcher(Configuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка во время запуска FileWatcherService, попробуйте обновить конфигурацию");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Останавливаем сервис.
    /// С помощью фабрики удаляем наблюдателя.
    /// Логируем изменения из хранилища.
    /// Флаг IsRunning необходим для корректного перезапуска при изменении конфигурации. 
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsRunning = false;
            _logger.LogInformation("Остановка сервиса...");
            _factory.TerminateWatcher();
            _storage.LogAllChanges(); 
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка во время остановки FileWatcherService");
        }

        return Task.CompletedTask;
    }
}