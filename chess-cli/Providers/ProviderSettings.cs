using ChessCli.Configuration;

namespace ChessCli.Providers;

public sealed class ProviderSettings
{
    public required string Provider { get; set; }

    public string? Model { get; set; }

    public string? Url { get; set; }

    public string ReasoningEffort { get; set; } = ReasoningEfforts.Medium;

    public ProviderProfile ToProfile() => new()
    {
        Model = Model,
        Url = Url,
        ReasoningEffort = ReasoningEffort
    };

    public void Validate()
    {
        if (!ProviderNames.IsValid(Provider))
        {
            throw new InvalidOperationException($"Unknown provider '{Provider}'.");
        }

        if (!ReasoningEfforts.IsValid(ReasoningEffort))
        {
            throw new InvalidOperationException(
                "Reasoning effort must be 'low', 'medium', or 'high'.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException(
                "No model is configured. Use --model <id> or /model <id> before requesting a move.");
        }

        if (string.IsNullOrWhiteSpace(Url) ||
            !Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "No valid HTTP(S) provider URL is configured. Use --url <uri> or /url <uri>.");
        }
    }

    public static ProviderSettings Resolve(
        AppConfig config,
        string? providerOverride,
        string? modelOverride,
        string? urlOverride,
        string? reasoningEffortOverride)
    {
        // Command-line values take precedence for this run; saved profiles supply
        // anything not explicitly overridden without changing the saved config.
        var provider = ProviderNames.Normalize(providerOverride ?? config.SelectedProvider);
        var profile = config.GetProfile(provider);
        return new ProviderSettings
        {
            Provider = provider,
            Model = modelOverride ?? profile.Model,
            Url = urlOverride ?? profile.Url,
            ReasoningEffort = reasoningEffortOverride ?? profile.ReasoningEffort
        };
    }
}
