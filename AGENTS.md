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
- output/
- .vs/
- .git/
- node_modules/
- packages/
- *.dll
- *.exe
- *.pdb
- *.zip

## Project context

This repository is a .NET 8 Console App named `DailyTrainTimetable`.
It fetches Taiwan Railway daily train timetable data from TDX, normalizes it into a small static JSON dataset, and publishes that dataset through GitHub Pages.

The app:

- Uses the TDX TRA DailyTrainTimetable API:
  `https://tdx.transportdata.tw/api/basic/v3/Rail/TRA/DailyTrainTimetable/TrainDate/{yyyy-MM-dd}`.
- Authenticates with TDX through OAuth client credentials.
- Reads credentials from environment variables:
  - `TDX_CLIENT_ID`
  - `TDX_CLIENT_SECRET`
- Generates data starting from the current `Asia/Taipei` date.
- Defaults to today plus the next 6 days, and supports `--days <number>`.
- Writes JSON files under `output/data`.
- Continues past a failed date, but exits non-zero if not all requested dates succeed.

## Project workflow

- Keep changes focused and avoid unrelated refactors.
- This is currently a small single-project app; prefer simple changes in `Program.cs` unless a larger structure is clearly justified.
- Do not commit TDX credentials, local secrets, or environment-specific values.
- Treat `output/` as generated deploy data. Do not manually edit generated JSON unless the task explicitly asks for generated sample data.
- If changing the generated JSON schema, update `Program.cs`, README schema examples, app SQLite notes, and `DataVersion` as needed.
- If changing date range behavior, update `ParseDays`, README local run notes, and GitHub Actions arguments as needed.
- If changing TDX request behavior, keep retry/rate-limit handling in mind, especially HTTP 429 and `Retry-After`.
- If changing GitHub Actions behavior, update `.github/workflows/update-train-data.yml`, `.github/workflows/keepalive.yml`, `.github/workflows/staleness-alert.yml`, and README schedule/Pages notes together.
- Never remove the `Refresh keepalive commit` step or `.github/heartbeat.json`: GitHub auto-disables `schedule` workflows after 60 days without repository push activity (`disabled_inactivity`). The step pushes a refreshed `lastRefreshedAt` every `intervalDays` (55) days and is a no-op otherwise. Keep `intervalDays < 60`. `.github/workflows/keepalive.yml` duplicates this logic as a backup so a disabled main workflow can still self-heal; keep both.

## Generated files

The app writes these files:

```text
output/data/{yyyyMMdd}.json
output/data/latest.json
output/data/stations.json
```

`{yyyyMMdd}.json` contains one date of train timetable data.
`latest.json` lists successful dates, generation time, and data version.
`stations.json` contains station IDs and names discovered in successful timetable files.

Use `System.Text.Json` and the existing camelCase, indented JSON settings when modifying output.

## Build and run commands

Use these commands from the repository root:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
```

For a local run, provide TDX credentials through environment variables:

```powershell
$env:TDX_CLIENT_ID = "<client id>"
$env:TDX_CLIENT_SECRET = "<client secret>"
dotnet run
```

To generate a different number of days:

```powershell
dotnet run -- --days 14
```

The GitHub Actions workflow currently runs:

```powershell
dotnet run --configuration Release --project DailyTrainTimetable.csproj -- --days 31
```

Schedule runs always use `--days 31`; `workflow_dispatch` overrides it through the `days` input (default `"31"`).

## GitHub Actions

The workflow files are:

```text
.github/workflows/update-train-data.yml   # fetch + test + Pages deploy + failure reporting
.github/workflows/keepalive.yml           # backup heartbeat refresh (self-heals a disabled main workflow)
.github/workflows/staleness-alert.yml     # hourly published-data staleness alert (data-staleness issue)
```

The main workflow:

- Takes `--days 31` on schedule; `workflow_dispatch` accepts an optional `days` string input (default `"31"`).
- Uses `TDX_CLIENT_ID` and `TDX_CLIENT_SECRET` repository secrets.
- Builds/runs the .NET app and uploads `output` as a GitHub Pages artifact.
- Requires GitHub Pages source to be set to `GitHub Actions`.
- On any step failure it opens/updates an issue labelled `workflow-failure` (`permissions: issues: write`) and closes it after a successful run.

The keepalive backup and the main `Refresh keepalive commit` step share the same `lastRefreshedAt + intervalDays` check, so both are no-op-safe and can run on the same day.

GitHub Actions cron expressions are UTC. Scheduled workflows only run on the GitHub default branch and may be delayed or skipped by GitHub under load. If a scheduled workflow stops firing, modifying the cron expression can help GitHub re-register the schedule.

## Coding notes

- Keep nullable annotations clean; the project uses `<Nullable>enable</Nullable>`.
- Prefer records for simple output DTOs, matching the current style.
- Keep Taiwan date calculations based on `Asia/Taipei` with the Windows fallback `Taipei Standard Time`.
- Preserve atomic JSON writes through `WriteJsonAtomicallyAsync`.
- Avoid adding third-party packages unless they solve a clear problem.
- If adding tests later, prefer focused tests for parsing, date selection, retry behavior, and JSON shape.
