<p align="center">
  <img src="Assets/logo.png" width="220" alt="BliBloup logo">
</p>

# BliBloup

BliBloup is a soundboard for Windows. It plays audio clips through a virtual microphone, so applications such as Discord, a game or a voice chat receive the sound as microphone input. The application is written in C# with .NET 8, Avalonia and NAudio.

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

Sounds are added from the Sounds tab. A sound can be trimmed, given its own thumbnail and assigned a global hotkey. Sounds play through a mix bus, so several of them can be active at the same time.

## Features

**Sound library.** The library supports multiple soundboards, multi-file import of MP3 and WAV files, drag and drop reordering, inline renaming, duplication and deletion with confirmation. Each sound can have a square thumbnail; imported images are center-cropped, resized to 256x256 and compressed, and sounds without a custom image use the bundled default thumbnail.

**Playback and editing.** Playback uses a mixer so several sounds can play simultaneously, with per-sound volume, bass, treble and pitch controls and a limiter on both the speaker and virtual microphone outputs. The trim editor shows the waveform and allows an audible preview. The waveform is interactive: the blue handles set the start and end of the selection, dragging across it selects a new range, and a click places the playhead. The result can be saved as a copy or used to overwrite the original file.

**Microphone capture.** The microphone is captured through WASAPI in shared mode with an event-driven buffer, so other applications can keep using it. The buffer size is configurable from 10 to 150 ms (40 ms by default): lower values reduce latency but increase CPU load and the risk of dropouts, while higher values are more stable. An exclusive capture mode is available for the lowest latency, at the cost of blocking the microphone for other applications; if the driver rejects it, BliBloup falls back to shared mode. On Windows the capture thread also requests the MMCSS "Pro Audio" priority.

**Hotkeys and background use.** Each sound can have its own global hotkey, and the transport actions (play/pause, stop, pause and emergency stop) are configurable as well. Hotkeys work while another application has focus, without bringing BliBloup to the front. The settings report duplicate combinations, shortcuts already used by Windows and combinations owned by other applications.

**Tray and notifications.** The tray icon shows the sound currently playing and provides actions to stop playback, enable or disable hotkeys and reopen the window. When a sound starts while the main window is not active, a short toast confirms it.

**Data storage.** Settings and the sound library are stored under `%APPDATA%\BliBloup`. The library is a readable JSON file saved atomically, with a previous-version backup and rotating timestamped backups. Data from older BoomBx installations is migrated automatically on first run. The `BLIBLOUP_DATA_DIR` environment variable can be used to keep a separate library.

## Credits

BliBloup is a rebranded fork of the open-source [BoomBx](https://github.com/sardeq/BoomBx) project by sardeq. The audio engine, soundboard and virtual microphone routing started there, and the original author deserves credit for that work.
