namespace ReleaseTools.Shared;

public record VersionInfo(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease,
    string? BuildMetadata,
    VersioningMode Mode
);
