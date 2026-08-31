namespace ReleaseTools.Shared;

public static class MetadataService
{
    public static string FormatFullVersion(string version, string? prerelease, string? buildMetadata)
    {
        var result = version;

        if (!string.IsNullOrEmpty(prerelease))
            result += $"-{prerelease}";

        if (!string.IsNullOrEmpty(buildMetadata))
            result += $"+{buildMetadata}";

        return result;
    }
}
