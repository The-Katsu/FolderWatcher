using FolderWatcher.BackgroundServices;
using FolderWatcher.Utils;
using FolderWatcher.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FolderWatcher.Configuration;

public class FileWatcherConfigurator
{
    private readonly IOptionsMonitor<FileWatcherConfiguration> _configurationMonitor;
    private readonly ILogger<FileWatcherConfigurator> _logger;
    private readonly FileWatcherValidator _validator;
    
    private FileWatcherService _service = null!;
    private DateTime _lastConfigurationChange = DateTime.MinValue;

    public FileWatcherConfigurator(
        IOptionsMonitor<FileWatcherConfiguration> configurationMonitor,
        ILogger<FileWatcherConfigurator> logger,
        FileWatcherValidator validator)
    {
        _logger = logger;
        _validator = validator;
        _configurationMonitor = configurationMonitor;
    }

    public void ConfigureService(FileWatcherService service)
    {
        _service = service;
        LoadConfiguration();
        _configurationMonitor.OnChange((_,_) => ReloadConfiguration());
    }

    /// <summary>
    /// С помощью валидатора проверяем корректность данных, если всё хорошо - присваиваем статус валидности и
    /// загружаем конфигурацию, иначе логируем список ошибок валидации.
    /// </summary>
    /// <exception cref="AggregateException">Список ошибок</exception>
    private void LoadConfiguration()
    {
        try
        {
            _logger.LogInformation("Загрузка конфигурации...");
            var validationResult = _validator.Validate(_configurationMonitor.CurrentValue);
            _service.IsConfigurationValid = validationResult is {IsPathValid: true, IsCronValid: true};

            if (validationResult.Exceptions.Any())
                throw new AggregateException("Ошибка валидации:", validationResult.Exceptions);

            _service.Configuration.Path = IoUtils.GetOsIndependentPath(_configurationMonitor.CurrentValue.Path);
            _service.Configuration.Cron = _configurationMonitor.CurrentValue.Cron;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка при загрузке конфигурации FileWatcher, сервис ожидает обновления конфигурации");
        }
    }
    
    /// <summary>
    /// Метод вызываемый OnChangeToken'ом при обновлении конфигурационного файла.
    /// Запускает загрузку конфигурации и в случае валидности данных перезапускает сервис с обновлёнными значениями.
    /// </summary>
    private void ReloadConfiguration()
    {
        // Предотвращение многократного срабатывания, подробнее в README
        // Ссылка на проблему https://github.com/dotnet/aspnetcore/issues/2542
        if (DateTime.Now - _lastConfigurationChange >= TimeSpan.FromSeconds(2))
        {
            _lastConfigurationChange = DateTime.Now;

            // остановка сервиса -> загрузка новой конфигурации -> запуск, если конфигурация валидна
            var token = new CancellationToken();
            if (_service.IsRunning) _service.StopAsync(token);
            LoadConfiguration();
            if (_service.IsConfigurationValid)
            {
                _logger.LogInformation("Конфигурация успешно обновлена, перезапуск сервиса...");
                _service.StartAsync(token);
            }
        }
    }
}