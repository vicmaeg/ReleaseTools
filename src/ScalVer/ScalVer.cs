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

var app = new CommandApp();
app.Configure(config =>
{
    config.AddCommand<NextCommand>("next")
        .WithDescription("Calculate the next ScalVer version without creating a tag")
        .WithExample(new[] { "next", "-d", "YYYYMM" })
        .WithExample(new[] { "next", "--prerelease", "alpha" })
        .WithExample(new[] { "next", "--buildmetadata" });
    config.AddCommand<TagCommand>("tag")
        .WithDescription("Create a git tag with the next ScalVer version")
        .WithExample(new[] { "tag", "-d", "YYYYMM" });
});

return await app.RunAsync(args);

#region Format Parser

public static class ScalVerDateFormatParser
{
    private static readonly HashSet<string> ValidFormats = new() { "YYYY", "YYYYMM", "YYYYMMDD" };

    public static string ParseDateFormatToSchema(string dateFormat)
    {
        if (string.IsNullOrWhiteSpace(dateFormat))
            throw new ArgumentException("Date format cannot be empty", nameof(dateFormat));

        var normalizedFormat = dateFormat.ToUpperInvariant().Trim();

        if (!ValidFormats.Contains(normalizedFormat))
        {
            throw new ArgumentException($"Invalid date format '{dateFormat}'. Valid formats: YYYY, YYYYMM, YYYYMMDD");
        }

        var datePart = normalizedFormat switch
        {
            "YYYY" => "{YYYY}",
            "YYYYMM" => "{YYYY}{MM}",
            "YYYYMMDD" => "{YYYY}{MM}{DD}",
            _ => throw new ArgumentException($"Unknown date format: {dateFormat}")
        };

        return $"{{MAJOR}}.{datePart}.{{PATCH}}";
    }

    public static bool ValidateFormat(string dateFormat)
    {
        if (string.IsNullOrWhiteSpace(dateFormat))
            return false;

        var normalizedFormat = dateFormat.ToUpperInvariant().Trim();
        return ValidFormats.Contains(normalizedFormat);
    }
}

#endregion

#region Version Calculator

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
        string dateFormat,
        string? prefix = null,
        string? folder = null,
        string? prereleaseIdentifier = null,
        bool includeBuildMetadata = false)
    {
        var schema = ScalVerDateFormatParser.ParseDateFormatToSchema(dateFormat);

        var headInfo = await _gitService.GetHeadInfoAsync();
        var latestTag = await _gitService.GetLatestStableTagAsync(prefix);

        if (latestTag == null)
        {
            return CalculateInitialVersion(schema, dateFormat, headInfo, prereleaseIdentifier, includeBuildMetadata);
        }

        var baseVersion = _gitService.ParseVersionFromTag(latestTag, prefix);
        var numCommits = await _gitService.CountCommitsSinceTagAsync(latestTag, folder);

        // Check if date would shrink (date resolution decreased)
        var wouldShrink = WouldDateShrink(dateFormat, baseVersion, headInfo.Date);

        // Determine if we should increment major (breaking changes don't apply in ScalVer, just date shrink)
        var shouldIncrementMajor = wouldShrink;

        var newMajor = shouldIncrementMajor ? baseVersion.Major + 1 : baseVersion.Major;
        var newPatch = shouldIncrementMajor ? 0 : baseVersion.Patch + 1;

        var newVersion = new VersionInfo(newMajor, 0, newPatch, null, null);
        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

        var metadataService = new MetadataService();
        var prerelease = metadataService.CalculatePrerelease(prereleaseIdentifier, numCommits);
        var buildMetadata = includeBuildMetadata ? headInfo.ShortHash : null;
        var fullVersion = metadataService.FormatFullVersion(versionString, prerelease, buildMetadata);

        var incrementReason = shouldIncrementMajor
            ? "date would shrink, incrementing major"
            : "same date window, incrementing patch";

        return new CalculationResult(
            Version: versionString,
            FullVersion: fullVersion,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            IncrementReason: incrementReason,
            Schema: schema,
            Prerelease: prerelease,
            BuildMetadata: buildMetadata
        );
    }

    private CalculationResult CalculateInitialVersion(
        string schema,
        string dateFormat,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        string? prereleaseIdentifier,
        bool includeBuildMetadata)
    {
        var versionInfo = new VersionInfo(0, 0, 0, null, null);
        var versionString = _schemaParser.ApplyVersion(schema, versionInfo, headInfo.Date, 0, headInfo.ShortHash, headInfo.Hash);

        var metadataService = new MetadataService();
        var prerelease = metadataService.CalculatePrerelease(prereleaseIdentifier, 0);
        var buildMetadata = includeBuildMetadata ? headInfo.ShortHash : null;
        var fullVersion = metadataService.FormatFullVersion(versionString, prerelease, buildMetadata);

        return new CalculationResult(
            Version: versionString,
            FullVersion: fullVersion,
            BaseTag: null,
            BaseVersion: null,
            CommitsSinceTag: 0,
            IncrementReason: "initial version",
            Schema: schema,
            Prerelease: prerelease,
            BuildMetadata: buildMetadata
        );
    }

    private bool WouldDateShrink(string dateFormat, VersionInfo baseVersion, DateTimeOffset newDate)
    {
        // In ScalVer, we compare dates based on the precision
        // This is a simplified check - in reality, we'd need to parse the date from the version
        // For now, assume no date shrink since we're using current date
        return false;
    }
}

