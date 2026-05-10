# Project Structure & Architecture

## Solution Overview

`Unosquare.FFME.sln` contains four compiled projects and one source-only folder, all targeting `net8.0-windows`.

| Project | Type | Output |
|---|---|---|
| `Unosquare.FFME` | Class library | `Unosquare.FFME.dll` (core engine, no WPF) |
| `Unosquare.FFME.Windows` | Class library | `ffme.win.dll` (the NuGet package) |
| `Unosquare.FFME.Windows.Sample` | WPF application | `ffmeplay.win.exe` |
| `Unosquare.FFME.Tests` | xUnit test project | `Unosquare.FFME.Tests.dll` |

`Unosquare.FFME.MediaElement/` is a source-only folder with no project file. Its `.cs` files are textually compiled into `Unosquare.FFME.Windows` via `<Compile Include="..\Unosquare.FFME.MediaElement\**\*.cs" />`. It holds the abstract `MediaElement` base, event args, and platform interfaces that are not WPF-specific.

### NuGet dependencies

| Package | Version | Used by |
|---|---|---|
| `FFmpeg.AutoGen` | 8.1.0 | Core, Windows lib, Tests |
| `Microsoft.CodeAnalysis.NetAnalyzers` | 10.0.203 | Core, Windows lib, Sample |
| `Microsoft.NET.Test.Sdk` | 18.5.1 | Tests |
| `xunit.v3` | 3.2.2 | Tests |
| `xunit.runner.visualstudio` | 3.1.5 | Tests |

No NAudio dependency. Audio output is handled by a custom WaveOut wrapper in `Rendering/Wave/`.

### Compiler settings (all projects)

- `LangVersion=preview`
- `TreatWarningsAsErrors=true`
- `AllowUnsafeBlocks=true` (required for FFmpeg P/Invoke)
- `WarningsNotAsErrors`: `CS8019` (unused using), `CS0436` (type conflict from source inclusion) in the Windows lib

---

## Three-Layer Architecture

```
┌──────────────────────────────────────────────┐
│  Layer 3 — MediaElement (WPF control)        │
│  Unosquare.FFME.MediaElement/ (source only)  │
│  Unosquare.FFME.Windows/                     │
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

Wraps FFmpeg. Converts compressed source bytes into format-normalised `MediaBlock` objects. The pipeline per frame is: **Read packet → Decode frame → Convert to block**.

### Key classes

| Class | Role |
|---|---|
| `MediaContainer` | Top-level wrapper; opens streams, exposes `Read`, `Decode`, `Convert` |
| `MediaComponentSet` | Collection of all active per-stream `MediaComponent` instances |
| `MediaComponent` | Base class: packet queue, frame decoding, block conversion |
| `AudioComponent`, `VideoComponent`, `SubtitleComponent` | Per-type subclasses of `MediaComponent` |
| `DataComponentSet` | Handles non-media (data) packet callbacks |
| `MediaBlock` / `AudioBlock` / `VideoBlock` / `SubtitleBlock` | Decoded, format-normalised output objects |
| `MediaFrame` / `AudioFrame` / `VideoFrame` / `SubtitleFrame` | Intermediate decoded frames before conversion to blocks |
| `MediaBlockBuffer` | Per-stream circular buffer of `MediaBlock` objects |
| `PacketQueue` | Circular buffer of compressed `MediaPacket` objects |
| `HardwareAccelerator` | Optional GPU decode (NVIDIA/Intel/AMD); falls back to software |

### FFmpeg interop

FFmpeg is accessed via **FFmpeg.AutoGen** (v8.1.0), which auto-generates P/Invoke signatures for FFmpeg 7.0. Utility helpers in `Unosquare.FFME/FFmpeg/`:

| File | Role |
|---|---|
| `FFInterop.cs` | String marshalling, error decoding, option enumeration |
| `FFAudioParams.cs` | Audio format descriptor wrapper |
| `FFDictionary.cs` / `FFDictionaryEntry.cs` | `AVDictionary` wrapper |
| `FFBPrint.cs` | `AVBPrint` wrapper |

The static `Library` class (in `Unosquare.FFME.Windows/`) must be initialised before any media operation:

```csharp
Library.FFmpegDirectory = @"C:\ffmpeg";
Library.LoadFFmpeg();
```

---

## Layer 2 — MediaEngine

**Location:** `Unosquare.FFME/Engine/`

Controls playback state and drives background workers. Never touches WPF — platform concerns are injected via `IMediaConnector`.

### MediaEngine (partial class)

| File | Content |
|---|---|
| `MediaEngine.cs` | Construction, properties, `IDisposable` |
| `MediaEngine.Connector.cs` | `IMediaConnector` callback dispatch |
| `MediaEngine.Controller.cs` | Playback control API (`Open`, `Close`, `Play`, etc.) |
| `MediaEngine.Workers.cs` | Worker lifecycle (creates `MediaWorkerSet`, starts/stops workers) |

### State

`MediaEngineState.cs` owns all playback state (position, duration, buffer levels, playback state enum, stream metadata, etc.). It extends `ViewModelBase` which implements `INotifyPropertyChanged`. Thread safety uses `volatile int`/`volatile bool` for flags, and `Volatile.Read`/`Volatile.Write` on `long` ticks for timing values — no `Atomic*` wrapper types.

Key notification methods:
- `ReportBufferingStatus()` — fires change notifications for buffering/download properties
- `ReportCommandStatus()` — fires for `IsSeeking`, `IsClosing`, `IsOpening`, `IsChanging`
- `ReportTimingStatus()` — fires for `IsPlaying`, `IsPaused`

### Background workers

All four workers extend `WorkerBase` (`Primitives/WorkerBase.cs`):

- Each worker runs on a **dedicated background `Thread`** (not a Task or Channel).
- Pause/resume are controlled by a `SemaphoreSlim` run gate.
- Stop uses a `CancellationTokenSource`.
- A `ManualResetEventSlim` allows other workers to signal an early wakeup via `RequestWakeup()`.
- `GetCycleDelay()` controls the inter-cycle sleep; the default is `Constants.DefaultTimingPeriod` (15 ms).

The three media workers are managed together by `MediaWorkerSet`:

```
PacketReadingWorker   →   reads compressed packets from MediaContainer
        ↓
