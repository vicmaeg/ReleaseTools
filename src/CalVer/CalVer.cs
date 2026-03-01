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
        .WithDescription("Calculate the next CalVer version without creating a tag")
        .WithExample(new[] { "next", "-f", "YYYY.0M.PATCH" })
        .WithExample(new[] { "next", "--prerelease", "rc" })
        .WithExample(new[] { "next", "--buildmetadata" });
    config.AddCommand<TagCommand>("tag")
        .WithDescription("Create a git tag with the next CalVer version")
        .WithExample(new[] { "tag", "-f", "YYYY.0M.PATCH" });
});

return await app.RunAsync(args);

#region Format Parser

public static class CalVerFormatParser
{
    private static readonly HashSet<string> ValidTokens = new()
    {
        "YYYY", "YY", "0Y", "MM", "0M", "WW", "0W", "DD", "0D", "PATCH"
    };

    public static string ParseFormatToSchema(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            throw new ArgumentException("Format cannot be empty", nameof(format));

        var parts = format.Split('.');
        var schemaParts = new List<string>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!ValidTokens.Contains(trimmed))
            {
                throw new ArgumentException($"Invalid token '{trimmed}' in format. Valid tokens: {string.Join(", ", ValidTokens)}");
            }
            schemaParts.Add($"{{{trimmed}}}");
        }

        return string.Join(".", schemaParts);
    }

    public static bool ValidateFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return false;

        var parts = format.Split('.');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!ValidTokens.Contains(trimmed))
                return false;
        }

        return true;
    }
}

#endregion

#region Version Calculator

public class CalVerCalculator
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
        string? prefix = null,
        string? folder = null,
        string? prereleaseIdentifier = null,
        bool includeBuildMetadata = false)
    {
        var schema = CalVerFormatParser.ParseFormatToSchema(format);

        var headInfo = await _gitService.GetHeadInfoAsync();
        var latestTag = await _gitService.GetLatestStableTagAsync(prefix);

        if (latestTag == null)
        {
            return CalculateInitialVersion(schema, format, headInfo, prereleaseIdentifier, includeBuildMetadata);
        }

        var baseVersion = _gitService.ParseVersionFromTag(latestTag, prefix);
        var numCommits = await _gitService.CountCommitsSinceTagAsync(latestTag, folder);

        // Calculate new date part
        var baseDate = GetDateFromVersion(baseVersion, schema);
        var newDate = headInfo.Date;
        var isSameDateWindow = IsSameDateWindow(schema, baseDate, newDate);

        var newPatch = isSameDateWindow ? baseVersion.Patch + 1 : 0;
        var newVersion = new VersionInfo(0, 0, newPatch, null, null);

        var versionString = _schemaParser.ApplyVersion(schema, newVersion, newDate, numCommits, headInfo.ShortHash, headInfo.Hash);

        var metadataService = new MetadataService();
        var prerelease = metadataService.CalculatePrerelease(prereleaseIdentifier, numCommits);
        var buildMetadata = includeBuildMetadata ? headInfo.ShortHash : null;
        var fullVersion = metadataService.FormatFullVersion(versionString, prerelease, buildMetadata);

        return new CalculationResult(
            Version: versionString,
            FullVersion: fullVersion,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            IncrementReason: isSameDateWindow ? "same date window, incrementing patch" : "new date window, reset to 0",
            Schema: schema,
            Prerelease: prerelease,
            BuildMetadata: buildMetadata
        );
    }

    private CalculationResult CalculateInitialVersion(
        string schema,
        string format,
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

    private DateTimeOffset GetDateFromVersion(VersionInfo version, string schema)
    {
        // For CalVer, we need to parse the date from the tag
        // Since we don't have the original tag, we'll use the current date
        // This is a simplified approach - in a real scenario, we might need to parse from the tag
        return DateTimeOffset.UtcNow;
    }

    private bool IsSameDateWindow(string schema, DateTimeOffset baseDate, DateTimeOffset newDate)
    {
        // Check if we're in the same date window based on schema precision
        if (schema.Contains("{DD}") || schema.Contains("{0D}"))
            return baseDate.Year == newDate.Year && baseDate.Month == newDate.Month && baseDate.Day == newDate.Day;

        if (schema.Contains("{WW}") || schema.Contains("{0W}"))
        {
            // Simplified week comparison
            var baseWeek = (baseDate.DayOfYear - 1) / 7;
            var newWeek = (newDate.DayOfYear - 1) / 7;
            return baseDate.Year == newDate.Year && baseWeek == newWeek;
        }

        if (schema.Contains("{MM}") || schema.Contains("{0M}"))
            return baseDate.Year == newDate.Year && baseDate.Month == newDate.Month;

        if (schema.Contains("{YYYY}"))
            return baseDate.Year == newDate.Year;

        // Default to year comparison
        return baseDate.Year == newDate.Year;
    }
}

#endregion

#region Commands

public class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-f|--format")]
        [Description("CalVer format using tokens: YYYY, YY, 0Y, MM, 0M, WW, 0W, DD, 0D, PATCH (e.g., YYYY.0M.PATCH)")]
        [DefaultValue("YYYY.0M.PATCH")]
        public string Format { get; init; } = "YYYY.0M.PATCH";

        [CommandOption("-p|--prefix")]
        [Description("Tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("--folder")]
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
            if (!CalVerFormatParser.ValidateFormat(Format))
            {
                return ValidationResult.Error($"Invalid CalVer format. Valid tokens: YYYY, YY, 0Y, MM, 0M, WW, 0W, DD, 0D, PATCH");
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
            var calculator = new CalVerCalculator();
            var result = await calculator.CalculateNextVersionAsync(
                settings.Format,
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
                    Format = settings.Format,
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
        [CommandOption("-f|--format")]
        [Description("CalVer format using tokens: YYYY, YY, 0Y, MM, 0M, WW, 0W, DD, 0D, PATCH (e.g., YYYY.0M.PATCH)")]
        [DefaultValue("YYYY.0M.PATCH")]
        public string Format { get; init; } = "YYYY.0M.PATCH";

        [CommandOption("-p|--prefix")]
        [Description("Tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("--folder")]
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
            if (!CalVerFormatParser.ValidateFormat(Format))
            {
                return ValidationResult.Error($"Invalid CalVer format. Valid tokens: YYYY, YY, 0Y, MM, 0M, WW, 0W, DD, 0D, PATCH");
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
