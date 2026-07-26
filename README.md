# League Screen Analyzer

League Screen Analyzer is a Windows-only .NET 8 application that captures a selected window, recognizes a visible League game clock, validates configured minimap structure, and records same-frame timestamped map observations. Missing evidence is unavailable; temporal history never creates a timestamp or map observation.

## Requirements

- Windows 10 2004 (build 19041) or later, or Windows 11
- Direct3D 11 graphics and an interactive desktop session
- .NET 8 SDK

No Python, FFmpeg, SQLite, OpenCV, learned classifier, general OCR, replay control, champion detection, tracking, or heatmap inference is used.

## Run and configure

```text
dotnet run --project src\LeagueScreenAnalyzer.App
```

1. Select a visible replay window.
2. Create or load a valid CLOCK region (and MINIMAP if saving a layout).
3. Select **League Replay HUD** (synthetic v1) or **League Replay HUD (real calibrated v2)** and a fixed playback speed.
4. Leave **Enable recognition** checked.
5. Inspect current status, recognized text, accepted time, explicitly historical last-accepted time, confidence, cadence, and rejection diagnostic.
6. Enter the ground-truth visible time in **Actual clock value** as `M:SS` or `MM:SS`.
7. Use **Save Clock Sample** to write one labeled calibration sample. Select **Save as unlabeled diagnostic only** only when intentionally collecting evidence without ground truth.

Playback-speed choices are 0.25x, 0.5x, 1x, 2x, 4x, and 8x. Profile and speed are locked during active capture. Recognition starts only when capture is active and CLOCK is configured, and stops with capture or when recognition is disabled.

Region editing remains source-normalized (`x`, `y`, `width`, `height` in `[0,1]`). Drag to create/move, use the eight handles to resize, Escape to cancel, and Delete/Clear to remove. Preview coordinates remain presentation-only and letterbox/pillarbox clicks are rejected.

## Constrained recognition

The production pipeline is:

```text
copied BGRA CLOCK crop
  -> integer luminance
  -> contrast-range check
  -> fixed or Otsu threshold
  -> configured foreground polarity
  -> column-projection character localization
  -> normalized 5x7 digit-template comparison
  -> constrained M:SS/MM:SS parse
  -> confidence threshold
  -> independent temporal validation
  -> Valid or unavailable status
```

This is deliberately not broad OCR. `ConstrainedClockImageRecognizer` returns image-supported candidates and diagnostics. `ClockTemporalValidator` accepts or rejects those candidates using history, source time, fixed playback speed, and profile tolerances. It never changes a recognized digit and never emits expected time when the crop is unreadable.

Clock states include `Valid`, `NotConfigured`, `NotVisible`, `Unreadable`, `Malformed`, `LowConfidence`, `Implausible`, `Backward`, and `Discontinuous`. A rejected `ClockReading` has no canonical `GameTime`; its raw candidate and nullable candidate time remain diagnostic evidence, while `LastAcceptedGameTime` is explicitly historical.

## Profiles and templates

The synthetic mechanics profile identifier is `league-replay-v1`, display name **League Replay HUD**, version 1. It defines pattern/character bounds, polarity, threshold strategy, confidence floor, maximum game time, playback speed, temporal tolerances, processing cap, and `ReplayContinuous` mode. Capture-layout schema version 1 may optionally reference the stable profile identifier as `clockProfileId`; profile data itself is not embedded in layout JSON.

The initial classifier masks are deterministic 5x7 seven-segment mechanics templates in `SevenSegmentTemplates.cs`. Their provenance is explicit: they are synthetic canonical masks and remain available for mechanical tests.

`league-replay-v2` supplements v1 with 65 small real League-derived templates generated from 13 explicitly labeled diagnostic bundles. Its manifest records source bundle, full label, character position, glyph, and preprocessing version for every template. Digit 8 has no real evidence; digits 4, 6, and 7 have only one independent source bundle each. The profile is calibrated evidence, not a production-readiness claim.

The CLI and WPF app use the same validated clock-profile catalog. Builds copy
`fixtures/clocks/**` beside each executable, and runtime discovery checks that packaged
directory first, then `%LOCALAPPDATA%\LeagueScreenAnalyzer\profiles`. Set
`LEAGUE_SCREEN_ANALYZER_CLOCK_PROFILES` to an explicit directory of profile
subdirectories for development. Only when no packaged profile manifests exist does the
catalog walk upward for a repository `fixtures/clocks` development fallback; normal
runtime discovery never depends on the process working directory. Malformed manifests,
missing templates, dependency failures, and duplicate stable IDs are rejected and
reported rather than replaced or silently downgraded.

To add a real template/profile revision:

