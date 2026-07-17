using System.Globalization;
using System.Text.RegularExpressions;

namespace HR.Modules.Platform.Services.SemanticCatalog;

/// <summary>Pure resolver for relative-date tokens used in metric filters. Returns a UTC date floor.</summary>
public static partial class RelativeDate
{
    public static DateTime Resolve(string token, DateTime nowUtc)
    {
        var today = nowUtc.Date;
        switch (token)
        {
            case "today": return today;
            case "startOfMonth": return new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            case "endOfMonth":
                return new DateTime(today.Year, today.Month,
                    DateTime.DaysInMonth(today.Year, today.Month), 0, 0, 0, DateTimeKind.Utc);
        }
        var m = OffsetRegex().Match(token);
        if (m.Success)
        {
            var days = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return m.Groups[1].Value == "+" ? today.AddDays(days) : today.AddDays(-days);
        }
        throw new FormatException($"Unknown relative-date token '{token}'.");
    }

    [GeneratedRegex(@"^today([+-])(\d+)d$")]
    private static partial Regex OffsetRegex();
}
