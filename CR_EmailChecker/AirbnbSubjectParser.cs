using System.Globalization;
using System.Text.RegularExpressions;

namespace EmailChecker;

public static class AirbnbDateParser
{
    /// <summary>
    /// Parses an Airbnb reservation subject line and extracts the check-in / check-out dates.
    /// Handles formats like "Oct 26 – Nov 2", "October 26–November 2, 2025", etc.
    /// Returns true if parsing succeeds; false otherwise.
    /// </summary>
    public static bool TryParseDateRange(
        string    input,
        out DateTime checkIn,
        out DateTime checkOut,
        DateTime? referenceDate = null)
    {
        checkIn = checkOut = default;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        string normalized = Normalize(input);

        const string months  = @"Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";
        string       pattern = $@"\b(?<sm>{months})\s+(?<sd>\d{{1,2}})(?:,?\s*(?<sy>\d{{4}}))?\s*-\s*(?:(?<em>{months})\s+)?(?<ed>\d{{1,2}})(?:,?\s*(?<ey>\d{{4}}))?";
        var          match   = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
            return false;

        string startMonthStr = match.Groups["sm"].Value;
        string endMonthStr   = match.Groups["em"].Success ? match.Groups["em"].Value : startMonthStr;

        int startDay   = int.Parse(match.Groups["sd"].Value, CultureInfo.InvariantCulture);
        int endDay     = int.Parse(match.Groups["ed"].Value, CultureInfo.InvariantCulture);
        int startMonth = MonthToNumber(startMonthStr);
        int endMonth   = MonthToNumber(endMonthStr);

        int? explicitStartYear = match.Groups["sy"].Success ? int.Parse(match.Groups["sy"].Value, CultureInfo.InvariantCulture) : null;
        int? explicitEndYear   = match.Groups["ey"].Success ? int.Parse(match.Groups["ey"].Value, CultureInfo.InvariantCulture) : null;

        var refDate    = referenceDate ?? DateTime.Today;
        int startYear  = explicitStartYear ?? refDate.Year;
        int endYear    = explicitEndYear   ?? InferEndYear(startYear, startMonth, startDay, endMonth, endDay);

        try
        {
            checkIn  = new DateTime(startYear, startMonth, startDay);
            checkOut = new DateTime(endYear,   endMonth,   endDay);
            return true;
        }
        catch
        {
            checkIn = checkOut = default;
            return false;
        }
    }

    // If the end date appears earlier in the calendar than the start date, the stay crosses a year boundary.
    private static int InferEndYear(int startYear, int startMonth, int startDay, int endMonth, int endDay)
    {
        bool crossesYear = endMonth < startMonth || (endMonth == startMonth && endDay < startDay);
        return crossesYear ? startYear + 1 : startYear;
    }

    private static int MonthToNumber(string month)
    {
        if (month.Equals("Sept", StringComparison.OrdinalIgnoreCase))
            return 9;

        var culture = CultureInfo.GetCultureInfo("en-US");
        var styles  = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;

        if (DateTime.TryParseExact(month, "MMMM", culture, styles, out var full))
            return full.Month;

        if (DateTime.TryParseExact(month, "MMM",  culture, styles, out var abbr))
            return abbr.Month;

        throw new ArgumentException($"Unrecognized month: {month}");
    }

    private static string Normalize(string s)
    {
        // Collapse all dash variants to hyphen.
        s = s.Replace('\u2010', '-')   // hyphen
             .Replace('\u2011', '-')   // non-breaking hyphen
             .Replace('\u2012', '-')   // figure dash
             .Replace('\u2013', '-')   // en dash
             .Replace('\u2014', '-')   // em dash
             .Replace('\u2015', '-')   // horizontal bar
             .Replace('\u2212', '-');  // minus sign

        // Collapse all space variants to regular space.
        s = s.Replace('\u00A0', ' ')   // non-breaking space
             .Replace('\u2007', ' ')   // figure space
             .Replace('\u202F', ' ')   // narrow no-break space
             .Replace('\u2009', ' ')   // thin space
             .Replace('\u200A', ' ')   // hair space
             .Replace('\u2008', ' ');  // punctuation space

        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
