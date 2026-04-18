#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:package CliWrap@3.10.0
#:package Spectre.Console.Cli@0.53.0
#:project ../shared/ReleaseTools.Shared.csproj

using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReleaseTools.Shared;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<NextCommand>();
app.Configure(config =>
{
    config.AddExample(new[] { "next", "-d", "YYYYMMDD", "-m", "2" });
    config.AddExample(new[] { "next", "-m", "1", "--prerelease", "alpha" });
    config.AddExample(new[] { "next", "-m", "1", "-b" });
});

return await app.RunAsync(args);

#region Calculator

public class ScalVerCalculator
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
        bool includeBuildMetadata = false)
    {
        var schema = ParseDateFormatToSchema(dateFormat);
        var granularity = SchemaParser.GetGranularityFromScalVerFormat(dateFormat);

        var headInfo = await _gitService.GetHeadInfoAsync();
        var patch = await _gitService.CountCommitsOnDateAsync(headInfo.Date, granularity, folder);

        var versionInfo = new VersionInfo(major, 0, patch, null, null);
        var versionString = _schemaParser.ApplyVersion(schema, versionInfo, headInfo.Date, patch, headInfo.ShortHash, headInfo.Hash);

        var metadataService = new MetadataService();
        var prerelease = metadataService.CalculatePrerelease(prereleaseIdentifier);
        var buildMetadata = includeBuildMetadata ? headInfo.ShortHash : null;
        var fullVersion = metadataService.FormatFullVersion(versionString, prerelease, buildMetadata);

        return new CalculationResult(
            Version: versionString,
            FullVersion: fullVersion,
            BaseTag: null,
            BaseVersion: null,
            CommitsSinceTag: patch,
            IncrementReason: $"{patch} commit(s) on {granularity.ToString().ToLower()} window",
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
}

#endregion

#region Command

public class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-m|--major")]
        [Description("Major version number (required)")]
        public int? Major { get; init; }

        [CommandOption("-d|--date-format")]
        [Description("Date format for ScalVer: YYYY, YYYYMM, or YYYYMMDD")]
        [DefaultValue("YYYYMM")]
        public string DateFormat { get; init; } = "YYYYMM";

        [CommandOption("--folder")]
        [Description("Filter commits to a specific folder path")]
        public string? Folder { get; init; }

        [CommandOption("-p|--prerelease")]
        [Description("Prerelease identifier (e.g., alpha, beta, rc)")]
        public string? Prerelease { get; init; }

        [CommandOption("-b|--buildmetadata")]
        [Description("Include build metadata (short SHA) in the version")]
        [DefaultValue(false)]
        public bool BuildMetadata { get; init; }

        [CommandOption("-o|--output")]
        [Description("Output format: text or json")]
        [DefaultValue("text")]
        public string Output { get; init; } = "text";

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

            if (!string.IsNullOrEmpty(Prerelease))
            {
                if (!Regex.IsMatch(Prerelease, @"^[a-zA-Z0-9]+$"))
                {
                    return ValidationResult.Error("Prerelease must be an alphanumeric identifier (e.g., 'alpha', 'beta', 'rc1')");
                }
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
                settings.BuildMetadata);

            if (settings.Output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    result.Version,
                    result.FullVersion,
                    DateFormat = settings.DateFormat,
                    Major = settings.Major,
                    result.CommitsSinceTag,
                    result.IncrementReason,
                    result.Schema,
                    result.Prerelease,
                    result.BuildMetadata
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.Write(json);
            }
            else
            {
                AnsiConsole.Write(result.FullVersion);
            }
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}

#endregion