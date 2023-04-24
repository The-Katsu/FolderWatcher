## Необходимо реализовать background сервис, который будет следить за папкой на диске и регистрировать все изменения, производимые в этой папке.  

---  
Задача реализована как консольное приложение на dotnet 7 с помощью HostedServices для background сервисов, IOptionsMonitor для обновления конфигурации, ILogger для логирования, Cronos для разбора cron-выражений.    
Приложение развёрнуто и работает в Docker контейнере.  
Смотреть запись работы приложения - https://disk.yandex.ru/i/yzl0JYKFVkRe8A

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
    - [Пример мониторинга папки в контейнера](#мониторинг-папки-в-контейнера)
    - [Пример перезапуска сервиса при изменении файла конфигурации в контейнере](#перезапуск-сервиса-при-изменения-файла-конфигурации-из-контейнера)
    - [Пример периодического отслеживания с cron выражения в контейнере](#периодическое-выполнение-учитывая-cron-выражения-в-контейнере)
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

Т.к. фильтры и middleware доступны только для MVC-pipeline, для обработки ошибок в IHostedService можно использовать классический try-cath для локальной обработки ошибок и AppDomain.CurrentDomain.UnhandledException для внешних(т.к. событие даёт возможность логировать информацию об ошибке, но не помечает её как обработанную, потому завершение работы будет в любом случае или с ошибкой необработанного исключение или exit(1) самостоятельно, для сохранения сервиса активным пользуемся try catch).  

Для обработки локальных ошибок методы приложения следует обернуть в try-cath блок.

```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    try
    {
        _logger.LogInformation(
            "Запуск сервиса...");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Ошибка Х:");
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
Оба параметра являются строковыми значениями.  
Пример appsettings.json файла:
```json
{
  "Path": "C:\\Users\\USER\\Documents",
  "Cron": "* * * * *"
}
```

## Список изменений по завершению работы.  

Для хранения информации об изменениях в папке создадим класс FileWatcherStorage.   
Для хранения путей используем HashSet как список уникальных значений.
Очищаем коллекции после вывода, присваивая им новую пустую коллекцию, т.к. Clear() сохраняет прежнюю Capacity (не вызывает TrimExcess()).

```csharp
public class FileWatcherStorage
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
Создадим класс для работы с cron выражениями, нам нужно получать 2 значения:  
* Когда следующий вызов.
* Находится ли следующий вызов в интервале уже.  
Идея такова, т.к. минимальный интервал выполнения cron-выражений - 1 минута (* * * * *), тогда если между текущем временем и следующим вызовом 1 минута или меньше, то приложение находится в интервале времени.  
Выражения работают в соответствии с локальным временем, а парсятся в соответствии с utc, потому вычисляем разницу и добавляем при поиске получении следующего вызова.
```csharp
public static class CronUtils
{
    var prev = cron
            .GetOccurrences(DateTime.UtcNow.AddHours(-1), next.AddMicroseconds(-1))
            .LastOrDefault(DateTime.MinValue);
        return next - prev <= TimeSpan.FromMinutes(1);

    public static DateTime GetNextOccurrence(CronExpression cron)
    {
        var utcOffset = DateTime.Now - DateTime.UtcNow;
        return cron.GetNextOccurrence(DateTime.UtcNow.AddTicks(utcOffset.Ticks))!.Value;
    }
}
```
Теперь мы создаём создаём таймер и подбираем ему стартовый интервал, если следующий вызов уже находится в интервале то мы стартуем без задержек, иначе ждём. Решение заключается в том, что мы останавливаем наблюдателя каждый раз при выходе за интервал работы и включаем при попадании в интервал, переключая флаг EnableRaisingEvents.  
```csharp
// Инициализируем наблюдателя
_fileWatcher = new FileSystemWatcher(_configuration.Path);

// настройка наблюдателя...

// создаём таймер
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
    _timer.Interval = delay.Milliseconds;
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
```  
Пример работы для выражения ' 25-30,35-40 12 * * * ' (каждый 12 час с 25 по 30 минуту и с 35 по 40 минуту). Запускаем в 12:27, попадаем в интервал и сразу запускаем наблюдатель, затем работа остановится в 30 минуту и начнётся с 35 по 40 минуты.    
![](https://sun9-79.userapi.com/impg/Ea_DEBuNf5kFZEpCgoDvGSMkxZpc5gIrjY94vQ/W_in6dRRmRc.jpg?size=824x748&quality=96&sign=f8f59274cf30c17ed92e570af4490b8e&type=album)

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
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<FileWatcherConfiguration>(context.Configuration);
    })
    .Build();
```
Создадим класс, ответственный за конфигурацию сервиса, создадим метод Configure и передадим в конструктор сервиса, который запустит конфигурацию приложения. Подробнее смотреть класс FileWatcherConfigurator.
```csharp
public sealed class FileWatcherService : IHostedService
{
    public FileWatcherService(FileWatcherConfigurator configurator)
    {
        configurator.ConfigureService(this);
    }
}
```
Конфигуратор получает из конструктора IOptionsMonitor с нашей конфигурацией, загружает конфигурацию и подписывается на изменения конфигурации. Если происходит изменение - конфигурация обновляется, сервис перезапускается(в случае валидной конфигурации, отлогирует ошибку).
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
*_lastConfigurationChange помогает с проблемой многократного срабатывания OnChange события при изменении настроек, подробнее о проблеме (https://github.com/dotnet/aspnetcore/issues/2542). Многократной подписки и т.п. не было, проверял дебагером, потому поставил задержку в 2 секунды перед следующим обновлением.
```csharp
private DateTime _lastConfigurationChange = DateTime.MinValue; 

private void ReloadConfiguration()
{
    if (DateTime.Now - _lastConfigurationChange >= TimeSpan.FromSeconds(2))
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
Пример обновления, запускаем с корректной конфигурацией, затем допускаем ошибку в пути и в cron выражении, получаем список из 2 ошибок (приложение всё ещё работает и ожидает обновления конфигурации), вводим корректную конфигурацию и сервис запускает наблюдателя:
![](https://sun9-61.userapi.com/impg/28QxzBduFsHNFmwnKNZjWx7ESeTO6C8Gji0q8A/MCDLQMsPRss.jpg?size=1422x712&quality=96&sign=77f15f62952c1611b4348e3919edebb0&type=album)
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

    public  Task StartAsync(CancellationToken cancellationToken)
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
## Мониторинг папки в контейнера
После перезагрузки создаём изображение нашего приложения и...  
Теперь надо привязать папку, которую мы хотим просматривать, предположим это будут документы
```powershell
-v <Путь на локальной машине>:<Закрепляем место в контейнере>
# пример
-v C:\Users\karma\Documents:C:\APP\DOCUMENTS
```  
Теперь если в конфигурации указать "Path": "C:\APP\DOCUMENTS", то мы можем просматривать изменения на локальной машине (в данном случае просматриваем "C:\Users\user\Documents"), контейнеру доступны все подпапки привязанной директории, потому мы можем устанавливать контроль за любой конкретной папкой изменив настройки.  
Пример наблюдения за C:\APP\DOCUMENTS.  
![](https://sun9-79.userapi.com/impg/YCf0hTrKJVemzLzy3V1GTtRelYGP-sva1XNhCg/kr7O22bw8x4.jpg?size=679x257&quality=96&sign=4459d813e7ded9b4b6c53d10f4dee3b1&type=album)  

## Перезапуск сервиса, при изменения файла конфигурации из контейнера
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
Пример работы приложения можно посмотреть в начале в виде записи экрана.  
Пример работы с папками вложенного уровня. Запускаемся с неподходящим временем (в 17:49, но интервал cron стоит для 10-20 минут), потому меняет выражение, сервис перезапускается с новой конфигурацией, затем вносим изменения в папке, получаем уведомления. Теперь изменим конфигурацию ещё раз, сервис остановился, мы общую информацию о сделанных изменениях и перешли в новую директорию для просмотра, сделали и сделали там пару изменений.  
![](https://sun9-65.userapi.com/impg/0aIuAyc_fgNzVegDNkVeuktdScz5URe6_wT7lA/qJ8wOeCWCNE.jpg?size=628x833&quality=96&sign=0ca85e32b023bfb5279a32686a5c272c&type=album)    
## Периодическое выполнение, учитывая cron выражения в контейнере.
Прикладываю скриншот работы приложения для интервала ' 30-35,40-45 * * * * ' - каждый час с 30 по 35 и с 40 по 45 минуту, после того как сервис остановится после 45 минуты после того, как сервис приостановится на 45 минуте - остановим контейнер и посмотрим вывод всех изменений.  
![](https://sun9-34.userapi.com/impg/iR6h8I8Jk4Us4VTqC_N9Ma2t4n--aVgOfKl3Ig/FagygXysOyc.jpg?size=773x864&quality=96&sign=8ee2e2a06a157024dfff7c6e697ae19b&type=album)  