using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ReleaseTools.Tests.Infrastructure;
using Xunit;

namespace ReleaseTools.Tests;

public class CalVerTests
{
    private static readonly DateTimeOffset Feb15 = new(2025, 2, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb23 = new(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb24 = new(2025, 2, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jan10 = new(2025, 1, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun10 = new(2025, 6, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SingleCommit_PatchIsCommitCountInWindow()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("2025.02.1", stdout);
    }

    [Fact]
    public async Task MultipleCommitsSameMonth_AllCounted()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: one", Feb15)
            .WithCommit("feat: two", Feb23)
            .WithCommit("fix: three", Feb24)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath);

        Assert.Equal("2025.02.3", stdout);
    }

    [Fact]
    public async Task PreviousMonthCommits_NotCounted()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: old", Jan10)
            .WithCommit("feat: older", Jan10)
            .WithCommit("feat: new", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath);

        Assert.Equal("2025.02.1", stdout);
    }

    [Fact]
    public async Task DailyFormat_CountsCommitsOfHeadDay()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: yesterday", Feb23)
            .WithCommit("feat: today", Feb24)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YYYY.0M.0D.PATCH");

        Assert.Equal("2025.02.24.1", stdout);
    }

    [Fact]
    public async Task ConcatenatedTokens_WithoutSeparators()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YY.0M0D.PATCH");

        Assert.Equal("25.0223.1", stdout);
    }

    [Fact]
    public async Task UnpaddedTokens_RenderUnpadded()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YYYY.MM.DD.PATCH");

        Assert.Equal("2025.2.23.1", stdout);
    }

    [Fact]
    public async Task ShortYear_RendersTwoDigits()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "0Y.0M.PATCH");

        Assert.Equal("25.02.1", stdout);
    }

    [Fact]
    public async Task WeekFormat_RendersIsoWeek()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YYYY.0W.PATCH");

        var expectedWeek = System.Globalization.ISOWeek.GetWeekOfYear(Feb23.UtcDateTime);
        Assert.Equal($"2025.{expectedWeek:D2}.1", stdout);
    }

    [Fact]
    public async Task NoPatchSegment_OmitsPatch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YYYY.0M");

        Assert.Equal("2025.02", stdout);
    }

    [Fact]
    public async Task FolderFilter_OnlyCountsFolderCommits()
    {
        using var repo = new GitTestRepo();
        repo.AddFile("api/service.txt", "api change");
        repo.Commit("feat: api", Feb23);
        repo.AddFile("web/page.txt", "web change");
        repo.Commit("feat: web", Feb23);

        var (_, apiStdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--folder", "api");
        var (_, allStdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath);

        Assert.Equal("2025.02.1", apiStdout);
        Assert.Equal("2025.02.2", allStdout);
    }

    [Fact]
    public async Task Prerelease_AppendsIdentifier()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "-p", "alpha");

        Assert.Equal("2025.02.1-alpha", stdout);
    }

    [Fact]
    public async Task BuildMetadata_AppendsShortSha()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "-b");

        Assert.Matches(@"^2025\.02\.1\+[a-f0-9]{7}$", stdout);
    }

    [Fact]
    public async Task PrereleaseAndBuildMetadata_Combined()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "-p", "rc", "-b");

        Assert.Matches(@"^2025\.02\.1-rc\+[a-f0-9]{7}$", stdout);
    }

    [Fact]
    public async Task JsonOutput_ContainsAllFields()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "-o", "json");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("2025.02.1", json.RootElement.GetProperty("version").GetString());
        Assert.Equal("2025.02.1", json.RootElement.GetProperty("fullVersion").GetString());
        Assert.Equal("{YYYY}.{0M}.{PATCH}", json.RootElement.GetProperty("schema").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("commitCount").GetInt32());
    }

    [Fact]
    public async Task PackageFormat_UsesShortMonthAndDay()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--format", "YYYY.MMDD.PATCH");

        Assert.Equal("2025.223.1", stdout);
    }

    [Fact]
    public async Task FolderScope_UsesLatestFolderCommitDateAndSha()
    {
        using var repo = new GitTestRepo();
        repo.AddFile("api/service.txt", "api");
        repo.Commit("feat: api", Feb23);
        var apiSha = repo.GetShortHead();
        repo.AddFile("web/page.txt", "web");
        repo.Commit("feat: web", Jun10);

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--folder", "api", "--buildmetadata");

        Assert.Equal($"2025.02.1+{apiSha}", stdout);
    }

    [Fact]
    public async Task FolderWithPathspecCharacters_IsLiteral()
    {
        using var repo = new GitTestRepo();
        repo.AddFile("apps/[api]/service.txt", "api");
        repo.Commit("feat: api", Feb23);

        var (exitCode, stdout, _) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--folder", "apps/[api]");

        Assert.Equal(0, exitCode);
        Assert.Equal("2025.02.1", stdout);
    }

    [Fact]
    public async Task CommitDate_IsRenderedInUtc()
    {
        var localMarch = new DateTimeOffset(2025, 3, 1, 0, 30, 0, TimeSpan.FromHours(2));
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: before UTC midnight", localMarch)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--format", "YYYY.0M.0D.PATCH");

        Assert.Equal("2025.02.28.1", stdout);
    }

    [Fact]
    public async Task IsoWeek_IsCultureIndependentAtYearBoundary()
    {
        var janFirst = new DateTimeOffset(2021, 1, 1, 12, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: ISO week boundary", janFirst)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--format", "YYYY.0W.PATCH");

        Assert.Equal("2021.53.1", stdout);
    }

    [Fact]
    public async Task DateWindow_ExcludesPreviousMonthBoundary()
    {
        var februaryEnd = new DateTimeOffset(2025, 2, 28, 23, 59, 59, TimeSpan.Zero);
        var marchStart = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: february", februaryEnd)
            .WithCommit("feat: march", marchStart)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("calver", repo.RepoPath);

        Assert.Equal("2025.03.1", stdout);
    }

    [Fact]
    public async Task MissingFolder_FailsClearly()
    {
        using var repo = new GitTestRepo();

        var (exitCode, stdout, stderr) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--folder", "missing");

        Assert.NotEqual(0, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("does not contain tracked files", stderr);
    }

    [Fact]
    public async Task InvalidOutputFormat_Fails()
    {
        using var repo = new GitTestRepo();

        var (exitCode, _, _) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--output", "yaml");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task InvalidFormat_UnknownToken_Fails()
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YYYY.XX.PATCH");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task InvalidFormat_MissingYear_Fails()
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "0M.PATCH");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task InvalidFormat_MonthAndWeek_Fails()
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YYYY.0M.0W.PATCH");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task InvalidFormat_DayWithoutMonth_Fails()
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "YYYY.0D.PATCH");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task InvalidFormat_WrongOrder_Fails()
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("calver", repo.RepoPath, "--format", "0M.YYYY.PATCH");

        Assert.NotEqual(0, exitCode);
    }

    [Theory]
    [InlineData(".YYYY.0M.PATCH")]
    [InlineData("YYYY..0M.PATCH")]
    [InlineData("YYYY.0M.PATCH.")]
    [InlineData("YYYY.PATCH.PATCH")]
    public async Task InvalidFormat_EmptyOrDuplicateSegmentsFail(string format)
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync(
            "calver", repo.RepoPath, "--format", format);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task NotAGitRepo_Fails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ReleaseTools_NoGit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var (exitCode, _, _) = await ToolRunner.RunAsync("calver", tempDir);

            Assert.NotEqual(0, exitCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
