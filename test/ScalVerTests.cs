using System;
using System.Threading.Tasks;
using ReleaseTools;
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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}");

        Assert.Equal("0.20250223.0", result.Version);
    }

    [Fact]
    public async Task BreakingChange_IncrementsMajor()
    {
        var date = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero))
            .WithTag("1.20250201.0")
            .WithCommit("feat!: breaking change", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}");

        Assert.Equal("2.20250315.0", result.Version);
    }

    [Fact]
    public async Task DateGrows_PatchResets()
    {
        var date = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero))
            .WithTag("1.202502.0")
            .WithCommit("feat: new feature", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}.{PATCH}");

        Assert.Equal("1.202503.0", result.Version);
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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}");

        Assert.Equal("1.20250215.1", result.Version);
    }

    [Fact]
    public async Task WithPrefix_Monorepo()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}",
            prefix: "api-");

        Assert.Equal("api-0.20250223.0", result.Version);
    }

    [Fact]
    public async Task ShortYear_Schema()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{0Y}{0M}.{PATCH}");

        Assert.Equal("0.2502.0", result.Version);
    }

    [Fact]
    public async Task YearlyToMonthly_ExpandsDate()
    {
        var date = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .WithTag("1.2025.0")
            .WithCommit("feat: new feature", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}.{PATCH}");

        Assert.Equal("1.202503.0", result.Version);
    }

    [Fact]
    public async Task YearlyToDaily_ExpandsDate()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .WithTag("1.2025.0")
            .WithCommit("feat: new feature", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}");

        Assert.Equal("1.20250223.0", result.Version);
    }

    [Fact]
    public async Task PreRelease_ScalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .WithCommit("feat: another", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}.{PATCH}-beta.{NUM_COMMITS}");

        Assert.Equal("0.2025.0-beta.2", result.Version);
    }

    [Fact]
    public async Task BreakingChangeInBody_IncrementsMajor()
    {
        var date = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero))
            .WithTag("1.202502.0")
            .WithCommit("feat: add feature\n\nBREAKING CHANGE: API redesigned", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}.{PATCH}");

        Assert.Equal("2.202503.0", result.Version);
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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{YYYY}{0M}{0D}.{PATCH}");

        Assert.Equal("1.20250215.1", result.Version);
    }
}
