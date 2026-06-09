namespace SmartCollect.Application.Common;

using System;

public static class TimeUtils
{
    /// <summary>
    /// Returns the current date (00:00:00) in the Brazilian time zone (America/Sao_Paulo).
    /// Safe across Windows and Linux.
    /// </summary>
    public static DateTime GetBrazilToday()
    {
        var brazilNow = GetBrazilNow();
        // .Date strips Kind to Unspecified which Npgsql rejects for "timestamp with time zone".
        // We return the equivalent UTC midnight for the Brazilian date.
        return DateTime.SpecifyKind(brazilNow.Date, DateTimeKind.Utc);
    }

    /// <summary>
    /// Returns the current date and time in the Brazilian time zone (America/Sao_Paulo).
    /// Safe across Windows and Linux.
    /// </summary>
    public static DateTime GetBrazilNow()
    {
        return ConvertToBrazilTime(DateTime.UtcNow);
    }

    public static DateTime ConvertToBrazilTime(DateTime utcDateTime)
    {
        // Ensure Kind is UTC so ConvertTimeFromUtc doesn't throw if it's Unspecified
        if (utcDateTime.Kind == DateTimeKind.Unspecified)
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
            }
            catch
            {
                return utcDateTime.AddHours(-3);
            }
        }
    }

    public static DateTime GetUtcTimeForBrazilMidnight(DateTime date)
    {
        var localMidnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            return TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
                return TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz);
            }
            catch
            {
                return new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(3);
            }
        }
    }
}
