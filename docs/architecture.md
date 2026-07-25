# Architecture

## Dependency direction

`LeagueScreenAnalyzer.Core` remains free of WPF, Win32, WinRT, Direct3D, and `Windows.Graphics.Capture`. It owns immutable domain records and `IFrameSource` plus the downstream processing contracts.

`LeagueScreenAnalyzer.Capture` multi-targets plain .NET 8 and Windows 10 build 19041. Platform-neutral targets contain fixture processing, capture lifecycle contracts, the controller, and bounded latest-frame delivery. Its Windows target additionally contains the picker, D3D device interop, capture frame pool, and CPU readback implementation.

`LeagueScreenAnalyzer.App` targets the same Windows SDK floor. It composes the capture implementation, provides the owner HWND through a small WPF service, marshals preview updates to the dispatcher, and presents them in a reusable `WriteableBitmap`. Capture logic does not live in code-behind; code-behind is limited to composition and window-close disposal.

The fixture CLI continues to consume the plain `net8.0` Capture target, so automated fixture processing does not acquire a Windows UI or graphics dependency.

## Capture flow

```text
Select Window command
  → CaptureController
  → WindowsCaptureSessionSelector
  → GraphicsCapturePicker (initialized with WPF HWND)
  → WindowsCaptureSession
  → Direct3D11CaptureFramePool (two GPU buffers)
  → SoftwareBitmap GPU-to-CPU copy
  → pooled tightly packed BGRA memory
  → LatestFrameQueue (one pending frame)
  → IFrameSource / CaptureController
  → WPF dispatcher
  → reusable WriteableBitmap
```

The controller state machine exposes `Idle`, `Selecting`, `Capturing`, `Stopped`, and `Error`. A selected session is initialized before the state changes to `Capturing`. Picker cancellation and permission/support failures become recoverable visible errors. Explicit stop cancels the frame pump, stops the platform session, drains completion, and disposes it. Repeated stop is safe.

`GraphicsCaptureItem.Closed` completes the session with `SourceClosed`. Invalid content dimensions complete it with `InvalidFrameSize`. Other asynchronous platform failures complete it with `Failure`. The controller translates each reason into a visible state and diagnostic. A subsequent selection first releases any completed session before opening the picker.

When `ContentSize` changes, the capture session logs the new dimensions and calls `Direct3D11CaptureFramePool.Recreate`. Existing frame-pool frames are discarded by the Windows API. Preview metadata and the WPF bitmap are resized on the next delivered frame.

## Frame ownership

- `Direct3D11CaptureFrame` is scoped to one frame-arrival callback and is never retained after readback.
- Its `IDirect3DSurface` is used only while `SoftwareBitmap.CreateCopyFromSurfaceAsync` runs.
- `SoftwareBitmap`, `BitmapBuffer`, and `IMemoryBufferReference` are disposed before the callback returns.
- The copied BGRA array is rented from `ArrayPool<byte>`. `Bgra32FramePayload.Dispose` returns it exactly once.
- `LatestFrameQueue` holds no more than one pending CPU frame. Replacing a stale frame immediately disposes its payload.
- `CaptureController` owns each delivered payload only during its synchronous `FrameArrived` notification and disposes it immediately afterward.
- The WPF handler copies pixels synchronously into a reusable `WriteableBitmap`; it does not retain the capture payload.
- Capture sessions own and dispose the Direct3D device projection, frame pool, and graphics capture session. Stop and async disposal are idempotent.
- The diagnostic PNG encoder reads the current WPF bitmap only when explicitly requested and closes its output stream immediately.

## Preview tradeoff and future extraction

The current presentation path performs a GPU-to-CPU copy and one CPU copy into WPF. It avoids a new full-size managed allocation per presented frame by pooling capture arrays and reusing the `WriteableBitmap`. It also rejects overlapping readbacks and replaces stale queued frames, favoring latency and bounded memory over presenting every source frame.

This costs more bandwidth than a D3D-backed WPF bridge. The `IFrameSource` boundary and `Bgra32FramePayload` keep presentation separate from session lifecycle while leaving the Windows session close to the source GPU surface. A later milestone can add GPU region extraction or a D3D presentation adapter without adding Windows types to Core.

## Structured diagnostics

Normal logging records picker open/cancel, capture start/stop, dimension changes, target closure, capture failures, and one-shot PNG paths. Per-frame logging is intentionally absent. The WPF composition root currently sends structured logs to the Visual Studio/debug output provider.

## Deterministic testing

Platform calls sit behind `ICaptureSessionSelector` and `ILiveCaptureSession`. Tests exercise controller transitions, cancellation, initialization failure, source closure, active/repeated stop, reselection, and dimension/sequence/timestamp updates with fakes. `LatestFrameQueue` tests prove stale disposal and a one-frame pending bound. Existing fixture tests continue against the plain .NET target.

## Existing processing flow

```text
IFrameSource
  → region extraction
  → clock and map validation
  → normalized observation timeline
  → storage
  → future analysis
```

Fixture payloads carry deterministic clock/visibility metadata. Live payloads carry an owned BGRA lease. Both cross the same `IFrameSource`/`SourceFrame` boundary; downstream live region extraction is intentionally deferred.

## Next milestone

Draw, edit, save, and preview normalized clock and minimap regions over the selected-window preview.
