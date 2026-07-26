# Architecture

## Dependency direction

`LeagueScreenAnalyzer.Core` owns platform-neutral frames, capture layouts, `ClockReading`, candidate/profile/diagnostic records, manual clock-label parsing, and recognition/validation interfaces. It has no WPF, WinRT, Direct3D, or presentation bitmap dependency.

`LeagueScreenAnalyzer.Imaging` implements constrained pixel recognition, parsing, profile catalog, temporal validation, bounded recognition work, diagnostic writing, and the `IGameClockReader` orchestrator. It depends only on Core and adds no image-processing package.

`LeagueScreenAnalyzer.Capture` retains Windows capture and fixture processing. Its BGRA payload implements Core's read-only `IClockImagePayload` boundary. `LeagueScreenAnalyzer.App` copies pixels into WPF bitmaps and an independently owned CLOCK buffer. `Storage` owns layout/session JSON, and `Cli` owns fixture and clock-evaluation commands.

## Live data flow and ownership

```text
Windows.Graphics.Capture
  -> D3D frame pool
  -> CPU BGRA pooled payload
  -> LatestFrameQueue (one pending source frame)
  -> CaptureController synchronous notification
     -> WPF preview/crop bitmap copies
     -> tightly packed owned CLOCK copy
        -> ClockRecognitionWorker (one replaceable pending crop)
           -> constrained image recognizer
           -> temporal validator
           -> dispatcher presentation
```

The controller disposes pooled source memory immediately after notification. Recognition sees only the owned small crop. The worker replaces and disposes stale pending crops, limits cadence to `min(maximumSamplesPerSecond, 4 × playbackSpeed)`, and never blocks preview rendering. Its semaphore represents an empty-to-occupied transition, not an enqueue count: replacement does not signal, and the consumer waits for cadence before atomically taking and clearing the one pending slot. Stop rejects new samples, cancels pending delays/recognition, disposes the pending sample, and drains any canceled run's signal before restart. Recognition-worker faults are reported through the recognition status and do not stop an otherwise healthy capture preview.

## Recognition responsibilities

`IClockImageRecognizer` is history-free. The initial implementation:

1. converts BGRA to luminance with integer weights;
2. rejects insufficient contrast;
3. applies profile-selected Otsu or fixed threshold and foreground polarity;
4. localizes nonempty x-runs and tight y bounds;
5. identifies a narrow sparse separator;
6. area-normalizes digit segments to 5×7 masks;
7. ranks digit templates by deterministic binary agreement;
8. builds bounded candidate combinations;
9. parses only `M:SS`, `MM:SS`, or `MMM:SS`, valid seconds, and profile maximum time;
10. applies the profile confidence floor.

Diagnostics retain normalized pixels, segment rectangles/pixels, preprocessing variant, candidates, per-character confidence, and precise failure reason.

`IClockTemporalValidator` receives only image-supported candidates. It owns accepted history and source timing. Image evidence and temporal evidence remain separately visible in `ClockReading`. Rejected readings have `GameTime = null`; `BestCandidate.ParsedGameTime` is diagnostic rather than canonical, and `LastAcceptedGameTime` is explicitly historical.

## Temporal state machine

For `ReplayContinuous`:

```text
no anchor + supported candidate -> Valid anchor
supported repeated second       -> Valid
supported expected progression  -> Valid and update anchor
image unavailable               -> unavailable; retain historical anchor
candidate < anchor              -> Backward
advance > elapsed × speed + tol -> Implausible
source timestamp regression     -> Discontinuous
long missing interval           -> Discontinuous
```

Whole-second display behavior is handled by accepting repeats and a profile forward tolerance. Speeds 0.25x through 8x scale expected progression. Profile/speed changes are prohibited while the live worker runs. No state transition synthesizes time.

The `BroadcastVod` enum value preserves a later policy seam for disappearance and forward gap anchoring. That policy is not complete and `ReplayContinuous` does not repair gaps.

## Profiles and layout association

`ClockRecognitionProfile` includes stable ID/name/version, expected pattern, character bounds, threshold/polarity, confidence floor, maximum time, temporal tolerances, fixed playback speed, cadence cap, and validation mode. `BuiltInClockProfiles` validates deterministic built-ins at construction.

`league-replay-v1` retains canonical synthetic seven-segment masks. Template-backed profiles resolve a validated manifest beneath `fixtures/clocks/<profile-id>`, inherit recognition settings from their declared base profile, support multiple provenance-tracked templates per glyph, and use nearest-template overlap scoring with one-pixel translation tolerance. Candidate confidence includes absolute glyph quality and first/second margin; the weakest glyph can make the whole clock unavailable. Calibration evaluation is independent-sample by construction and emits separate apparent-training and leave-one-sample-out reports without changing normal `ReplayContinuous` validation.

