<p align="center">
  <img src="Assets/logo.png" width="220" alt="BliBloup logo">
</p>

<h1 align="center">BliBloup</h1>

<p align="center">
  Free soundboard for playing audio through a virtual microphone (or any output you like).
</p>

<h1 align="center">Build &amp; Run</h1>

Requires the .NET 8 SDK on Windows.

```powershell
dotnet build -c Release
.\bin\Release\net8.0\win-x64\BliBloup.exe
```

Single-file self-contained build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
.\publish\BliBloup.exe
```

<div>

## 🎯 What is BliBloup?

BliBloup is a soundboard for Windows that plays audio clips through a virtual microphone, so your friends in Discord, a game or any voice application hear them as your microphone input. It is built with Avalonia UI and NAudio.

## ❓ How to use

1. Make sure VB-Cable is installed (BliBloup detects it and can install it for you).
2. After restarting your device, open Settings: set the playback device to `CABLE Input` and the microphone to your real microphone.
3. In your game or Discord, select `Cable Output` as the microphone input.
4. Import your audio files, assign hotkeys if you want, and play them.

## ✨ Key Features

### 🎚️ Soundboard

- **Sound library** — import MP3/WAV files (multi-select), organise multiple soundboards, reorder sounds with drag and drop.
- **Trim editor** — non-destructive waveform selection with audible preview; save as a trimmed copy or overwrite the original.
- **Custom thumbnails** — every sound can have its own picture; imports are center-cropped, resized to 256×256 and compressed automatically.
- **Editing tools** — duplicate, rename inline, delete with confirmation, reset a thumbnail to the default.
- **Simultaneous playback** — several sounds can play at once through a real mix bus with an anti-clipping limiter.

### ⌨️ Background control

- **Global hotkeys** — assign a combination to each sound plus play/pause, stop, pause and an emergency stop key.
- **Works without focus** — play a sound while a game or another app is in the foreground; BliBloup never steals focus.
- **Conflict detection** — duplicate combinations, Windows-reserved shortcuts and combinations already owned by other applications are reported.
- **Tray and notifications** — the tray shows what is playing, offers Stop all and hotkey toggling, and a discreet toast confirms background playback.

### 💾 Reliability

- **Safe persistence** — readable JSON library, atomic saves, previous-version backup and rotating backups.
- **Single instance** — a second launch focuses the running window instead of starting another copy.
- **Easy upgrades** — older data folders are migrated automatically on first run.

### 🖥️ Technical

- **Stack** — .NET 8, Avalonia UI, NAudio/WASAPI, SharpHook global keyboard hooks.
- **Windows build** — a self-contained `win-x64` executable is produced by the publish command above; the code is structured with Avalonia to allow future ports.

## 📦 Requirements

VB-Cable Virtual Audio Device is required for virtual microphone routing. BliBloup verifies its presence at startup and offers an installation path.

</div>

## Credits

BliBloup is a rebranded fork of the open-source [BoomBx](https://github.com/sardeq/BoomBx) project by sardeq. Many thanks to the original author for the audio engine, soundboard and virtual microphone routing foundations.
