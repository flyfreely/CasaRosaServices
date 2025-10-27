using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmailChecker
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;

    public static class AirbnbDateParser
    {
        // Parse a date range like:
        // "Reservation for Oceanview..., Oct 26 – Nov 2"
        // "… October 26–November 2, 2025"
        // Returns true if parsed; false otherwise.
        public static bool TryParseDateRange(
            string input,
            out DateTime checkIn,
            out DateTime checkOut,
            DateTime? referenceDate = null)
        {
            checkIn = default;
            checkOut = default;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            // 1) Normalize: convert various unicode spaces and dashes to ASCII
            string normalized = NormalizeForParsing(input);

            // 2) Regex to capture month/day[/year] – month?/day[/year]
            // - Accepts short or long month names (Jan/January, Sep/Sept/September, etc.)
            // - Year may appear on either or both sides, with or without comma
            // - Range separator can be -, –, —, etc. (already normalized to '-')
            var monthPattern = @"Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";
            var pattern = $@"\b(?<sm>{monthPattern})\s+(?<sd>\d{{1,2}})(?:,?\s*(?<sy>\d{{4}}))?\s*-\s*(?:(?<em>{monthPattern})\s+)?(?<ed>\d{{1,2}})(?:,?\s*(?<ey>\d{{4}}))?";
            var rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var m = rx.Match(normalized);
            if (!m.Success)
                return false;

            // 3) Extract components
            string sm = m.Groups["sm"].Value;
            string em = m.Groups["em"].Success ? m.Groups["em"].Value : sm; // end month defaults to start month if omitted

            int sd = int.Parse(m.Groups["sd"].Value, CultureInfo.InvariantCulture);
            int ed = int.Parse(m.Groups["ed"].Value, CultureInfo.InvariantCulture);

            int? sy = m.Groups["sy"].Success ? int.Parse(m.Groups["sy"].Value, CultureInfo.InvariantCulture) : (int?)null;
            int? ey = m.Groups["ey"].Success ? int.Parse(m.Groups["ey"].Value, CultureInfo.InvariantCulture) : (int?)null;

            int smNum = MonthToNumber(sm);
            int emNum = MonthToNumber(em);

            var refDate = referenceDate ?? DateTime.Today;

            // 4) Infer years if missing
            int yearStart = sy ?? refDate.Year;
            int yearEnd;

            if (ey.HasValue)
            {
                yearEnd = ey.Value;
            }
            else
            {
                // If end month/day appears to be earlier than start month/day, assume rollover to next year
                bool crossesYear =
                    (emNum < smNum) || (emNum == smNum && ed < sd);

                yearEnd = crossesYear ? yearStart + 1 : yearStart;
            }

            // 5) Build DateTime (use invariant culture to be explicit)
            try
            {
                checkIn = new DateTime(yearStart, smNum, sd);
                checkOut = new DateTime(yearEnd, emNum, ed);
                return true;
            }
            catch
            {
                // If invalid day for month, etc.
                checkIn = default;
                checkOut = default;
                return false;
            }
        }

        private static int MonthToNumber(string month)
        {
            // Normalize to title case short form via DateTime.ParseExact
            // Try "MMMM" first, then "MMM"
            if (DateTime.TryParseExact(month, "MMMM", CultureInfo.GetCultureInfo("en-US"),
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var full))
            {
                return full.Month;
            }

            // Handle "Sept" explicitly since ParseExact("MMM") doesn't accept it
            if (month.Equals("Sept", StringComparison.OrdinalIgnoreCase))
                return 9;

            if (DateTime.TryParseExact(month, "MMM", CultureInfo.GetCultureInfo("en-US"),
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var abbr))
            {
                return abbr.Month;
            }

            throw new ArgumentException($"Unrecognized month: {month}");
        }

        private static string NormalizeForParsing(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            // Replace various dashes with simple hyphen
            s = s
                .Replace('\u2010', '-') // hyphen
                .Replace('\u2011', '-') // non-breaking hyphen
                .Replace('\u2012', '-') // figure dash
                .Replace('\u2013', '-') // en dash
                .Replace('\u2014', '-') // em dash
                .Replace('\u2015', '-') // horizontal bar
                .Replace('\u2212', '-'); // minus

            // Replace common non-breaking/thin spaces with normal space
            s = s
                .Replace('\u00A0', ' ') // nbsp
                .Replace('\u2007', ' ') // figure space
                .Replace('\u202F', ' ') // narrow no-break space
                .Replace('\u2009', ' ') // thin space
                .Replace('\u200A', ' ') // hair space
                .Replace('\u2008', ' '); // punctuation space

            // Collapse multiple spaces
            s = Regex.Replace(s, @"\s+", " ");

            return s.Trim();
        }
    }

}
