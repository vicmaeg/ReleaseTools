#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:property PackageId=ReleaseTools.ScalVer
#:property ToolCommandName=scalver
#:property Description=Calculate scalable calendar versions from Git commit dates.
#:package CliWrap@3.10.0
#:package Spectre.Console.Cli@0.53.0
#:include shared/GitService.cs
#:include shared/SchemaParser.cs
#:include shared/MetadataService.cs
#:include shared/VersionInfo.cs
#:include shared/CalculationResult.cs
#:include shared/DateGranularity.cs
#:include shared/OutputFormat.cs

using System.ComponentModel;
using System.Text.Json;
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
    config.AddExample(["-m", "1"]);
    config.AddExample(["-m", "2", "-d", "YYYYMMDD"]);
    config.AddExample(["-m", "1", "--prerelease", "alpha", "-b"]);
});

return await app.RunAsync(args);

public sealed class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-m|--major <NUMBER>")]
        [Description("Major version number (required, bump manually for breaking changes)")]
        public int? Major { get; init; }

        [CommandOption("-d|--date-format <FORMAT>")]
        [Description("Date format for ScalVer: YYYY, YYYYMM, or YYYYMMDD")]
        [DefaultValue("YYYYMM")]
        public string DateFormat { get; init; } = "YYYYMM";

        [CommandOption("--folder <PATH>")]
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
            var validFormats = new[] { "YYYY", "YYYYMM", "YYYYMMDD" };
            if (!validFormats.Contains(DateFormat.ToUpperInvariant().Trim()))
            {
                return ValidationResult.Error("Invalid date format. Valid formats: YYYY, YYYYMM, YYYYMMDD");
            }

            if (Major is null or < 0)
            {
                return ValidationResult.Error("Major version must be a non-negative integer");
            }

            if (!string.IsNullOrEmpty(Prerelease) && !Regex.IsMatch(Prerelease, @"^[0-9A-Za-z-]+$"))
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
            var calculator = new ScalVerCalculator();
            var result = await calculator.CalculateNextVersionAsync(
                settings.Major!.Value,
                settings.DateFormat,
                settings.Folder,
                settings.Prerelease,
                settings.BuildMetadata,
                cancellationToken);

            if (settings.Output == OutputFormat.Json)
            {
                var json = JsonSerializer.Serialize(new
                {
                    result.Version,
                    result.FullVersion,
                    DateFormat = settings.DateFormat,
                    Major = settings.Major,
                    result.CommitCount,
                    result.IncrementReason,
                    result.Schema,
                    result.Prerelease,
                    result.BuildMetadata
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.Write(json);
            }
            else
            {
                Console.Write(result.FullVersion);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

file sealed class ScalVerCalculator
{
    private readonly GitService _gitService;
    private readonly SchemaParser _schemaParser;

    public ScalVerCalculator(string? workingDirectory = null)
    {
        _gitService = new GitService { WorkingDirectory = workingDirectory };
        _schemaParser = new SchemaParser();
    }

    public async Task<CalculationResult> CalculateNextVersionAsync(
        int major,
        string dateFormat,
        string? folder = null,
        string? prereleaseIdentifier = null,
        bool includeBuildMetadata = false,
        CancellationToken cancellationToken = default)
    {
        var schema = ParseDateFormatToSchema(dateFormat);
        var granularity = GetGranularity(dateFormat);

        var normalizedFolder = await _gitService.ValidateFolderAsync(folder, cancellationToken);
        var headInfo = await _gitService.GetHeadInfoAsync(normalizedFolder, cancellationToken);
        var patch = await _gitService.CountCommitsOnDateAsync(
            headInfo.Date,
            granularity,
            normalizedFolder,
            cancellationToken);

        var versionInfo = new VersionInfo(major, 0, patch);
        var versionString = _schemaParser.ApplyVersion(schema, versionInfo, headInfo.Date);

        var prerelease = prereleaseIdentifier;
        var buildMetadata = includeBuildMetadata ? headInfo.ShortHash : null;
        var fullVersion = MetadataService.FormatFullVersion(versionString, prerelease, buildMetadata);

        return new CalculationResult(
            Version: versionString,
            FullVersion: fullVersion,
            BaseTag: null,
            CommitCount: patch,
            IncrementReason: $"{patch} commit(s) in {granularity.ToString().ToLowerInvariant()} window",
            Schema: schema,
            Prerelease: prerelease,
            BuildMetadata: buildMetadata
        );
    }

    private static string ParseDateFormatToSchema(string dateFormat)
    {
        if (string.IsNullOrWhiteSpace(dateFormat))
            throw new ArgumentException("Date format cannot be empty", nameof(dateFormat));

        var normalized = dateFormat.ToUpperInvariant().Trim();

        var datePart = normalized switch
        {
            "YYYY" => "{YYYY}",
            "YYYYMM" => "{YYYY}{0M}",
            "YYYYMMDD" => "{YYYY}{0M}{0D}",
            _ => throw new ArgumentException($"Invalid date format '{dateFormat}'. Valid formats: YYYY, YYYYMM, YYYYMMDD")
        };

        return $"{{MAJOR}}.{datePart}.{{PATCH}}";
    }

    private static DateGranularity GetGranularity(string dateFormat)
    {
        return dateFormat.ToUpperInvariant().Trim() switch
        {
            "YYYYMMDD" => DateGranularity.Day,
            "YYYYMM" => DateGranularity.Month,
            "YYYY" => DateGranularity.Year,
            _ => DateGranularity.Month
        };
    }
}
