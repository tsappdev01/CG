namespace CGTOOL.Web.Data.Governance;

/// <summary>When a notification goes out, and when its reminders follow, is stated in UAE Standard
/// Time -- the working day of the office that reads them -- not in whatever time zone the server
/// happens to be set to. UAE Standard Time is UTC+4 and has never observed daylight saving, so the
/// offset is constant and "08:00 Saturday" means the same instant all year.
///
/// Stored "...Utc" values come back from EF Core with DateTimeKind.Unspecified (SQL Server's
/// datetime2 carries no zone), which ConvertTimeFromUtc accepts; it rejects only Kind.Local, so
/// nothing here may be handed a value that has already been converted.</summary>
public static class UaeTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    /// <summary>Now, as a clock on a wall in Dubai reads it.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    public static DateTime FromUtc(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

    /// <summary>The UTC instant of a date and time read as UAE local -- what to store for a send that
    /// the admin asked for at, say, 08:00 on the 3rd.</summary>
    public static DateTime ToUtc(DateTime uaeLocal, TimeOnly timeOfDay) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(uaeLocal.Date.Add(timeOfDay.ToTimeSpan()), DateTimeKind.Unspecified), Zone);

    public static string ToUaeDisplay(this DateTime utc, string format) => FromUtc(utc).ToString(format);

    /// <summary>The zone database is named differently per platform -- IANA on Linux, the Windows
    /// registry ids on Windows -- and .NET only cross-maps them where ICU is available. Try both, and
    /// fall back to the fixed +4 offset rather than letting a missing zone take the app down: the
    /// offset is the whole of what UAE Standard Time is.</summary>
    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Dubai", "Arabian Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("UAE Standard Time", TimeSpan.FromHours(4), "UAE Standard Time", "UAE Standard Time");
    }
}
