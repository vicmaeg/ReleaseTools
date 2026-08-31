using System.Globalization;
using CliWrap;
using CliWrap.Buffered;

namespace ReleaseTools.Shared;

public sealed record GitHead(string Hash, string ShortHash, DateTimeOffset Date);

public sealed class GitService
{
    public string? WorkingDirectory { get; init; }

    public async Task<string?> ValidateFolderAsync(
        string? folder,
        CancellationToken cancellationToken = default)
    {
        await RunGitAsync(["rev-parse", "--show-toplevel"], cancellationToken);

        if (string.IsNullOrWhiteSpace(folder))
            return null;

        if (Path.IsPathRooted(folder))
            throw new ArgumentException("Folder must be relative to the repository root", nameof(folder));

        var normalized = folder.Replace('\\', '/').Trim().TrimEnd('/');
        if (normalized is "" or ".")
            return null;

        if (normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("Folder must be a normalized path inside the repository", nameof(folder));

        var result = await RunGitAsync(
            ["ls-files", "--error-unmatch", "--", ToLiteralPathspec(normalized)],
            cancellationToken,
            allowFailure: true);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new ArgumentException($"Folder '{folder}' does not contain tracked files at HEAD", nameof(folder));

        return normalized;
    }

    public async Task<GitHead> GetHeadInfoAsync(
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "log", "-1", "--format=%H%x00%h%x00%cI" };
        AddFolder(args, folder);

        var result = await RunGitAsync(args, cancellationToken);
        var parts = result.StandardOutput.Trim().Split('\0');

        if (parts.Length != 3 || !DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            throw new InvalidOperationException("Git returned invalid HEAD information");

        return new GitHead(parts[0], parts[1], date.ToUniversalTime());
    }

    public async Task<IReadOnlyList<string>> GetReachableTagsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await RunGitAsync(["tag", "--merged", "HEAD"], cancellationToken);

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<IReadOnlyList<string>> GetCommitMessagesAsync(
        string? tag = null,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        var revision = tag is null ? "HEAD" : $"{tag}..HEAD";
        var args = new List<string> { "log", revision, "--format=%B%x00" };
        AddFolder(args, folder);

        var result = await RunGitAsync(args, cancellationToken);

        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(message => message.Trim('\r', '\n'))
            .Where(message => message.Length > 0)
            .ToArray();
    }

    public async Task<int> CountCommitsOnDateAsync(
        DateTimeOffset date,
        DateGranularity granularity,
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = GetDateRange(date, granularity);
        var args = new List<string>
        {
            "log",
            "HEAD",
            "--format=%cI%x00",
            $"--since={start.AddSeconds(-1):O}",
            $"--before={end:O}"
        };
        AddFolder(args, folder);

        var result = await RunGitAsync(args, cancellationToken);

        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Select(value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime())
            .Count(timestamp => timestamp >= start && timestamp < end);
    }

    private async Task<BufferedCommandResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        var result = await Cli.Wrap("git")
            .WithArguments(arguments)
            .WithWorkingDirectory(WorkingDirectory ?? ".")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (!allowFailure && !result.IsSuccess)
        {
            var detail = result.StandardError.Trim();
            throw new InvalidOperationException(
                detail.Length == 0 ? "Git command failed" : $"Git command failed: {detail}");
        }

        return result;
    }

    private static void AddFolder(List<string> arguments, string? folder)
    {
        if (folder is null)
            return;

        arguments.Add("--");
        arguments.Add(ToLiteralPathspec(folder));
    }

    private static string ToLiteralPathspec(string folder) => $":(top,literal){folder}";

    private static (DateTimeOffset Start, DateTimeOffset End) GetDateRange(
        DateTimeOffset date,
        DateGranularity granularity)
    {
        var utc = date.ToUniversalTime();
        var day = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);

        return granularity switch
        {
            DateGranularity.Day => (day, day.AddDays(1)),
            DateGranularity.Week => (StartOfIsoWeek(day), StartOfIsoWeek(day).AddDays(7)),
            DateGranularity.Month => (
                new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)),
            DateGranularity.Year => (
                new DateTimeOffset(utc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(utc.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null)
        };
    }

    private static DateTimeOffset StartOfIsoWeek(DateTimeOffset date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
