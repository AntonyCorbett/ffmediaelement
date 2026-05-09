# Project Structure & Architecture

## Solution Overview

`Unosquare.FFME.sln` contains four projects targeting `net8.0-windows` (Debug) and `net8.0-windows` + `net48` (Release).

| Project | Type | Output |
|---|---|---|
| `Unosquare.FFME` | Shared project (`.shproj`) | No DLL — compiled into consumers |
| `Unosquare.FFME.MediaElement` | Shared project (`.shproj`) | No DLL — compiled into consumers |
| `Unosquare.FFME.Windows` | Class library | `ffme.win.dll` (the NuGet package) |
| `Unosquare.FFME.Windows.Sample` | WPF application | `ffmeplay.win.exe` |

The two shared projects (`.shproj`) are textually imported into `Unosquare.FFME.Windows` at build time using `<Import Project="..." />`. They exist so the core logic can be shared with hypothetical future platform targets without referencing a separate assembly.

---

## Three-Layer Architecture

The entire system is structured as three layers, each wrapping the one below.

```
┌──────────────────────────────────────────────┐
│  Layer 3 — MediaElement (WPF control)        │
│  Unosquare.FFME.Windows/ + MediaElement/     │
├──────────────────────────────────────────────┤
│  Layer 2 — MediaEngine (playback control)    │
│  Unosquare.FFME/Engine/                      │
├──────────────────────────────────────────────┤
│  Layer 1 — MediaContainer (FFmpeg boundary)  │
│  Unosquare.FFME/Container/                   │
└──────────────────────────────────────────────┘
```

---

## Layer 1 — MediaContainer

**Location:** `Unosquare.FFME/Container/`

Wraps FFmpeg. Converts compressed bytes from a source (file, URL, or custom `IMediaInputStream`) into format-normalized `MediaBlock` objects that the engine can render without knowing anything about codecs.

The pipeline per frame is: **Read packet → Decode frame → Convert to block**

### Key classes

| Class | Role |
|---|---|
| `MediaContainer.cs` | Top-level wrapper; opens streams, exposes `Read`, `Decode`, `Convert` |
| `MediaComponentSet.cs` | Collection of all active `MediaComponent` objects (one per stream) |
| `MediaComponent.cs` | Per-stream handler: packet queue, frame decoding, block conversion |
| `MediaBlockBuffer.cs` | Per-stream circular buffer of decoded `MediaBlock` objects |
| `PacketQueue.cs` | Circular buffer of compressed `MediaPacket` objects |
| `HardwareAccelerator.cs` | Optional GPU decode (NVIDIA/Intel/AMD), falls back to software |

### FFmpeg interop

FFmpeg is accessed via **FFmpeg.AutoGen** (NuGet v8.1.0), which auto-generates P/Invoke signatures for FFmpeg 7.0. `AllowUnsafeBlocks` is required. Utility helpers live in `Unosquare.FFME/FFmpeg/`:

- `FFInterop.cs` — string marshalling, error code decoding, option enumeration
- `FFAudioParams.cs` — audio format descriptor wrapper
- `FFDictionary.cs` — `AVDictionary` wrapper
- `FFBPrint.cs` — `AVBPrint` wrapper

The static `Library` class (in `Unosquare.FFME.Windows/`) must be initialized before any media operation:

```csharp
Library.FFmpegDirectory = @"C:\ffmpeg";
Library.LoadFFmpeg();
```

---

## Layer 2 — MediaEngine

**Location:** `Unosquare.FFME/Engine/`

Controls playback state and drives three background workers. It never touches WPF — platform concerns are injected via `IMediaConnector`.

### State machine

`MediaEngineState.cs` owns all playback state: position, duration, buffer levels, component indices, playback state enum, etc. It implements `INotifyPropertyChanged`. The WPF layer polls it every 15 ms.

### Three background workers

All extend `IntervalWorkerBase` → `WorkerBase` (`Unosquare.FFME/Primitives/`). Each is an adaptive-timing background loop.

