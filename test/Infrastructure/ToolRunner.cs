using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;

namespace ReleaseTools.Tests.Infrastructure;

public static class ToolRunner
{
    private static readonly string ToolsDir = Path.Combine(Path.GetTempPath(), "ReleaseTools_Tools");
    private static readonly Lazy<Task> BuildTools = new(BuildToolsAsync);

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string tool,
        string workingDirectory,
        params string[] args)
    {
        await BuildTools.Value;

        var cliArgs = new List<string> { Path.Combine(ToolsDir, $"{tool}.dll") };
        cliArgs.AddRange(args);

        var result = await Cli.Wrap("dotnet")
            .WithArguments(cliArgs)
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return (result.ExitCode, result.StandardOutput.Trim(), result.StandardError);
    }

    private static async Task BuildToolsAsync()
    {
        Directory.CreateDirectory(ToolsDir);
        var repoRoot = FindRepoRoot();

        foreach (var tool in new[] { "semver", "calver", "scalver" })
        {
            var result = await Cli.Wrap("dotnet")
                .WithArguments(new[] { "build", Path.Combine(repoRoot, "src", $"{tool}.cs"), "-c", "Release", "-o", ToolsDir })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to build {tool}:\n{result.StandardOutput}\n{result.StandardError}");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "semver.cs")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root containing src/semver.cs");
    }
}
