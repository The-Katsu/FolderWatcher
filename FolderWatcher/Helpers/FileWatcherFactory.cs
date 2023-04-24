using Cronos;
using FolderWatcher.Configuration;
using FolderWatcher.Utils;
using Microsoft.Extensions.Logging;

namespace FolderWatcher.Helpers;

public class FileWatcherFactory
{
    private readonly ILogger<FileWatcherFactory> _logger;
    private readonly FileWatcherStorage _storage;

    private FileWatcherConfiguration _configuration = null!;
    private FileSystemWatcher? _fileWatcher;
    private System.Timers.Timer? _timer;

    public FileWatcherFactory(ILogger<FileWatcherFactory> logger, FileWatcherStorage storage)
    {
        _logger = logger;
        _storage = storage;
    }

    /// <summary>
    /// Запоминаем конфигурацию для всего класса.
    /// Инициализируем наблюдателя и подписываемся на события.
    /// Затем объявляем таймер и управляем с его помощью наблюдателем:
    /// Логика работы таймера простая:
    /// Минимальная работа cron - 1 минута (* * * * *)
    /// Получается, если между работами разница 1 минута и меньше, то они идут друг за другом.
    /// Таким образом получаем интервал, с учётом того, что работа не должна начинаться раньше первого тика за день.
    /// Включение и выключение контролируем флагом isWatching
    /// </summary>
    /// <param name="configuration">Конфигурация сервиса</param>
    public void CreateWatcher(FileWatcherConfiguration configuration)
    {
        _configuration = configuration;
        
        _fileWatcher = new FileSystemWatcher(_configuration.Path);
        _fileWatcher.IncludeSubdirectories = true;
        _fileWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
        _fileWatcher.Created += OnChanged;
        _fileWatcher.Changed += OnChanged;
        _fileWatcher.Deleted += OnChanged;
        _fileWatcher.Renamed += OnChanged;

        _timer = new System.Timers.Timer();
        var isWatching = false;
        var cronExpression = CronExpression.Parse(_configuration.Cron);
        var now = DateTime.Now;
        var next = CronUtils.GetNextOccurrence(cronExpression);
        var delay = next - now;
        _timer.Interval = CronUtils.CheckInterval(cronExpression, next) ? 1 : delay.TotalMilliseconds;
        _timer.Elapsed += (_,_) =>
        {
            now = DateTime.Now;
            next = CronUtils.GetNextOccurrence(cronExpression);
            delay = next - now;
            _timer.Interval = delay.TotalMilliseconds;
            if (delay <= TimeSpan.FromMinutes(1) && !isWatching)
            {
                _logger.LogInformation("Запущен просмотр папки '{WatchPath}'...", _configuration.Path);
                isWatching = true;
                _fileWatcher.EnableRaisingEvents = true;
            }
            else if (delay > TimeSpan.FromMinutes(1) && isWatching)
            {
                _logger.LogInformation("Просмотр папки '{WatchPath}' приостановлен до {Next}...", 
                    _configuration.Path, next.ToString("g"));
                isWatching = false;
                _fileWatcher.EnableRaisingEvents = false;
            }
        };
        _timer.Enabled = true;
    }
    
    /// <summary>
    /// Удаляем таймер
    /// Отписываемся от всех событий наблюдателя и удаляем его.
    /// </summary>
    public void TerminateWatcher()
    {
        _timer?.Dispose(); 

        if (_fileWatcher is null) return;
            
        _fileWatcher.Created -= OnChanged;
        _fileWatcher.Changed -= OnChanged;
        _fileWatcher.Deleted -= OnChanged;
        _fileWatcher.Renamed -= OnChanged;
        _fileWatcher.EnableRaisingEvents = false;
        _fileWatcher.Dispose();
    }
    
    /// <summary>
    /// Проверяем источник события, если папка - пропускаем, если файл - логируем и сохраняем изменения. 
    /// </summary>
    /// <param name="sender">Представляет объект, который вызвал событие</param>
    /// <param name="e">Источник события</param>
    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return;
        
        var path = IoUtils.GetRelativePath(_configuration.Path, e.FullPath);
        switch (e.ChangeType)
        {
            case WatcherChangeTypes.Created:
                _logger.LogInformation("Создан: {EFullPath}", path);
                _storage.AddCreated(path);
                break;
            case WatcherChangeTypes.Deleted:
                _logger.LogInformation("Удален: {EFullPath}",path);
                _storage.AddDeleted(path);
                break;
            case WatcherChangeTypes.Changed:
                _logger.LogInformation("Изменен: {EFullPath}", path);
                _storage.AddChanged(path);
                break;
            case WatcherChangeTypes.Renamed:
                var renamed = (RenamedEventArgs) e;
                var oldPath = IoUtils.GetRelativePath(_configuration.Path, renamed.OldFullPath);
                _logger.LogInformation("Переименован: {EOldFullPath} -> {EFullPath}",oldPath,path);
                _storage.UpdateAfterRenaming(oldPath, path);
                break;
        }
    }
}