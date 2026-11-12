<p align="center">
  <img src="Assets/logo.png" width="220" alt="BliBloup logo">
</p>

# BliBloup

BliBloup is a soundboard for Windows. It plays audio clips through a virtual microphone, so applications such as Discord, a game or a voice chat receive the sound as microphone input. The application is written in C# with .NET 8, Avalonia and NAudio.

Current release: **1.1.0**. See [CHANGELOG.md](CHANGELOG.md) for the full release notes.

## Requirements

BliBloup runs on Windows x64. Routing audio through a virtual microphone requires VB-Cable; the application checks for the driver at startup and offers to install it. The published executable is self-contained, so no .NET runtime is needed on the target machine.

## Build and run

```powershell
dotnet build -c Release
.\bin\Release\net8.0\win-x64\BliBloup.exe
```

To produce the standalone single-file executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\BliBloup.exe
```

## Usage

Open Settings and set the playback device to `CABLE Input` and the microphone to your real microphone. In the application you want to send audio to, select `Cable Output` as the microphone.

Sounds are added from the Sounds tab. A sound can be trimmed, given its own thumbnail and assigned a global hotkey. Sounds play through a mix bus, so several of them can be active at the same time. The Voice Changer tab applies EQ, pitch, distortion, echo, reverb and robot effects to the live microphone; the soundboard remains unaffected.

## Features

**Sound library.** The library supports multiple soundboards, multi-file import of MP3 and WAV files, a responsive tile grid, drag and drop reordering, inline renaming, duplication and deletion with confirmation. Each sound can have a square thumbnail; imported images are center-cropped, resized to 256x256 and compressed, and sounds without a custom image use the bundled default thumbnail. Audio files are decoded once into memory when the library loads, so starting a sound does not decode the file again; decoded buffers are released when a sound is removed and a bounded LRU cache keeps memory use in check.

**Playback and editing.** Playback uses a mixer so several sounds can play simultaneously, with per-sound volume, bass, treble and pitch controls and a limiter on both the speaker and virtual microphone outputs. Each sound also has a Mute the preview option that keeps it in the virtual microphone mix while silencing it on the local speakers or headphones. Per-sound effects include soft-clipping distortion, an echo with adjustable delay and feedback, a light reverb and a ring-modulator robot voice; all of them are smoothed and bypassed when set to zero. The trim editor shows the waveform and allows an audible preview. The waveform is interactive: the blue handles set the start and end of the selection, dragging across it selects a new range, and a click places the playhead. During playback a white playhead follows the actual audio position in real time, stays inside the selected range and returns to the start of the selection when loop mode wraps. The result can be saved as a copy or used to overwrite the original file.

**Voice Changer.** The microphone can be processed in real time with tone EQ, pitch shifting, soft-clipping distortion, echo, reverb and a robot ring modulator. Ten presets are provided (Chipmunk, Demon, Robot, Phone/Radio, Cave, Alien/Insect, Monster/Ogre, Megaphone, Ghost/Spirit and Underwater), and monitoring on the local speakers/headphones is optional to avoid feedback. The processed voice goes to the virtual microphone; soundboard clips bypass the voice changer.

**Interface.** The dark interface uses a single accent colour across the sidebar, selection frames, sliders and primary actions. Sliders have a modern double-circle thumb, the panel sections use rounded cards, expanders have discreet dark headers, and the Per-clip hotkey list has a fixed header with a modern arrow-free scrollbar.

**Reliability and security.** The audio engine rejects invalid or missing files cleanly, protects against empty/corrupt looped streams, avoids multichannel downmix overflows and recovers a stopped output on the next sound. JSON loading tolerates partially invalid libraries, audio overwrites use a recoverable replacement, and TTS text is passed to eSpeak as isolated arguments rather than a shell-like command line.

**Microphone capture.** The microphone is captured through WASAPI in shared mode with an event-driven buffer, so other applications can keep using it. The buffer size is configurable from 10 to 150 ms (40 ms by default): lower values reduce latency but increase CPU load and the risk of dropouts, while higher values are more stable. An exclusive capture mode is available for the lowest latency, at the cost of blocking the microphone for other applications; if the driver rejects it, BliBloup falls back to shared mode. On Windows the capture thread also requests the MMCSS "Pro Audio" priority.

**Hotkeys and background use.** Each sound can have its own global hotkey, and the transport actions (play/pause, stop, pause and emergency stop) are configurable as well. Hotkeys work while another application has focus, without bringing BliBloup to the front. The settings report duplicate combinations, shortcuts already used by Windows and combinations owned by other applications.

**Tray and notifications.** The tray icon shows the sound currently playing and provides actions to stop playback, enable or disable hotkeys and reopen the window. When a sound starts while the main window is not active, a short toast confirms it.

**Data storage.** Settings and the sound library are stored under `%APPDATA%\BliBloup`. The library is a readable JSON file saved atomically, with a previous-version backup and rotating timestamped backups. Data from older BoomBx installations is migrated automatically on first run. The `BLIBLOUP_DATA_DIR` environment variable can be used to keep a separate library.

## Credits

BliBloup is a rebranded fork of the open-source [BoomBx](https://github.com/sardeq/BoomBx) project by sardeq. The audio engine, soundboard and virtual microphone routing started there, and the original author deserves credit for that work.
