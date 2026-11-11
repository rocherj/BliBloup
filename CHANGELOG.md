# Changelog

Notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/) and the project uses semantic versioning.

## [1.0.0] - 2026-11-11

First public release of BliBloup, based on BoomBx 0.1.6.

### Added

- Soundboard library with multiple boards, MP3/WAV import, drag and drop reordering, inline renaming, duplication and deletion with confirmation.
- Waveform trim editor with audible preview and the choice between saving a trimmed copy or overwriting the original file.
- Mixing playback so several sounds can play at the same time, with per-sound volume, bass, treble and pitch controls.
- Peak limiter on the speaker and virtual microphone outputs to avoid clipping.
- Global hotkeys per sound and for the transport actions (play/pause, stop, pause, emergency stop), working while other applications have focus.
- Hotkey conflict detection, including duplicate combinations, Windows reserved shortcuts and combinations already registered by other applications.
- System tray icon showing the current sound, with actions to stop playback, toggle hotkeys and reopen the window.
- Background playback notification shown only when the main window is not active.
- Custom sound thumbnails: click the thumbnail in the sound panel to import an image, which is center-cropped, resized to 256x256 and compressed. A default thumbnail is bundled and a reset action restores it.
- Readable JSON library format with atomic saves, previous-version backup and rotating timestamped backups.
- Automatic migration of older libraries and of the previous BoomBx data folder.
- Single-instance protection: launching a second copy focuses the existing window.
- Keyboard focus indicators and improved contrast in the dark theme.
- Microphone latency settings: WASAPI shared capture with a configurable 10-150 ms buffer (40 ms default), an exclusive mode option that falls back to shared when the driver rejects it, and MMCSS "Pro Audio" thread priority for the capture thread.
- Trim editor reworked around an interactive waveform control: draggable start and end handles, drag-to-select, click-to-place playhead and a 3 px threshold between a click and a drag. The previous start/end sliders were removed.

### Changed

- Sound panel layout reworked so the trim editor and the Play/Stop transport remain visible while the settings scroll.
- The title in the sound panel is now an editable field, which made the separate Rename button unnecessary.
- Empty states added for an empty soundboard and for no selection in the sound panel.
- Application logo, default thumbnail, name and data folder updated to BliBloup.
- The GitHub link in Settings now points to github.com/rocherj.

### Fixed

- Global hotkey hook is detached and disposed on close instead of leaking.
- Startup is silent; the splash screen no longer plays the bundled sound.
- Sound properties raise change notifications, so titles, hotkey badges and sliders refresh immediately.
- Custom sound thumbnails are stored in a single icons folder instead of a nested one.
- Trim start and end controls can no longer cross each other, which previously caused a crash.
- Truncated controls in the trim section were fixed.
- SharpHook package warning removed by pinning the resolved 6.0.0 version.

### Removed

- Trello link in Settings.
- Dead code from earlier versions, including the old WPF hotkey manager, empty partial classes, unused sample providers and converters, and duplicated data path handling.

## Earlier versions

Versions 0.1.2 to 0.1.6 were developed in the original BoomBx project. See the upstream repository for their change history.