```
PacketReadingWorker   →   reads compressed packets from MediaContainer
        ↓
FrameDecodingWorker   →   decodes packets into MediaBlocks, fills MediaBlockBuffer
        ↓
BlockRenderingWorker  →   reads blocks at clock-appropriate times, calls renderers
```

- **`PacketReadingWorker.cs`** — keeps ~1 second of compressed data buffered; pauses when buffers are full, resumes when they drain.
- **`FrameDecodingWorker.cs`** — reads from packet queues, decodes via FFmpeg, writes normalized blocks to `MediaBlockBuffer`.
- **`BlockRenderingWorker.cs`** (31 KB, the most complex worker) — drives the real-time rendering loop; reads from `MediaBlockBuffer`, calls the platform `IMediaRenderer` per media type, manages clock synchronisation and seek recovery.

### Command manager

`Commands/CommandManager.*.cs` — five partial class files, three command queues:

| Queue | Commands | Behaviour |
|---|---|---|
| Direct | `Open`, `Close`, `ChangeMedia` | Exclusive; blocks until complete |
| Priority | `Play`, `Pause`, `Stop` | Queued; processed before seeks |
| Seek | `Seek`, `StepForward`, `StepBackward` | Coalesced — a pending seek is replaced by a later one |

All commands return `Task<bool>`. Exceptions are caught and posted as `MediaFailed` events.

### Timing

`TimingController.cs` maintains independent `RealtimeClock` instances per media type. The clocks are started/stopped/adjusted by the rendering worker based on buffer levels and seek operations. Workers use these clocks, not wall-clock comparisons, to decide when to render a block.

---

## Layer 3 — MediaElement (WPF)

**Location:** `Unosquare.FFME.MediaElement/` (abstract base) + `Unosquare.FFME.Windows/` (WPF implementation)

### MediaElement partial classes

| File | Content |
|---|---|
| `MediaElement.cs` | Core logic; owns `MediaEngine`; runs 15 ms `DispatcherTimer` to sync state |
| `MediaElement.Properties.cs` | All WPF dependency properties |
| `MediaElement.Events.cs` | All event declarations |

The `Source` dependency property is notification-only — setting it does not trigger `Open`. Always call `await Media.Open(uri)` explicitly.

### Platform bridge

`Platform/MediaConnector.cs` implements `IMediaConnector` — the callback bridge from `MediaEngine` to the WPF element (state change notifications, renderer dispatch, command results).

`Platform/GuiContext.cs` wraps `Dispatcher` to marshal calls onto the UI thread from worker threads.

### Renderers (`Rendering/`)

| File(s) | Responsibility |
|---|---|
| `AudioRenderer.cs` | NAudio-based output; integrates SoundTouch for pitch-preserving speed changes |
| `VideoRenderer.cs`, `InteropVideoRenderer.cs`, `ImageHost.cs` | Writes decoded RGB frames to a WPF `WriteableBitmap` |
| `SubtitleRenderer.cs`, `SubtitlesControl.cs` | Text overlay |
| `ClosedCaptionsControl.cs`, `ClosedCaptionsBuffer.cs` | CEA-608 caption parsing and display |

`Platform/SoundTouch.cs` loads `SoundTouch.dll` dynamically at runtime by probing the FFmpeg binary directory. If absent, speed changes alter pitch — no error is thrown.

---

## Threading Primitives (`Unosquare.FFME/Primitives/`)

Custom thread-safe types used throughout the hot path instead of `lock`:

| Type | Backed by |
|---|---|
| `AtomicBoolean`, `AtomicInteger`, `AtomicLong`, `AtomicDouble` | `Interlocked` |
| `AtomicDateTime`, `AtomicTimeSpan` | `Interlocked` on the underlying `long` ticks |
| `AtomicTypeBase<T>` | Generic base for the above |
| `CircularBuffer` | Byte-level ring buffer (used for audio) |
| `MediaTypeDictionary<T>` | Fixed-size dictionary keyed by `MediaType` enum (Audio/Video/Subtitle/Data) |
| `RealtimeClock` | High-resolution elapsed-time clock with pause/resume |
| `WorkerBase` / `IntervalWorkerBase` | Abstract background thread base with lifecycle management |

