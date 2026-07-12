using System.Text.Json.Serialization;

namespace ChessCli.Configuration;

public sealed class AppConfig
{
    public string SelectedProvider { get; set; } = ProviderNames.Ollama;

    public Dictionary<string, ProviderProfile> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void EnsureDefaults()
    {
        // Deserialization may create a case-sensitive dictionary; normalize it
        // so provider names behave consistently across config files and commands.
        Providers = new Dictionary<string, ProviderProfile>(Providers, StringComparer.OrdinalIgnoreCase);
        EnsureProfile(ProviderNames.Ollama, "http://localhost:11434/v1/");
        EnsureProfile(ProviderNames.OpenAi, "https://api.openai.com/v1/");
        EnsureProfile(ProviderNames.Compatible, null);

        if (!ProviderNames.IsValid(SelectedProvider))
        {
            SelectedProvider = ProviderNames.Ollama;
        }
    }

    public ProviderProfile GetProfile(string provider)
    {
        // Callers can safely request a profile even when loading an older or
        // partially populated config file.
        EnsureDefaults();
        return Providers[provider];
    }

    private void EnsureProfile(string name, string? defaultUrl)
    {
        if (!Providers.TryGetValue(name, out var profile))
        {
            profile = new ProviderProfile();
            Providers[name] = profile;
        }

        profile.Url ??= defaultUrl;
        // Older config files have no reasoning field; use the new medium default.
        profile.ReasoningEffort = ReasoningEfforts.IsValid(profile.ReasoningEffort)
            ? ReasoningEfforts.Normalize(profile.ReasoningEffort)
            : ReasoningEfforts.Medium;
    }
}

public sealed class ProviderProfile
{
    public string? Model { get; set; }

    public string? Url { get; set; }

    public string ReasoningEffort { get; set; } = ReasoningEfforts.Medium;

    public ProviderProfile Clone() => new()
    {
        Model = Model,
        Url = Url,
        ReasoningEffort = ReasoningEffort
    };
}

public static class ReasoningEfforts
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static bool IsValid(string? value) =>
        value is not null &&
        (value.Equals(Low, StringComparison.OrdinalIgnoreCase) ||
         value.Equals(Medium, StringComparison.OrdinalIgnoreCase) ||
         value.Equals(High, StringComparison.OrdinalIgnoreCase));

    // Persist canonical values so config files remain predictable across commands.
    public static string Normalize(string value) => value.ToLowerInvariant() switch
    {
        Low => Low,
        Medium => Medium,
        High => High,
        _ => throw new ArgumentException($"Unknown reasoning effort '{value}'.")
    };
}

public static class ProviderNames
{
    public const string Ollama = "ollama";
    public const string OpenAi = "openai";
    public const string Compatible = "compatible";

    public static bool IsValid(string? value) =>
        value is not null &&
        (value.Equals(Ollama, StringComparison.OrdinalIgnoreCase) ||
         value.Equals(OpenAi, StringComparison.OrdinalIgnoreCase) ||
         value.Equals(Compatible, StringComparison.OrdinalIgnoreCase));

    // Keep provider keys canonical so each provider has exactly one persisted profile.
    public static string Normalize(string value) => value.ToLowerInvariant() switch
    {
        Ollama => Ollama,
        OpenAi => OpenAi,
        Compatible => Compatible,
        _ => throw new ArgumentException($"Unknown provider '{value}'.")
    };
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class AppJsonContext : JsonSerializerContext;