1. Pause or inspect the replay at the desired frame and type its visible time into **Actual clock value**.
2. Capture the small crop with **Save Clock Sample**. The normalized human label and parsed seconds/milliseconds are retained in `result.json`.
3. Select examples covering every digit, minute rollover, background variation, scale, and compression.
4. Evaluate the saved bundles directly with `evaluate-clock --diagnostics`.
5. Derive/replace classifier references only from the retained labeled evidence.
6. Increment the profile version and rerun the evaluator.

Never infer calibration labels from temporal expectation alone.

## Temporal policy

`ReplayContinuous` implements:

- first above-threshold parsed candidate acceptance;
- repeated displayed seconds;
- ordinary ticks and minute rollover;
- expected game advance = source elapsed × configured playback speed;
- whole-second rendering tolerance from the profile;
- rejection of backward candidates;
- rejection of forward movement beyond the expected advance plus tolerance;
- source-timestamp regression as `Discontinuous`;
- brief unavailable frames without losing the historical anchor;
- a long unavailable interval as `Discontinuous`, not automatic repair.

`BroadcastVod` exists as a policy seam but complete interruption/gap anchoring is intentionally not implemented.

## Bounded processing

Capture already retains only the latest pending full frame. The UI synchronously copies the small CLOCK crop before pooled frame disposal, then submits it to `ClockRecognitionWorker`, which also has only one replaceable pending sample. Processing is off the dispatcher. Its target rate is `min(profile cap, 4 × playback speed)` samples per source second, with a 12 samples/second initial cap. Stale pending crops are replaced and preview rendering never waits for recognition.

## Diagnostics and evaluation

One **Save Clock Sample** request writes:

```text
artifacts/clock-samples/clock-sample-.../
  original-clock.bmp
  normalized-clock.pgm
  segment-00.pgm ...
  result.json
```

For a labeled save, `result.json` contains `sampleKind: "labeled"`, the normalized user-supplied `explicitLabel`, and `explicitLabelSeconds`/`explicitLabelMilliseconds`, in addition to candidates and character confidence, accepted/rejected status and reason, temporal-history summary, profile/version, playback speed, source sequence/timestamp, and actual cadence. A successful labeled save shows the bundle path and clears the label field; it never infers or increments a subsequent label.

Blank or malformed values cannot be saved as labeled samples. Surrounding whitespace is ignored, seconds must be `00` through `59`, and the UI gives a corrective message. An unlabeled bundle is written only after selecting **Save as unlabeled diagnostic only** with an empty label field; its JSON records `sampleKind: "unlabeledDiagnostic"` and a null `explicitLabel`. The existing capture diagnostic bundle remains available for full-frame/layout inspection. Nothing writes continuously.

Evaluate a labeled manifest with:

```text
dotnet run --project src/LeagueScreenAnalyzer.Cli -- evaluate-clock \
  --profile league-replay-v1 \
  --manifest fixtures/clocks/synthetic-seven-segment/manifest.json \
  --output artifacts/clock-evaluation
```

Evaluate all labeled diagnostic bundles beneath the normal app output directory directly (unlabeled bundles are deterministically skipped):

```text
dotnet run --project src/LeagueScreenAnalyzer.Cli -- analyze-clock-diagnostics \
  --profile league-replay-v1 \
  --diagnostics artifacts/clock-samples \
  --output artifacts/clock-calibration-analysis

dotnet run --project src/LeagueScreenAnalyzer.Cli -- build-clock-profile \
  --base-profile league-replay-v1 \
  --profile-id league-replay-v2 \
  --diagnostics artifacts/clock-samples \
  --output-profile fixtures/clocks/league-replay-v2

dotnet run --project src/LeagueScreenAnalyzer.Cli -- evaluate-clock \
  --profile league-replay-v2 \
  --diagnostics artifacts/clock-samples \
  --output artifacts/clock-evaluation-v2
```

The profile stored in each diagnostic bundle is capture provenance: it records which recognizer and preprocessing produced the original result. It is not an evaluation restriction. `evaluate-clock --profile league-replay-v3` always decodes the saved `original-clock.bmp`, runs v3, and compares the new result only with the explicit user label. Reports keep `capturedWithProfile`, `evaluatedWithProfile`, the original candidate/status, and the new candidate/status separate; evaluation never rewrites `result.json`.

Likewise, `--base-profile` selects inherited recognition settings and the preprocessing/segmentation workflow used to build a new profile. Each source crop is reprocessed with that workflow and aligned to its explicit label; stored old segments and recognized candidates are not treated as truth. Thus v1 and v2 captures can be mixed when constructing v3. `compatibility.json` records accepted and rejected samples, source capture profiles, target profiles, and concrete failures such as missing/corrupt crops or ambiguous alignment.