Capture layout schema 1 may store nullable `clockProfileId`. This is an intentional reference only: visual classifier and policy data do not leak into general layout JSON. Older schema-1 files without the field remain valid.

The v1 classifier uses documented synthetic 5×7 masks. They validate mechanics, not League accuracy. Real profile calibration must retain small labeled source crops and provenance, revise classifier references, increment the profile version, and pass the evaluator with special attention to false accepts.

## Result semantics

Statuses are:

- `Valid`: canonical `GameTime` exists;
- `NotConfigured`: recognition or CLOCK region is disabled/missing;
- `NotVisible`: insufficient contrast/no foreground;
- `Unreadable`: no supported image evidence;
- `Malformed`: localized text cannot parse;
- `LowConfidence`: image candidate below profile threshold;
- `Implausible`: forward movement violates replay timing;
- `Backward`: candidate moves behind the anchor;
- `Discontinuous`: source regression or broken continuous-replay assumptions.

Legacy fixture enum values remain for serialized/source compatibility, while fixture processing now emits refined states.

## Diagnostics, evaluation, and logging

Explicit clock bundles include original BMP, normalized PGM, each segmented PGM, and JSON with recognition, temporal, profile, playback, cadence, source identity, and label mode. A labeled save requires a user-supplied `M:SS` or `MM:SS` value parsed independently of recognition and temporal history. Core normalizes that value and exposes its total seconds/milliseconds; the diagnostic writer persists all three representations. An unlabeled bundle requires an explicit UI choice and is marked `unlabeledDiagnostic`. Writes occur only on command.

The profile in a diagnostic `result.json` is immutable source provenance: it identifies the recognizer/profile version, preprocessing, original recognition result, and candidate present at capture time. The explicit user label is the only ground truth. The profile requested by `evaluate-clock` is a separate target selected at evaluation time, and the base profile requested by `build-clock-profile` is a third role that supplies inherited settings and the current preprocessing/segmentation workflow.

The CLI reads either labeled, small P2 PGM manifests or recursively discovers diagnostic `result.json` files. For every usable labeled bundle it decodes the sibling `original-clock.bmp` and reruns the requested target profile, regardless of whether the crop was captured under v1, v2, or another supported source profile. Cross-profile evaluation does not overwrite source JSON. Reports distinguish `capturedWithProfile`, `evaluatedWithProfile`, original candidate/status, and newly evaluated candidate/status. Compatibility entries retain deterministic accept/reject decisions and reasons for unlabeled samples, malformed labels/provenance, unsupported schemas, missing or corrupt crops, and current-profile processing failures.

Profile construction similarly ignores stored recognition candidates and stored segment pixels as training truth. It decodes the original crop, reruns the requested base profile's preprocessing and segmentation, aligns the result to the explicit `M:SS` or `MM:SS` label, and stops template extraction for ambiguous samples. Template provenance records the source diagnostic bundle and capture profile separately from the generated target profile. This makes mixed-version labeled datasets durable calibration assets: old samples are intentionally reusable and need not be recaptured for every profile revision.

Discovery and processing are relative-path sorted and emit correct accepts/rejects, false accepts/rejects, per-character/exact accuracy, confidence distribution, confusions, compatibility decisions, and per-sample evidence. A wrong accepted label counts as both failed expected recognition and false acceptance. Reports preserve provenance so synthetic results cannot be mistaken for real measurements.

Normal structured logs cover enable/disable, profile/speed selection, worker lifecycle, rejection category/reason, discontinuity/regression, profile failures, and diagnostic writes. Accepted high-frequency candidates are debug-level.

## Testing and manual boundary

Deterministic tests cover manual label formats/normalization/errors, labeled and explicitly unlabeled persistence, diagnostic-bundle evaluator discovery, recognition parsers, image dimensions/polarity/segmentation/templates/separator/confidence/ambiguity, no-character and low-contrast images, all temporal transitions and required speeds, non-fabrication/history semantics, latest-sample replacement, immutable running settings, diagnostics, profile/layout loading, evaluator metrics, and all earlier capture/editor fixtures.

Platform capture, visual WPF state distinction, several-minute real replay recognition, real minute rollovers, real calibration artifacts, responsiveness under replay, and process cleanup require an interactive manual run without physical mouse/keyboard automation. Synthetic fixtures are never reported as real validation.

## Known limitations

The initial segmentation assumes separable x-runs and the initial masks are not League-derived. Real antialiasing, scale, shadows, compression, and background variation remain unmeasured. Broadcast-VOD gap anchoring, minimap validation, automatic region discovery, OCR fallback, replay control, and persistent live timelines are intentionally absent.

## Next milestone

Validate configured MINIMAP visibility, emit timestamped valid minimap observations, and create explicit unavailable intervals when the clock or minimap cannot be trusted.
