## Необходимо реализовать background сервис, который будет следить за папкой на диске и регистрировать все изменения, производимые в этой папке.  

---  
Задача реализована с помощью HostedService для background service (FileWatcherService), IOptionsMonitor для обновления конфигурации, ILogger для логирования, Cronos для разбора cron-выражений.    
Приложение развёрнуто и работает в Docker контейнере.  
Смотреть запись работы приложения - https://disk.yandex.ru/i/QwS-ZUNvOdzcNQ

## Требования
- [Поддержка windows и linux-style путей](#поддержка-windows-и-linux-style-путей)
- [Приложение должно корректно обрабатывать ошибки, возникающие в процессе работы (некорректная конфигурация,отсутствие указанной папки и т.д.), выводя понятные сообщения пользователю](#обработка-ошибок)
- [Информация о пути до папки должна считываться из конфигурационного файла формата json](#json-конфигурация)
- [По завершению работы программы, должен быть выведен список созданных, обновленных и удаленных файлов](#список-изменений-по-завершению-работы)
---

## Дополнительные задания
- [Реализовать возможность установки определенного периода времени отслеживания изменений (например, с 8:00 до 18:00) с помощью cron-выражения,которое должно считываться также из файла настроек](#установка-периода-времени-с-помощью-cron-выражений)  
- [Реализовать возможность изменения настроек без необходимости перезапуска приложения](#изменение-настроек-без-перезапуска-приложения)
- [Реализовать логирование в приложении с помощью стандартного механизма ILogger](#реализация-логирования-с-помощью-ilogger)  
- [Завернуть решение в docker](#контейнеризация-приложения)
___

## Поддержка windows и linux-style путей

```csharp
public static class IoUtils
{
    public static string GetOsIndependentPath(string path)
    {
        path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        return Path.GetFullPath(path);
    }

    // код
}
```

Сначала используем метод Path.Combine() для объединения относительного пути из конфигурационного файла с базовой директорией приложения, которую мы можем получить с помощью свойства AppDomain.CurrentDomain.BaseDirectory.  
Затем мы используем метод Path.GetFullPath() для получения абсолютного пути до папки, учитывая возможные различия в форматах путей между операционными системами.


## Обработка ошибок

Т.к. фильтры и middleware доступны только для MVC-pipeline, для обработки ошибок в IHostedService можно использовать классический try-cath для локальной обработки ошибок и AppDomain.CurrentDomain.UnhandledException для внешних(т.к. событие даёт возможность логировать информацию об ошибке, но не помечает её как обработанную, потому завершение работы будет в любом случае или с ошибкой необработанного исключение или exit(1) самостоятельно, для сохранения сервиса активным пользуемся try catch).  

Для обработки локальных ошибок методы приложения следует обернуть в try-cath блок.

```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation(
            "Запуск сервиса... Ожидаем рабочий период времени учитывая cron '{Cron}' ...", Configuration.Cron);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Ошибка во время запуска FileWatcherService, попробуйте обновить конфигурацию");
    }
    return Task.CompletedTask;
}
```  
GlobalExceptionHandler в виде сервиса.

```csharp
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
```

## JSON конфигурация.

```csharp
var host = new HostBuilder()
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile(
            "appsettings.json", 
            optional: false, 
            reloadOnChange: true);
    })
    .Build();
```

С помощью метода AddJsonFile добавляем JSON-файл "appsettings.json" в конфигурацию приложения.  
Параметр optional установлен в значение false, что означает, что если файл не найден, то будет выброшено исключение.   
Параметр reloadOnChange установлен в значение true, что означает, что при изменении файла, конфигурация будет автоматически перезагружена.

## Список изменений по завершению работы.  

Для хранения информации об изменениях в папке создадим класс FileWatcherStorage.   
Для хранения путей используем HashSet как список уникальных значений.
Очищаем коллекции после вывода, присваивая им новую пустую коллекцию, т.к. Clear() сохраняет прежнюю Capacity (не вызывает TrimExcess()).

```csharp
public sealed class FileWatcherStorage
{
    private readonly ILogger<FileWatcherStorage> _logger;
    
    private HashSet<string> _created;
    private HashSet<string> _changed;
    private HashSet<string> _deleted;

    public FileWatcherStorage(ILogger<FileWatcherStorage> logger)
    {
        _logger = logger;
        _created = new HashSet<string>();
        _changed = new HashSet<string>();
        _deleted = new HashSet<string>();
    }

    public void AddCreated(string filePath) => _created.Add(filePath);
    public void AddChanged(string filePath) => _changed.Add(filePath);
    public void AddDeleted(string filePath) => _deleted.Add(filePath);
    
    public void UpdateAfterRenaming(string oldPath, string newPath)
    {
        if (_created.Contains(oldPath))
        {
            _created.Remove(oldPath);
            _created.Add(newPath);
        }

        if (_changed.Contains(oldPath))
        {
            _created.Remove(oldPath);
            _created.Add(newPath);
        }
    }

    public void LogAllChanges()
    {
        if(_created.Any())
        {
            _logger.LogInformation("Добавленные файлы:\n{Arr}", string.Join('\n', _created));
            _created = new HashSet<string>();
        }
        if(_changed.Any())
        {
            _logger.LogInformation("Обновленные файлы:\n{Arr}", string.Join('\n', _changed));
            _changed = new HashSet<string>();
        }
        if(_deleted.Any())
        {
            _logger.LogInformation("Удаленные файлы:\n{Arr}", string.Join('\n', _deleted));
            _deleted = new HashSet<string>();
        }
    }
}
```  

Пример использования:

```csharp
private void OnFileCreated(object sender, FileSystemEventArgs e)
{
    var path = IoUtils.GetRelativePath(_configuration.Path, e.FullPath);
    _logger.LogInformation("Создан: {EFullPath}", path);
    _storage.AddCreated(path);
}
```  

---

## Установка периода времени с помощью cron-выражений  
Для корректной работы используйте интервалы пример - (* 8-18 * * 1-5):(понедельник-пятница с 8 до 18). Т.к. cron-выражения задают период выполнения, а не интервал работы. По условию нам необходимы именно интервалы, как и для работы с FileSystemWatcher (т.к. работает на ивентах и необходима подписка для отслеживания изменений), потому берём первый и последний запланированный запуск и задаём интервал между ними.  
Конечно можно запустить Quartz или Hangfire с cron-выражением, но опять же нам нужен интервал, а не период.
```csharp
public static class CronUtils
{
    public static DateTime GetFirstOccurrenceOfTheDay(CronExpression cron) =>
        cron
            .GetOccurrences(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))
            .FirstOrDefault(x => x.Day == DateTime.Today.Day, DateTime.MinValue);
    
    public static DateTime GetLastOccurrenceOfTheDay(CronExpression cron) =>
        cron
            .GetOccurrences(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))
            .LastOrDefault(x => x.Day == DateTime.Today.Day, DateTime.MinValue);
}
```
После этого запускаем таймер и настраиваем временные рамки так, что сервис не будет получать события, если время выполнения не соответствует временным рамкам из конфигурации.
```csharp
// Инициализируем наблюдателя
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
```  
Пример работы для выражения ' 20 16-17 22 1-6 6 ' (каждую 20 минуту с 16 по 17 часы 22 числа в месяцы с 1 по 6 по субботам)  
![](https://sun9-41.userapi.com/impg/CYR7d-mxbdyT_PAMLfSy0xtiv9Np42qcoK-OjQ/4CqLG84mOmw.jpg?size=826x692&quality=96&sign=1a7ef12533dcbd50fbcbb17d379a325a&type=album)

## Изменение настроек без перезапуска приложения  

Для реализации изменения настроек без перезапуска приложения можно использовать IOptionsMonitor. Для этого создадим класс с соответствующими полями.
```csharp
public class FileWatcherConfiguration
{
    public string Path { get; set; } = null!;
    public string Cron { get; set; } = null!;
}
```  
Зарегистрируем класс в сервисах как TOptions.
```csharp
var host = new HostBuilder()
    .ConfigureLogging(logging =>
    {
        // кофигурация логера
    })
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile(
            "appsettings.json", 
            optional: false, 
            reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<FileWatcherConfiguration>(context.Configuration);
        // остальные сервисы
    })
    .Build();
```
Создадим класс (подробнее смотреть класс FileWatcherConfigurator), который будет ответственен за конфигурацию сервиса и создадим метод для применения конфигурации.
```csharp
public sealed class FileWatcherService : IHostedService
{
    public FileWatcherService(
        FileWatcherConfigurator configurator)
    {
        configurator.ConfigureService(this);
    }

    //код
}
```
Сервис на старте загружает конфигурацию и подписывается на изменения конфигурации. Если происходит изменение - конфигурация обновляется, сервис перезапускается.
```csharp
private readonly IOptionsMonitor<FileWatcherConfiguration> _configurationMonitor;
private FileWatcherService _service = null!;

public FileWatcherConfigurator(
    IOptionsMonitor<FileWatcherConfiguration> configurationMonitor)
{
    _configurationMonitor = configurationMonitor;
}

public void ConfigureService(FileWatcherService service)
{
    _service = service;
    LoadConfiguration();
    _configurationMonitor.OnChange((_,_) => ReloadConfiguration());
}
```
Метод вызывает загрузку конфигурации, в случае успеха перезапускает сервис с обновлёнными данными, иначе загрузчик отлогирует об ошибках. Код метода LoadConfiguration смотреть в классе FileWatcherConfigurator.    
*_lastConfigurationChange помогает с проблемой многократного срабатывания OnChange события при изменении настроек, подробнее о проблеме (https://github.com/dotnet/aspnetcore/issues/2542). Многократной подписки и т.п. не было, проверял дебагером, потому поставил секундную задержку перед следующим выполнением.
```csharp
private DateTime _lastConfigurationChange = DateTime.MinValue; 

private void ReloadConfiguration()
{
    if (DateTime.Now - _lastConfigurationChange >= TimeSpan.FromSeconds(1))
    {
        _lastConfigurationChange = DateTime.Now;

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
```
Пример обновления с корректной и некорректной конфигурацией:
![](https://sun9-14.userapi.com/impg/Bep9Ur7iE8eoh4GV16ZKDDlk6YaYXs-2tYcc0g/7FWhrXumE5g.jpg?size=1183x623&quality=96&sign=96f436531beca30e9663d7224f1ff0d9&type=album)
## Реализация логирования с помощью ILogger  

Пример объявления, очищаем хост от всех реализаций с помощью ClearProviders() и добавляем логирование в консоль, используем SimpleConsole для настройки вывода времени логирования, т.к. у Console эта опция помечена как устаревшая.  
```csharp
var host = new HostBuilder()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options => 
            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ");
    })
    .ConfigureAppConfiguration((_, config) =>
    {
        // конфигурация
    })
    .ConfigureServices((context, services) =>
    {
        // сервисы
    })
    .Build();
```
Получаем логер с помощью конструкта класса и используем.

```csharp
public sealed class FileWatcherService : IHostedService
{
    private readonly ILogger<FileWatcherService> _logger;

    public FileWatcherService(
        ILogger<FileWatcherService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Запуск сервиса...");
                // код
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка во время запуска FileWatcherService");
            }
        }, cancellationToken);
    }
}
```  
## Контейнеризация приложения  
FileWatcherService работает с Windows API, потому при Linux контейнеризации приложение запустить можно, но уведомления получать нельзя. На сцену выходят Windows контейнеры :)  
Открываем Powershell и пишем:
```powershell
& $Env:ProgramFiles\Docker\Docker\DockerCli.exe -SwitchDaemon .
```  
Теперь у докера есть возможность переключиться на Windows контейнеры.  
![](https://sun9-45.userapi.com/impg/7VHzddwshwjEN2oBr4Ka5mZ6UvEKwVgqCJr1QQ/RAl8daD09UA.jpg?size=294x422&quality=96&sign=143d590c47067cd1afc37296a581ec81&type=album)  
Теперь возвращаемся в Powershell, вводим и готовимся к перезагрузке
```powershell
Enable-WindowsOptionalFeature -Online -FeatureName $("Microsoft-Hyper-V", "Containers") -All
```  
После перезагрузки созадём изображение нашего приложения и...  
Теперь надо привязать диск, который мы хотим просматривать, предположим это будут документы
```powershell
-v <Путь на локальной машине>:<Закрепляем место в контейнере>
# пример
-v C:\Users\karma\Documents:C:\APP\DOCUMENTS
```  
Теперь если в конфигурации указать "Path": "C:\APP\DOCUMENTS", то мы можем просматривать изменения на локальной машине.  
![](https://sun9-28.userapi.com/impg/u2H-QBAdYJrUv82yXC73RJYjgDnxC-5awB990A/F6Dh1J4BhS4.jpg?size=633x211&quality=96&sign=38db3791e9f9306a7382f75db21e424a&type=album)  
Но, есть но, контейнер не отслеживает события по не привязанным к нему файлам, а это значит, что даже удалив appsettings.json ничего не изменится, но мы ведь хотим менять конфигурацию. Для этого в коде укажите вложенную папку для файла конфигурации, учитывая особенности OS внутри контейнера.  
```csharp
.ConfigureAppConfiguration((_, config) =>
    {
        config
        --> .SetBasePath("C:\\app\\config") <--
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
```  
Теперь нужно привязать директорию, из которой мы хотим менять наш файл конфигурации.
```powershell
-v C:\Users\karma\Documents\Config:C:\APP\CONFIG
```  
Результат:
![](https://sun9-24.userapi.com/impg/pNJXPBHix9S30SZIE7rC83F6BO_mVXZONHe29A/8wWk3bNY0uM.jpg?size=1126x262&quality=96&sign=9caf1696823da8ec26ba2780a21b29ed&type=album)  
*В начале закреплена запись работы приложения.