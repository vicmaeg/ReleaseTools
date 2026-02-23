#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:package CliWrap@3.10.0
#:package Spectre.Console.Cli@0.53.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ReleaseTools;

public enum VersioningMode
{
    SemVer,
    CalVer,
    ScalVer
}

public enum VersionIncrement
{
    None,
    Patch,
    Minor,
    Major
}

public record CommitInfo(
    string Hash,
    string ShortHash,
    string Message,
    string Type,
    string? Scope,
    bool Breaking,
    DateTimeOffset Date
);

public record VersionInfo(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease,
    string? BuildMetadata,
    VersioningMode Mode
);

public record CalculationResult(
    string Version,
    string? TagName,
    VersioningMode Mode,
    string? BaseTag,
    VersionInfo? BaseVersion,
    int CommitsSinceTag,
    VersionIncrement Increment,
    string? IncrementReason,
    string Schema
);

public class SchemaMismatchException : Exception
{
    public VersioningMode RequestedMode { get; }
    public VersioningMode ExistingMode { get; }
    public string? ExistingTag { get; }

    public SchemaMismatchException(string message, VersioningMode requestedMode, VersioningMode existingMode, string? existingTag = null)
        : base(message)
    {
        RequestedMode = requestedMode;
        ExistingMode = existingMode;
        ExistingTag = existingTag;
    }
}

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

