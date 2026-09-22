namespace CGTOOL.Web.Data.Governance;

/// <summary>All "...Utc" DateTime properties are stored as UTC values but come back from EF Core (and
/// from DateTime.UtcNow itself) with DateTimeKind.Unspecified -- SQL Server's datetime2 carries no
/// timezone metadata. These treat the raw value as UTC and convert to the server's local time zone for
/// display, matching how DateTime.Now is already used elsewhere in the UI (e.g. print footers).</summary>
public static class DateTimeDisplayExtensions
{
    public static DateTime ToLocalDisplay(this DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    public static string ToLocalDisplay(this DateTime utc, string format) =>
        ToLocalDisplay(utc).ToString(format);
}
