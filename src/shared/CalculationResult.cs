namespace ReleaseTools.Shared;

public record CalculationResult(
    string Version,
    string FullVersion,
    string? BaseTag,
    VersionInfo? BaseVersion,
    int CommitsSinceTag,
    string? IncrementReason,
    string Schema,
    string? Prerelease,
    string? BuildMetadata
);
