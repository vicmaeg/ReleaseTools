using System.Text.RegularExpressions;

namespace ReleaseTools.Shared;

public class CommitAnalyzer
{
    private static readonly Regex ConventionalCommitRegex = new(
        @"^(?<type>\w+)(?:\((?<scope>\w+)\))?(?<breaking>!):\s*(?<description>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex BreakingChangeFooterRegex = new(
        @"BREAKING CHANGE:\s*(.+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> FeatureTypes = new() { "feat" };
    private static readonly HashSet<string> PatchTypes = new() { "fix", "perf", "revert" };

    public CommitInfo ParseCommit(string hash, string shortHash, string message, DateTimeOffset date)
    {
        var match = ConventionalCommitRegex.Match(message);

        if (!match.Success)
        {
            return new CommitInfo(hash, shortHash, message, "other", null, false, date);
        }

        var type = match.Groups["type"].Value.ToLowerInvariant();
        var scope = match.Groups["scope"].Success ? match.Groups["scope"].Value : null;
        var breaking = match.Groups["breaking"].Success;

        if (!breaking && BreakingChangeFooterRegex.IsMatch(message))
        {
            breaking = true;
        }

        return new CommitInfo(hash, shortHash, message, type, scope, breaking, date);
    }

    public VersionIncrement DetermineIncrement(IEnumerable<CommitInfo> commits)
    {
        var commitList = commits.ToList();

        if (commitList.Any(c => c.Breaking))
            return VersionIncrement.Major;

        if (commitList.Any(c => FeatureTypes.Contains(c.Type)))
            return VersionIncrement.Minor;

        if (commitList.Any(c => PatchTypes.Contains(c.Type)))
            return VersionIncrement.Patch;

        return VersionIncrement.None;
    }

    public string GetIncrementReason(IEnumerable<CommitInfo> commits, VersionIncrement increment)
    {
        var commitList = commits.ToList();

        return increment switch
        {
            VersionIncrement.Major when commitList.Any(c => c.Breaking)
                => "breaking changes detected",
            VersionIncrement.Minor when commitList.Any(c => FeatureTypes.Contains(c.Type))
                => "feat commits detected",
            VersionIncrement.Patch when commitList.Any(c => PatchTypes.Contains(c.Type))
                => "fix/perf commits detected",
            _ => "no version-relevant commits"
        };
    }
}
