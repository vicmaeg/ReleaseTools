namespace ReleaseTools.Shared;

public sealed record CalculationResult(
    string Version,
    string FullVersion,
    string? BaseTag,
    int CommitCount,
    string IncrementReason,
    string Schema,
    string? Prerelease,
    string? BuildMetadata
);
