# Codex Instructions

## Token / context usage

- Be conservative with context usage.
- Do not scan the entire repository unless explicitly requested.
- Before reading many files, inspect the project structure first.
- Prefer reading only files directly related to the task.
- Avoid reading generated files, build outputs, binaries, and large data files.

## Ignore paths

Do not read or analyze these paths unless explicitly requested:

- bin/
- obj/
- .vs/
- .git/
- node_modules/
- packages/
- *.dll
- *.exe
- *.pdb
- *.zip

## Project context

This repository is a .NET 8 Console App named `PttStockTelegramNotifier`.
It runs once and exits. GitHub Actions is responsible for scheduled execution.

The app:

- Crawls PTT Stock board HTML with `HttpClient`.
- Parses posts with HtmlAgilityPack.
- Applies `Ptt.FilterRules` from `appsettings.json`.
- Sends matched posts through Telegram Bot API.
- Updates `notified-posts.json` only after a Telegram notification succeeds.

## Project workflow

- For small changes, identify the minimal files needed.
- Keep changes focused and avoid unrelated refactors.
- Do not rewrite the whole project unless explicitly requested.
- Do not commit secrets, Telegram bot tokens, chat IDs, or local environment values.
- Treat `notified-posts.json` as committed app state; edit it only when the task explicitly involves notification history.
- If changing filter behavior, update `Services/PttPostFilter.cs`, `Models/FilterRule.cs`, `appsettings.json`, README, and filter tests as needed.
- If changing GitHub Actions behavior, update `.github/workflows/ptt-stock-notifier.yml` and README cron/action notes as needed.

## Filter rules

Filtering uses `Ptt.FilterRules` in `appsettings.json`.

- Run all `Action = Exclude` rules first.
- If any Exclude rule matches, do not notify.
- Then run `Action = Include` rules.
- Notify only when at least one Include rule matches.
- Empty rules must not match anything.
- For `MatchMode = All`, each configured condition group must match.
- For list fields such as `TitleTypes`, `Keywords`, and `Authors`, values inside the same field are treated as any-match within that condition group.

## Build and test commands

Use these commands from the repository root:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build -- --test-filter
```

For a normal local run, Telegram secrets must be provided through environment variables:

```powershell
$env:TELEGRAM_BOT_TOKEN = "<bot token>"
$env:TELEGRAM_CHAT_ID = "<chat id>"
dotnet run --configuration Release
```

## GitHub Actions

The workflow file is:

```text
.github/workflows/ptt-stock-notifier.yml
```

The workflow supports:

- `workflow_dispatch` for manual runs.
- `schedule` for cron runs.
- `contents: write` so `notified-posts.json` can be committed back after successful notifications.

Scheduled workflows only run on the GitHub default branch and may be delayed or skipped by GitHub under load. If schedule stops firing, modifying the cron expression can help GitHub re-register the scheduled workflow.
