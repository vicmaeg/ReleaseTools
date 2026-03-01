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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}.{PATCH}");

        Assert.Equal("2025.02.0", result.Version);
    }

    [Fact]
    public async Task NoTags_YearMonthDay_Returns_DateVersion()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}{0D}.{PATCH}");

        Assert.Equal("2025.0223.0", result.Version);
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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}.{PATCH}");

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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}.{PATCH}");

        Assert.Equal("2025.03.0", result.Version);
    }

    [Fact]
    public async Task DailySchema_UsesDay()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}{0D}.{PATCH}");

        Assert.Equal("2025.0223.0", result.Version);
    }

    [Fact]
    public async Task SameDay_IncrementsPatch()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date)
            .WithTag("2025.0223.0")
            .WithCommit("feat: new feature", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}{0D}.{PATCH}");

        Assert.Equal("2025.0223.1", result.Version);
    }

    [Fact]
    public async Task DifferentDay_ResetsPatch()
    {
        var date1 = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        var date2 = new DateTimeOffset(2025, 2, 25, 10, 0, 0, TimeSpan.Zero);

        using var repo = new GitTestRepoBuilder()
            .WithCommit("initial", date1)
            .WithTag("2025.0223.5")
            .WithCommit("feat: new feature", date2)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}{0D}.{PATCH}");

        Assert.Equal("2025.0225.0", result.Version);
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
            "{YYYY}.{0M}.{PATCH}",
            prefix: "api-");

        Assert.Equal("2025.02.0", result.Version);
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
            "{YY}.{0M}.{PATCH}");

        Assert.Equal("25.02.0", result.Version);
    }

    [Fact]
    public async Task PreRelease_CalVer()
    {
        var date = new DateTimeOffset(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", date)
            .WithCommit("feat: another", date)
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{YYYY}.{0M}.{PATCH}-alpha.{NUM_COMMITS}");

        Assert.Equal("2025.02.0-alpha.2", result.Version);
    }
}
