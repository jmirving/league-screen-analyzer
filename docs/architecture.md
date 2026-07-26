# Architecture

## Dependency direction

`LeagueScreenAnalyzer.Core` stays independent of WPF, Win32, WinRT, Direct3D, and `Windows.Graphics.Capture`. It owns `NormalizedRegion`, `CaptureLayout`, semantic `RegionType`, coordinate primitives, `PreviewCoordinateMapper`, `RegionEditor`, and aspect-ratio compatibility policy.

`LeagueScreenAnalyzer.Capture` preserves the existing multi-target architecture. Its plain .NET target contains fixture processing, lifecycle contracts, `CaptureController`, and bounded latest-frame delivery; its Windows target adds the native picker, D3D device, frame pool, and CPU readback.

`LeagueScreenAnalyzer.Storage` persists schema-versioned layouts without a UI dependency. `LeagueScreenAnalyzer.App` composes those services, marshals frames to WPF, owns reusable display bitmaps, and renders derived overlay geometry. `MainWindow` code-behind only forwards pointer/size/key events and supplies the HWND; edit and capture behavior remain outside it.

## Capture and presentation flow

```text
GraphicsCapturePicker initialized with WPF HWND
  → WindowsCaptureSession
  → Direct3D11CaptureFramePool
  → SoftwareBitmap GPU-to-CPU copy
  → pooled BGRA memory
  → LatestFrameQueue (one pending frame, stale frame disposed)
  → CaptureController synchronous FrameArrived notification
  ├─→ reusable full-frame WriteableBitmap
  ├─→ reusable CLOCK crop WriteableBitmap
  └─→ reusable MINIMAP crop WriteableBitmap
```

The controller owns each delivered payload through the synchronous notification and disposes it immediately afterward. The UI copies pixels before returning and never retains pooled memory. Crop buffers are recreated only when pixel dimensions change. There is no crop worker queue, so no crop work can accumulate; the existing one-frame source queue supplies latest-frame semantics.

Stop, source closure, invalid dimensions, and frame-pool recreation retain the editor and its unsaved state. A new window selection does not automatically load or clear a layout.

## Coordinate systems

There are three explicit coordinate systems:

- source pixels, whose dimensions come from the captured frame;
- normalized source coordinates, authoritative values from 0 through 1;
- WPF preview coordinates, device-independent units local to the preview surface.

For source size `(Sw, Sh)` and preview size `(Pw, Ph)`, the mapper calculates:

```text
scale = min(Pw / Sw, Ph / Sh)
viewportWidth  = Sw × scale
viewportHeight = Sh × scale
viewportX = (Pw - viewportWidth) / 2
viewportY = (Ph - viewportHeight) / 2
```

Normalized points and rectangles map through that centered viewport. Unused vertical space is letterboxing; unused horizontal space is pillarboxing. `PreviewToNormalized` returns no point outside the viewport, preventing bar clicks from creating invalid annotations. Overlay geometry is recalculated when either source or WPF preview dimensions change; normalized values are never rewritten by a resize.

## Region editor

`RegionEditor` stores two nullable working values keyed by the closed `RegionType` enum: CLOCK and MINIMAP. It is not a generic annotation collection. Its transaction states are create, move, and resize. A transaction records the pre-edit region; commit returns a structured result, while cancel restores that snapshot.

All operations use normalized coordinates. Movement clamps while preserving size. Eight edge/corner handles change only their associated edges. Edges clamp to `[0, 1]` and to a configurable minimum width/height (1% defaults), so a region cannot invert or collapse. A saved snapshot supports deterministic unsaved-change detection. Clearing removes only the requested semantic region.

The WPF view model performs hit-testing using mapper-produced overlay rectangles. Selecting **Edit CLOCK** or **Edit MINIMAP** makes creation intent explicit when that region is absent. Existing rectangles may be clicked, moved, or resized. Labels distinguish semantics independently of border color; only the selected region shows handles.

## Layout persistence and compatibility

`JsonCaptureLayoutStore` defaults to `%LOCALAPPDATA%\LeagueScreenAnalyzer\CaptureLayouts` and accepts an override directory. Schema version 1 requires a name and complete, valid CLOCK and MINIMAP regions. Optional `sourceAspectRatio` records the source geometry under which the user saved.

Save serializes to a unique temporary file in the destination directory, flushes it, then uses same-volume move/replace. Existing names require `overwrite: true`. Load rejects malformed JSON, unknown properties, missing region fields, unsupported schemas, file/name mismatches, and invalid normalized bounds. Delete is explicit and independent of capture-session disposal.

`SourceAspectRatioCompatibility` defines a material change as a relative difference strictly greater than 2%. Matching-ratio resolution changes naturally preserve alignment. A material mismatch retains all coordinates and emits a visible warning plus structured log so the user can adjust manually.

## Diagnostics and logging

Normal-level structured logs cover capture lifecycle plus region create/move/resize/clear, layout save/load/delete, rejected malformed layouts, aspect mismatch, and diagnostic export. Pointer-move events and ordinary frames are not logged.

An explicit diagnostic bundle contains the source-resolution full frame, a source-resolution annotated frame, configured crops, and active layout JSON in one timestamped `artifacts` directory. Nothing is continuously saved.

## Testing seams

The mapper, editor, compatibility rule, JSON store, capture selector/session, and latest-frame queue are independently testable. Deterministic tests exercise aspect-preserving mapping and bar rejection, all edit directions and invariants, transaction rollback/commit, persistence error cases and atomic-file cleanup, lifecycle preservation and source-size changes, controller transitions, pooled-frame replacement, and the original fixture pipeline. Platform picker and D3D behavior remain a manual Windows test.

## Manual validation

Run the WPF application and verify:

1. select/stop/reselect and unexpected target closure;
2. create both labeled regions, move them, and use every handle;
3. boundary/minimum enforcement and Escape cancellation;
4. overlay alignment during analyzer and target-window resizes;
5. bar clicks ignored for mismatched preview/source ratios;
6. both crops update without persisted frame streams;
7. save, explicit overwrite, load, delete, and reload after restart;
8. more-than-2% aspect mismatch warning without region mutation;
9. stop/source closure preserves layout state;
10. one-shot diagnostic bundle contents;
11. application shutdown leaves no analyzer process.

## Known limitations

The CPU readback/presentation path favors simplicity over zero-copy throughput. Aspect similarity cannot guarantee identical broadcast graphics. Crop sampling is nearest source-pixel rectangle expansion via floor/ceiling and performs no validation. The layout UI uses filename-compatible names. No OCR, map validation, automatic discovery, database, recording, or replay control exists.

## Next milestone

Read and validate the visible game clock from the configured Clock region, while treating missing or implausible readings as unavailable rather than guessing.