Old labeled samples are intentionally reusable across profile versions. Users do not need to edit diagnostic JSON or recapture the same CLOCK image for every new profile. Diagnostic evaluation writes separately labeled apparent-training and leave-one-sample-out reports for every template-backed profile. It resets temporal state for every independent crop and reports visual versus temporal rejection counts.

## Minimap validation and observation recording

The minimap pipeline is precision-first. `IMapImageValidator` is history-free and reports crop geometry/aspect ratio, integer luminance mean/variance/minimum/maximum, near-black and near-uniform fractions, thresholded horizontal/vertical edge density, border consistency, corner consistency, and nullable reference similarity. `StructuralMinimapValidator` applies thresholds stored in a versioned profile. Temporal history never promotes invalid pixels, and an unchanged minimap is not rejected.

Map states are `Valid`, `NotConfigured`, `Missing`, `Obscured`, `Misaligned`, `LowInformation`, `LowConfidence`, `IncompatibleGeometry`, and `Unknown`. A valid result requires a profile ID and complete features. `league-replay-minimap-v1` targets `ReplayContinuous`, normalizes diagnostics to 128x128 grayscale, and stores thresholds and provenance in `fixtures/minimaps/league-replay-minimap-v1/profile.json`. It is synthetic structural calibration, not measured real-replay accuracy, so the WPF UI marks its recordings experimental.

CLI and WPF resolve minimap profiles through the same deterministic catalog. Packaged profiles are copied under the executable-relative `fixtures/minimaps` directory, so discovery does not depend on the process working directory; an explicit development override, user-installed profile directory, and repository fixture fallback are also supported. Malformed and duplicate stable IDs are rejected with visible diagnostics and are never silently replaced. The WPF selector shows stable ID, version, calibration status, active runtime ID, and source path; selection is enabled before capture, immutable during capture, and enabled again after stop.

Region editing applies shared semantic source-pixel geometry. MINIMAP creation and every resize handle maintain a square while moves preserve size and bounds. A legacy minimap whose source-pixel aspect deviation is at most 2.5% is treated as rounding drift and normalized around its center; larger deviations are retained with a warning for manual correction. Validation uses a stricter 1% square tolerance. CLOCK remains flexible but must be a wide horizontal crop with a minimum 2:1 source-pixel ratio; invalid geometry is rejected before recognition.

The MINIMAP crop is copied before pooled capture memory is released and submitted to `MinimapValidationWorker`, which has one replaceable pending item. Clock and map results complete independently but are retained in bounded 16-entry evidence maps and joined only by identical source sequence and timestamp. A canonical observation requires that exact match, an image-supported clock with temporal status `Accepted`, and a `Valid` map. Other paired frames are `Unavailable` with `source-frame-mismatch`, `clock-unavailable`, or `minimap-unavailable`; the latest historical clock is never attached to a newer map.

The WPF **Minimap Validation and Session Recording** section exposes profile, enable/state/confidence/features/reason, explicit sample label, session mode, cadence, recording status, current game time, valid/unavailable/saved counts, gap count, accepted bounds, achieved resolution, output path, and warning. It offers labeled/unlabeled diagnostics, start/stop, and open-folder controls. Profile selection is immutable for the complete capture lifetime; mode and cadence are immutable during recording. Recording start requires active capture, both regions, enabled clock recognition and map validation, and selected profiles.

### Explicit sample labels and CLI

A minimap diagnostic contains the original lossless BMP, normalized PGM, and `result.json` with explicit `Valid`, `Invalid`, `Uncertain`, or explicitly `Unlabeled` ground truth; features; profile/version; status/confidence/reasons; source identity/time; nullable accepted clock context; dimensions; and layout. Clock validity, previous map state, and validator output never choose the label.

```text
dotnet run --project src/LeagueScreenAnalyzer.Cli -- analyze-minimap-diagnostics --diagnostics artifacts/minimap-samples --output artifacts/minimap-analysis

dotnet run --project src/LeagueScreenAnalyzer.Cli -- build-minimap-profile --profile-id league-replay-minimap-v1 --diagnostics artifacts/minimap-samples --output-profile artifacts/minimap-profile

dotnet run --project src/LeagueScreenAnalyzer.Cli -- evaluate-minimap --profile league-replay-minimap-v1 --diagnostics artifacts/minimap-samples --output artifacts/minimap-evaluation
```

Analysis reports label and feature distributions. Building requires explicit valid evidence and records valid/invalid counts as provenance; a built profile is marked calibrated only when both classes exist. Evaluation excludes uncertain/unlabeled samples from primary metrics and reports totals, TP/TN/FP/FN, precision, recall, feature/confidence distributions, rejection reasons, and per-sample results. Zero false positives is the priority.

