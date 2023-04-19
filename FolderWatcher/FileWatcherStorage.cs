namespace FolderWatcher;

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