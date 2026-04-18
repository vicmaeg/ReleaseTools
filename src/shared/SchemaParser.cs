using System.Text.RegularExpressions;

namespace ReleaseTools.Shared;

public class SchemaParser
{
    public IEnumerable<string> GetSchemaTokens(string schema)
    {
        var regex = new Regex(@"\{(\w+)\}");
        var matches = regex.Matches(schema);
        foreach (Match match in matches)
        {
            yield return match.Groups[1].Value;
        }
    }

    public string ApplyVersion(
        string schema,
        VersionInfo version,
        DateTimeOffset commitDate,
        int numCommits,
        string shortSha,
        string sha)
    {
        var result = schema;

        result = result.Replace("{MAJOR}", version.Major.ToString());
        result = result.Replace("{MINOR}", version.Minor.ToString());
        result = result.Replace("{PATCH}", version.Patch.ToString());

        result = result.Replace("{YYYY}", commitDate.Year.ToString());
        result = result.Replace("{YY}", (commitDate.Year % 100).ToString());
        result = result.Replace("{0Y}", (commitDate.Year % 100).ToString("D2"));
        result = result.Replace("{MM}", commitDate.Month.ToString());
        result = result.Replace("{0M}", commitDate.Month.ToString("D2"));
        result = result.Replace("{WW}", GetWeekOfYear(commitDate).ToString());
        result = result.Replace("{0W}", GetWeekOfYear(commitDate).ToString("D2"));
        result = result.Replace("{DD}", commitDate.Day.ToString());
        result = result.Replace("{0D}", commitDate.Day.ToString("D2"));

        result = result.Replace("{SHA}", sha);
        result = result.Replace("{SHORTSHA}", shortSha);
        result = result.Replace("{NUM_COMMITS}", numCommits.ToString());

        return result;
    }

    public static DateGranularity GetGranularityFromScalVerFormat(string dateFormat)
    {
        return dateFormat.ToUpperInvariant().Trim() switch
        {
            "YYYYMMDD" => DateGranularity.Day,
            "YYYYMM" => DateGranularity.Month,
            "YYYY" => DateGranularity.Year,
            _ => DateGranularity.Month
        };
    }

    private int GetWeekOfYear(DateTimeOffset date)
    {
        var cal = System.Globalization.CultureInfo.CurrentCulture.Calendar;
        return cal.GetWeekOfYear(date.DateTime, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }
}