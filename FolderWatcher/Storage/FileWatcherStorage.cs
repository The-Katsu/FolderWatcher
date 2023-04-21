using Microsoft.Extensions.Logging;

namespace FolderWatcher.Storage;

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
        CheckData(); // удалить, если нужен вывод без проверки.
        
        if(_created.Any()) 
            _logger.LogInformation("Добавленные файлы:\n{Arr}", string.Join('\n', _created));
        if(_changed.Any()) 
            _logger.LogInformation("Обновленные файлы:\n{Arr}", string.Join('\n', _changed));
        if(_deleted.Any()) 
            _logger.LogInformation("Удаленные файлы:\n{Arr}", string.Join('\n', _deleted));
        
        // Очищаем списки, после вывода, не использую Clear(),
        // т.к. Capacity остаётся прежней и списки после нескольких итераций записи могут занимать слишком много памяти.
        _created = new HashSet<string>();
        _changed = new HashSet<string>();
        _deleted = new HashSet<string>();
    }

    private void CheckData()
    {
        // проверяем, что созданные файлы существуют.
        _created = _created.Where(Path.Exists).ToHashSet();
        // проверяем, что изменённые файлы существуют.
        _changed = _changed.Where(Path.Exists).ToHashSet();
        // проверяем, что файлы действительн удалены.
        _deleted = _deleted.Where(path => !Path.Exists(path)).ToHashSet();
    }
}