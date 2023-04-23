using Cronos;

namespace FolderWatcher.Utils;

public static class CronUtils
{
    /// <summary>
    /// Стартовая граница запуска на сегодня
    /// </summary>
    /// <returns>Первое время запуска на сегодня</returns>
    public static DateTime GetFirstOccurrenceOfTheDay(CronExpression cron) =>
        cron
            .GetOccurrences(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))
            .FirstOrDefault(x => x.Day == DateTime.Today.Day, DateTime.MinValue);
    
    /// <summary>
    /// Крайняя граница запуска на сегодня.
    /// </summary>
    /// <returns>Последнее время запуска на сегодня</returns>
    public static DateTime GetLastOccurrenceOfTheDay(CronExpression cron) =>
        cron
            .GetOccurrences(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))
            .LastOrDefault(x => x.Day == DateTime.Today.Day, DateTime.MinValue);
}