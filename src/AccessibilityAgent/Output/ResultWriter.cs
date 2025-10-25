using System.Buffers;
using System.Text;
using System.Text.Json;
using AccessibilityAgent.Checks;
using AccessibilityAgent.Cli;

namespace AccessibilityAgent.Output;

internal enum OutputFormat
{
    Text,
    Json
}

internal static class ResultWriter
{
    public static OutputFormat DetermineFormat(ParsedArguments parsed)
    {
        if (parsed.HasFlag(["json"]))
        {
            return OutputFormat.Json;
        }

        var formatValue = parsed.GetOption(["format"]);
        if (string.Equals(formatValue, "json", StringComparison.OrdinalIgnoreCase))
        {
            return OutputFormat.Json;
        }

        return OutputFormat.Text;
    }

    public static void Write(IEnumerable<CheckResult> results, OutputFormat format, bool includeSummary)
    {
        var materialized = results.ToList();

        if (format == OutputFormat.Json)
        {
            WriteAsJson(materialized);
            return;
        }

        foreach (var result in materialized)
        {
            var status = result.Success ? "OK" : "FAIL";
            var durationSuffix = result.DurationMs is { } ms ? $" ({ms:F1} ms)" : string.Empty;
            Console.WriteLine($"{status,-4} {result.Check}: {result.Message}{durationSuffix}");
        }

        if (includeSummary && materialized.Count > 1)
        {
            var successCount = materialized.Count(r => r.Success);
            var failureCount = materialized.Count - successCount;
            Console.WriteLine($"Total checks: {materialized.Count}, success: {successCount}, failed: {failureCount}");
        }
    }

    private static void WriteAsJson(IReadOnlyList<CheckResult> results)
    {
        // Пишем JSON в буфер, затем в Console.Out — это поддерживает перенаправление вывода в тестах
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();

        foreach (var result in results)
        {
            writer.WriteStartObject();
            writer.WriteString("check", result.Check);
            writer.WriteBoolean("success", result.Success);
            writer.WriteString("message", result.Message);

            if (result.DurationMs.HasValue)
            {
                writer.WriteNumber("durationMs", Math.Round(result.DurationMs.Value, 3));
            }

            if (result.Data is { Count: > 0 })
            {
                writer.WriteStartObject("data");
                foreach (var kvp in result.Data)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.Flush();

        var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        Console.Out.Write(json);
    }
}
