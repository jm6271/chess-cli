using System.Text.Json;

namespace ChessCli.Configuration;

public sealed class ConfigStore
{
    public ConfigStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "chess-cli",
            "config.json");
    }

    public string Path { get; }

    public AppConfig Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(Path))
        {
            // A first run is normal, so return usable defaults without warning.
            var initial = new AppConfig();
            initial.EnsureDefaults();
            return initial;
        }

        try
        {
            using var stream = File.OpenRead(Path);
            var config = JsonSerializer.Deserialize(stream, AppJsonContext.Default.AppConfig) ?? new AppConfig();
            config.EnsureDefaults();
            return config;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken preferences file should not prevent the game from starting.
            warning = $"Could not read config '{Path}': {exception.Message}. Using defaults.";
            var fallback = new AppConfig();
            fallback.EnsureDefaults();
            return fallback;
        }
    }

    public async Task SaveAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        config.EnsureDefaults();
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path + ".tmp";

        try
        {
            // Write a complete temporary file first, then replace the target so a
            // process interruption cannot leave a partially serialized config.
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    config,
                    AppJsonContext.Default.AppConfig,
                    cancellationToken);
            }

            File.Move(temporaryPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
