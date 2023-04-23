namespace FolderWatcher.Utils;

public static class IoUtils
{
    /// <summary>
    /// Преобразование пути в подходящее для любой OS
    /// </summary>
    /// <param name="path">Путь из конфигурации</param>
    /// <returns>Путь поддерживающий Windows & Linux </returns>
    public static string GetOsIndependentPath(string path)
    {
        path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Получаем путь файла относительно папки просмотра
    /// </summary>
    /// <param name="basePath">Путь из конфигурации</param>
    /// <param name="filePath">Путь к файлу</param>
    /// <returns>Путь файла в папке</returns>
    public static string GetRelativePath(string basePath, string filePath) =>
        Path.GetRelativePath(basePath, filePath);
}