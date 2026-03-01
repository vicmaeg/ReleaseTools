namespace ReleaseTools.Shared;

public record CalculationResult(
    string Version,
    string? TagName,
    VersioningMode Mode,
    string? BaseTag,
    VersionInfo? BaseVersion,
    int CommitsSinceTag,
    VersionIncrement Increment,
    string? IncrementReason,
    string Schema
);
