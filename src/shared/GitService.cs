using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;

namespace ReleaseTools.Shared;

public class GitService
{
    public string? WorkingDirectory { get; set; }

    public async Task<bool> IsGitRepositoryAsync()
    {
        var result = await Cli.Wrap("git")
            .WithArguments("rev-parse --is-inside-work-tree")
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        return result.IsSuccess && result.StandardOutput.Trim() == "true";
    }

    public async Task<string?> GetLatestTagAsync(string? prefix = null)
    {
        var args = "describe --tags --abbrev=0";
        if (!string.IsNullOrEmpty(prefix))
        {
            args += $" --match \"{prefix}*\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            return null;

        return result.StandardOutput.Trim();
    }

    public async Task<string?> GetLatestStableTagAsync(string? prefix = null)
    {
        var tag = await GetLatestTagAsync(prefix);
        if (tag == null)
            return null;

        var versionPart = prefix != null && tag.StartsWith(prefix)
            ? tag.Substring(prefix.Length)
            : tag;

        if (IsPreRelease(versionPart))
        {
            var allTags = await GetAllTagsAsync(prefix);
            foreach (var t in allTags)
            {
                var v = prefix != null && t.StartsWith(prefix) ? t.Substring(prefix.Length) : t;
                if (!IsPreRelease(v))
                    return t;
            }
            return null;
        }

        return tag;
    }

    private bool IsPreRelease(string version)
    {
        return version.Contains("-") || version.Contains("+");
    }

    public async Task<IEnumerable<string>> GetAllTagsAsync(string? prefix = null)
    {
        var args = "tag -l";
        if (!string.IsNullOrEmpty(prefix))
        {
            args += $" \"{prefix}*\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .ExecuteBufferedAsync();

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t));
    }

    public async Task<IEnumerable<(string Hash, string ShortHash, string Message, DateTimeOffset Date)>> GetCommitsSinceTagAsync(
        string tag,
        string? folder = null)
    {
        var args = $"log {tag}..HEAD --pretty=format:%H%x00%h%x00%s%x00%ci";
        if (!string.IsNullOrEmpty(folder))
        {
            args += $" -- \"{folder}\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            return Enumerable.Empty<(string, string, string, DateTimeOffset)>();

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('\0');
                if (parts.Length < 4)
                    return (null, null, null, DateTimeOffset.MinValue)!;
                return (parts[0], parts[1], parts[2], DateTimeOffset.Parse(parts[3]));
            })
            .Where(c => c.Item1 != null);
    }

    public async Task<(string Hash, string ShortHash, DateTimeOffset Date)> GetHeadInfoAsync()
    {
        var result = await Cli.Wrap("git")
            .WithArguments("log -1 --pretty=format:%H%x00%h%x00%ci")
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            throw new InvalidOperationException("Failed to get HEAD info");

        var parts = result.StandardOutput.Trim().Split('\0');
        var date = DateTimeOffset.Parse(parts[2]);

        return (parts[0], parts[1], date);
    }

    public async Task<int> CountCommitsSinceTagAsync(string tag, string? folder = null)
    {
        var args = $"rev-list {tag}..HEAD --count";
        if (!string.IsNullOrEmpty(folder))
        {
            args += $" -- \"{folder}\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            return 0;

        return int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    public async Task<int> CountCommitsOnDateAsync(DateTimeOffset date, DateGranularity granularity, string? folder = null)
    {
        var (since, until) = GetDateRange(date, granularity);

        var args = $"rev-list --count --since=\"{since:O}\" --until=\"{until:O}\" HEAD";
        if (!string.IsNullOrEmpty(folder))
        {
            args += $" -- \"{folder}\"";
        }

        var result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (!result.IsSuccess)
            return 0;

        return int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    private static (DateTimeOffset since, DateTimeOffset until) GetDateRange(DateTimeOffset date, DateGranularity granularity)
    {
        return granularity switch
        {
            DateGranularity.Day => (date.Date, date.Date.AddDays(1)),
            DateGranularity.Week => (GetStartOfWeek(date), GetStartOfWeek(date).AddDays(7)),
            DateGranularity.Month => (new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, date.Offset),
                                       new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, date.Offset).AddMonths(1)),
            DateGranularity.Year => (new DateTimeOffset(date.Year, 1, 1, 0, 0, 0, date.Offset),
                                      new DateTimeOffset(date.Year + 1, 1, 1, 0, 0, 0, date.Offset)),
            _ => (date.Date, date.Date.AddDays(1))
        };
    }

    private static DateTimeOffset GetStartOfWeek(DateTimeOffset date)
    {
        var diff = (date.DayOfWeek - DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-diff).Date;
    }

    public async Task CreateTagAsync(string tagName, string? message = null, bool annotated = false)
    {
        var tagMessage = message ?? $"Release {tagName}";
        var args = annotated
            ? $"tag -a \"{tagName}\" -m \"{tagMessage}\""
            : $"tag \"{tagName}\" -m \"{tagMessage}\"";

        await Cli.Wrap("git")
            .WithArguments(args)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .ExecuteAsync();
    }

    public async Task PushTagAsync(string tagName)
    {
        await Cli.Wrap("git")
            .WithArguments($"push origin {tagName}")
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .ExecuteAsync();
    }

    public VersionInfo ParseVersionFromTag(string tag, string? prefix)
    {
        var versionPart = prefix != null && tag.StartsWith(prefix)
            ? tag.Substring(prefix.Length)
            : tag;

        return ParseVersionString(versionPart);
    }

    public VersionInfo ParseVersionString(string version)
    {
        var preRelease = "";
        var buildMetadata = "";

        if (version.Contains('+'))
        {
            var parts = version.Split('+');
            version = parts[0];
            buildMetadata = parts[1];
        }

        if (version.Contains('-'))
        {
            var dashIndex = version.IndexOf('-');
            var potentialPre = version.Substring(dashIndex + 1);
            if (Regex.IsMatch(potentialPre, @"^[a-zA-Z0-9.-]+$"))
            {
                preRelease = potentialPre;
                version = version.Substring(0, dashIndex);
            }
        }

        var components = version.Split('.');

        return new VersionInfo(
            components.Length > 0 ? int.TryParse(components[0], out var m) ? m : 0 : 0,
            components.Length > 1 ? int.TryParse(components[1], out var mi) ? mi : 0 : 0,
            components.Length > 2 ? int.TryParse(components[2], out var p) ? p : 0 : 0,
            string.IsNullOrEmpty(preRelease) ? null : preRelease,
            string.IsNullOrEmpty(buildMetadata) ? null : buildMetadata
        );
    }
}
