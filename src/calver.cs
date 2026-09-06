#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:property ManagePackageVersionsCentrally=true
#:property PackageId=ReleaseTools.CalVer
#:property AssemblyVersion=1.0.0.0
#:property FileVersion=1.0.0.0
#:property ToolCommandName=calver
#:property Description=Calculate calendar versions from Git commit dates.
#:package CliWrap
#:package Spectre.Console.Cli
#:include shared/GitService.cs
#:include shared/SchemaParser.cs
#:include shared/MetadataService.cs
#:include shared/VersionInfo.cs
#:include shared/CalculationResult.cs
#:include shared/DateGranularity.cs
#:include shared/OutputFormat.cs
#:include shared/OutputWriter.cs

using System.ComponentModel;
using System.Text.RegularExpressions;
using ReleaseTools.Shared;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<NextCommand>();
app.Configure(config =>
{
    config.ConfigureConsole(AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error)
    }));
    config.AddExample([]);
    config.AddExample(["--format", "YY.0M0D.PATCH"]);
    config.AddExample(["--format", "YYYY.0M", "-p", "rc"]);
    config.AddExample(["-b"]);
});

return await app.RunAsync(args);

public sealed class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--format <FORMAT>")]
        [Description("CalVer format using tokens: YYYY, YY, 0Y, MM, 0M, WW, 0W, DD, 0D, PATCH")]
        [DefaultValue("YYYY.MM.PATCH")]
        public string Format { get; init; } = "YYYY.MM.PATCH";

        [CommandOption("-f|--folder <PATH>")]
        [Description("Repository-relative folder whose commits determine the version")]
        public string? Folder { get; init; }

        [CommandOption("-p|--prerelease <ID>")]
        [Description("Prerelease identifier (e.g., alpha, beta, rc)")]
        public string? Prerelease { get; init; }

        [CommandOption("-b|--buildmetadata")]
        [Description("Include build metadata (short SHA) in the version")]
        [DefaultValue(false)]
        public bool BuildMetadata { get; init; }

        [CommandOption("-o|--output <FORMAT>")]
        [Description("Output format: text or json")]
        [DefaultValue(OutputFormat.Text)]
        public OutputFormat Output { get; init; } = OutputFormat.Text;

        public override ValidationResult Validate()
        {
            var formatResult = CalVerFormatValidator.Validate(Format);
            if (!formatResult.Successful)
                return formatResult;

            if (!string.IsNullOrEmpty(Prerelease) &&
                !Regex.IsMatch(Prerelease, @"^[0-9A-Za-z-]+$"))
            {
                return ValidationResult.Error(
                    "Prerelease must be a single alphanumeric or hyphenated label (for example: alpha, beta, rc-1)");
            }

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var calculator = new CalVerCalculator();
            var result = await calculator.CalculateNextVersionAsync(
                settings.Format,
                settings.Folder,
                settings.Prerelease,
                settings.BuildMetadata,
                cancellationToken);

            OutputWriter.Write(result, settings.Output);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

file static class CalVerFormatValidator
{
    private static readonly string[] TokensByLength = ["YYYY", "PATCH", "0M", "0D", "0W", "0Y", "MM", "DD", "WW", "YY"];

    private static readonly HashSet<string> YearTokens = ["YYYY", "YY", "0Y"];
    private static readonly HashSet<string> MonthTokens = ["MM", "0M"];
    private static readonly HashSet<string> WeekTokens = ["WW", "0W"];
    private static readonly HashSet<string> DayTokens = ["DD", "0D"];

    public static List<string> ParseTokens(string format)
    {
        var tokens = new List<string>();
        var pos = 0;

        while (pos < format.Length)
        {
            if (format[pos] == '.')
            {
                pos++;
                continue;
            }

            var matched = false;
            foreach (var token in TokensByLength)
            {
                if (pos + token.Length <= format.Length &&
                    format.Substring(pos, token.Length) == token)
                {
                    tokens.Add(token);
                    pos += token.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
                throw new ArgumentException($"Invalid token near position {pos} in format '{format}'");
        }

        return tokens;
    }

    public static (List<string> tokens, List<int> segmentBreaks) ParseSegments(string format)
    {
        var tokens = new List<string>();
        var segmentBreaks = new List<int>();
        var pos = 0;

        while (pos < format.Length)
        {
            if (format[pos] == '.')
            {
                segmentBreaks.Add(tokens.Count);
                pos++;
                continue;
            }

            var matched = false;
            foreach (var token in TokensByLength)
            {
                if (pos + token.Length <= format.Length &&
                    format.Substring(pos, token.Length) == token)
                {
                    tokens.Add(token);
                    pos += token.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
                throw new ArgumentException($"Invalid token near position {pos} in format '{format}'");
        }

        return (tokens, segmentBreaks);
    }

    public static string BuildSchema(string format)
    {
        var (tokens, segmentBreaks) = ParseSegments(format);
        var sb = new System.Text.StringBuilder();
        var segIdx = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (segIdx < segmentBreaks.Count && i == segmentBreaks[segIdx])
            {
                sb.Append('.');
                segIdx++;
            }

            sb.Append('{').Append(tokens[i]).Append('}');
        }

        return sb.ToString();
    }

    public static ValidationResult Validate(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return ValidationResult.Error("Format cannot be empty");

        if (format.StartsWith('.') || format.EndsWith('.') || format.Contains("..", StringComparison.Ordinal))
            return ValidationResult.Error("Format cannot contain empty dot-separated segments");

        List<string> tokens;
        try
        {
            tokens = ParseTokens(format);
        }
        catch (ArgumentException ex)
        {
            return ValidationResult.Error(ex.Message);
        }

        if (tokens.Count == 0)
            return ValidationResult.Error("Format must contain at least one token");

        if (tokens.Count(token => token == "PATCH") > 1)
            return ValidationResult.Error("Format can contain at most one PATCH token");

        var hasYear = tokens.Any(t => YearTokens.Contains(t));
        var hasMonth = tokens.Any(t => MonthTokens.Contains(t));
        var hasWeek = tokens.Any(t => WeekTokens.Contains(t));
        var hasDay = tokens.Any(t => DayTokens.Contains(t));

        if (!hasYear)
            return ValidationResult.Error("Format must include a year token (YYYY, YY, or 0Y)");

        if (hasMonth && hasWeek)
            return ValidationResult.Error("Month and Week tokens are mutually exclusive. Use either month (MM, 0M) or week (WW, 0W), not both.");

        if (hasDay && !hasMonth)
            return ValidationResult.Error("Day tokens (DD, 0D) require a month token (MM or 0M). Day without month is ambiguous.");

        var categories = tokens.Select(t =>
            YearTokens.Contains(t) ? 0 :
            MonthTokens.Contains(t) ? 1 :
            WeekTokens.Contains(t) ? 2 :
            DayTokens.Contains(t) ? 3 :
            t == "PATCH" ? 4 : -1).ToList();

        for (var i = 1; i < categories.Count; i++)
        {
            if (categories[i] < categories[i - 1])
                return ValidationResult.Error("Tokens must be in order: Year → Month/Week → Day → PATCH");
        }

        foreach (var token in tokens)
        {
            var group = YearTokens.Contains(token) ? "year" :
                        MonthTokens.Contains(token) ? "month" :
                        WeekTokens.Contains(token) ? "week" :
                        DayTokens.Contains(token) ? "day" : null;

            if (group != null)
            {
                var count = tokens.Count(t =>
                    (group == "year" && YearTokens.Contains(t)) ||
                    (group == "month" && MonthTokens.Contains(t)) ||
                    (group == "week" && WeekTokens.Contains(t)) ||
                    (group == "day" && DayTokens.Contains(t)));

                if (count > 1)
                    return ValidationResult.Error($"Duplicate {group} token detected. Use only one {group} token (e.g., {(group == "year" ? "YYYY" : group == "month" ? "0M" : group == "week" ? "0W" : "0D")})");
            }
        }

        return ValidationResult.Success();
    }

    public static DateGranularity GetGranularity(string format)
    {
        var tokens = ParseTokens(format);
        if (tokens.Any(t => DayTokens.Contains(t))) return DateGranularity.Day;
        if (tokens.Any(t => WeekTokens.Contains(t))) return DateGranularity.Week;
        if (tokens.Any(t => MonthTokens.Contains(t))) return DateGranularity.Month;
        return DateGranularity.Year;
    }

    public static bool ContainsPatch(string format)
    {
        return ParseTokens(format).Contains("PATCH");
    }
}

file sealed class CalVerCalculator
{
    private readonly GitService _gitService;
    private readonly SchemaParser _schemaParser;

    public CalVerCalculator(string? workingDirectory = null)
    {
        _gitService = new GitService { WorkingDirectory = workingDirectory };
        _schemaParser = new SchemaParser();
    }

    public async Task<CalculationResult> CalculateNextVersionAsync(
        string format,
        string? folder = null,
        string? prereleaseIdentifier = null,
        bool includeBuildMetadata = false,
        CancellationToken cancellationToken = default)
    {
        var schema = CalVerFormatValidator.BuildSchema(format);
        var granularity = CalVerFormatValidator.GetGranularity(format);
        var hasPatch = CalVerFormatValidator.ContainsPatch(format);

        var normalizedFolder = await _gitService.ValidateFolderAsync(folder, cancellationToken);
        var headInfo = await _gitService.GetHeadInfoAsync(normalizedFolder, cancellationToken);
        var patch = hasPatch
            ? await _gitService.CountCommitsOnDateAsync(headInfo.Date, granularity, normalizedFolder, cancellationToken)
            : 0;

        var versionInfo = new VersionInfo(0, 0, patch);
        var versionString = _schemaParser.ApplyVersion(schema, versionInfo, headInfo.Date);

        var prerelease = prereleaseIdentifier;
        var buildMetadata = includeBuildMetadata ? headInfo.ShortHash : null;
        var fullVersion = MetadataService.FormatFullVersion(versionString, prerelease, buildMetadata);

        var incrementReason = hasPatch
            ? $"{patch} commit(s) in {granularity.ToString().ToLowerInvariant()} window"
            : "no patch segment";

        return new CalculationResult(
            Version: versionString,
            FullVersion: fullVersion,
            BaseTag: null,
            CommitCount: patch,
            IncrementReason: incrementReason,
            Schema: schema,
            Prerelease: prerelease,
            BuildMetadata: buildMetadata,
            Format: format
        );
    }
}
