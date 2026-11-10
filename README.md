<p align="center">
  <img src="Assets/logo.png" width="220" alt="BliBloup logo">
</p>

<h1 align="center">BliBloup</h1>

<p align="center">
  Free soundboard for playing audio through a virtual microphone (or anything if you want).
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

BliBloup is a cross-platform audio routing tool designed for content creators, streamers, and having fun with your friends. Easily play sounds through a virtual microphones with one-click simplicity.

## ❓How to use
If you want to play audio through your microphone to your friends in a game or discord do the following:
- Make sure you have VB-Cable installed (BliBloup will prompt to install it for you).
- After you have restarted your device, Go to settings make sure CABLE Input is set to playback device and your microphone as microphone input
- Set your input in your game/discord to Cable OUTPUT
- Add your own audios and Play them!

## ✨ Key Features


### 🚀 Core Functionality
- **VB-Cable Integration**  
  Automatic detection and one-click installation of VB-Cable drivers
- **Virtual Audio Routing**  
  Route system audio through virtual microphone inputs
- **Low-Latency Playback**  
  Powered by NAudio's WASAPI exclusive mode

### 🎚️ Audio Control
- **Per-Sound Audio Control**  
  Individual sound settings.
- **Format Conversion**  
  Automatic sample rate/channel conversion (48kHz stereo output)

### 📚 Sound Management
- **Custom Sound Library**  
  Import and organize MP3/WAV files with custom icons
- **Preset System**  
  Save favorite audio configurations

### 🛠️ Advanced Features
- **Audio Device Hot-Swapping**  
  Change outputs without restarting
- **Automatic Driver Verification**  
  Checks VB-Cable installation status on launch
- **Silent Installation**  
  Admin-free VB-Cable setup for organizations

## 🖥️ Technical Highlights
- **Cross-Platform**  
  Windows, macOS, and Linux support (Avalonia UI)
- **Modern Architecture**  
  MVVM pattern with .NET 8
- **Accessible**  
  Screen reader support and high-contrast themes

> **Note**: Requires VB-Cable Virtual Audio Device for full functionality  
> (Automatically installed through BliBloup)
> Will be making my own drivers eventually so this is temporary

</div>

## Credits

BliBloup is a rebranded fork of the open-source [BoomBx](https://github.com/sardeq/BoomBx) project by sardeq. Many thanks to the original author for the audio engine, soundboard and virtual microphone routing foundations.
