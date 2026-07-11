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
    }
}

public sealed class ProviderProfile
{
    public string? Model { get; set; }

    public string? Url { get; set; }

    public ProviderProfile Clone() => new() { Model = Model, Url = Url };
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
