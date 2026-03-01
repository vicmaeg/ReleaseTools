namespace ReleaseTools.Shared;

public record CommitInfo(
    string Hash,
    string ShortHash,
    string Message,
    string Type,
    string? Scope,
    bool Breaking,
    DateTimeOffset Date
);