`SyncLockerFactory` / `ISyncLocker` provide a unified reader-writer lock abstraction used on the container's read and decode sync roots.

---

## Sample Application

`Unosquare.FFME.Windows.Sample/` is a fully-featured reference player. It demonstrates:

- Playlist management (`ViewModels/`)
- Stream selection (cycle audio/video/subtitle streams at runtime)
- Hardware acceleration toggling
- FFmpeg video/audio filtergraph application
- Screenshot capture and packet recording (no re-encoding)
- Closed caption channel selection

**This is the best place to look for usage examples** — the unit test gap (see below) makes it the de-facto behavioural reference.

---

## Modernisation Notes

### What is already modern
- Targets .NET 8 (alongside net48 in Release)
- FFmpeg 7.0 (recently upgraded)
- SDK-style `.csproj` throughout
- `TreatWarningsAsErrors`, Roslyn analyzers, `LangVersion=preview`

### High-priority improvements

**1. Add a test project**  
There are zero automated tests. The safest first step before any refactoring is adding an xUnit project that exercises `MediaContainer` directly (no WPF required). The container's `Open` / `Read` / `Decode` / `Convert` methods are pure-ish and testable with a local media file.

**2. Replace SoundTouch dynamic loading**  
`Platform/SoundTouch.cs` probes the FFmpeg directory for `SoundTouch.dll` at runtime using `NativeLibrary.Load`. This is fragile — deployment is manual and errors are silent. Options:
- Add [SoundTouch.NET](https://www.nuget.org/packages/SoundTouch.NET/) as a NuGet dependency
- Or remove pitch correction entirely if not needed

**3. Consolidate the shared project pattern**  
`.shproj` was a workaround for the era before multi-targeting. The two shared projects (`Unosquare.FFME`, `Unosquare.FFME.MediaElement`) could be converted to ordinary class libraries (targeting `netstandard2.0` or `net8.0`), which would give them proper build outputs, enable testing in isolation, and remove the need for the source-level `Import` hack. This is a medium-effort refactor with high long-term payoff.

**4. Modernise the worker threading model**  
`WorkerBase` / `IntervalWorkerBase` pre-date `System.Threading.Channels` and structured async. The three workers could be reimplemented as `Channel`-based producers/consumers, making back-pressure and cancellation more idiomatic. The `Atomic*` types could mostly be replaced with `volatile` fields or `Interlocked` calls at their use sites — there's no need for the wrapper hierarchy in modern C#.

**5. Push state changes rather than polling**  
The 15 ms `DispatcherTimer` in `MediaElement.cs` that syncs `MediaEngineState` to WPF dependency properties is a polling loop. A more modern approach would have `MediaEngineState` raise `INotifyPropertyChanged` events that propagate through `IObservable<T>` or `BindingSource` notifications, eliminating the timer entirely.

**6. Upgrade NuGet dependencies**  
- `FFmpeg.AutoGen` v8.1.0 — check for a newer release aligned with FFmpeg 7.x
- `Microsoft.CodeAnalysis.NetAnalyzers` — keep at latest
- NAudio — check current version; the `AudioRenderer` may benefit from updated APIs

**7. Drop net48 if your consuming application doesn't need it**  
Dual-targeting adds complexity. If your WPF app targets .NET 8+, removing `net48` from the Release build simplifies the build, eliminates conditional reference groups in the `.csproj`, and allows use of APIs not available on .NET Framework.

**8. Centralise magic numbers**  
Values like the 15 ms dispatcher interval, the ~1-second packet buffer target, and audio buffer sizes are scattered as literals. Gathering them into a static `MediaEngineOptions` or `PlaybackConfiguration` class makes tuning and testing much easier.

### Low-priority / cosmetic

- The `Analyzers.ruleset` file at the root is the old-style suppression format; modern suppressions go in `.editorconfig` or `GlobalSuppressions.cs`
- `appveyor.yml` references AppVeyor CI which is tied to the original Unosquare account — replace or remove if you set up your own CI
- Several XML doc comments are incomplete or use placeholder text from the original authors
