using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FolderWatcher;

public sealed class FileWatcherService : IHostedService, IDisposable
{
    private readonly IConfiguration _config;
    private readonly FileWatcherStorage _storage;
    private readonly ConfigurationValidator _validator;
    private FileSystemWatcher? _fileWatcher;
    private readonly string _watchPath;
    private bool _disposed = false;

    public FileWatcherService(
        IConfiguration config, 
        FileWatcherStorage storage, 
        ConfigurationValidator validator)
    {
        _config = config;
        _storage = storage;
        _validator = validator;
        _watchPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            _config.GetValue<string>("Path"));
        _watchPath = Path.GetFullPath(_watchPath);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                if (!_validator.ValidatePath())
                    throw new DirectoryNotFoundException($"Папки '{_watchPath}' не существует");

                _fileWatcher = new FileSystemWatcher(_watchPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };

                _fileWatcher.Created += OnFileCreated;
                _fileWatcher.Changed += OnFileChanged;
                _fileWatcher.Deleted += OnFileDeleted;
                _fileWatcher.Renamed += OnFileRenamed;

                _fileWatcher.EnableRaisingEvents = true;

                Console.WriteLine($"Просмотр папки '{_watchPath}' в ожидании изменений...");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_fileWatcher is not null)
        {
            _fileWatcher.Created -= OnFileCreated;
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Deleted -= OnFileDeleted;
            _fileWatcher.Renamed -= OnFileRenamed;

            _fileWatcher.EnableRaisingEvents = false;

            _fileWatcher.Dispose();
            _storage.WriteAllChanges();
        }

        return Task.CompletedTask;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _fileWatcher?.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"Создан: {e.FullPath}");
        _storage.AddCreated(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"Изменен: {e.FullPath}");
        _storage.AddChanged(e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine($"Удален: {e.FullPath}");
        _storage.AddDeleted(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Console.WriteLine($"Переименован: {e.OldFullPath} to {e.FullPath}");
        _storage.UpdateCreated(e.OldFullPath, e.FullPath);
    }
}