namespace CGTOOL.Web.Data.Governance;

/// <summary>Turns a stored UTC instant into the time a reader in Dubai would have seen on a clock.
///
/// All "...Utc" properties are stored as UTC but come back from EF Core -- and from DateTime.UtcNow
/// itself -- with DateTimeKind.Unspecified, because SQL Server's datetime2 carries no timezone. So
/// the kind is asserted before converting; without that, ToLocalTime on an Unspecified value treats
/// it as already local and returns it unchanged in some configurations.
///
/// The target is UAE time explicitly, not the server's local time. Those are the same thing on
/// UATWEB01 and the difference never showed, but they are not the same promise: the first says
/// "this is what the declarant's clock read", which is what every screen here means, and the second
/// says "this is what the machine's clock read", which is only right while the machine happens to
/// be in Dubai. Moving the site to a host anywhere else would have quietly re-dated every
/// submission, reminder and audit line in the application.</summary>
public static class DateTimeDisplayExtensions
{
    public static DateTime ToLocalDisplay(this DateTime utc) =>
        UaeTime.FromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    public static string ToLocalDisplay(this DateTime utc, string format) =>
        ToLocalDisplay(utc).ToString(format);
}
