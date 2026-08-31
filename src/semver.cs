#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:property PackageId=ReleaseTools.SemVer
#:property ToolCommandName=semver
#:property Description=Calculate semantic versions from Git history and Conventional Commits.
#:package CliWrap@3.10.0
#:package Spectre.Console.Cli@0.53.0
#:include shared/GitService.cs
#:include shared/MetadataService.cs
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
    config.AddExample([]);
    config.AddExample(["-p", "api-", "-f", "apps/api"]);
    config.AddExample(["--prerelease", "alpha", "-b"]);
});

return await app.RunAsync(args);

public sealed class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--prefix <PREFIX>")]
        [Description("Literal tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("-f|--folder <PATH>")]
        [Description("Repository-relative folder whose commits determine the version")]
        public string? Folder { get; init; }

        [CommandOption("--prerelease <ID>")]
        [Description("Prerelease label; the matching commit count is appended automatically")]
        public string? Prerelease { get; init; }

        [CommandOption("-b|--buildmetadata")]
        [Description("Include the effective HEAD short SHA as build metadata")]
        [DefaultValue(false)]
        public bool BuildMetadata { get; init; }

        [CommandOption("-o|--output <FORMAT>")]
        [Description("Output format: text or json")]
        [DefaultValue(OutputFormat.Text)]
        public OutputFormat Output { get; init; } = OutputFormat.Text;

        public override ValidationResult Validate()
        {
            if (!string.IsNullOrEmpty(Prerelease) &&
                !Regex.IsMatch(Prerelease, @"^[0-9A-Za-z-]+$"))
            {
                return ValidationResult.Error(
                    "Prerelease must be a single alphanumeric or hyphenated label (for example: alpha, beta, rc-1)");
            }

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await new SemVerCalculator().CalculateNextVersionAsync(
                settings.Prefix,
                settings.Folder,
                settings.Prerelease,
                settings.BuildMetadata,
                cancellationToken);

            if (settings.Output == OutputFormat.Json)
            {
                Console.Write(JsonSerializer.Serialize(new
                {
                    result.Version,
                    result.FullVersion,
                    result.BaseTag,
                    result.CommitCount,
                    result.IncrementReason,
                    result.Schema,
                    result.Prerelease,
                    result.BuildMetadata
                }, new JsonSerializerOptions { WriteIndented = true }));
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

file enum VersionIncrement
{
    None,
    Patch,
    Minor,
    Major
}

file static class CommitAnalyzer
{
    private static readonly Regex ConventionalCommit = new(
        @"^(?<type>[A-Za-z][A-Za-z0-9-]*)(?:\((?<scope>[^)\r\n]+)\))?(?<breaking>!)?:\s*(?<description>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex BreakingFooter = new(
        @"^BREAKING(?: CHANGE|-CHANGE):\s*.+$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static (VersionIncrement Increment, string Reason) Analyze(IEnumerable<string> messages)
    {
        var highest = VersionIncrement.None;

        foreach (var message in messages)
        {
            var subject = message.Split('\n', 2)[0].TrimEnd('\r');
            var match = ConventionalCommit.Match(subject);
            if (!match.Success)
                continue;

            var increment = match.Groups["breaking"].Success || BreakingFooter.IsMatch(message)
                ? VersionIncrement.Major
                : match.Groups["type"].Value.ToLowerInvariant() switch
                {
                    "feat" => VersionIncrement.Minor,
                    "fix" or "perf" or "revert" => VersionIncrement.Patch,
                    _ => VersionIncrement.None
                };

            if (increment > highest)
                highest = increment;
        }

        return (highest, highest switch
        {
            VersionIncrement.Major => "breaking changes detected",
            VersionIncrement.Minor => "feat commits detected",
            VersionIncrement.Patch => "fix/perf/revert commits detected",
            _ => "no version-relevant commits"
        });
    }
}

file sealed record SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease,
    string? BuildMetadata)
{
    private static readonly Regex Pattern = new(
        @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)" +
        @"(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?" +
        @"(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled);

    public static bool TryParse(string value, out SemanticVersion? version)
    {
        version = null;
        var match = Pattern.Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            return false;
        }

        var prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
        if (prerelease?.Split('.').Any(identifier =>
                identifier.All(char.IsDigit) && identifier.Length > 1 && identifier[0] == '0') == true)
        {
            return false;
        }

        version = new SemanticVersion(
            major,
            minor,
            patch,
            prerelease,
            match.Groups["build"].Success ? match.Groups["build"].Value : null);
        return true;
    }

    public int CompareCore(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }
}

file sealed class SemVerCalculator
{
    private const string Schema = "{MAJOR}.{MINOR}.{PATCH}";
    private readonly GitService _git;

    public SemVerCalculator(string? workingDirectory = null)
    {
        _git = new GitService { WorkingDirectory = workingDirectory };
    }

    public async Task<CalculationResult> CalculateNextVersionAsync(
        string? prefix = null,
        string? folder = null,
        string? prereleaseIdentifier = null,
        bool includeBuildMetadata = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedFolder = await _git.ValidateFolderAsync(folder, cancellationToken);
        var head = await _git.GetHeadInfoAsync(normalizedFolder, cancellationToken);
        var baseTag = FindHighestStableTag(
            await _git.GetReachableTagsAsync(cancellationToken),
            prefix);

        if (baseTag is null)
        {
            var initialMessages = await _git.GetCommitMessagesAsync(
                folder: normalizedFolder,
                cancellationToken: cancellationToken);
            var commitCount = initialMessages.Count;
            var prerelease = prereleaseIdentifier is null
                ? null
                : $"{prereleaseIdentifier}.{commitCount}";

            return CreateResult(
                new SemanticVersion(0, 1, 0, null, null),
                null,
                commitCount,
                "initial version",
                prerelease,
                includeBuildMetadata ? head.ShortHash : null);
        }

        var messages = await _git.GetCommitMessagesAsync(
            baseTag.Value.Tag,
            normalizedFolder,
            cancellationToken);
        var (increment, reason) = CommitAnalyzer.Analyze(messages);
        var next = Increment(baseTag.Value.Version, increment);
        var prereleaseValue = prereleaseIdentifier is not null && increment != VersionIncrement.None
            ? $"{prereleaseIdentifier}.{messages.Count}"
            : null;

        return CreateResult(
            next,
            baseTag.Value.Tag,
            messages.Count,
            reason,
            prereleaseValue,
            includeBuildMetadata ? head.ShortHash : null);
    }

    private static (string Tag, SemanticVersion Version)? FindHighestStableTag(
        IEnumerable<string> tags,
        string? prefix)
    {
        (string Tag, SemanticVersion Version)? best = null;

        foreach (var tag in tags)
        {
            if (prefix is not null && !tag.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var versionText = prefix is null ? tag : tag[prefix.Length..];
            if (!SemanticVersion.TryParse(versionText, out var parsed) || parsed!.Prerelease is not null)
                continue;

            if (best is null)
            {
                best = (tag, parsed);
                continue;
            }

            var comparison = parsed.CompareCore(best.Value.Version);
            var preferCandidate = comparison > 0 ||
                (comparison == 0 && parsed.BuildMetadata is null && best.Value.Version.BuildMetadata is not null) ||
                (comparison == 0 && (parsed.BuildMetadata is null) == (best.Value.Version.BuildMetadata is null) &&
                 string.CompareOrdinal(tag, best.Value.Tag) > 0);

            if (preferCandidate)
                best = (tag, parsed);
        }

        return best;
    }

    private static SemanticVersion Increment(SemanticVersion version, VersionIncrement increment) => increment switch
    {
        VersionIncrement.Major => version with { Major = version.Major + 1, Minor = 0, Patch = 0, BuildMetadata = null },
        VersionIncrement.Minor => version with { Minor = version.Minor + 1, Patch = 0, BuildMetadata = null },
        VersionIncrement.Patch => version with { Patch = version.Patch + 1, BuildMetadata = null },
        _ => version with { BuildMetadata = null }
    };

    private static CalculationResult CreateResult(
        SemanticVersion version,
        string? baseTag,
        int commitCount,
        string reason,
        string? prerelease,
        string? buildMetadata)
    {
        var versionString = $"{version.Major}.{version.Minor}.{version.Patch}";
        return new CalculationResult(
            versionString,
            MetadataService.FormatFullVersion(versionString, prerelease, buildMetadata),
            baseTag,
            commitCount,
            reason,
            Schema,
            prerelease,
            buildMetadata);
    }
}
