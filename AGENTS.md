# Agents.md

## Repository layout

- `chess-cli.slnx` is the .NET 10 solution.
- `chess-cli/` contains the console application: `Cli/` for interactive input, `Game/` for chess and notation logic, `Providers/` for LLM integrations, and `Configuration/` for persisted settings.
- `chess-cli.Tests/` contains xUnit tests mirroring the application areas.
- `README.md` documents prerequisites, startup options, and interactive commands.

Run the test suite with `dotnet test chess-cli.slnx`.

## Coding conventions

Add concise comments to any code that you generate, to help keep things easy to understand.
