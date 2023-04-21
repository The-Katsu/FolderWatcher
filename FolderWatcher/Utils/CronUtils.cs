using Cronos;

namespace FolderWatcher.Utils;

public static class CronUtils
{
    public static DateTime GetFirstOccurrenceOfTheDay(CronExpression cron) =>
        cron
            .GetOccurrences(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))
            .FirstOrDefault(x => x.Day == DateTime.Today.Day, DateTime.MinValue);
    
    public static DateTime GetLastOccurrenceOfTheDay(CronExpression cron) =>
        cron
            .GetOccurrences(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))
            .LastOrDefault(x => x.Day == DateTime.Today.Day, DateTime.MinValue);
}