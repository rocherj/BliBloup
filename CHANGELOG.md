# Changelog

Notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/) and the project uses semantic versioning.

## [1.1.0] - 2026-11-12

Second public release, focused on real-time voice processing, a more consistent interface and a
large stability/security pass over the audio pipeline, persistence and TTS.

### Added

- Voice Changer page with real-time microphone EQ, pitch, distortion, echo, reverb and robot effects, local headphone monitoring and ten presets (Chipmunk, Demon, Robot, Phone/Radio, Cave, Alien/Insect, Monster/Ogre, Megaphone, Ghost/Spirit, Underwater).
- Live white playhead in the trim waveform, synchronized with playback and honoring the trim selection and loop mode.
- Modern donut slider thumbs with accent hover/pressed states, rounded expander headers with restored dark styling, harmonized buttons, rounded sidebar/soundboard selection, unified presets panel and a modern arrow-free scrollbar.
- Default window size updated to 1505 × 842.

### Changed

- Voice presets are grouped in a single wrapping **Presets** section.
- The right-hand sound panel now follows the same accent color, spacing and control styling as the left sidebar.
- Per-clip hotkey settings have a fixed header and an independently scrolling list.

### Fixed

- Audio hangs on empty/corrupt looped sources and buffer overflow when downmixing 5.1/7.1 files.
- Race conditions between the UI and audio threads when resetting the pitch shifter or replacing EQ filters; effect parameters are snapshotted per buffer.
- Stopped speaker outputs are invalidated and recreated on the next sound instead of silently swallowing playback.
- Voice monitoring with normal default speakers and duplicate clip routing on the same output device.
- JSON library loading now tolerates null `Sounds` collections and null board entries instead of discarding the whole library.
- Trim overwrite uses an atomic replace with a temporary backup; async trim saves no longer update the wrong board/sound if the selection changed.
- Voice changer settings saves are protected against disk-full/locked-file failures, and pending voice/trim settings are flushed when the window closes.
- VB-Cable installation detects success correctly and no longer freezes the UI while waiting for the installer.
- Deleting or removing the selected sound now clears the right-hand sound panel.
- File pickers, dialogs and delete actions can no longer crash the application when a dialog is cancelled or the storage provider fails.
- TTS input is passed to eSpeak via `ArgumentList` instead of a concatenated command line; eSpeak initialization runs off the UI thread, its output pipes are read concurrently and a 60-second timeout prevents deadlocks.
- Scrollbar thumbs and list focus visuals behave correctly, including the Per-clip hotkeys scrollbar.

### Security

- Removed command-line argument injection through the TTS text field.

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
- Sounds are decoded once into an in-memory PCM cache when the library loads or a file is imported; triggering a sound reads the decoded buffer instead of reopening the file, with LRU eviction and invalidation when a sound is removed or a file changes.
- Per-sound Mute the preview option: the clip keeps playing in the virtual microphone mix but is silenced on the local speakers/headphones. The setting is stored with the sound and applied to voices that are already playing.
- Pitch shifter bypass at exactly 1.0x and smoothed pitch changes, fixing the metallic tail that used to remain after returning to normal pitch.
- Per-sound effects: soft-clipping distortion, a circular echo/delay with feedback, a light three-comb reverb and a ring-modulator robot voice. Parameters are ramped to avoid clicks and each effect is fully bypassed when set to zero.
- Trim Preview selection now plays only on the local speakers/headphones without feeding the virtual microphone.
- Microphone latency settings: WASAPI shared capture with a configurable 10-150 ms buffer (40 ms default), an exclusive mode option that falls back to shared when the driver rejects it, and MMCSS "Pro Audio" thread priority for the capture thread.
- Trim editor reworked around an interactive waveform control: draggable start and end handles, drag-to-select, click-to-place playhead and a 3 px threshold between a click and a drag. The previous start/end sliders were removed.

### Changed

- Sound panel layout reworked so the trim editor and the Play/Stop transport remain visible while the settings scroll.
- Sound settings are grouped into collapsible Tone, Pitch & Modulation, Distortion and Space & FX sections; Volume, Loop and Mute stay at the top and Play/Stop/Save remain in the fixed footer.
- Selected sound tiles use a rounded accent frame around the whole tile instead of top and bottom selection lines.
- The title in the sound panel is now an editable field, which made the separate Rename button unnecessary.
- Empty states added for an empty soundboard and for no selection in the sound panel.
- Application logo, default thumbnail, name and data folder updated to BliBloup.
- The GitHub link in Settings now points to github.com/rocherj.

### Fixed

- Global hotkey hook is detached and disposed on close instead of leaking.
- Pitch Reset button: forces 1.0x and discards the pitch-shifter state, so the metallic tail cannot survive a return to normal pitch.
- The right panel now scrolls all settings (including TRIM) below the fixed header; the compact accent frame around selected tiles and the responsive WrapPanel grid replace the previous clipped two-line selection.
- Settings: the version line at the bottom of the page is no longer clipped by the window edge.
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
