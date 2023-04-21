namespace FolderWatcher.Utils;

public static class IoUtils
{
    public static string GetOsIndependentPath(string path)
    {
        path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        return Path.GetFullPath(path);
    }
}