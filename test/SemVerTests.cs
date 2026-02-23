using System;
using System.Threading.Tasks;
using ReleaseTools;
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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

        Assert.Equal("0.1.0", result.Version);
        Assert.Equal(VersioningMode.SemVer, result.Mode);
    }

    [Fact]
    public async Task FeatCommit_Increments_Minor()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: add new feature")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

        Assert.Equal("1.1.0", result.Version);
        Assert.Equal(VersionIncrement.Minor, result.Increment);
    }

    [Fact]
    public async Task BreakingChange_Increments_Major()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat!: breaking API change")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

        Assert.Equal("2.0.0", result.Version);
        Assert.Equal(VersionIncrement.Major, result.Increment);
    }

    [Fact]
    public async Task BreakingChangeInBody_Increments_Major()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: add new feature\n\nBREAKING CHANGE: API redesigned")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

        Assert.Equal("2.0.0", result.Version);
    }

    [Fact]
    public async Task FixCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("fix: resolve issue")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

        Assert.Equal("1.0.1", result.Version);
        Assert.Equal(VersionIncrement.Patch, result.Increment);
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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}",
            prefix: "api-");

        Assert.Equal("api-1.1.0", result.Version);
    }

    [Fact]
    public async Task PreReleaseSchema_GeneratesCorrectly()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .WithCommit("feat: another feature")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}-alpha.{NUM_COMMITS}");

        Assert.Equal("1.1.0-alpha.2", result.Version);
    }

    [Fact]
    public async Task SchemaMismatch_ReturnsError()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("2025.02.0")
            .WithCommit("feat: new feature")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);

        await Assert.ThrowsAsync<SchemaMismatchException>(() =>
            calculator.CalculateNextVersionAsync(
                "{MAJOR}.{MINOR}.{PATCH}"));
    }

    [Fact]
    public async Task PerfCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("perf: optimize database queries")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

        Assert.Equal("1.0.1", result.Version);
    }

    [Fact]
    public async Task RevertCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("revert: feat: old feature")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

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

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}");

        Assert.Equal("1.0.0", result.Version);
        Assert.Equal(VersionIncrement.None, result.Increment);
    }

    [Fact]
    public async Task WithBuildMetadata_GeneratesCorrectly()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var calculator = new VersionCalculator(repo.RepoPath);
        var result = await calculator.CalculateNextVersionAsync(
            "{MAJOR}.{MINOR}.{PATCH}+{SHORTSHA}");

        Assert.EndsWith("+", result.Version);
    }
}
