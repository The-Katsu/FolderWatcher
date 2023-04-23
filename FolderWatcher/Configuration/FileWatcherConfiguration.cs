namespace FolderWatcher.Configuration;

public class FileWatcherConfiguration
{
    public string Path { get; set; } = null!;
    public string Cron { get; set; } = null!;
}