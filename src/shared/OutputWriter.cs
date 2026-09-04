using System.Text.Json;

namespace ReleaseTools.Shared;

public static class OutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Write(CalculationResult result, OutputFormat format)
    {
        if (format == OutputFormat.Json)
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        else
            Console.Write(result.FullVersion);
    }
}
