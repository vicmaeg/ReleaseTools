using System;
using System.Threading.Tasks;
using ReleaseTools.Shared;
using ReleaseTools.Tests.Infrastructure;
using Xunit;

namespace ReleaseTools.Tests;

public class CalVerTests
{
    [Fact]
    public async Task NoTags_Returns_DateVersion()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.0M.PATCH");

        Assert.Equal("2025.02.0", result.Version);
    }

    [Fact]
    public async Task NoTags_YearMonthDay_Returns_DateVersion()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.MM.DD.PATCH");

        Assert.Equal("2025.2.23.0", result.Version);
    }

    [Fact]
    public async Task SameMonth_IncrementsPatch()
    {
        var date1 = new DateTimeOffset(2025, 2, 15, 10, 0, 0, TimeSpan.Zero);
        var date2 = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date1)
            .WithTag("2025.02.0")
            .WithCommit("feat: new feature", date2)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.0M.PATCH");

        Assert.Equal("2025.02.1", result.Version);
    }

    [Fact]
    public async Task DifferentMonth_ResetsPatch()
    {
        var date1 = new DateTimeOffset(2025, 2, 15, 10, 0, 0, TimeSpan.Zero);
        var date2 = new DateTimeOffset(2025, 3, 10, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date1)
            .WithTag("2025.02.5")
            .WithCommit("feat: new feature", date2)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.0M.PATCH");

        Assert.Equal("2025.03.0", result.Version);
    }

    [Fact]
    public async Task DailySchema_UsesDay()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.0M.0D.PATCH");

        Assert.Equal("2025.02.23.0", result.Version);
    }

    [Fact]
    public async Task SameDay_IncrementsPatch()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date)
            .WithTag("2025.02.23.0")
            .WithCommit("feat: new feature", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.0M.0D.PATCH");

        Assert.Equal("2025.02.23.1", result.Version);
    }

    [Fact]
    public async Task DifferentDay_ResetsPatch()
    {
        var date1 = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        var date2 = new DateTimeOffset(2025, 2, 25, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date1)
            .WithTag("2025.02.23.5")
            .WithCommit("feat: new feature", date2)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.0M.0D.PATCH");

        Assert.Equal("2025.02.25.0", result.Version);
    }

    [Fact]
    public async Task WithPrefix_Monorepo()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("YYYY.0M.PATCH", prefix: "api-");

        Assert.Equal("2025.02.0", result.Version);
    }

    [Fact]
    public async Task ShortYear_Schema()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync("0Y.0M.PATCH");

        Assert.Equal("25.02.0", result.Version);
    }

    [Fact]
    public async Task Prerelease_CalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .WithCommit("feat: another", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "YYYY.0M.PATCH",
            prereleaseIdentifier: "alpha");

        Assert.Equal("2025.02.0", result.Version);
        Assert.Equal("2025.02.0-alpha.2", result.FullVersion);
        Assert.Equal("alpha.2", result.Prerelease);
    }

    [Fact]
    public async Task BuildMetadata_CalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "YYYY.0M.PATCH",
            includeBuildMetadata: true);

        Assert.Equal("2025.02.0", result.Version);
        Assert.NotNull(result.BuildMetadata);
        Assert.Contains("+", result.FullVersion);
    }

    [Fact]
    public async Task PrereleaseAndBuildMetadata_CalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .WithCommit("feat: another", date)
            .Build();

        var calculator = new CalVerCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "YYYY.0M.PATCH",
            prereleaseIdentifier: "rc",
            includeBuildMetadata: true);

        Assert.Equal("2025.02.0", result.Version);
        Assert.Equal("rc.2", result.Prerelease);
        Assert.NotNull(result.BuildMetadata);
        Assert.Matches(@"^2025\.02\.0-rc\.2\+[a-f0-9]+$", result.FullVersion);
    }
}

#region Calculator Class for Tests

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

        // Parse date from the tag
        var baseDate = ParseDateFromTag(latestTag, prefix, schema);
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

    private DateTimeOffset ParseDateFromTag(string tag, string? prefix, string schema)
    {
        // Extract version part from tag
        var versionPart = prefix != null && tag.StartsWith(prefix)
            ? tag.Substring(prefix.Length)
            : tag;

        var components = versionPart.Split('.');
        
        // Parse based on schema tokens
        int year = DateTimeOffset.UtcNow.Year;
        int month = DateTimeOffset.UtcNow.Month;
        int day = DateTimeOffset.UtcNow.Day;

        // Find the PATCH component index
        var patchIndex = Array.IndexOf(schema.Split('.'), "{PATCH}");
        
        // Parse date components before PATCH
        for (int i = 0; i < patchIndex && i < components.Length; i++)
        {
            var component = components[i];
            
            // Try to parse year
            if (component.Length == 4 && int.TryParse(component, out var y) && y > 2000 && y < 2100)
            {
                year = y;
            }
            // Try to parse year-month combined
            else if (component.Length == 6 && int.TryParse(component, out var ym))
            {
                year = ym / 100;
                month = ym % 100;
            }
            // Try to parse year-month-day combined
            else if (component.Length == 8 && int.TryParse(component, out var ymd))
            {
                year = ymd / 10000;
                month = (ymd / 100) % 100;
                day = ymd % 100;
            }
            // Parse individual month or day
            else if (int.TryParse(component, out var value))
            {
                if (value >= 1 && value <= 12 && i == patchIndex - 1)
                    month = value;
                else if (value >= 1 && value <= 31)
                    day = value;
            }
        }

        try
        {
            return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        }
        catch
        {
            return DateTimeOffset.UtcNow;
        }
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
