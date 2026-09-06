using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("0.1.0", stdout);
    }

    [Fact]
    public async Task FeatCommit_Increments_Minor()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: add new feature")
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("1.1.0", stdout);
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

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.1.0", stdout);
    }

    [Fact]
    public async Task BreakingChange_Increments_Major()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat!: breaking API change")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("2.0.0", stdout);
    }

    [Fact]
    public async Task FixCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("fix: resolve issue")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.0.1", stdout);
    }

    [Fact]
    public async Task PerfCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("perf: optimize database queries")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.0.1", stdout);
    }

    [Fact]
    public async Task RevertCommit_Increments_Patch()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("revert: feat: old feature")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.0.1", stdout);
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

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.0.0", stdout);
    }

    [Fact]
    public async Task PreReleaseTags_AreSkipped()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithTag("1.1.0-alpha.1")
            .WithTag("1.1.0-beta.1")
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "-o", "json");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("1.0.0", json.RootElement.GetProperty("baseTag").GetString());
        Assert.Equal("1.0.0", json.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task MonorepoPrefix_FiltersTags()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("api-1.0.0")
            .WithTag("web-2.0.0")
            .WithCommit("feat: new api feature")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--prefix", "api-");

        Assert.Equal("1.1.0", stdout);
    }

    [Fact]
    public async Task FolderFilter_OnlyCountsFolderCommits()
    {
        using var repo = new GitTestRepo();
        repo.AddFile("web/page.txt", "initial web");
        repo.Commit("chore: add web app");
        repo.Tag("1.0.0");
        repo.AddFile("api/service.txt", "api change");
        repo.Commit("feat: api feature");

        var (_, apiStdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "-f", "api");
        var (_, webStdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "-f", "web");

        Assert.Equal("1.1.0", apiStdout);
        Assert.Equal("1.0.0", webStdout);
    }

    [Fact]
    public async Task Prerelease_AppendsIdentifier()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--prerelease", "alpha");

        Assert.Equal("1.1.0-alpha.1", stdout);
    }

    [Fact]
    public async Task BuildMetadata_AppendsShortSha()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "-b");

        Assert.Matches(@"^1\.1\.0\+[a-f0-9]{7}$", stdout);
    }

    [Fact]
    public async Task PrereleaseAndBuildMetadata_Combined()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--prerelease", "beta", "-b");

        Assert.Matches(@"^1\.1\.0-beta\.1\+[a-f0-9]{7}$", stdout);
    }

    [Fact]
    public async Task InvalidPrerelease_Fails()
    {
        using var repo = new GitTestRepoBuilder().Build();

        var (exitCode, _, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--prerelease", "alpha beta");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task JsonOutput_ContainsAllFields()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("feat: new feature")
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "-o", "json");

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("1.1.0", json.RootElement.GetProperty("version").GetString());
        Assert.Equal("1.1.0", json.RootElement.GetProperty("fullVersion").GetString());
        Assert.Equal("1.0.0", json.RootElement.GetProperty("baseTag").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("commitCount").GetInt32());
        Assert.Equal("feat commits detected", json.RootElement.GetProperty("incrementReason").GetString());
        Assert.Equal("{MAJOR}.{MINOR}.{PATCH}", json.RootElement.GetProperty("schema").GetString());
        Assert.False(json.RootElement.TryGetProperty("format", out _));
        Assert.False(json.RootElement.TryGetProperty("major", out _));
        Assert.False(json.RootElement.TryGetProperty("dateFormat", out _));
    }

    [Theory]
    [InlineData("feat(core-api): add endpoint")]
    [InlineData("feat(web/client): add screen")]
    [InlineData("feat(data.access): add query")]
    public async Task FlexibleScopes_IncrementMinor(string message)
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit(message)
            .Build();

        var (exitCode, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("1.1.0", stdout);
    }

    [Theory]
    [InlineData("BREAKING CHANGE: the old API was removed")]
    [InlineData("BREAKING-CHANGE: the old API was removed")]
    public async Task BreakingFooter_IncrementsMajor(string footer)
    {
        using var repo = new GitTestRepo();
        repo.Tag("1.2.3");
        repo.Commit("feat: replace API", body: footer);

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("2.0.0", stdout);
    }

    [Fact]
    public async Task BuildMetadataTag_IsAStableBase()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.2.3+build.7")
            .WithCommit("fix: repair issue")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.2.4", stdout);
    }

    [Fact]
    public async Task VPrefix_MustBeExplicit()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("v1.2.3")
            .WithCommit("fix: repair issue")
            .Build();

        var (_, bareOutput, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);
        var (_, prefixedOutput, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--prefix", "v");

        Assert.Equal("0.1.0", bareOutput);
        Assert.Equal("1.2.4", prefixedOutput);
    }

    [Fact]
    public async Task HighestReachableStableTag_IsSelected()
    {
        using var repo = new GitTestRepo();
        repo.Tag("1.0.0");
        repo.Commit("chore: prepare two");
        repo.Tag("2.0.0");
        repo.Commit("chore: prepare alpha");
        repo.Tag("2.1.0-alpha.1");
        repo.Commit("fix: repair issue");

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("2.0.1", stdout);
    }

    [Fact]
    public async Task HighestVersionWinsWhenLowerStableTagIsNearer()
    {
        using var repo = new GitTestRepo();
        repo.Tag("2.0.0");
        repo.Commit("chore: maintain older line");
        repo.Tag("1.5.0");
        repo.Commit("fix: repair issue");

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("2.0.1", stdout);
    }

    [Fact]
    public async Task PlainTagWinsTieWithBuildMetadataTag()
    {
        using var repo = new GitTestRepo();
        repo.Tag("1.2.3");
        repo.Tag("1.2.3+build.7");

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--output", "json");

        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("1.2.3", json.RootElement.GetProperty("baseTag").GetString());
    }

    [Fact]
    public async Task Prefix_IsMatchedExactly()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("api.v-1.0.0")
            .WithCommit("feat: add endpoint")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "--prefix", "api.v-");

        Assert.Equal("1.1.0", stdout);
    }

    [Fact]
    public async Task Prefix_DoesNotMatchLongerName()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("apix-1.0.0")
            .WithTag("api-1.0.0")
            .WithCommit("feat: add endpoint")
            .Build();

        var (_, apiPrefix, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--prefix", "api");
        var (_, apiDash, _) = await ToolRunner.RunAsync("semver", repo.RepoPath, "--prefix", "api-");

        Assert.Equal("0.1.0", apiPrefix);
        Assert.Equal("1.1.0", apiDash);
    }

    [Fact]
    public async Task TagOnUnmergedBranch_IsIgnored()
    {
        using var repo = new GitTestRepo();
        repo.Tag("1.0.0");
        repo.Checkout("experimental");
        repo.Commit("feat: future feature");
        repo.Tag("9.0.0");
        repo.CheckoutExisting("main");
        repo.Commit("fix: current fix");

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.0.1", stdout);
    }

    [Fact]
    public async Task MalformedAndUnrelatedTags_AreIgnored()
    {
        using var repo = new GitTestRepo();
        repo.Tag("1.0.0");
        repo.Tag("01.2.3");
        repo.Tag("deployment-ready");
        repo.Commit("fix: current fix");

        var (_, stdout, _) = await ToolRunner.RunAsync("semver", repo.RepoPath);

        Assert.Equal("1.0.1", stdout);
    }

    [Fact]
    public async Task NoRelevantChange_PrereleaseIsStillApplied()
    {
        using var repo = new GitTestRepoBuilder()
            .WithTag("1.0.0")
            .WithCommit("docs: clarify usage")
            .Build();

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "-p", "alpha");

        Assert.Equal("1.0.0-alpha.1", stdout);
    }

    [Fact]
    public async Task InitialPrerelease_CountsMatchingHistory()
    {
        using var repo = new GitTestRepo();

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "--prerelease", "alpha");

        Assert.Equal("0.1.0-alpha.1", stdout);
    }

    [Fact]
    public async Task FolderScope_UsesLatestFolderCommitForBuildMetadata()
    {
        using var repo = new GitTestRepo();
        repo.AddFile("api/service.txt", "api");
        repo.Commit("feat: api");
        var apiSha = repo.GetShortHead();
        repo.AddFile("web/page.txt", "web");
        repo.Commit("feat: web");

        var (_, stdout, _) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "--folder", "api", "--buildmetadata");

        Assert.Equal($"0.1.0+{apiSha}", stdout);
    }

    [Fact]
    public async Task MissingFolder_FailsClearly()
    {
        using var repo = new GitTestRepo();

        var (exitCode, stdout, stderr) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "--folder", "missing");

        Assert.NotEqual(0, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("does not contain tracked files", stderr);
    }

    [Theory]
    [InlineData("../api")]
    [InlineData("apps//api")]
    public async Task EscapingOrUnnormalizedFolder_Fails(string folder)
    {
        using var repo = new GitTestRepo();

        var (exitCode, _, _) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "--folder", folder);

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task AbsoluteFolder_Fails()
    {
        using var repo = new GitTestRepo();

        var (exitCode, _, _) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "--folder", Path.GetFullPath(repo.RepoPath));

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task InvalidOutputFormat_Fails()
    {
        using var repo = new GitTestRepo();

        var (exitCode, stdout, stderr) = await ToolRunner.RunAsync(
            "semver", repo.RepoPath, "--output", "yaml");

        Assert.NotEqual(0, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("Failed to convert", stderr);
    }

    [Fact]
    public async Task NotAGitRepo_Fails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ReleaseTools_NoGit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var (exitCode, _, _) = await ToolRunner.RunAsync("semver", tempDir);

            Assert.NotEqual(0, exitCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