### Cadence, gaps, and portable dataset

Accepted observations are bucketed by game time at 250, 500, 1000, or 2000 ms. One candidate wins each bucket; a higher-confidence candidate replaces the previous candidate, duplicate timestamps do not duplicate files, and achieved resolution is reported honestly as median positive saved-game-time spacing.

A gap is created only between valid game-time anchors with unavailable observations between them. Start is the last valid anchor, end is the first later valid anchor, and ordered distinct reasons are retained. Same reasons merge naturally. Start/end unavailable periods remain partial coverage flags. No zero/inverted gap, interpolated map frame, or inferred position is produced. In `ReplayContinuous`, a gap of at least five game seconds warns; `BroadcastVod` is a structural seam and has no broadcast replay-window classifier yet.

```text
session-<timestamp-guid>/
  manifest.json
  timeline.jsonl
  summary.json
  gaps.json
  map/frames/000018420.bmp
  diagnostics/invalid-map/
  diagnostics/invalid-clock/
```

The manifest contains schema/session/source/mode/layout/profile IDs, speed, requested cadence, source and accepted-time bounds, dimensions, and application version. Timeline entries retain source identity, nullable canonical game time, clock/map status and confidence, observation status, nullable relative saved path, and unavailable reason. The summary contains coverage/cadence/gap/open-boundary metrics. Selected crops are ordinary lossless 32-bit BMP files, never database blobs. Metadata is atomically finalized through temporary files on stop; image processing and candidate delivery remain bounded.

## Build and test

```text
dotnet restore LeagueScreenAnalyzer.sln
dotnet build LeagueScreenAnalyzer.sln
dotnet test LeagueScreenAnalyzer.sln
scripts\verify.cmd
git diff --check
```

Tests cover parsing, preprocessing, polarity, segmentation, separator/template matching, candidate ordering/confidence, missing/low-contrast crops, temporal history, minute rollover, failures, discontinuities, playback speeds, non-fabrication, bounded worker replacement, speed immutability, diagnostic writing, profile/layout persistence, evaluation, and all prior capture/editor/fixture behavior.

## Manual replay validation

Real validation must remain manual:

1. configure/load CLOCK and select the replay window;
2. select `league-replay-v1`, set 1x, and start capture;
3. compare visible and recognized time for several minutes;
4. check repeated seconds, minute rollovers, and unavailable-state presentation;
5. type a visible value such as `3:40`, save it, open the reported `result.json`, and confirm `explicitLabel` is exactly `"3:40"`;
6. repeat immediately before and after a minute boundary, entering each visible value independently;
7. confirm malformed values and impossible seconds cannot be saved as labeled samples;
8. confirm a blank label requires selecting **Save as unlabeled diagnostic only**, and that entering a label while that mode is selected asks you to clear it;
9. run `evaluate-clock --diagnostics` over the saved root and inspect the report;
10. repeat briefly at 0.25x or 0.5x and at 4x, then stop capture and confirm recognition work and the analyzer process exit cleanly.

Do not use physical input automation for this verification.

For the minimap milestone, manually load valid CLOCK/MINIMAP regions, select `league-replay-v3` and `league-replay-minimap-v1`, enable both workers, and verify the visible crop's status and features. Save explicit valid and practical invalid/obscured samples. Record a short experimental session without pause/seek/resize/layout changes; compare timeline game times and BMP filenames to the visible replay clock; confirm repeated seconds remain cadence-bounded; inspect manifest, timeline, summary, gaps, and paths; verify no invalid map is marked valid; confirm preview responsiveness; stop capture; and confirm no analyzer process remains.

## Known limitations

- The checked-in templates and labeled images are synthetic mechanics fixtures, not real League replay samples.
- Real League typography, antialiasing, scaling, shadows, and background treatment still require calibration.
- Column projection assumes visible inter-character gaps; touching glyphs can become malformed.
- `ReplayContinuous` intentionally refuses repair after long absence or discontinuity.
- Crop correctness is user-configured; automatic CLOCK discovery is out of scope.
- CPU BGRA readback/copy favors safe ownership over zero-copy throughput.
- `league-replay-minimap-v1` is synthetic calibration; real replay precision is not measured or claimed.
- Border/corner signals are coarse and no landmark/reference mask is calibrated yet.
- Broadcast-VOD replay-window classification is not implemented.
- Session metadata is retained in memory until atomic finalization; processing queues and image candidates remain bounded.

## Next milestone

Consume saved timestamped minimap observations and detect a first limited set of map points of interest, while preserving confidence and provenance.