#endregion

#region Commands

public class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-d|--date-format")]
        [Description("Date format for ScalVer: YYYY, YYYYMM, or YYYYMMDD")]
        [DefaultValue("YYYYMM")]
        public string DateFormat { get; init; } = "YYYYMM";

        [CommandOption("-p|--prefix")]
        [Description("Tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("-f|--folder")]
        [Description("Filter commits to a specific folder path")]
        public string? Folder { get; init; }

        [CommandOption("--prerelease")]
        [Description("Prerelease identifier (e.g., alpha, beta, rc). Will be formatted as {identifier}.{commits}")]
        public string? Prerelease { get; init; }

        [CommandOption("--buildmetadata")]
        [Description("Include build metadata (short SHA) in the version")]
        [DefaultValue(false)]
        public bool BuildMetadata { get; init; }

        [CommandOption("-o|--output")]
        [Description("Output format: text or json")]
        [DefaultValue("text")]
        public string Output { get; init; } = "text";

        public override ValidationResult Validate()
        {
            if (!ScalVerDateFormatParser.ValidateFormat(DateFormat))
            {
                return ValidationResult.Error("Invalid date format. Valid formats: YYYY, YYYYMM, YYYYMMDD");
            }

            if (!string.IsNullOrEmpty(Prerelease))
            {
                if (!Regex.IsMatch(Prerelease, @"^[a-zA-Z]+$"))
                {
                    return ValidationResult.Error("Prerelease must be a single alphabetic identifier (e.g., 'alpha', 'beta', 'rc')");
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
                settings.DateFormat,
                settings.Prefix,
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
                    result.BaseTag,
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
        catch (SchemaMismatchException ex)
        {
            AnsiConsole.MarkupLine("[red]Error: Schema mismatch[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}

public class TagCommand : AsyncCommand<TagCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-d|--date-format")]
        [Description("Date format for ScalVer: YYYY, YYYYMM, or YYYYMMDD")]
        [DefaultValue("YYYYMM")]
        public string DateFormat { get; init; } = "YYYYMM";

        [CommandOption("-p|--prefix")]
        [Description("Tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("-f|--folder")]
        [Description("Filter commits to a specific folder path")]
        public string? Folder { get; init; }

        [CommandOption("-m|--message")]
        [Description("Tag message")]
        public string? Message { get; init; }

        [CommandOption("-a|--annotate")]
        [Description("Create an annotated tag")]
        [DefaultValue(false)]
        public bool Annotated { get; init; }

        [CommandOption("--push")]
        [Description("Push tag to origin after creation")]
        [DefaultValue(false)]
        public bool Push { get; init; }

        [CommandOption("-o|--output")]
        [Description("Output format: text or json")]
        [DefaultValue("text")]
        public string Output { get; init; } = "text";

        public override ValidationResult Validate()
        {
            if (!ScalVerDateFormatParser.ValidateFormat(DateFormat))
            {
                return ValidationResult.Error("Invalid date format. Valid formats: YYYY, YYYYMM, YYYYMMDD");
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
                settings.DateFormat,
                settings.Prefix,
                settings.Folder,
                null,  // No prerelease for tags
                false); // No buildmetadata for tags

            var gitService = new GitService();
            var tagName = settings.Prefix != null ? $"{settings.Prefix}{result.Version}" : result.Version;

            await gitService.CreateTagAsync(tagName, settings.Message, settings.Annotated);

            if (settings.Push)
            {
                await gitService.PushTagAsync(tagName);
            }

            if (settings.Output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    Version = result.Version,
                    TagName = tagName,
                    Annotated = settings.Annotated,
                    Pushed = settings.Push,
                    result.Schema
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.Write(json);
            }
            else
            {
                AnsiConsole.WriteLine($"Created tag: {tagName}");
                if (settings.Push)
                {
                    AnsiConsole.WriteLine("Pushed to origin");
                }
            }
            return 0;
        }
        catch (SchemaMismatchException ex)
        {
            AnsiConsole.MarkupLine("[red]Error: Schema mismatch[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}

#endregion
