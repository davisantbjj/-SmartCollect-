namespace SmartCollect.Application.Services;

internal static class DispatchWindowTimeZoneResolver
{
    public static bool TryResolve(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;

        if (TryFind(timeZoneId, out timeZone))
            return true;

        if (timeZoneId.Equals("America/Sao_Paulo", StringComparison.OrdinalIgnoreCase))
            return TryFind("E. South America Standard Time", out timeZone);

        if (timeZoneId.Equals("E. South America Standard Time", StringComparison.OrdinalIgnoreCase))
            return TryFind("America/Sao_Paulo", out timeZone);

        return false;
    }

    private static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
