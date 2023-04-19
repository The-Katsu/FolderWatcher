## Необходимо реализовать background сервис, который будет следить за папкой на диске и регистрировать все изменения, производимые в этой папке.  

---

## Требования
- [Поддержка windows и linux-style путей](#поддержка-windows-и-linux-style-путей)
- [Приложение должно корректно обрабатывать ошибки, возникающие в процессе работы (некорректная конфигурация,отсутствие указанной папки и т.д.), выводя понятные сообщения пользователю](#обработка-ошибок)
- [Информация о пути до папки должна считываться из конфигурационного файла формата json](#json-конфигурация)
- [По завершению работы программы, должен быть выведен список созданных, обновленных и удаленных файлов](#список-изменений-по-завершению-работы)
---

## Поддержка windows и linux-style путей

```csharp
string _watchPath = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory, 
    _config.GetValue<string>("Path")));

_watchPath = Path.GetFullPath(_watchPath);
```

Сначала используем метод Path.Combine() для объединения относительного пути из конфигурационного файла с базовой директорией приложения, которую мы можем получить с помощью свойства AppDomain.CurrentDomain.BaseDirectory.  
Затем мы используем метод Path.GetFullPath() для получения абсолютного пути до папки, учитывая возможные различия в форматах путей между операционными системами.


## Обработка ошибок

Т.к. фильтры и middleware доступны только для MVC-pipeline, для обработки ошибок в IHostedService можно использовать классический try-cath для локальной обработки ошибок и AppDomain.CurrentDomain.UnhandledException для внешних.  

Для обработки локальный ошибок метод StartAsync следует обернуть в try-cath блок.

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
                // обработка ошибки
            }
        }, cancellationToken);
    }
```  

Для обработки ошибок извне подходит событие AppDomain.CurrentDomain.UnhandledException.

```csharp
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
        Console.WriteLine("Unhandled Exception:");
        Console.WriteLine(ex.Message);
        Console.WriteLine(ex.StackTrace);
        
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

Для хранения информации об изменениях в папке создадим класс FileWatcherStorage  

```csharp
public sealed class FileWatcherStorage
{
    private List<string> _created;
    private List<string> _changed;
    private List<string> _deleted;

    public FileWatcherStorage()
    {
        _created = new List<string>();
        _changed = new List<string>();
        _deleted = new List<string>();
    }

    public void AddCreated(string filePath) => _created.Add(filePath);
    public void AddChanged(string filePath) => _changed.Add(filePath);
    public void AddDeleted(string filePath) => _deleted.Add(filePath);
    public void UpdateCreated(string oldPath, string newPath)
    {
        _created.Remove(oldPath);
        _created.Add(newPath);
    }

    public void WriteAllChanges()
    {
        Console.WriteLine($"Добавленные файлы:\n{string.Join('\n', _created.Distinct())}");
        Console.WriteLine($"Обновленные файлы:\n{string.Join('\n', _changed.Distinct())}");
        Console.WriteLine($"Удаленные файлы:\n{string.Join('\n', _deleted.Distinct())}");
    }
}
```  

Пример использования:

```csharp
private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"Created: {e.FullPath}");
        _storage.AddCreated(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"Changed: {e.FullPath}");
        _storage.AddChanged(e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"Deleted: {e.FullPath}");
        _storage.AddDeleted(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Console.WriteLine($"Renamed: {e.OldFullPath} to {e.FullPath}");
        _storage.UpdateCreated(e.OldFullPath, e.FullPath);
    }
```