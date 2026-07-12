using System.Text.Json.Serialization;

namespace ChessCli.Providers;

internal sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required ChatMessage[] Messages { get; init; }

    // Reasoning-capable OpenAI-compatible models use this to favor deeper analysis.
    [JsonPropertyName("reasoning_effort")]
    public string ReasoningEffort { get; init; } = "medium";
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

internal sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public ChatChoice[] Choices { get; init; } = [];
}

internal sealed class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
internal partial class ChatJsonContext : JsonSerializerContext;
