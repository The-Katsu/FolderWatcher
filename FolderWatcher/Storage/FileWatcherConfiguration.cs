namespace FolderWatcher.Storage;

public class FileWatcherConfiguration
{
    public required string Path { get; set; }
    public required string Cron { get; set; }
}