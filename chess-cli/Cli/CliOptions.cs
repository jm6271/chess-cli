namespace ChessCli.Cli;

public sealed record CliOptions(
    string? LoadPath,
    string? SavePath,
    string? LlmColor,
    string? Provider,
    string? Model,
    string? Url,
    string? ReasoningEffort,
    bool Debug);
