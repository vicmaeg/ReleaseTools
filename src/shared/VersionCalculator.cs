namespace ReleaseTools.Shared;

public class VersionCalculator
{
    private readonly SchemaParser _schemaParser = new();
    private readonly CommitAnalyzer _commitAnalyzer = new();
    private readonly GitService _gitService = new();

    public VersionCalculator()
    {
    }

    public VersionCalculator(string workingDirectory)
    {
        _gitService.WorkingDirectory = workingDirectory;
    }

    public async Task<CalculationResult> CalculateNextVersionAsync(
        string schema,
        string? prefix = null,
        string? folder = null)
    {
        var mode = _schemaParser.DetectMode(schema);

        if (!_schemaParser.ValidateSchema(schema, mode))
        {
            throw new ArgumentException($"Invalid schema '{schema}' for {mode} mode");
        }

        var headInfo = await _gitService.GetHeadInfoAsync();
        var latestTag = await _gitService.GetLatestStableTagAsync(prefix);

        if (latestTag == null)
        {
            return CalculateInitialVersion(schema, mode, headInfo);
        }

        var baseVersion = _gitService.ParseVersionFromTag(latestTag, prefix, mode);

        if (baseVersion.Mode != mode)
        {
            throw new SchemaMismatchException(
                $"Current schema: {schema} ({mode})\nLatest tag: {latestTag} ({baseVersion.Mode})\n\nCannot switch between versioning modes automatically.\nPlease manually tag with the new schema to continue.\n\nExample: git tag 0.1.0 -m \"Switch to {mode}\"",
                mode,
                baseVersion.Mode,
                latestTag
            );
        }

        var commits = (await _gitService.GetCommitsSinceTagAsync(latestTag, folder)).ToList();
        var numCommits = await _gitService.CountCommitsSinceTagAsync(latestTag, folder);

        return mode switch
        {
            VersioningMode.SemVer => CalculateSemVer(schema, baseVersion, commits, headInfo, numCommits, latestTag),
            VersioningMode.CalVer => CalculateCalVer(schema, baseVersion, headInfo, numCommits, latestTag),
            VersioningMode.ScalVer => CalculateScalVer(schema, baseVersion, commits, headInfo, numCommits, latestTag),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private CalculationResult CalculateInitialVersion(
        string schema,
        VersioningMode mode,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo)
    {
        var version = mode switch
        {
            VersioningMode.SemVer => "0.1.0",
            VersioningMode.CalVer => FormatCalVer(schema, headInfo.Date, 0),
            VersioningMode.ScalVer => FormatScalVer(schema, 0, headInfo.Date, 0),
            _ => throw new ArgumentOutOfRangeException()
        };

        return new CalculationResult(
            Version: version,
            TagName: version,
            Mode: mode,
            BaseTag: null,
            BaseVersion: null,
            CommitsSinceTag: 0,
            Increment: VersionIncrement.None,
            IncrementReason: "initial version",
            Schema: schema
        );
    }

    private CalculationResult CalculateSemVer(
        string schema,
        VersionInfo baseVersion,
        List<CommitInfo> commits,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        int numCommits,
        string latestTag)
    {
        var increment = _commitAnalyzer.DetermineIncrement(commits);

        var newVersion = increment switch
        {
            VersionIncrement.Major => new VersionInfo(baseVersion.Major + 1, 0, 0, null, null, VersioningMode.SemVer),
            VersionIncrement.Minor => new VersionInfo(baseVersion.Major, baseVersion.Minor + 1, 0, null, null, VersioningMode.SemVer),
            VersionIncrement.Patch => new VersionInfo(baseVersion.Major, baseVersion.Minor, baseVersion.Patch + 1, null, null, VersioningMode.SemVer),
            _ => baseVersion
        };

        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

        return new CalculationResult(
            Version: versionString,
            TagName: versionString,
            Mode: VersioningMode.SemVer,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            Increment: increment,
            IncrementReason: _commitAnalyzer.GetIncrementReason(commits, increment),
            Schema: schema
        );
    }

    private CalculationResult CalculateCalVer(
        string schema,
        VersionInfo baseVersion,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        int numCommits,
        string latestTag)
    {
        var newPatch = baseVersion.Patch + 1;

        var newVersion = new VersionInfo(0, 0, newPatch, null, null, VersioningMode.CalVer);

        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

        return new CalculationResult(
            Version: versionString,
            TagName: versionString,
            Mode: VersioningMode.CalVer,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            Increment: newPatch > baseVersion.Patch ? VersionIncrement.Patch : VersionIncrement.None,
            IncrementReason: newPatch > baseVersion.Patch ? "same date window, incrementing patch" : "new date window",
            Schema: schema
        );
    }

    private CalculationResult CalculateScalVer(
        string schema,
        VersionInfo baseVersion,
        List<CommitInfo> commits,
        (string Hash, string ShortHash, DateTimeOffset Date) headInfo,
        int numCommits,
        string latestTag)
    {
        var breakingIncrement = _commitAnalyzer.DetermineIncrement(commits) == VersionIncrement.Major;
        var wouldShrink = WouldDateShrink(schema, baseVersion, headInfo.Date);

        int newMajor;
        int newPatch;

        if (breakingIncrement || wouldShrink)
        {
            newMajor = baseVersion.Major + 1;
            newPatch = 0;
        }
        else
        {
            newMajor = baseVersion.Major;
            newPatch = baseVersion.Patch + 1;
        }

        var newVersion = new VersionInfo(newMajor, 0, newPatch, null, null, VersioningMode.ScalVer);

        var versionString = _schemaParser.ApplyVersion(schema, newVersion, headInfo.Date, numCommits, headInfo.ShortHash, headInfo.Hash);

        var increment = breakingIncrement ? VersionIncrement.Major :
                       wouldShrink ? VersionIncrement.Major :
                       newPatch > baseVersion.Patch ? VersionIncrement.Patch : VersionIncrement.None;

        return new CalculationResult(
            Version: versionString,
            TagName: versionString,
            Mode: VersioningMode.ScalVer,
            BaseTag: latestTag,
            BaseVersion: baseVersion,
            CommitsSinceTag: numCommits,
            Increment: increment,
            IncrementReason: breakingIncrement ? "breaking changes detected" :
                            wouldShrink ? "date would shrink, incrementing major" :
                            "same date window, incrementing patch",
            Schema: schema
        );
    }

    private bool WouldDateShrink(string schema, VersionInfo baseVersion, DateTimeOffset newDate)
    {
        var currentDateWidth = GetDateWidth(schema);
        var newDateWidth = GetDateWidth(schema);

        return currentDateWidth > newDateWidth;
    }

    private int GetDateWidth(string schema)
    {
        if (schema.Contains("{YYYY}{0M}{0D}") || schema.Contains("{YYYY}{MM}{DD}"))
            return 3;
        if (schema.Contains("{YYYY}{0M}") || schema.Contains("{YYYY}{MM}"))
            return 2;
        if (schema.Contains("{YYYY}"))
            return 1;
        return 0;
    }

    private string FormatCalVer(string schema, DateTimeOffset date, int patch)
    {
        var version = new VersionInfo(0, 0, patch, null, null, VersioningMode.CalVer);
        return _schemaParser.ApplyVersion(schema, version, date, 0, "", "");
    }

    private string FormatScalVer(string schema, int major, DateTimeOffset date, int patch)
    {
        var version = new VersionInfo(major, 0, patch, null, null, VersioningMode.ScalVer);
        return _schemaParser.ApplyVersion(schema, version, date, 0, "", "");
    }
}
