namespace ChessCli.Cli;

/// <summary>Writes optional ANSI styling while keeping redirected output plain.</summary>
internal sealed class TerminalText
{
    private const string Reset = "\u001b[0m";
    private readonly bool _useColor;

    public TerminalText(bool useColor)
    {
        _useColor = useColor;
    }

    public Task WriteAsync(TextWriter writer, string value, string ansiCode) =>
        writer.WriteAsync(Format(value, ansiCode));

    public Task WriteLineAsync(TextWriter writer, string value, string ansiCode) =>
        writer.WriteLineAsync(Format(value, ansiCode));

    private string Format(string value, string ansiCode) =>
        _useColor ? $"\u001b[{ansiCode}m{value}{Reset}" : value;
}