FrameDecodingWorker   →   decodes packets into MediaBlocks, fills MediaBlockBuffer
        ↓
BlockRenderingWorker  →   reads blocks at clock-appropriate times, calls platform renderers
```

- **`PacketReadingWorker`** — keeps ~1 second of compressed data buffered; pauses when buffers are full.
- **`FrameDecodingWorker`** — reads from packet queues, decodes via FFmpeg, writes normalised blocks to `MediaBlockBuffer`.
- **`BlockRenderingWorker`** — the most complex worker; drives real-time rendering, calls `IMediaRenderer` per media type, manages clock sync and seek recovery. Overrides `StartWorkerThread()` to use a `ThreadPriority.Highest` thread.

### Command manager

`CommandManager` is itself a `WorkerBase` (a fourth background loop). Its partial class files:

| File | Content |
|---|---|
| `CommandManager.cs` | Core loop; dispatches from the three queues |
| `CommandManager.Direct.cs` | `Open`, `Close`, `ChangeMedia` — exclusive, blocks until complete |
| `CommandManager.Priority.cs` | `Play`, `Pause`, `Stop` — queued, processed before seeks |
| `CommandManager.Seek.cs` | `Seek`, `StepForward`, `StepBackward` — coalesced (a pending seek is replaced by a later one) |
| `CommandManager.Enums.cs` | Command type enumerations |

All commands return `Task<bool>`. Exceptions are caught and posted as `MediaFailed` events.

### Timing

`TimingController.cs` maintains independent `RealtimeClock` instances per media type. Clocks are started/stopped/adjusted by the rendering worker based on buffer state and seek operations. Workers use these, not wall-clock comparisons.

### Constants

All tuning values live in `Unosquare.FFME/Constants.cs`:

| Constant | Value |
|---|---|
| `DefaultTimingPeriod` | 15 ms (worker cycle delay) |
| `PropertyUpdatesInterval` | 30 ms |
| `MinVideoFrameDuration` | 10 ms |
| `MaxVideoFrameDuration` | 50 ms |
| `MinVideoBlocks` | 8 |
| `MinAudioBlocks` | 48 (~1 s at 48 kHz) |
| `MinSubtitleBlocks` | 4 |
| Audio format | S16, 48 kHz, 2 channels |
| Video pixel format | BGRA 32-bit |
| Live stream buffer target | 500–1000 ms |

---

## Layer 3 — MediaElement (WPF)

### Source-only base (`Unosquare.FFME.MediaElement/`)

Contains the platform-agnostic base code compiled into the Windows lib. Not a buildable project on its own.

| Folder / File | Content |
|---|---|
| `MediaElement.cs` | Abstract base; commands (`Open`, `Close`, `Play`, etc.) |
| `MediaElement.Events.cs` | Abstract event declarations |
| `MediaElement.Properties.cs` | Abstract property declarations |
| `Platform/IGuiContext.cs` | Thread-dispatch abstraction |
| `Platform/IPropertyProxy.cs` | Dependency-property write abstraction |
| `Platform/MediaConnector.cs` | Base `IMediaConnector` implementation |
| `Platform/PropertyProxy.cs`, `ClassProxy.cs`, `PropertyMapper.cs` | DependencyProperty reflection helpers |
| `Common/` | 14 event-args types (`MediaOpenedEventArgs`, `PositionChangedEventArgs`, etc.) |

### WPF implementation (`Unosquare.FFME.Windows/`)

#### MediaElement partial classes

| File | Content |
|---|---|
| `MediaElement.cs` | Owns `MediaEngine`; wires up state change flow; 15 ms `DispatcherTimer` |
| `MediaElement.Events.cs` | WPF-specific event declarations |
| `MediaElement.Properties.cs` | All WPF `DependencyProperty` definitions |

#### State update flow

`MediaEngineState` raises `INotifyPropertyChanged` from worker threads. `MediaElement` subscribes at construction:

```csharp
MediaCore.State.PropertyChanged += (_, e) => _propertyUpdates.Add(e.PropertyName);
```

Changed property names accumulate in a `ConcurrentBag<string>`. A `DispatcherTimer` at `DispatcherPriority.DataBind` fires every **15 ms** and drains the bag in `CoerceMediaCoreState`, updating WPF dependency properties on the UI thread. The timer is a no-op when the bag is empty.

#### Platform bridge (`Platform/`)

| File | Role |
|---|---|
| `MediaConnector.cs` | Implements `IMediaConnector`; bridges `MediaEngine` callbacks to WPF events |
| `GuiContext.cs` | Wraps `Dispatcher` for thread-safe UI marshalling |
| `GuiContextType.cs` | Enum: `WPF` or `WinForms` |
| `SoundTouch.cs` | P/Invoke wrapper for `SoundTouch.dll`; loaded dynamically via `NativeLibrary.Load` from the FFmpeg directory |

#### Renderers (`Rendering/`)

| File(s) | Responsibility |
|---|---|
| `AudioRenderer.cs` | Audio output via the custom `Wave/` layer; integrates SoundTouch for pitch-preserving speed changes |
| `Wave/` (10 files) | Custom WaveOut implementation: `DirectSoundPlayer`, `LegacyAudioPlayer`, `WaveOutBuffer`, formats, interop |
| `VideoRendererBase.cs`, `VideoRenderer.cs`, `InteropVideoRenderer.cs`, `ImageHost.cs`, `ElementHostBase.cs` | Writes decoded BGRA frames to a WPF `WriteableBitmap`; `ImageHost` optionally runs on its own `Dispatcher` thread for multi-threaded video |
| `SubtitleRenderer.cs`, `SubtitlesControl.cs` | Text subtitle overlay |
| `ClosedCaptionsControl.cs`, `ClosedCaptionsBuffer.cs`, `ClosedCaptionsCell.cs`, `ClosedCaptionsCellState.cs` | CEA-608 caption display |

#### Common (`Common/`)

Event-args types for rendering callbacks (`RenderingVideoEventArgs`, `RenderingAudioEventArgs`, `RenderingSubtitlesEventArgs`) and supporting types (`BitmapDataBuffer`, `RendererOptions`, `AudioDeviceInfo`).

---

## Threading Primitives (`Unosquare.FFME/Primitives/`)

| Type | Role |
|---|---|
| `WorkerBase` | Abstract background worker; dedicated `Thread`, `SemaphoreSlim` gate, `CancellationTokenSource` stop, `ManualResetEventSlim` wakeup |
| `IWorker` | Worker lifecycle interface (`StartAsync`, `PauseAsync`, `ResumeAsync`, `StopAsync`) |
| `WorkerState` | Enum: `Created`, `Running`, `Paused`, `Stopped` |
| `CircularBuffer` | Byte-level ring buffer used for audio sample queuing |
| `MediaTypeDictionary<T>` | Fixed-size dictionary keyed by `MediaType` enum (Audio/Video/Subtitle/Data) |
| `RealtimeClock` | High-resolution elapsed-time clock with pause/resume |
| `VerticalSyncContext` | VSync timing support for the rendering worker |
| `SyncLockerFactory` / `ISyncLocker` | Reader-writer lock abstraction used on the container's read and decode sync roots |

The `Atomic*` wrapper types (`AtomicBoolean`, `AtomicInteger`, etc.) have been removed. Shared mutable state now uses `volatile` fields or direct `Interlocked` calls at the point of use.

---

## Tests (`Unosquare.FFME.Tests/`)

xUnit v3 project. Tests that exercise FFmpeg are skipped unless `FFME_FFMPEG_DIR` points to the FFmpeg shared-binary directory; they use FFmpeg's built-in `lavfi` virtual input device (no media files needed).

| File | Covers |
|---|---|
| `PrimitivesTests.cs` | `WorkerBase` lifecycle (start, pause, resume, stop, dispose) |
| `ClosedCaptionsTests.cs` | CEA-608 packet parsing |
| `VideoSeekIndexTests.cs` | Seek index build and lookup |
| `PlaylistTests.cs` | Playlist serialisation/deserialisation |
| `MediaContainerTests.cs` | Full `Open` → `Read` → `Decode` → `Convert` pipeline (FFmpeg required) |
| `LibraryTests.cs` | Library initialisation |
| `Fixtures/FfmpegFixture.cs` | Shared class fixture that loads FFmpeg once per test run |

---

## Sample Application (`Unosquare.FFME.Windows.Sample/`)

A fully-featured reference player demonstrating all major library features.

| Area | Files |
|---|---|
| Entry / main window | `App.xaml.cs`, `MainWindow.xaml.cs`, `MainWindow.MediaEvents.cs`, `MainWindow.MediaRendering.cs` |
| Commands | `AppCommands.cs` |
| ViewModels (MVVM) | `RootViewModel.cs`, `ControllerViewModel.cs`, `PlaylistViewModel.cs`, `AttachedViewModel.cs` |
| UI controls | `Controls/ControllerPanelControl`, `PlaylistPanelControl`, `PropertiesPanelControl` |
| Foundation utilities | `FileInputStream.cs` (custom `IMediaInputStream`), `ThumbnailGenerator.cs`, `TransportStreamRecorder.cs`, `ReactiveExtensions.cs`, `DeferredAction.cs`, `DelegateCommand.cs` |
| Playlist | `CustomPlaylistEntry.cs`, `CustomPlaylistEntryCollection.cs` |

**This is the best place to look for usage examples** — the sample exercises playlist management, stream selection (audio/video/subtitle cycle), hardware acceleration toggling, FFmpeg filtergraph application, screenshot capture, and packet recording.

---

## Remaining Improvement Areas

**1. Eliminate the DispatcherTimer for state updates**
`MediaEngineState` already raises `INotifyPropertyChanged` from worker threads. The next step is driving WPF dependency property updates directly from those events via `Dispatcher.InvokeAsync`, removing the 15 ms timer. The trade-off is losing the natural coalescing the timer provides for high-frequency updates (e.g. `Position` during playback).

**2. Replace SoundTouch dynamic loading**
`Platform/SoundTouch.cs` loads `SoundTouch.dll` at runtime via `NativeLibrary.Load` by probing the FFmpeg directory. Deployment is manual and failures are silent. Options: add SoundTouch.NET as a NuGet dependency, or remove pitch correction if not needed.

**3. Consolidate the MediaElement source-inclusion pattern**
`Unosquare.FFME.MediaElement/` has no project file and is textually included into the Windows lib. Converting it to a proper class library (`net8.0` or `netstandard2.0`) would give it its own build output, make it independently testable, and remove the source-inclusion workaround.

**4. Modernise the worker threading model**
`WorkerBase` / the three workers pre-date `System.Threading.Channels`. Reimplementing as `Channel`-based producers/consumers would make back-pressure and cancellation more idiomatic.

**5. Upgrade NuGet dependencies**
- `FFmpeg.AutoGen` v8.1.0 — check for a release aligned with FFmpeg 7.x
- NAudio is no longer a dependency; the custom `Wave/` layer serves the same purpose
