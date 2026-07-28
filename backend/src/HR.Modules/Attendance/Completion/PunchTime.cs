using System.Text.RegularExpressions;

namespace HR.Modules.Attendance.Completion;

/// <summary>Validates the "HH:mm" (24h) punch strings the correction form submits. A blank string is
/// valid and means "leave this punch unchanged". TODO(tz): times are wall-clock, timezone-naïve, matching
/// the rest of the attendance engine — do not convert here until the engine migrates system-wide.</summary>
public static class PunchTime
{
    private static readonly Regex Hhmm = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);
    public static bool HasValue(string? hhmm) => !string.IsNullOrWhiteSpace(hhmm);
    public static bool IsValid(string? hhmm) => !HasValue(hhmm) || Hhmm.IsMatch(hhmm!.Trim());
}
