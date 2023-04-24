using Cronos;

namespace FolderWatcher.Utils;

public static class CronUtils
{
    /// <summary>
    /// Ищем предществующую следующему запуску дату,
    /// если между ней и следующей датой интервал меньше или равен 1 минуте,
    /// то мы находимся в интервале.
    /// </summary>
    /// <param name="cron">выражение</param>
    /// <param name="next">следующая дата запуска</param>
    /// <returns></returns>
    public static bool CheckInterval(CronExpression cron, DateTime next)
    {
        var prev = cron
            .GetOccurrences(DateTime.UtcNow.AddHours(-1), next.AddMicroseconds(-1))
            .LastOrDefault(DateTime.MinValue);
        return next - prev <= TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Вычисляем разницу в часовых поясах, т.к. Cronos принимает только значения по Utc, а cron выражения в
    /// конфигурации пишутся в соответствии с локальным временем - разницу придётся добавить.
    /// </summary>
    /// <param name="cron">Cron выражения, полученное с помощью Cronos</param>
    /// <returns>Следующее время запуска</returns>
    public static DateTime GetNextOccurrence(CronExpression cron)
    {
        var utcOffset = DateTime.Now - DateTime.UtcNow;
        return cron.GetNextOccurrence(DateTime.UtcNow.AddTicks(utcOffset.Ticks))!.Value;
    }
}