using System.Text.RegularExpressions;

namespace ReleaseTools.Shared;

public class SchemaParser
{
    private static readonly HashSet<string> DateTokens = new()
    {
        "YYYY", "YY", "0Y", "MM", "0M", "WW", "0W", "DD", "0D"
    };

    private static readonly HashSet<string> SemVerTokens = new()
    {
        "MAJOR", "MINOR", "PATCH"
    };

    public VersioningMode DetectMode(string schema)
    {
        bool hasMajor = schema.Contains("{MAJOR}");
        bool hasMinor = schema.Contains("{MINOR}");
        bool hasPatch = schema.Contains("{PATCH}");
        bool hasDateTokens = DateTokens.Any(t => schema.Contains($"{{{t}}}"));

        if (hasMajor && hasDateTokens)
            return VersioningMode.ScalVer;

        if (hasDateTokens && !hasMajor)
            return VersioningMode.CalVer;

        if (hasMajor || hasMinor || hasPatch)
            return VersioningMode.SemVer;

        throw new ArgumentException($"Cannot determine versioning mode from schema: {schema}");
    }

    public IEnumerable<string> GetSchemaTokens(string schema)
    {
        var regex = new Regex(@"\{(\w+)\}");
        var matches = regex.Matches(schema);
        foreach (Match match in matches)
        {
            yield return match.Groups[1].Value;
        }
    }

    public bool ValidateSchema(string schema, VersioningMode mode)
    {
        var tokens = GetSchemaTokens(schema).ToList();

        return mode switch
        {
            VersioningMode.SemVer => tokens.All(t => SemVerTokens.Contains(t) || t == "SHA" || t == "SHORTSHA" || t == "NUM_COMMITS"),
            VersioningMode.CalVer => tokens.All(t => DateTokens.Contains(t) || t == "PATCH" || t == "SHA" || t == "SHORTSHA" || t == "NUM_COMMITS"),
            VersioningMode.ScalVer => tokens.All(t => SemVerTokens.Contains(t) || DateTokens.Contains(t) || t == "SHA" || t == "SHORTSHA" || t == "NUM_COMMITS"),
            _ => false
        };
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

    private int GetWeekOfYear(DateTimeOffset date)
    {
        var cal = System.Globalization.CultureInfo.CurrentCulture.Calendar;
        return cal.GetWeekOfYear(date.DateTime, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
    }
}
