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
        .WithDescription("Calculate the next SemVer version without creating a tag")
        .WithExample(new[] { "next" })
        .WithExample(new[] { "next", "--prerelease", "alpha" })
        .WithExample(new[] { "next", "--prerelease", "beta", "--buildmetadata" });
    config.AddCommand<TagCommand>("tag")
        .WithDescription("Create a git tag with the next SemVer version")
        .WithExample(new[] { "tag" })
        .WithExample(new[] { "tag", "-p", "api-", "--push" });
});

return await app.RunAsync(args);

#region Commit Analysis

public record CommitInfo(
    string Hash,
    string ShortHash,
    string Message,
    string Type,
    string? Scope,
    bool Breaking,
    DateTimeOffset Date
);

public class CommitAnalyzer
{
    private static readonly Regex ConventionalCommitRegex = new(
        @"^(?<type>\w+)(?:\((?<scope>\w+)\))?(?<breaking>!):\s*(?<description>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex BreakingChangeFooterRegex = new(
        @"BREAKING CHANGE:\s*(.+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> FeatureTypes = new() { "feat" };
    private static readonly HashSet<string> PatchTypes = new() { "fix", "perf", "revert" };

    public CommitInfo ParseCommit(string hash, string shortHash, string message, DateTimeOffset date)
    {
        var match = ConventionalCommitRegex.Match(message);

        if (!match.Success)
        {
            return new CommitInfo(hash, shortHash, message, "other", null, false, date);
        }

        var type = match.Groups["type"].Value.ToLowerInvariant();
        var scope = match.Groups["scope"].Success ? match.Groups["scope"].Value : null;
        var breaking = match.Groups["breaking"].Success;

        if (!breaking && BreakingChangeFooterRegex.IsMatch(message))
        {
            breaking = true;
        }

        return new CommitInfo(hash, shortHash, message, type, scope, breaking, date);
    }

    public VersionIncrement DetermineIncrement(IEnumerable<CommitInfo> commits)
    {
        var commitList = commits.ToList();

        if (commitList.Any(c => c.Breaking))
            return VersionIncrement.Major;

        if (commitList.Any(c => FeatureTypes.Contains(c.Type)))
            return VersionIncrement.Minor;

        if (commitList.Any(c => PatchTypes.Contains(c.Type)))
            return VersionIncrement.Patch;

        return VersionIncrement.None;
    }

    public string GetIncrementReason(IEnumerable<CommitInfo> commits, VersionIncrement increment)
    {
        var commitList = commits.ToList();

        return increment switch
        {
            VersionIncrement.Major when commitList.Any(c => c.Breaking)
                => "breaking changes detected",
            VersionIncrement.Minor when commitList.Any(c => FeatureTypes.Contains(c.Type))
                => "feat commits detected",
            VersionIncrement.Patch when commitList.Any(c => PatchTypes.Contains(c.Type))
                => "fix/perf commits detected",
            _ => "no version-relevant commits"
        };
    }
}

public enum VersionIncrement
{
    None,
    Patch,
    Minor,
    Major
}

#endregion

#region Version Calculator

public class SemVerCalculator
{
    private readonly GitService _gitService;
    private readonly SchemaParser _schemaParser;
    private readonly CommitAnalyzer _commitAnalyzer;

    public SemVerCalculator(string? workingDirectory = null)
    {
        _gitService = new GitService { WorkingDirectory = workingDirectory };
        _schemaParser = new SchemaParser();
        _commitAnalyzer = new CommitAnalyzer();
    }

    public async Task<CalculationResult> CalculateNextVersionAsync(
        string? prefix = null,
        string? folder = null,
        string? prereleaseIdentifier = null,
        bool includeBuildMetadata = false)
    {
        const string schema = "{MAJOR}.{MINOR}.{PATCH}";

        var headInfo = await _gitService.GetHeadInfoAsync();
        var latestTag = await _gitService.GetLatestStableTagAsync(prefix);

        if (latestTag == null)
        {
            return CalculateInitialVersion(schema, headInfo, prereleaseIdentifier, includeBuildMetadata);
        }

        var baseVersion = _gitService.ParseVersionFromTag(latestTag, prefix);
        var commits = await _gitService.GetCommitsSinceTagAsync(latestTag, folder);
        var commitInfos = commits.Select(c => _commitAnalyzer.ParseCommit(c.Hash, c.ShortHash, c.Message, c.Date)).ToList();
        var numCommits = await _gitService.CountCommitsSinceTagAsync(latestTag, folder);

        var increment = _commitAnalyzer.DetermineIncrement(commitInfos);

        var newMajor = baseVersion.Major;
        var newMinor = baseVersion.Minor;
        var newPatch = baseVersion.Patch;

        switch (increment)
        {
            case VersionIncrement.Major:
                newMajor++;
                newMinor = 0;
                newPatch = 0;
                break;
            case VersionIncrement.Minor:
                newMinor++;
                newPatch = 0;
                break;
            case VersionIncrement.Patch:
                newPatch++;
                break;
        }

        var newVersion = new VersionInfo(newMajor, newMinor, newPatch, null, null);
        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

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
            IncrementReason: _commitAnalyzer.GetIncrementReason(commitInfos, increment),
            Schema: schema,
            Prerelease: prerelease,
            BuildMetadata: buildMetadata
        );
    }

    private CalculationResult CalculateInitialVersion(
        string schema,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        string? prereleaseIdentifier,
        bool includeBuildMetadata)
    {
        const string initialVersion = "0.1.0";
        var versionInfo = new VersionInfo(0, 1, 0, null, null);
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
}

#endregion

#region Commands

public class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public class Settings : CommandSettings
    {
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
            var calculator = new SemVerCalculator();
            var result = await calculator.CalculateNextVersionAsync(
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
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var calculator = new SemVerCalculator();
            var result = await calculator.CalculateNextVersionAsync(
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
