using Cronos;
using FolderWatcher.Storage;
using FolderWatcher.Utils;
using FolderWatcher.Validation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FolderWatcher.BackgroundServices;

public sealed class FileWatcherService : IHostedService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly FileWatcherStorage _storage;
    private readonly ConfigurationValidator _validator;
    private readonly IOptionsMonitor<FileWatcherConfiguration> _configurationMonitor;
    private DateTime _lastConfigurationChange = DateTime.MinValue;
    private FileSystemWatcher? _fileWatcher;
    private System.Timers.Timer? _timer;
    private string _watchPath = string.Empty;
    private CronExpression? _cronExpression;
    private bool _validated = false;
    private bool _isRunning = false;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IOptionsMonitor<FileWatcherConfiguration> configurationMonitor, 
        FileWatcherStorage storage, 
        ConfigurationValidator validator)
    {
        _logger = logger;
        _configurationMonitor = configurationMonitor;
        _configurationMonitor.OnChange((_, _) => Task.Run(ReloadConfig).Wait());
        _storage = storage;
        _validator = validator;
        LoadConfig();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            _isRunning = true;
            try
            {
                if (_validated)
                {
                    _logger.LogInformation(
                        "Запуск сервиса {Now}... Ожидаем рабочий период времени учитывая cron '{Cron}' ...", 
                        DateTime.Now, _cronExpression);
                    
                    // Инициализируем наблюдателя и подписываемся на создание, изменение и удаление
                    _fileWatcher = new FileSystemWatcher(_watchPath)
                    {
                        IncludeSubdirectories = true, 
                        NotifyFilter = NotifyFilters.FileName | 
                                       NotifyFilters.LastWrite | 
                                       NotifyFilters.DirectoryName, 
                        Filter = "*.*"
                    };
                    
                    _fileWatcher.Created += OnFileCreated;
                    _fileWatcher.Changed += OnFileChanged;
                    _fileWatcher.Deleted += OnFileDeleted;
                    _fileWatcher.Renamed += OnFileRenamed;

                    
                    // Инициализируем таймер для включения/выключения наблюдателя в зависимости от периода времени
                    _timer = new System.Timers.Timer();
                    var isJobStarted = false;
                    _timer.Interval = 1; // Для первого старта без задержек
                    _timer.AutoReset = true; // Держим таймер активным для проверки периода
                    _timer.Elapsed += (_, _) =>
                    {
                        _timer.Interval = 30000; // 30 секунд
                        var start = CronUtils.GetFirstOccurrenceOfTheDay(_cronExpression);
                        var end = CronUtils.GetLastOccurrenceOfTheDay(_cronExpression);
                        var now = DateTime.Now;
                        // Если в рабочем промежутке и не запущен, то запускаем
                        if (now >= start && now <= end && !isJobStarted) 
                        {
                            _logger.LogInformation(
                                "Запущен просмотр папки '{WatchPath}' в период {Start} : {End}...", 
                                _watchPath, start, end);
                            isJobStarted = true;
                            _fileWatcher.EnableRaisingEvents = true;
                        }
                        // Если не в рабочем промежутке и запущен, то останавливаем
                        else if ((now < start || now > end) && isJobStarted)
                        {
                            _logger.LogInformation("Время - {End} Просмотр папки '{WatchPath}' остановлен...", 
                                DateTime.Now, _watchPath);
                            isJobStarted = false;
                            _fileWatcher.EnableRaisingEvents = false;
                            _storage.WriteAllChanges();
                        }
                    };
                    _timer.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка во время запуска FileWatcherService");
            }
        }, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            _isRunning = false;
            try
            {
                _logger.LogInformation("Остановка сервиса...");
                
                _timer?.Dispose(); 

                if (_fileWatcher is null) return;
                
                _fileWatcher.Created -= OnFileCreated;
                _fileWatcher.Changed -= OnFileChanged;
                _fileWatcher.Deleted -= OnFileDeleted;
                _fileWatcher.Renamed -= OnFileRenamed;
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
               
                _storage.WriteAllChanges(); 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка во время остановки FileWatcherService");
            }
        }, cancellationToken);
    }

    #region Загрузка конфигурации

    private void LoadConfig()
    {
        try
        {
            _logger.LogInformation("Загрузка конфигурации...");
            var exceptions = new List<Exception>();
            var validationResult = _validator.Validate(_configurationMonitor.CurrentValue);
            _validated = validationResult is {IsPathValid: true, IsCronValid: true};
            
            if (!validationResult.IsPathValid)
                exceptions.Add(new DirectoryNotFoundException(
                    $"Папки '{_configurationMonitor.CurrentValue.Path}' не существует"));

            if (!validationResult.IsCronValid)
                exceptions.Add(new ArgumentException(
                    $"Ошибка в cron выражении '{_configurationMonitor.CurrentValue.Cron}'"));

            if (exceptions.Any())
                throw new AggregateException("Ошибка валидации:", exceptions);
            
            _watchPath = IoUtils.GetOsIndependentPath(_configurationMonitor.CurrentValue.Path);
            _cronExpression = CronExpression.Parse(_configurationMonitor.CurrentValue.Cron);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Ошибка при загрузке конфигурации FileWatcherService, сервис ожидает обновления конфигурации");
        }
    }
    
    private async Task ReloadConfig()
    {
        // Предотвращение многократного срабатывания, подробнее в README
        // Ссылка на проблему https://github.com/dotnet/aspnetcore/issues/2542
        if (DateTime.Now - _lastConfigurationChange >= TimeSpan.FromSeconds(1))
        {
            _lastConfigurationChange = DateTime.Now;
            
            // остановка сервиса -> загрузка новой конфигурации -> запуск, если конфигурация валидна
            var token = new CancellationToken();
            if(_isRunning) await StopAsync(token);
            
            LoadConfig();

            if (_validated)
            {
                _logger.LogInformation("Конфигурация успешно обновлена, перезапуск сервиса...");
                await StartAsync(token);
            }
        }
    }

    #endregion
    
    #region Обработка событий

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("Создан: {EFullPath}", e.FullPath);
        _storage.AddCreated(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("Изменен: {EFullPath}", e.FullPath);
        _storage.AddChanged(e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        _logger.LogInformation("Удален: {EFullPath}", e.FullPath);
        _storage.AddDeleted(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        _logger.LogInformation("Переименован: {EOldFullPath} -> {EFullPath}", e.OldFullPath, e.FullPath);
        _storage.UpdateAfterRenaming(e.OldFullPath, e.FullPath);
    }

    #endregion
}