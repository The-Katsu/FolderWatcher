## Необходимо реализовать background сервис, который будет следить за папкой на диске и регистрировать все изменения, производимые в этой папке.  

---

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
}
```

Сначала используем метод Path.Combine() для объединения относительного пути из конфигурационного файла с базовой директорией приложения, которую мы можем получить с помощью свойства AppDomain.CurrentDomain.BaseDirectory.  
Затем мы используем метод Path.GetFullPath() для получения абсолютного пути до папки, учитывая возможные различия в форматах путей между операционными системами.


## Обработка ошибок

Т.к. фильтры и middleware доступны только для MVC-pipeline, для обработки ошибок в IHostedService можно использовать классический try-cath для локальной обработки ошибок и AppDomain.CurrentDomain.UnhandledException для внешних(не получится использовать везде, т.к. событие даёт возможность логировать информацию об ошибке, но не помечает её как обработанную, потому завершение работы будет в любом случае или с ошибкой необработанного исключение или exit(1) самостоятельно).  

Для обработки локальных ошибок методы приложения следует обернуть в try-cath блок.

```csharp
public async Task StartAsync(CancellationToken cancellationToken)
{
    await Task.Run(() =>
    {
        try
        {
            // код
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка в блоке Х");
        }
    }, cancellationToken);
}
```  

Для обработки ошибок извне подходит событие AppDomain.CurrentDomain.UnhandledException.  
После того, как ошибка будет выведена в консоль - приложение закроется с кодом 1 (приложение завершилось с ошибкой).  
*Как вариант событие также можно объявить при конфигурации приложения в Program.cs, но вынести его в отдельный класс и запустить сервисом, чтобы при остановке отписаться от события больше похоже на best practice :)   

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
Для вывода используем логер, т.к. при остановке сервиса нужно логировать о завершении сервиса и вывести в консоль список изменений, если делать одно с помощью логера, а другое с помощью Console.WriteLine(), то вывод может наложиться друг на друга и текст будет вперемешку.  
Для хранения путей используем HashSet как список уникальных значений, потому что один и тот же файл, к примеру может изменяться или создавать (удалил, отменил удаление) множество раз и выводить многократно один и тот же файл не корректно.  
Перед тем как вывести результаты на консоль используем метод CheckData(), чтобы проверить файлы на соответствие их положению в папке статусу, созданные и изменённые файлы должны действительно существовать на диске, а удалённые файлы отсутствовать. Если нужен вывод всех изменений без проверки, то строку можно просто закомментировать.  
Очищаем коллекции после вывода, присваивая им новую пустую коллекцию, т.к. Clear() сохраняет прежнюю Capacity (не вызывает TrimExcess()), тогда мы имеем вероятность, что коллекция будет только разрастаться, а новая коллекция на 0 элемент будет занимать места как предыдущая, допустим до этого было 100 изменений и мы будет хранить коллекцию, с выделенной памятью под 100 элементов, но их будет 0... Да... Так что new HashSet().

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

    public void WriteAllChanges()
    {
        CheckData(); 
        
        if(_created.Any()) 
            _logger.LogInformation("Добавленные файлы:\n{Arr}", string.Join('\n', _created));
        if(_changed.Any()) 
            _logger.LogInformation("Обновленные файлы:\n{Arr}", string.Join('\n', _changed));
        if(_deleted.Any()) 
            _logger.LogInformation("Удаленные файлы:\n{Arr}", string.Join('\n', _deleted));
        
        _created = new HashSet<string>();
        _changed = new HashSet<string>();
        _deleted = new HashSet<string>();
    }

    private void CheckData()
    {
        _created = _created.Where(Path.Exists).ToHashSet();
        _changed = _changed.Where(Path.Exists).ToHashSet();
        _deleted = _deleted.Where(path => !Path.Exists(path)).ToHashSet();
    }
}
```  

Пример использования:

```csharp
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
    _logger.LogInformation("Переименован: {EOldFullPath} -> {EFullPath}", 
        e.OldFullPath, e.FullPath);
    _storage.UpdateAfterRenaming(e.OldFullPath, e.FullPath);
}
```  

---

