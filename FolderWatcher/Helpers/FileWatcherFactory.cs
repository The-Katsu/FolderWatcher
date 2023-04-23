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
    /// 1. Проверяем интервал рабочего времени.
    /// 2. В случае подходящего интервала запускаем наблюдателя (если не запущен)
    /// 3. В случае выхода за интервал отключаем таймер (если включен)
    /// Включение и выключение контролируем флагом isWatching
    /// Первый раз таймер запускаем с минимальным интервалом, далее выставляем интервал проверки 30 секунд.
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
        _fileWatcher.Renamed += OnRenamed;

        _timer = new System.Timers.Timer();
        var isWatching = false;
        var cronExpression = CronExpression.Parse(_configuration.Cron);
        _timer.Interval = 1; // Для первого старта без задержек
        _timer.AutoReset = true; // Держим таймер активным для проверки периода
        _timer.Elapsed += (_,_) =>
        {
            _timer.Interval = 30000; // 30 секунд, успеть в минимальный интервал * * * * * (каждую минуту) 
            var start = CronUtils.GetFirstOccurrenceOfTheDay(cronExpression);
            var end = CronUtils.GetLastOccurrenceOfTheDay(cronExpression);
            var now = DateTime.Now;
            if (now >= start && now <= end && !isWatching) 
            {
                _logger.LogInformation(
                    "Запущен просмотр папки '{WatchPath}', {Date} в период {Start} : {End}...", 
                    _configuration.Path, now.ToString("d"), start.ToString("t"), end.ToString("t"));
                isWatching = true;
                _fileWatcher.EnableRaisingEvents = true;
            }
            else if ((now < start || now > end) && isWatching)
            {
                _logger.LogInformation(
                    "Просмотр папки '{WatchPath}' приостановлен до следующего периода...", _configuration.Path);
                isWatching = false;
                _fileWatcher.EnableRaisingEvents = false;
                _storage.LogAllChanges();
            }
        };
        _timer.Start();
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
        _fileWatcher.Renamed -= OnRenamed;
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
        }
    }
    
    /// <summary>
    /// Проверяем источник события, если папка - пропускаем, если файл - логируем и сохраняем изменения.
    /// FileSystemEventArgs не имеет поля OldFullPath, потому выносим в отдельный метод.
    /// </summary>
    /// <param name="sender">Представляет объект, который вызвал событие</param>
    /// <param name="e">Источник события</param>
    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return;
        
        var oldPath = IoUtils.GetRelativePath(_configuration.Path, e.OldFullPath);
        var newPath = IoUtils.GetRelativePath(_configuration.Path, e.FullPath);
        _logger.LogInformation("Переименован: {EOldFullPath} -> {EFullPath}",oldPath,newPath);
        _storage.UpdateAfterRenaming(oldPath, newPath);
    }
}