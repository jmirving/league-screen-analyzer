# League Screen Analyzer

League Screen Analyzer is a Windows-only .NET 8 application for capturing League of Legends video sources and producing synchronized, analysis-ready game data. The WPF application can now select a visible application window with the supported Windows graphics-capture picker and show a live preview.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11.
- Compatible Direct3D 11 graphics hardware and a desktop session where `Windows.Graphics.Capture` is supported.
- .NET 8 SDK to build from source.

The selected window may remain partially or fully covered while capture continues. Windows and applications can still opt out of capture, and protected video may appear blank.

## Current scope

- Immutable domain models and implementation-neutral processing interfaces.
- Deterministic JSON fixture source, fixture-aware clock/map processors, CLI processing, and JSON artifacts.
- Supported Windows picker for user-selected window capture.
- Live, aspect-preserving WPF preview with source title, dimensions, sequence, timestamp, and status.
- Explicit stop, selected-window closure detection, and frame-pool recreation after a size change.
- A one-frame PNG diagnostic saved on demand to `artifacts`.
- Structured lifecycle logging through `Microsoft.Extensions.Logging`.

This milestone does not select regions, run OCR, persist live frame streams, launch `.rofl` files, control replay speed, or analyze images.

## Project structure

- `src/LeagueScreenAnalyzer.App` — WPF MVVM application and preview adapter.
- `src/LeagueScreenAnalyzer.Cli` — fixture-processing command and application service.
- `src/LeagueScreenAnalyzer.Core` — immutable domain models and platform-neutral contracts.
- `src/LeagueScreenAnalyzer.Capture` — fixture pipeline, capture controller, and Windows capture implementation.
- `src/LeagueScreenAnalyzer.Imaging` — reserved for later image processing.
- `src/LeagueScreenAnalyzer.Storage` — JSON session artifact writer.
- `tests/LeagueScreenAnalyzer.Tests` — deterministic lifecycle, queue, domain, and service tests.
- `fixtures` — human-authored session manifests.
- `docs/architecture.md` — dependency, capture, ownership, and data-flow decisions.

## Build and test

From the repository root:

```text
dotnet restore LeagueScreenAnalyzer.sln
dotnet build LeagueScreenAnalyzer.sln
dotnet test LeagueScreenAnalyzer.sln
scripts\verify.cmd
```

## Live preview

Run:

```text
dotnet run --project src\LeagueScreenAnalyzer.App
```

Manual test:

1. Click **Select Window** and choose a browser, video player, or other visible application window in the Windows picker.
2. Confirm the title, dimensions, sequence/timestamp, and live preview update.
3. Cover the selected window and confirm capture continues.
4. Resize the selected window and confirm the preview follows without distortion.
5. Click **Save Diagnostic Frame**, then open the reported PNG under `artifacts` to inspect colors, orientation, scaling, and dimensions.
6. Click **Stop**, then select a different window.
7. Repeat capture and close the selected target window; confirm the application reports a clear stopped/error state.
8. Close League Screen Analyzer and confirm no application process remains.

Picker cancellation is reported as a visible, recoverable error so that the user can immediately try again.

## Fixture CLI

```text
dotnet run --project src/LeagueScreenAnalyzer.Cli -- process-fixture --source fixtures/valid-continuous/session.json --output artifacts/valid-continuous
```

The output contains `timeline.jsonl` and `summary.json`. Additional fixtures model a broadcast interruption and an invalid clock jump.

## Current limitations

- Preview uses GPU-to-CPU readback into BGRA pixels and then copies into a reusable WPF `WriteableBitmap`. This is intentionally simple and safe, but more expensive than a zero-copy D3D presentation bridge.
- A maximum of one copied preview frame waits for presentation. Slow rendering drops and disposes stale frames.
- HDR sources are requested as 8-bit BGRA and may not reproduce HDR color exactly.
- Diagnostic saving snapshots only the current preview; continuous recording is not implemented.
- Capture availability and protected-content behavior remain controlled by Windows and the selected application.

## Next milestone

Draw, edit, save, and preview normalized clock and minimap regions over the selected-window preview.
