# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```powershell
# Restore packages
dotnet restore

# Build the library
dotnet build Unosquare.FFME.Windows/Unosquare.FFME.Windows.csproj -c Debug
dotnet build Unosquare.FFME.Windows/Unosquare.FFME.Windows.csproj -c Release

# Build the sample player
dotnet build Unosquare.FFME.Windows.Sample/Unosquare.FFME.Windows.Sample.csproj -c Debug

# Run the sample player
dotnet run --project Unosquare.FFME.Windows.Sample/Unosquare.FFME.Windows.Sample.csproj

# Pack the NuGet package
dotnet pack Unosquare.FFME.Windows/Unosquare.FFME.Windows.csproj -c Release
```

## Automated Tests

The `Unosquare.FFME.Tests` project contains xunit tests covering primitives, closed caption parsing, the video seek index, playlist serialization, and the `MediaContainer` pipeline.

Tests that exercise FFmpeg (opening streams, decoding frames) are decorated with `[SkippableFact]` and are skipped unless the `FFME_FFMPEG_DIR` environment variable points to the FFmpeg shared binary directory.

```powershell
# Run pure unit tests (no FFmpeg required)
dotnet test Unosquare.FFME.Tests/Unosquare.FFME.Tests.csproj

# Run all tests including FFmpeg integration tests
$env:FFME_FFMPEG_DIR = "C:\path\to\ffmpeg\shared"
dotnet test Unosquare.FFME.Tests/Unosquare.FFME.Tests.csproj
```

## Runtime Requirement

FFmpeg **shared** binaries (v7.0, x64) must be present on disk before running. Set `Library.FFmpegDirectory` to their path before any media operations. The sample app sets this in `App.xaml.cs`. SoundTouch v2.1.1 (`SoundTouch.dll`) is optional — place it alongside the FFmpeg binaries to enable pitch-preserving speed changes.

## Project Layout

Two shared projects (`.shproj`) provide all core logic and are textually compiled into the library — they produce no DLL of their own:

- **`Unosquare.FFME/`** — core engine, container, workers, FFmpeg interop, primitives
- **`Unosquare.FFME.MediaElement/`** — abstract `MediaElement` base and event args

One compiled library imports both shared projects:

- **`Unosquare.FFME.Windows/`** — WPF control, platform renderers, the NuGet output (`ffme.win.dll`)

One reference application:

- **`Unosquare.FFME.Windows.Sample/`** — full-featured player (`ffmeplay.win.exe`); the canonical usage reference

## Three-Layer Architecture

### Layer 1 — MediaContainer (`Unosquare.FFME/Container/`)

The FFmpeg boundary. Owns the packet → frame → block pipeline:

- `MediaContainer.cs` — opens input streams, drives `Read` / `Decode` / `Convert` operations
- `MediaComponent.cs` — one instance per stream type (audio/video/subtitle); handles packet queuing and frame decoding via FFmpeg codecs
- `MediaBlockBuffer.cs` — per-stream circular buffer of decoded, format-normalized `MediaBlock` objects (RGB video frames, PCM audio samples)
- `HardwareAccelerator.cs` — optional GPU decode setup (NVIDIA/Intel/AMD); falls back to software

FFmpeg is accessed via **FFmpeg.AutoGen** (NuGet, auto-generated P/Invoke). `AllowUnsafeBlocks` is required.

### Layer 2 — MediaEngine (`Unosquare.FFME/Engine/`)

Orchestrates playback through three background workers and a command manager:

| Worker | File | Responsibility |
|---|---|---|
| `PacketReadingWorker` | `Engine/PacketReadingWorker.cs` | Continuously reads compressed packets; targets ~1 second of buffer |
| `FrameDecodingWorker` | `Engine/FrameDecodingWorker.cs` | Decodes packets into blocks, writes to `MediaBlockBuffer` |
| `BlockRenderingWorker` | `Engine/BlockRenderingWorker.cs` | Reads blocks at the right clock position, calls platform renderers |

All workers extend `IntervalWorkerBase` (`Primitives/IntervalWorkerBase.cs`) → `WorkerBase`, which is an adaptive-timing background loop with `StartAsync` / `PauseAsync` / `ResumeAsync` / `Dispose` lifecycle.

**Command routing** (`Commands/CommandManager.*.cs`) — three queues:
- **Direct** (`CommandManager.Direct.cs`) — `Open`, `Close`, `ChangeMedia`; exclusive, executes immediately
- **Priority** (`CommandManager.Priority.cs`) — `Play`, `Pause`, `Stop`; queued but ahead of seeks
- **Seek** (`CommandManager.Seek.cs`) — coalesced: a pending seek is replaced by a later one

`MediaEngineState.cs` holds all playback state and implements `INotifyPropertyChanged`. The WPF layer polls it every 15 ms via a `DispatcherTimer`.

### Layer 3 — MediaElement (`Unosquare.FFME.Windows/`)

The WPF `UserControl` that consumer applications use. Split into partial classes:

- `MediaElement.cs` — core logic, owns `MediaEngine`, drives the 15 ms sync timer
- `MediaElement.Properties.cs` — all WPF dependency properties (`Source`, `Position`, `Volume`, `SpeedRatio`, `IsLooping`, etc.)
- `MediaElement.Events.cs` — event declarations (`MediaOpened`, `MediaFailed`, `FrameDecoded`, etc.)

Platform renderers in `Rendering/`:
- `AudioRenderer.cs` — NAudio-based output with optional SoundTouch integration
- `VideoRenderer.cs` / `InteropVideoRenderer.cs` / `ImageHost.cs` — writes decoded RGB frames to a WPF `WriteableBitmap`
- `SubtitleRenderer.cs` + `SubtitlesControl.cs` — text overlay
- `ClosedCaptionsControl.cs` + `ClosedCaptionsBuffer.cs` — CEA-608 caption parsing and display

`Platform/MediaConnector.cs` implements `IMediaConnector`, the bridge between `MediaEngine` callbacks and the WPF element. `Platform/GuiContext.cs` wraps `Dispatcher` for thread-safe UI callbacks.

## Key Design Patterns

**Thread safety without `lock`:** Hot-path state uses custom `Atomic*` types (`Primitives/AtomicBoolean.cs`, `AtomicTimeSpan.cs`, etc.) backed by `Interlocked` operations, not monitors. Use these for any new shared state accessed by workers.

**Timing:** `TimingController.cs` maintains independent real-time clocks per media type. `RealtimeClock.cs` is the underlying clock primitive. Workers use these, not wall-clock comparisons.

**MediaTypeDictionary:** A fixed-size, typed dictionary keyed by `MediaType` enum (Audio/Video/Subtitle/Data). Used extensively for per-type state, buffers, and renderers.

## Compiler Settings

- `LangVersion=preview` — latest C# preview features are in use
- `TreatWarningsAsErrors=true` — keep this; only `CS8019` is excluded (see copilot-instructions)
- `AllowUnsafeBlocks=true` — required for FFmpeg P/Invoke
- All builds target `net10.0-windows`