public class CommitAnalyzer
{
    private static readonly Regex ConventionalCommitRegex = new(
        @"^(?<type>\w+)(?:\((?<scope>\w+)\))?(?<breaking>!)?:\s*(?<description>.+)$",
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

public class GitService
{
    public string? WorkingDirectory { get; set; }

    public async Task<bool> IsGitRepositoryAsync()
    {
        var result = await Cli.Wrap("git")
            .WithArguments("rev-parse --is-inside-work-tree")
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return result.IsSuccess && result.StandardOutput.Trim() == "true";
    }

    public async Task<string?> GetLatestTagAsync(string? prefix = null)
    {
        var args = "describe --tags --abbrev=0";
        if (!string.IsNullOrEmpty(prefix))
        {
            args += $" --match \"{prefix}*\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            return null;

        return result.StandardOutput.Trim();
    }

    public async Task<string?> GetLatestStableTagAsync(string? prefix = null)
    {
        var tag = await GetLatestTagAsync(prefix);
        if (tag == null)
            return null;

        var versionPart = prefix != null && tag.StartsWith(prefix)
            ? tag.Substring(prefix.Length)
            : tag;

        if (IsPreRelease(versionPart))
        {
            var allTags = await GetAllTagsAsync(prefix);
            foreach (var t in allTags)
            {
                var v = prefix != null && t.StartsWith(prefix) ? t.Substring(prefix.Length) : t;
                if (!IsPreRelease(v))
                    return t;
            }
            return null;
        }

        return tag;
    }

    private bool IsPreRelease(string version)
    {
        return version.Contains("-alpha") ||
               version.Contains("-beta") ||
               version.Contains("-rc") ||
               Regex.IsMatch(version, @"-[\w.]+");
    }

    public async Task<IEnumerable<string>> GetAllTagsAsync(string? prefix = null)
    {
        var args = "tag -l";
        if (!string.IsNullOrEmpty(prefix))
        {
            args += $" \"{prefix}*\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .ExecuteBufferedAsync();

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t));
    }

    public async Task<IEnumerable<CommitInfo>> GetCommitsSinceTagAsync(
        string tag,
        string? folder = null)
    {
        var args = $"log {tag}..HEAD --pretty=format:%H%x00%h%x00%s%x00%ci";
        if (!string.IsNullOrEmpty(folder))
        {
            args += $" -- \"{folder}\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            return Enumerable.Empty<CommitInfo>();

        var commitAnalyzer = new CommitAnalyzer();

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('\0');
                if (parts.Length < 4)
                    return null!;
                return commitAnalyzer.ParseCommit(parts[0], parts[1], parts[2], DateTimeOffset.Parse(parts[3]));
            })
            .Where(c => c != null)
            .Cast<CommitInfo>();
    }

    public async Task<(string Hash, string ShortHash, DateTimeOffset Date)> GetHeadInfoAsync()
    {
        var result = await Cli.Wrap("git")
            .WithArguments("log -1 --pretty=format:%H%x00%h%x00%ci")
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            throw new InvalidOperationException("Failed to get HEAD info");

        var parts = result.StandardOutput.Trim().Split('\0');
        var date = DateTimeOffset.Parse(parts[2]);

        return (parts[0], parts[1], date);
    }

    public async Task<int> CountCommitsSinceTagAsync(string tag, string? folder = null)
    {
        var args = $"rev-list {tag}..HEAD --count";
        if (!string.IsNullOrEmpty(folder))
        {
            args += $" -- \"{folder}\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            return 0;

        return int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    public async Task CreateTagAsync(string tagName, string? message = null, bool annotated = false)
    {
        var args = annotated
            ? $"tag -a \"{tagName}\" -m \"{message ?? $"Release {tagName}"}\""
            : $"tag \"{tagName}\" -m \"{message ?? $"Release {tagName}"}\"";

        await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .ExecuteAsync();
    }

    public async Task PushTagAsync(string tagName)
    {
        await Cli.Wrap("git")
            .WithArguments($"push origin {tagName}")
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .ExecuteAsync();
    }

    public VersionInfo ParseVersionFromTag(string tag, string? prefix, VersioningMode mode)
    {
        var versionPart = prefix != null && tag.StartsWith(prefix)
            ? tag.Substring(prefix.Length)
            : tag;

        return ParseVersionString(versionPart, mode);
    }

    public VersionInfo ParseVersionString(string version, VersioningMode mode)
    {
        var preRelease = "";
        var buildMetadata = "";

        if (version.Contains('+'))
        {
            var parts = version.Split('+');
            version = parts[0];
            buildMetadata = parts[1];
        }

        if (version.Contains('-'))
        {
            var dashIndex = version.LastIndexOf('-');
            var potentialPre = version.Substring(dashIndex);
            if (potentialPre.Contains('.') || Regex.IsMatch(potentialPre, @"-\w+"))
            {
                preRelease = potentialPre;
                version = version.Substring(0, dashIndex);
            }
        }

        var components = version.Split('.');

        return mode switch
        {
            VersioningMode.SemVer => new VersionInfo(
                components.Length > 0 ? int.TryParse(components[0], out var m) ? m : 0 : 0,
                components.Length > 1 ? int.TryParse(components[1], out var mi) ? mi : 0 : 0,
                components.Length > 2 ? int.TryParse(components[2], out var p) ? p : 0 : 0,
                preRelease,
                buildMetadata,
                mode
            ),
            VersioningMode.CalVer => new VersionInfo(
                0,
                0,
                components.Length > 2 ? int.TryParse(components[2], out var p) ? p : 0 : 0,
                preRelease,
                buildMetadata,
                mode
            ),
            VersioningMode.ScalVer => new VersionInfo(
                components.Length > 0 ? int.TryParse(components[0], out var m) ? m : 0 : 0,
                0,
                components.Length > 2 ? int.TryParse(components[2], out var p) ? p : 0 : 0,
                preRelease,
                buildMetadata,
                mode
            ),
            _ => new VersionInfo(0, 0, 0, null, null, mode)
        };
    }
}

public class VersionCalculator
{
    private readonly SchemaParser _schemaParser = new();
    private readonly CommitAnalyzer _commitAnalyzer = new();
    private readonly GitService _gitService = new();

    public VersionCalculator()
    {
    }

    public VersionCalculator(string workingDirectory)
    {
        _gitService.WorkingDirectory = workingDirectory;
    }

    public async Task<CalculationResult> CalculateNextVersionAsync(
        string schema,
        string? prefix = null,
        string? folder = null)
    {
        var mode = _schemaParser.DetectMode(schema);

        if (!_schemaParser.ValidateSchema(schema, mode))
        {
            throw new ArgumentException($"Invalid schema '{schema}' for {mode} mode");
        }

        var headInfo = await _gitService.GetHeadInfoAsync();
        var latestTag = await _gitService.GetLatestStableTagAsync(prefix);

        if (latestTag == null)
        {
            return CalculateInitialVersion(schema, mode, headInfo);
        }

        var baseVersion = _gitService.ParseVersionFromTag(latestTag, prefix, mode);

        if (baseVersion.Mode != mode)
        {
            throw new SchemaMismatchException(
                $"Current schema: {schema} ({mode})\nLatest tag: {latestTag} ({baseVersion.Mode})\n\nCannot switch between versioning modes automatically.\nPlease manually tag with the new schema to continue.\n\nExample: git tag 0.1.0 -m \"Switch to {mode}\"",
                mode,
                baseVersion.Mode,
                latestTag
            );
        }

        var commits = (await _gitService.GetCommitsSinceTagAsync(latestTag, folder)).ToList();
        var numCommits = await _gitService.CountCommitsSinceTagAsync(latestTag, folder);

        return mode switch
        {
            VersioningMode.SemVer => CalculateSemVer(schema, baseVersion, commits, headInfo, numCommits, latestTag),
            VersioningMode.CalVer => CalculateCalVer(schema, baseVersion, headInfo, numCommits, latestTag),
            VersioningMode.ScalVer => CalculateScalVer(schema, baseVersion, commits, headInfo, numCommits, latestTag),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private CalculationResult CalculateInitialVersion(
        string schema,
        VersioningMode mode,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo)
    {
        var version = mode switch
        {
            VersioningMode.SemVer => "0.1.0",
            VersioningMode.CalVer => FormatCalVer(schema, headInfo.Date, 0),
            VersioningMode.ScalVer => FormatScalVer(schema, 0, headInfo.Date, 0),
            _ => throw new ArgumentOutOfRangeException()
        };

        return new CalculationResult(
            Version: version,
            TagName: version,
            Mode: mode,
            BaseTag: null,
            BaseVersion: null,
            CommitsSinceTag: 0,
            Increment: VersionIncrement.None,
            IncrementReason: "initial version",
            Schema: schema
        );
    }

    private CalculationResult CalculateSemVer(
        string schema,
        VersionInfo baseVersion,
        List<CommitInfo> commits,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        int numCommits,
        string latestTag)
    {
        var increment = _commitAnalyzer.DetermineIncrement(commits);

        var newVersion = increment switch
        {
            VersionIncrement.Major => new VersionInfo(baseVersion.Major + 1, 0, 0, null, null, VersioningMode.SemVer),
            VersionIncrement.Minor => new VersionInfo(baseVersion.Major, baseVersion.Minor + 1, 0, null, null, VersioningMode.SemVer),
            VersionIncrement.Patch => new VersionInfo(baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1, null, null, VersioningMode.SemVer),
            _ => baseVersion
        };

        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

        return new CalculationResult(
            Version: versionString,
            TagName: versionString,
            Mode: VersioningMode.SemVer,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            Increment: increment,
            IncrementReason: _commitAnalyzer.GetIncrementReason(commits, increment),
            Schema: schema
        );
    }

    private CalculationResult CalculateCalVer(
        string schema,
        VersionInfo baseVersion,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        int numCommits,
        string latestTag)
    {
        var newPatch = baseVersion.Patch + 1;

        var newVersion = new VersionInfo(0, 0, newPatch, null, null, VersioningMode.CalVer);

        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

        return new CalculationResult(
            Version: versionString,
            TagName: versionString,
            Mode: VersioningMode.CalVer,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            Increment: newPatch > baseVersion.Patch ? VersionIncrement.Patch : VersionIncrement.None,
            IncrementReason: newPatch > baseVersion.Patch ? "same date window, incrementing patch" : "new date window",
            Schema: schema
        );
    }

    private CalculationResult CalculateScalVer(
        string schema,
        VersionInfo baseVersion,
        List<CommitInfo> commits,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        int numCommits,
        string latestTag)
    {
        var breakingIncrement = _commitAnalyzer.DetermineIncrement(commits) == VersionIncrement.Major;
        var wouldShrink = WouldDateShrink(schema, baseVersion, headInfo.Date);

        int newMajor;
        int newPatch;

        if (breakingIncrement || wouldShrink)
        {
            newMajor = baseVersion.Major + 1;
            newPatch = 0;
        }
        else
        {
            newMajor = baseVersion.Major;
            newPatch = baseVersion.Patch + 1;
        }

        var newVersion = new VersionInfo(newMajor, 0, newPatch, null, null, VersioningMode.ScalVer);

        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

        var increment = breakingIncrement ? VersionIncrement.Major :
                       wouldShrink ? VersionIncrement.Major :
                       newPatch > baseVersion.Patch ? VersionIncrement.Patch : VersionIncrement.None;

        return new CalculationResult(
            Version: versionString,
            TagName: versionString,
            Mode: VersioningMode.ScalVer,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            Increment: increment,
            IncrementReason: breakingIncrement ? "breaking changes detected" :
                            wouldShrink ? "date would shrink, incrementing major" :
                            "same date window, incrementing patch",
            Schema: schema
        );
    }

    private bool WouldDateShrink(string schema, VersionInfo baseVersion, DateTimeOffset newDate)
    {
        var currentDateWidth = GetDateWidth(schema);
        var newDateWidth = GetDateWidth(schema);

        return currentDateWidth > newDateWidth;
    }

    private int GetDateWidth(string schema)
    {
        if (schema.Contains("{YYYY}{0M}{0D}") || schema.Contains("{YYYY}{MM}{DD}"))
            return 3;
        if (schema.Contains("{YYYY}{0M}") || schema.Contains("{YYYY}{MM}"))
            return 2;
        if (schema.Contains("{YYYY}"))
            return 1;
        return 0;
    }

    private string FormatCalVer(string schema, DateTimeOffset date, int patch)
    {
        var version = new VersionInfo(0, 0, patch, null, null, VersioningMode.CalVer);
        return _schemaParser.ApplyVersion(schema, version, date, 0, "", "");
    }

    private string FormatScalVer(string schema, int major, DateTimeOffset date, int patch)
    {
        var version = new VersionInfo(major, 0, patch, null, null, VersioningMode.ScalVer);
        return _schemaParser.ApplyVersion(schema, version, date, 0, "", "");
    }
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.AddCommand<NextCommand>("next")
                .WithDescription("Calculate the next version without creating a tag")
                .WithExample(new[] { "next", "-s", "{MAJOR}.{MINOR}.{PATCH}" });
            
            config.AddCommand<TagCommand>("tag")
                .WithDescription("Create a git tag with the next version")
                .WithExample(new[] { "tag", "-s", "{MAJOR}.{MINOR}.{PATCH}" });
        });

        return await app.RunAsync(args);
    }
}

public class NextCommand : Command<NextCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-s|--schema")]
        [Description("Version schema (e.g., {MAJOR}.{MINOR}.{PATCH})")]
        public required string Schema { get; init; }

        [CommandOption("-p|--prefix")]
        [Description("Tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("-f|--folder")]
        [Description("Filter commits to a specific folder path")]
        public string? Folder { get; init; }

        [CommandOption("-o|--output")]
        [Description("Output format: text or json")]
        [DefaultValue("text")]
        public string Output { get; init; } = "text";
    }

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var calculator = new VersionCalculator();
            var result = calculator.CalculateNextVersionAsync(settings.Schema, settings.Prefix, settings.Folder).GetAwaiter().GetResult();

            if (settings.Output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    result.Version,
                    result.Mode,
                    result.BaseTag,
                    CommitsSinceTag = result.CommitsSinceTag,
                    Increment = result.Increment.ToString(),
                    result.IncrementReason,
                    result.Schema
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.Write(json);
            }
            else
            {
                AnsiConsole.Write(result.Version);
            }
            return 0;
        }
        catch (SchemaMismatchException ex)
        {
            AnsiConsole.MarkupLine("[red]Error: Schema mode mismatch[/]");
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

public class TagCommand : Command<TagCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-s|--schema")]
        [Description("Version schema (e.g., {MAJOR}.{MINOR}.{PATCH})")]
        public required string Schema { get; init; }

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

    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var calculator = new VersionCalculator();
            var result = calculator.CalculateNextVersionAsync(settings.Schema, settings.Prefix, settings.Folder).GetAwaiter().GetResult();

            var gitService = new GitService();
            var tagName = settings.Prefix != null ? $"{settings.Prefix}{result.Version}" : result.Version;

            gitService.CreateTagAsync(tagName, settings.Message, settings.Annotated).GetAwaiter().GetResult();

            if (settings.Push)
            {
                gitService.PushTagAsync(tagName).GetAwaiter().GetResult();
            }

            if (settings.Output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    Version = result.Version,
                    TagName = tagName,
                    result.Mode,
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
            AnsiConsole.MarkupLine("[red]Error: Schema mode mismatch[/]");
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

