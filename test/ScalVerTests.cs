using System;
using System.Threading.Tasks;
using ReleaseTools.Shared;
using ReleaseTools.Tests.Infrastructure;
using Xunit;

namespace ReleaseTools.Tests;

public class ScalVerTests
{
    [Fact]
    public async Task NoTags_Returns_0_Date_0()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYYMMDD");

        Assert.Equal("0.20250223.0", result.Version);
    }

    [Fact]
    public async Task SameDate_IncrementsPatch()
    {
        var date = new DateTimeOffset(2025, 2, 15, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date)
            .WithTag("1.20250215.0")
            .WithCommit("feat: new feature", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYYMMDD");

        Assert.Equal("1.20250215.1", result.Version);
    }

    [Fact]
    public async Task WithPrefix_Monorepo()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYYMMDD", prefix: "api-");

        Assert.Equal("0.20250223.0", result.Version);
    }

    [Fact]
    public async Task YearMonthFormat_Schema()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYYMM");

        Assert.Equal("0.202502.0", result.Version);
    }

    [Fact]
    public async Task YearOnlyFormat_Schema()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY");

        Assert.Equal("0.2025.0", result.Version);
    }

    [Fact]
    public async Task Prerelease_ScalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .WithCommit("feat: another", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "YYYY",
            prereleaseIdentifier: "beta");

        Assert.Equal("0.2025.0", result.Version);
        Assert.Equal("0.2025.0-beta.2", result.FullVersion);
        Assert.Equal("beta.2", result.Prerelease);
    }

    [Fact]
    public async Task BuildMetadata_ScalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "YYYYMM",
            includeBuildMetadata: true);

        Assert.Equal("0.202502.0", result.Version);
        Assert.NotNull(result.BuildMetadata);
        Assert.Contains("+", result.FullVersion);
    }

    [Fact]
    public async Task PrereleaseAndBuildMetadata_ScalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .WithCommit("feat: another", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "YYYYMMDD",
            prereleaseIdentifier: "rc",
            includeBuildMetadata: true);

        Assert.Equal("0.20250223.0", result.Version);
        Assert.Equal("rc.2", result.Prerelease);
        Assert.NotNull(result.BuildMetadata);
        Assert.Matches(@"^0\.20250223\.0-rc\.2\+[a-f0-9]+$", result.FullVersion);
    }

    [Fact]
    public async Task MultipleCommits_SameDate_IncrementsOnce()
    {
        var date = new DateTimeOffset(2025, 2, 15, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date)
            .WithTag("1.20250215.0")
            .WithCommit("feat: feature A", date)
            .WithCommit("feat: feature B", date)
            .WithCommit("fix: bug", date)
            .Build();

        var calculator = new ScalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYYMMDD");

        Assert.Equal("1.20250215.1", result.Version);
    }
}

#region Calculator Classes for Tests

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

        // Determine if we should increment major (ScalVer doesn't use breaking changes, just date shrink)
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
