using System;
using System.IO;
using CliWrap;
using CliWrap.Buffered;

namespace ReleaseTools.Tests.Infrastructure;

public class GitTestRepo : IDisposable
{
    public string WorkingDirectory { get; }
    public string RepoPath { get; private set; }

    public GitTestRepo()
    {
        WorkingDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ReleaseTools_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkingDirectory);
        RepoPath = WorkingDirectory;

        InitializeRepo();
    }

    private void InitializeRepo()
    {
        RunGit("init -b main");
        RunGit("config user.email \"test@test.com\"");
        RunGit("config user.name \"Test User\"");

        AddFile("README.md", "# Test Repository");
        Commit("Initial commit");
    }

    public void AddFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(RepoPath, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content);
        RunGit($"add \"{relativePath}\"");
    }

    public void ModifyFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(RepoPath, relativePath);
        File.WriteAllText(fullPath, content);
        RunGit($"add \"{relativePath}\"");
    }

    public void Commit(string message, DateTimeOffset? date = null, string? body = null)
    {
        var args = $"commit --allow-empty -m \"{message}\"";
        if (body != null)
        {
            args += $" -m \"{body}\"";
        }
        if (date.HasValue)
        {
            args += $" --date=\"{date.Value:yyyy-MM-dd HH:mm:ss zzz}\"";
        }

        // The tools read the committer date (%ci), so it must be set explicitly for dated commits
        var env = date.HasValue
            ? new Dictionary<string, string?> { ["GIT_COMMITTER_DATE"] = date.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") }
            : null;

        RunGit(args, env);
    }

    public void Tag(string name, string? message = null)
    {
        var args = message != null
            ? $"tag -a \"{name}\" -m \"{message}\""
            : $"tag \"{name}\"";
        RunGit(args);
    }

    public void Checkout(string branch)
    {
        RunGit($"checkout -b {branch}");
    }

    public void CheckoutExisting(string branch)
    {
        RunGit($"checkout {branch}");
    }

    public void Merge(string branch)
    {
        RunGit($"merge --no-ff --no-edit {branch}");
    }

    public string GetShortHead()
    {
        return RunGitWithOutput("rev-parse --short HEAD");
    }

    private void RunGit(string args, IReadOnlyDictionary<string, string?>? env = null)
    {
        var cli = Cli.Wrap("git")
            .WithWorkingDirectory(RepoPath)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None);

        if (env != null)
        {
            cli = cli.WithEnvironmentVariables(env);
        }

        var result = cli.ExecuteBufferedAsync().GetAwaiter().GetResult();

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Git command failed: git {args}\n{result.StandardError}");
        }
    }

    private string RunGitWithOutput(string args)
    {
        var result = Cli.Wrap("git")
            .WithWorkingDirectory(RepoPath)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync()
            .GetAwaiter()
            .GetResult();

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Git command failed: git {args}\n{result.StandardError}");

        return result.StandardOutput.Trim();
    }

    public void Dispose()
    {
        if (Directory.Exists(WorkingDirectory))
        {
            try
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

public class GitTestRepoBuilder
{
    private readonly List<(string Path, string Content)> _files = new();
    private readonly List<(string Message, DateTimeOffset? Date)> _commits = new();
    private readonly List<(string Name, string? Message)> _tags = new();
    private string _initialCommitMessage = "Initial commit";
    private string _branchName = "main";

    public GitTestRepoBuilder WithFile(string path, string content)
    {
        _files.Add((path, content));
        return this;
    }

    public GitTestRepoBuilder WithCommit(string message, DateTimeOffset? date = null)
    {
        _commits.Add((message, date));
        return this;
    }

    public GitTestRepoBuilder WithTag(string name, string? message = null)
    {
        _tags.Add((name, message));
        return this;
    }

    public GitTestRepoBuilder WithInitialCommitMessage(string message)
    {
        _initialCommitMessage = message;
        return this;
    }

    public GitTestRepoBuilder WithBranch(string branchName)
    {
        _branchName = branchName;
        return this;
    }

    public GitTestRepo Build()
    {
        var repo = new GitTestRepo();

        if (_branchName != "main")
        {
            repo.Checkout(_branchName);
        }

        foreach (var file in _files)
        {
            repo.AddFile(file.Path, file.Content);
        }

        // Commit files if any were added
        if (_files.Count > 0)
        {
            repo.Commit(_initialCommitMessage);
        }

        // Create tags first (they tag the current HEAD)
        foreach (var tag in _tags)
        {
            repo.Tag(tag.Name, tag.Message);
        }

        // Then add additional commits (these come AFTER the tags)
        foreach (var commit in _commits)
        {
            repo.Commit(commit.Message, commit.Date);
        }

        return repo;
    }
}
