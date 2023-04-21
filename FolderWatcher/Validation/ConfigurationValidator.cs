using Cronos;
using FolderWatcher.Storage;
using FolderWatcher.Utils;

namespace FolderWatcher.Validation;

public sealed class ConfigurationValidator
{
    public ValidationResult Validate(FileWatcherConfiguration configuration) => 
        new(ValidatePath(configuration.Path), ValidateCron(configuration.Cron));

    private bool ValidatePath(string path)
    {
        path = IoUtils.GetOsIndependentPath(path);
        return Directory.Exists(path);
    }

    private bool ValidateCron(string cron)
    {
        try
        {
            CronExpression.Parse(cron);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}