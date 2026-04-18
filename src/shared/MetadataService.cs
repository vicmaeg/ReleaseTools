namespace ReleaseTools.Shared;

public class MetadataService
{
    public string? CalculatePrerelease(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;

        return identifier;
    }

    public string FormatFullVersion(string version, string? prerelease, string? buildMetadata)
    {
        var result = version;

        if (!string.IsNullOrEmpty(prerelease))
            result += $"-{prerelease}";

        if (!string.IsNullOrEmpty(buildMetadata))
            result += $"+{buildMetadata}";

        return result;
    }
}
