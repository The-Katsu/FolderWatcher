using Microsoft.Extensions.Logging;

namespace FolderWatcher.Helpers;

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
    
    /// <summary>
    /// Удаляем старое название из списка, добавляем новое.
    /// Для поддержания актуальности списка.
    /// </summary>
    /// <param name="oldPath">Путь до переименования</param>
    /// <param name="newPath">Путь после переименования</param>
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

    /// <summary>
    /// Вывод всех изменений текущей сессии.
    /// Очищаем списки после вывода с помощью new(), чтобы обновить Capacity.
    /// </summary>
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