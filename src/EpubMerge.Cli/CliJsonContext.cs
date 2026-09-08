using System.Text.Json.Serialization;

namespace EpubMerge.Cli;

internal sealed record CliProgressEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("completed")] int Completed,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("message")] string Message);

internal sealed record CliResultEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("output")] string Output,
    [property: JsonPropertyName("books")] int Books);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(CliProgressEvent))]
[JsonSerializable(typeof(CliResultEvent))]
internal partial class CliJsonContext : JsonSerializerContext;
