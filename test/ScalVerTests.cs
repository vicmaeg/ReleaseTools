using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ReleaseTools.Tests.Infrastructure;
using Xunit;

namespace ReleaseTools.Tests;

public class ScalVerTests
{
    private static readonly DateTimeOffset Feb15 = new(2025, 2, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb23 = new(2025, 2, 23, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jun10 = new(2025, 6, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MajorIsRequired()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task DefaultFormat_YearMonth()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "1");

        Assert.Equal(0, exitCode);
        Assert.Equal("1.202502.1", stdout);
    }

    [Fact]
    public async Task DayFormat_CountsCommitsOfHeadDay()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: one", Feb15)
            .WithCommit("feat: two", Feb23)
            .WithCommit("fix: three", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "2", "-d", "YYYYMMDD");

        Assert.Equal("2.20250223.2", stdout);
    }

    [Fact]
    public async Task YearFormat_CountsWholeYear()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: one", Feb15)
            .WithCommit("feat: two", Feb23)
            .WithCommit("feat: three", Jun10)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "0", "-d", "YYYY");

        Assert.Equal("0.2025.3", stdout);
    }

    [Fact]
    public async Task MonthFormat_ResetsCountEachMonth()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: february", Feb23)
            .WithCommit("feat: june", Jun10)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "1", "-d", "YYYYMM");

        Assert.Equal("1.202506.1", stdout);
    }

    [Fact]
    public async Task InvalidDateFormat_Fails()
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "1", "-d", "YYMMDD");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Prerelease_AppendsIdentifier()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "1", "-p", "beta");

        Assert.Equal("1.202502.1-beta", stdout);
    }

    [Fact]
    public async Task BuildMetadata_AppendsShortSha()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "1", "-b");

        Assert.Matches(@"^1\.202502\.1\+[a-f0-9]{7}$", stdout);
    }

    [Fact]
    public async Task PrereleaseAndBuildMetadata_Combined()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "1", "-p", "rc", "-b");

        Assert.Matches(@"^1\.202502\.1-rc\+[a-f0-9]{7}$", stdout);
    }

    [Fact]
    public async Task JsonOutput_ContainsAllFields()
    {
        using var repo = new GitTestRepoBuilder()
            .WithCommit("feat: initial", Feb23)
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("scalver", repo.RepoPath, "-m", "2", "-o", "json");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("2.202502.1", json.RootElement.GetProperty("version").GetString());
        Assert.Equal("2.202502.1", json.RootElement.GetProperty("fullVersion").GetString());
        Assert.Equal("{MAJOR}.{YYYY}{0M}.{PATCH}", json.RootElement.GetProperty("schema").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("commitCount").GetInt32());
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
            "scalver", repo.RepoPath, "--major", "1", "--folder", "api", "--buildmetadata");

        Assert.Equal($"1.202502.1+{apiSha}", stdout);
    }

    [Fact]
    public async Task MissingFolder_FailsClearly()
    {
        using var repo = new GitTestRepo();

        var (exitCode, stdout, stderr) = await ToolRunner.RunAsync(
            "scalver", repo.RepoPath, "--major", "1", "--folder", "missing");

        Assert.NotEqual(0, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("does not contain tracked files", stderr);
    }

    [Fact]
    public async Task InvalidOutputFormat_Fails()
    {
        using var repo = new GitTestRepo();

        var (exitCode, _, _) = await ToolRunner.RunAsync(
            "scalver", repo.RepoPath, "--major", "1", "--output", "yaml");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task NotAGitRepo_Fails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ReleaseTools_NoGit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var (exitCode, _, _) = await ToolRunner.RunAsync("scalver", tempDir, "-m", "1");

            Assert.NotEqual(0, exitCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
