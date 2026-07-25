# League Screen Analyzer

League Screen Analyzer is a Windows-first .NET 8 application for capturing selected regions of League of Legends video sources and producing synchronized, analysis-ready game data. The current milestone establishes deterministic processing boundaries; it does not capture a live window yet.

## Ingestion paths

The architecture supports two required source paths:

1. **Professional match VODs played for capture.** A valid frame requires both the broadcast clock and minimap. Replays, overlays, fight windows, desk segments, and similar interruptions remain explicit unavailable intervals.
2. **Personal `.rofl` replays.** The application will launch the League replay client and capture its rendered output. It will not decode `.rofl` files directly. A stable replay layout and variable playback speed will allow extraction speed to trade against game-time resolution.

## Current scope

This milestone provides:

- Immutable domain models with strict normalized-region validation.
- Replaceable asynchronous frame, extraction, validation, observation, and storage interfaces.
- JSON fixture manifests and deterministic fixture-aware processors.
- Stateful clock progression validation and explicit gap detection.
- A CLI that writes a JSON Lines timeline and JSON summary.
- A minimal, non-capturing WPF MVVM shell.
- Fast deterministic xUnit coverage and a root verification script.

## Project structure

- `src/LeagueScreenAnalyzer.App` — WPF application shell.
- `src/LeagueScreenAnalyzer.Cli` — fixture-processing command and application service.
- `src/LeagueScreenAnalyzer.Core` — domain models and implementation-neutral contracts.
- `src/LeagueScreenAnalyzer.Capture` — fixture source and deterministic processing pipeline.
- `src/LeagueScreenAnalyzer.Imaging` — reserved for future image-based implementations.
- `src/LeagueScreenAnalyzer.Storage` — JSON session artifact writer.
- `tests/LeagueScreenAnalyzer.Tests` — deterministic unit and service tests.
- `fixtures` — human-authored session manifests.
- `docs/architecture.md` — dependency and data-flow decisions.

## Build and test

From the repository root:

```text
dotnet restore LeagueScreenAnalyzer.sln
dotnet build LeagueScreenAnalyzer.sln
dotnet test LeagueScreenAnalyzer.sln
```

Run the complete verification workflow on Windows:

```text
scripts\verify.cmd
```

## Fixture CLI

Process the valid continuous fixture:

```text
dotnet run --project src/LeagueScreenAnalyzer.Cli -- process-fixture --source fixtures/valid-continuous/session.json --output artifacts/valid-continuous
```

The output directory contains:

- `timeline.jsonl` — one normalized observation per source frame.
- `summary.json` — counts, game-time bounds, gaps, and rejected clocks.

Additional fixtures model a broadcast interruption and an invalid clock jump.

## Current milestone

The deterministic vertical slice proves that streaming sources, region extraction, clock and map validation, timeline normalization, explicit gaps, and artifact writing can be developed without WPF interaction or a running League client.

## Non-goals

This milestone intentionally does not implement:

- Windows screen capture or replay-client launching.
- OpenCV or another imaging library.
- OCR.
- SQLite.
- FFmpeg.
- Direct `.rofl` decoding.
- League-specific analysis, champion detection, or heatmaps.
- Guessing or interpolating data across unavailable intervals.

## Next milestone

Add a selected-window preview using `Windows.Graphics.Capture`, while preserving the same `IFrameSource` and downstream fixture-testable processing boundaries.
