# League Screen Analyzer

League Screen Analyzer is a Windows-only .NET 8 application that captures a user-selected window and lets the user configure exactly two source-relative regions: **CLOCK** and **MINIMAP**. The existing `Windows.Graphics.Capture` picker, bounded latest-frame delivery, lifecycle handling, and reusable WPF preview remain the capture foundation.

No OCR, automatic region discovery, minimap validation, replay control, or video recording is included in this milestone.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- Compatible Direct3D 11 graphics hardware and an interactive desktop session
- .NET 8 SDK to build from source

## Region workflow

Run:

```text
dotnet run --project src\LeagueScreenAnalyzer.App
```

Then:

1. Select a visible window with **Select Window**.
2. Choose **Edit** beside CLOCK or MINIMAP.
3. If that region is missing, drag inside the rendered video to create it.
4. Drag inside a region to move it. Select it and drag any of the eight white handles to resize it.
5. Use **Escape** to cancel the active drag, **Delete** or **Clear** to remove the selected semantic region.
6. Confirm both enlarged crop previews update and the normalized coordinates remain stable while the analyzer window resizes.
7. Enter a layout name and choose **Save As**. Enable the explicit overwrite checkbox to replace an existing name.
8. Select a saved layout to load or delete it. A stopped or unexpectedly closed source does not remove the active/unsaved regions.
9. Use **Save Diagnostic Bundle** to write the current evidence under `artifacts`.

Dragging in letterbox or pillarbox bars does not create a region. Editing is enabled only while capture is active and has produced source dimensions. CLOCK uses a gold border and MINIMAP uses blue, but both also have permanent text labels so identity never depends on color.

## Normalized coordinates

The authoritative region values are source-relative:

```text
x, y, width, height ∈ [0, 1]
width > 0, height > 0
x + width <= 1, y + height <= 1
```

WPF device-independent preview coordinates are derived presentation state only. `PreviewCoordinateMapper` computes a uniform scale and centered rendered-video viewport from the source dimensions and preview-control dimensions. It converts normalized points/regions through that viewport and rejects points outside it. Source resolution changes therefore recalculate overlay and crop pixels without changing normalized regions.

## Capture layouts

Layouts are JSON schema version 1:

```json
{
  "schemaVersion": 1,
  "name": "LCK Broadcast 2026",
  "sourceAspectRatio": 1.7777777777777777,
  "clockRegion": {
    "x": 0.42,
    "y": 0.01,
    "width": 0.16,
    "height": 0.06
  },
  "minimapRegion": {
    "x": 0.81,
    "y": 0.7,
    "width": 0.18,
    "height": 0.28
  }
}
```

`sourceAspectRatio` is optional compatibility metadata; both region objects are required. Normal application storage is:

```text
%LOCALAPPDATA%\LeagueScreenAnalyzer\CaptureLayouts
```

Tests and development composition can supply another directory to `JsonCaptureLayoutStore`. Saves use a same-directory temporary file followed by atomic replacement where supported. Malformed JSON, unknown fields, unsupported versions, missing coordinates, invalid bounds, filename/path misuse, and implicit overwrites are rejected with visible errors.

A loaded/saved layout retains its normalized regions for every source. If the new source aspect ratio differs by **more than 2% relative to the stored ratio**, the UI shows a compatibility warning but does not clear, stretch, or reshape either region.

## Crop and diagnostic behavior

The capture session still permits only one pending CPU frame. During its synchronous frame notification, the WPF adapter copies the full BGRA frame into a reusable full-size `WriteableBitmap` and copies each configured rectangle into a reusable crop `WriteableBitmap`. Crop bitmaps are recreated only when their pixel dimensions change. There is no secondary crop queue, per-frame persistence, OCR, or OpenCV dependency; stale source frames are already replaced by the bounded capture queue.

One explicit diagnostic request creates:

```text
artifacts/capture-diagnostic-YYYYMMDD-HHMMSS-fff/
  full-frame.png
  annotated-frame.png
  clock-crop.png          (when CLOCK is configured)
  minimap-crop.png        (when MINIMAP is configured)
  active-layout.json
```

The annotated frame is rendered at source pixel dimensions with labeled rectangles.

## Build and test

```text
dotnet restore LeagueScreenAnalyzer.sln
dotnet build LeagueScreenAnalyzer.sln
dotnet test LeagueScreenAnalyzer.sln
scripts\verify.cmd
git diff --check
```

Tests cover domain invariants, matching and mismatched preview aspect ratios, letterbox/pillarbox rejection, coordinate round trips, create/move/all resize directions, boundary and minimum-size enforcement, cancellation, unsaved state, JSON persistence failures and overwrite/delete behavior, capture lifecycle preservation, source resize behavior, aspect compatibility, existing capture lifecycle, and bounded latest-frame semantics.

## Project structure

- `src/LeagueScreenAnalyzer.App` — WPF MVVM preview, overlay event forwarding, crop presentation, and diagnostic rendering
- `src/LeagueScreenAnalyzer.Core` — domain records plus platform-neutral coordinate, edit, and compatibility services
- `src/LeagueScreenAnalyzer.Capture` — fixtures, capture controller, bounded frame delivery, and Windows capture
- `src/LeagueScreenAnalyzer.Storage` — JSON artifacts and named capture-layout persistence
- `src/LeagueScreenAnalyzer.Cli` — deterministic fixture command
- `src/LeagueScreenAnalyzer.Imaging` — reserved for later image processing
- `tests/LeagueScreenAnalyzer.Tests` — deterministic unit and lifecycle tests

## Known limitations

- Capture uses GPU-to-CPU BGRA readback and WPF bitmap copies rather than a zero-copy D3D presentation bridge.
- Protected or application-blocked content may be blank; HDR is requested as 8-bit BGRA.
- Compatibility is intentionally an aspect-ratio warning, not proof that a different broadcast graphic uses the same positions.
- Layout names map to local JSON filenames and therefore exclude invalid filename/path characters.
- Crop presentation runs on the WPF dispatcher. The bounded upstream queue favors current frames over presenting every frame.
- Diagnostic files are created only on request and are not a recording mechanism.

## Next milestone

Read and validate the visible game clock from the configured Clock region, while treating missing or implausible readings as unavailable rather than guessing.
