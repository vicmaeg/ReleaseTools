namespace ReleaseTools.Shared;

public class SchemaMismatchException : Exception
{
    public string? ExistingTag { get; }

    public SchemaMismatchException(string message, string? existingTag = null)
        : base(message)
    {
        ExistingTag = existingTag;
    }
}
