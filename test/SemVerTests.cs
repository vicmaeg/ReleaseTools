using System;
using System.Threading.Tasks;
using ReleaseTools.Shared;
using ReleaseTools.Tests.Infrastructure;
using Xunit;

namespace ReleaseTools.Tests;

public class SemVerTests
{
    [Fact]
    public async Task NoTags_Returns_0_1_0()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("0.1.0", result.Version);
        Assert.Equal("0.1.0", result.FullVersion);
    }

    [Fact]
    public async Task FeatCommit_Increments_Minor()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: add new feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("1.1.0", result.Version);
        Assert.Equal("feat commits detected", result.IncrementReason);
    }

    [Fact]
    public async Task BreakingChange_Increments_Major()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat!: breaking API change")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("2.0.0", result.Version);
        Assert.Equal("breaking changes detected", result.IncrementReason);
    }

    [Fact]
    public async Task BreakingChangeInBody_Increments_Major()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: add new feature\n\nBREAKING CHANGE: API redesigned")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("2.0.0", result.Version);
    }

    [Fact]
    public async Task FixCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("fix: resolve issue")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("1.0.1", result.Version);
        Assert.Equal("fix/perf commits detected", result.IncrementReason);
    }

    [Fact]
    public async Task MultipleFeats_OnlyIncrementOnce()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: add feature A")
            .WithCommit("feat: add feature B")
            .WithCommit("feat: add feature C")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("1.1.0", result.Version);
    }

    [Fact]
    public async Task PreReleaseTags_AreSkipped()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithTag("1.1.0-alpha.1")
            .WithTag("1.1.0-beta.1")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("1.0.0", result.BaseTag);
    }

    [Fact]
    public async Task MonorepoPrefix_FiltersTags()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("api-1.0.0")
            .WithTag("web-2.0.0")
            .WithCommit("feat: new api feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            prefix: "api-");

        Assert.Equal("1.1.0", result.Version);
    }

    [Fact]
    public async Task Prerelease_GeneratesCorrectly()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .WithCommit("feat: another feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            prereleaseIdentifier: "alpha");

        Assert.Equal("1.1.0", result.Version);
        Assert.Equal("1.1.0-alpha.2", result.FullVersion);
        Assert.Equal("alpha.2", result.Prerelease);
    }

    [Fact]
    public async Task BuildMetadata_GeneratesCorrectly()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            includeBuildMetadata: true);

        Assert.Equal("1.1.0", result.Version);
        Assert.NotNull(result.BuildMetadata);
        Assert.Contains("+", result.FullVersion);
    }

    [Fact]
    public async Task PrereleaseAndBuildMetadata_GeneratesCorrectly()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            prereleaseIdentifier: "beta",
            includeBuildMetadata: true);

        Assert.Equal("1.1.0", result.Version);
        Assert.Equal("beta.1", result.Prerelease);
        Assert.NotNull(result.BuildMetadata);
        Assert.Matches(@"^1\.1\.0-beta\.1\+[a-f0-9]+$", result.FullVersion);
    }

    [Fact]
    public async Task PerfCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("perf: optimize database queries")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("1.0.1", result.Version);
    }

    [Fact]
    public async Task RevertCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("revert: feat: old feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("1.0.1", result.Version);
    }

    [Fact]
    public async Task NoRelevantCommits_NoIncrement()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("docs: update README")
            .WithCommit("chore: update dependencies")
            .WithCommit("style: format code")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync();

        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("no version-relevant commits", result.IncrementReason);
    }

    [Fact]
    public async Task WithBuildMetadata_Flag_GeneratesShortSha()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var calculator = new SemVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            includeBuildMetadata: true);

        Assert.NotNull(result.BuildMetadata);
        Assert.Equal(7, result.BuildMetadata!.Length); // short SHA is 7 chars
        Assert.EndsWith($"+{result.BuildMetadata}", result.FullVersion);
    }
}

#region Calculator Classes for Tests

public record CommitInfo(
    string Hash,
    string ShortHash,
    string Message,
    string Type,
    string? Scope,
    bool Breaking,
    DateTimeOffset Date
);

public enum VersionIncrement
{
    None,
    Patch,
    Minor,
    Major
}

public class CommitAnalyzer
{
    private static readonly System.Text.RegularExpressions.Regex ConventionalCommitRegex = new(
        @"^(?<type>\w+)(?:\((?<scope>\w+)\))?(?<breaking>!):\s*(?<description>.+)$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex BreakingChangeFooterRegex = new(
        @"BREAKING CHANGE:\s*(.+)",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

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
