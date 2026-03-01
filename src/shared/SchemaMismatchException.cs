namespace ReleaseTools.Shared;

public class SchemaMismatchException : Exception
{
    public VersioningMode RequestedMode { get; }
    public VersioningMode ExistingMode { get; }
    public string? ExistingTag { get; }

    public SchemaMismatchException(string message, VersioningMode requestedMode, VersioningMode existingMode, string? existingTag = null)
        : base(message)
    {
        RequestedMode = requestedMode;
        ExistingMode = existingMode;
        ExistingTag = existingTag;
    }
}
