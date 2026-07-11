# chess-cli

`chess-cli` tracks a standard chess game while a human moves pieces on a physical board and an LLM supplies moves through an OpenAI-compatible API.

## Requirements
- Ollama, OpenAI, or another server exposing `POST /v1/chat/completions`
- A model name

For OpenAI, set the API key in the environment before starting the program:

```powershell
$env:OPENAI_API_KEY = "your-key"
```

API keys are never stored in the application config file.

## Run

Ollama is the default provider and the LLM plays black by default:

```bash
chess-cli --model gemma3:12b
```

Use OpenAI and have the model open as white:

```bash
chess-cli --provider openai --model your-model --llm-color white
```

Use any OpenAI-compatible endpoint:

```powershell
chess-cli --provider compatible --url http://localhost:8080/v1 --model local-model
```

Load a game and automatically save it when the game ends:

```powershell
chess-cli --provider ollama --model gemma3:12b --load game.pgn --save finished.pgn
```

Run `chess-cli --help` for all startup options.

Print the installed application version:

```bash
chess-cli --version
```

## Playing

Enter a legal move in Standard Algebraic Notation, such as `e4`, `Nf3`, `O-O`, or `Qh4#`. The configured LLM color moves automatically after a human move. Model responses are checked against the legal moves; an invalid response is retried up to three times without changing the board.

Interactive commands:

- `/move` asks the LLM to make exactly one move for the current side, then returns to the prompt. This supports switching sides or manually advancing LLM-vs-LLM play.
- `/board` displays the current ASCII board.
- `/save [path]` saves the current game. `.pgn` writes PGN; `.txt` and extensionless paths write numbered SAN movetext.
- `/new` starts over after confirming any unsaved moves can be discarded.
- `/provider ollama|openai|compatible` switches provider.
- `/model <id>` changes the current provider's model.
- `/url <uri>` changes the current provider's API base URL.
- `/help` shows command help.
- `/quit` exits, confirming first if moves have not been saved.

Changes made with `/provider`, `/model`, and `/url` are persisted as per-provider profiles under the user's application-data directory at `chess-cli/config.json`. Startup arguments override those preferences for one run only.
