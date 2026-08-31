namespace ReleaseTools.Shared;

public class SchemaParser
{
    public string ApplyVersion(string schema, VersionInfo version, DateTimeOffset commitDate)
    {
        var result = schema;
        var utc = commitDate.ToUniversalTime();

        result = result.Replace("{MAJOR}", version.Major.ToString());
        result = result.Replace("{MINOR}", version.Minor.ToString());
        result = result.Replace("{PATCH}", version.Patch.ToString());

        result = result.Replace("{YYYY}", utc.Year.ToString());
        result = result.Replace("{YY}", (utc.Year % 100).ToString());
        result = result.Replace("{0Y}", (utc.Year % 100).ToString("D2"));
        result = result.Replace("{MM}", utc.Month.ToString());
        result = result.Replace("{0M}", utc.Month.ToString("D2"));
        result = result.Replace("{WW}", System.Globalization.ISOWeek.GetWeekOfYear(utc.UtcDateTime).ToString());
        result = result.Replace("{0W}", System.Globalization.ISOWeek.GetWeekOfYear(utc.UtcDateTime).ToString("D2"));
        result = result.Replace("{DD}", utc.Day.ToString());
        result = result.Replace("{0D}", utc.Day.ToString("D2"));

        return result;
    }

}
