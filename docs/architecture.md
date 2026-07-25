# Architecture

## Processing flow

```text
Frame source
  → region extraction
  → clock and map validation
  → normalized observation timeline
  → storage
  → future analysis
```

`IFrameSource` asynchronously streams implementation-neutral `SourceFrame` values. `IRegionExtractor` applies a `CaptureLayout` and returns clock and minimap `RegionFrame` values. `IGameClockReader` and `IMapFrameValidator` independently describe their evidence; `IObservationProcessor` marks an observation valid only when both results are valid. `ISessionArtifactWriter` persists the completed timeline without placing a database or image-format dependency in the domain.

## Dependency direction

`LeagueScreenAnalyzer.Core` contains immutable domain models and contracts. It has no dependency on WPF, Windows capture APIs, storage, or an imaging implementation.

The outer projects implement those contracts:

- `Capture` currently supplies fixture frames, fixture-aware processors, and session orchestration.
- `Storage` currently supplies JSON Lines and JSON artifact persistence.
- `Cli` composes the deterministic vertical slice.
- `App` is an inert MVVM shell and contains no processing logic.
- `Imaging` is intentionally empty until real image-backed implementations are introduced.

Payloads cross boundaries through the marker interface `IFramePayload`. Production capture can later carry an image lease or reference without changing domain records. The fixture source carries synthetic clock and visibility metadata instead.

## Timeline and gaps

A timeline observation is valid only when its clock reading and map validation are both valid. Unavailable frames keep their diagnostics but do not receive a guessed game time.

The session processor emits a gap only when one or more unavailable observations occur between two valid game-time anchors. Leading and trailing unavailable frames remain unavailable but cannot form a bounded `GapInterval` because one anchor is absent.

The deterministic clock reader accepts repeated values and minute rollover. It rejects backward movement and forward movement that is implausible relative to elapsed source time and the configured maximum fixture playback rate.

## Why fixtures are first-class

Screen capture, OCR, broadcast layouts, and the League replay client are slow and environment-dependent integration points. JSON fixtures make visibility, clock text, timing, and discontinuities explicit and reviewable. They let contributors reproduce pipeline behavior without League, GPU capture support, binary assets, or manual UI steps.

Fixtures are not a temporary alternative code path. They implement the same interfaces that production capture and imaging components will implement, so they remain useful for regression tests as the system grows.

## Why the CLI is first-class

The CLI is the composition root for automated processing. It exercises manifest loading, streaming, extraction, validation, observation construction, gap detection, and storage in one deterministic command. Tests invoke its `FixtureProcessingService` directly rather than spawning a child process.

Keeping the workflow outside WPF prevents business logic from accumulating in code-behind and gives future agents a fast, scriptable development loop. The WPF application can later compose the same services for preview and capture sessions.