## Установка периода времени с помощью cron-выражений  
Неполное решение. Работает только с интервальными значения выражений, например '* 8-18 * * *' для работы в интервале с 8 часов до 18 или '30 1-10 7-12 * *' будет запускать работу c 7 по 12 месяц с 1:30 до 10:30 и т.п., если будет '* 8,17-18 * * *' интервал будет также браться первое и последнее время запуска (с 8:00 до 18:00 часов), т.к. для отслеживания изменений необходима подписка на события FileSystemWatcher и если подписаться на события по тригеру можно, то когда отписываться от них cron-выражение нам не скажет, т.к. планирует только выполнение функции, а не сессию. Можно использовать Quartz или Hangfire для запуска cron работ, но в любом случае нужно будет включать/отключать в рабочий/нерабочий период FileWatcher.EnableRaisingEvents и ?определять? конечное время наблюдения за папкой.   
CRON-выражение используется в основном для повторяющегося срабатывания по расписанию. В нашем случае по условию необходимо обеспечить работу сервиса наблюдателя в определённый период времени (например 8:00-18:00). Выражение имеет структуру * * * * * (минута час день(месяца) месяц день(недели)), также встречаются выражения из 6 * с добавлением секунд. Для проверки cron-выражений используйте (https://crontab.guru/).  
! Идея решения: Т.к. наблюдение за файлами реализовано с помощью FileSystemWatcher и подписок на события Created, Changed, Deleted, Renamed и флага EnableRaisingEvents, который эти события разрешает, с помощью System.Timers.Timer мы раз в 30 секунд будем проверять рабочее время сервиса на соответствие временным рамкам заданным cron-выражением, если период правильный то наблюдатель запускается(если не запущен), если вне периода, то останавливается(если запущен). Для этого получаем первое и последнее время срабатывания за день, на их основе формируем период работы.
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
var fileWatcher = new FileSystemWatcher(_watchPath)
{
    IncludeSubdirectories = true, 
    NotifyFilter = NotifyFilters.FileName | 
                    NotifyFilters.LastWrite | 
                    NotifyFilters.DirectoryName, 
    Filter = "*.*"
};

fileWatcher.Created += OnFileCreated;
fileWatcher.Changed += OnFileChanged;
fileWatcher.Deleted += OnFileDeleted;
fileWatcher.Renamed += OnFileRenamed;


var timer = new System.Timers.Timer();
var isJobStarted = false;
timer.Interval = 1; // Для первого старта без задержек
timer.AutoReset = true; // Держим таймер активным для проверки периода
timer.Elapsed += (_, _) =>
{
    timer.Interval = 30000; // 30 секунд
    var start = CronUtils.GetFirstOccurrenceOfTheDay(_cronExpression);
    var end = CronUtils.GetLastOccurrenceOfTheDay(_cronExpression);
    var now = DateTime.Now;
    // Если в рабочем промежутке и не запущен, то запускаем
    if (now >= start && now <= end && !isJobStarted) 
    {
        isJobStarted = true;
        fileWatcher.EnableRaisingEvents = true;
    }
    // Если не в рабочем промежутке и запущен, то останавливаем
    else if ((now < start || now > end) && isJobStarted)
    {
        isJobStarted = false;
        fileWatcher.EnableRaisingEvents = false;
        _storage.WriteAllChanges();
    }
};
_timer.Start();
```  
Пример работы для '52 13-17 * * *'  
![](https://sun9-45.userapi.com/impg/iYFgFcpEcasrrfUCAxXYvY8hRs_91mjnk4q2lg/pGLMDtSpuhA.jpg?size=958x487&quality=96&sign=04f4a469e7abd7f85e6f582653985c03&type=album)

## Изменение настроек без перезапуска приложения  

Для реализации изменения настроек без перезапуска приложения можно использовать IOptionsMonitor. Для этого создадим класс с соответствующими полями.
```csharp
public class FileWatcherConfiguration
{
    public required string Path { get; set; }
    public required string Cron { get; set; }
}
```  
Зарегистрируем класс в сервисах как конфигурацию TOptions.
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
Передадим класс в конструктор как IOptionsMonitor, который возвращает актуальные значения конфигурации, для отслеживания изменений есть метод OnChange(), в который передадим метод обновляющий конфигурацию.
```csharp
public sealed class FileWatcherService : IHostedService
{
    private readonly IOptionsMonitor<FileWatcherConfiguration> _configurationMonitor;

    public FileWatcherService(
        IOptionsMonitor<FileWatcherConfiguration> configurationMonitor)
    {
        _configurationMonitor = configurationMonitor;
        _configurationMonitor.OnChange((_, _) => Task.Run(ReloadConfig).Wait());
    }

    //код
}
```
Метод ReloadConfig использует метод LoadConfig, который проверяет текущие значения конфигурации на валидность, в случае успеха перезапустить сервис с новыми настройками, в случае исключения логировать ошибку в консоль.  
*Столкнулся с проблемой, что событие срабатывает дважды при изменении настроек, подробнее о проблеме (https://github.com/dotnet/aspnetcore/issues/2542). Двойной регистрации и т.п. не было, проверял дебагером, потому поставил временное ограничение между последним обновлением и следующим.
```csharp
private DateTime _lastConfigurationChange = DateTime.MinValue; 

private async Task ReloadConfig()
{
    if (DateTime.Now - _lastConfigurationChange >= TimeSpan.FromSeconds(1))
    {
        _lastConfigurationChange = DateTime.Now;
            
        var token = new CancellationToken();

        if(_isRunning) 
            await StopAsync(token);
        
        LoadConfig();

        if (_validated)
        {
            _logger.LogInformation("Конфигурация успешно обновлена, перезапуск сервиса...");
            await StartAsync(token);
        }
    }
}

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
```
## Реализация логирования с помощью ILogger  

Пример объявления, очищаем хост от всех реализаций с помощью ClearProviders() и добавляем логирование в консоль AddConsole().  
```csharp
var host = new HostBuilder()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
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
Пример обновления с корректной и некорректной конфигурацией:
![](https://sun9-68.userapi.com/impg/TNGvXbRoT3G5Alv5lXwmV5Z0l_skBE54i7h1Lw/7lcwSikrJXE.jpg?size=1019x869&quality=96&sign=b3a609d2fa9a0ad14d0271e8fe4ce13f&type=album)