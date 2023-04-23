using Cronos;
using FolderWatcher.Configuration;
using FolderWatcher.Utils;

namespace FolderWatcher.Validation;

public sealed class FileWatcherValidator
{
    private List<Exception> _exceptions = null!;
    
    /// <summary>
    /// Валидация конфигурации целиком, сбор ошибок.
    /// </summary>
    /// <param name="configuration">Конфигурация сервиса</param>
    /// <returns>Результат валидации (Валидность пути, Валидность cron-выражения, Список ошибок)</returns>
    public FileWatcherValidationResult Validate(FileWatcherConfiguration configuration)
    {
        _exceptions = new List<Exception>();
        return new FileWatcherValidationResult(
            ValidatePath(configuration.Path), ValidateCron(configuration.Cron), _exceptions);
    }

    /// <summary>
    /// Получаем независимый от OS путь, если папка существует - путь валиден,
    /// иначе создаём ошибку DirectoryNotFoundException, путь не валиден.
    /// </summary>
    /// <param name="path">Путь для наблюдателя из конфигурации</param>
    /// <returns>Валидность пути</returns>
    private bool ValidatePath(string path)
    {
        path = IoUtils.GetOsIndependentPath(path);
        
        if (Directory.Exists(path)) return true;
        
        _exceptions.Add(new DirectoryNotFoundException($"Папки '{path}' не существует"));
        return false;
    }

    /// <summary>
    /// Cronos парсить только корректные выражения,
    /// потому если во время парсинга была ошибка - выражение не валидно.
    /// </summary>
    /// <param name="cron">CRON-выражение из конфигурации</param>
    /// <returns>Валидность cron-выражния</returns>
    private bool ValidateCron(string cron)
    {
        try
        {
            CronExpression.Parse(cron);
            return true;
        }
        catch (Exception ex)
        {
            _exceptions.Add(new ArgumentException($"Ошибка в крон выражении '{cron}' - {ex.Message}"));
            return false;
        }
    }
}