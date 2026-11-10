```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/)

## [Unreleased]
### Added
- VB Cable driver configuration with app
- Sound Library System
- Microphone and playback devices config
- Global hotkeys: play the soundboard while BoomBx is in the background, without stealing focus
- Per-clip hotkeys, configurable play/pause, stop, pause and emergency stop combinations
- Tray indicator showing the sound currently playing, with stop and enable/disable hotkeys actions
- Hotkey conflict detection (duplicate combinations, combinations owned by another app, Windows reserved shortcuts)
- Non-destructive trim per clip with waveform preview, audible preview and save as copy / overwrite
- Simultaneous playback of several sounds (real mixing instead of stop-and-play)
- Peak limiter on both the speaker mix and the virtual microphone mix to prevent clipping
- Readable library format (v2) with atomic saves, previous-version backup and rotating timestamped backups
- Automatic migration of legacy libraries, recovery from a corrupt library file and a documented data-directory override
- Single instance: a second launch focuses the running BoomBx instead of starting another one
- The first soundboard is selected automatically at startup
- Drag and drop reordering of the sounds in a soundboard
- Duplicate, rename and delete (with confirmation) actions, plus quick hotkey editing and deletion directly on each sound tile
- Redesigned sound panel: the title is an obvious inline edit, settings scroll, and the trim editor plus Play/Stop transport stay pinned at the bottom (visible even in a small window)
- Clear empty states: "No sound selected" placeholder and a "This soundboard is empty" message with a direct Add Sound action
- Keyboard focus rings and higher contrast for the dark theme; smaller minimum window size
- Discreet background playback toast (no focus steal) and a single click on the tray icon reopens the window
- Actionable audio error messages (missing file, no output device, unavailable routing)
- Sound thumbnails: bundled default thumbnail (soundboard.png), click the thumbnail in the sound panel to pick a custom image, and a missing custom image falls back to the default

### Removed
- Redundant Rename button (the inline Title field is the rename control)
- Dead code: the old WPF `HotkeyManager`, empty partials, unused sample providers/converters and the duplicated data path handling

### Fixed
- Performance and UI optimizations
- Fix sound Icon bug
- Global hotkey hook is now detached and disposed on close instead of leaking
- Application startup is silent: the splash screen no longer plays the bundled Vine Boom sound
- Sound properties now raise change notifications, so hotkey badges, titles and sliders refresh live
- Custom sound icons are stored once (the old code created a nested "icons/icons" folder)
- Removed the outdated SharpHook package warning (pinned to the resolved 6.0.0)

---

## [0.1.2] - 2025-05-03
### Added
- Add Splash Window
- Optimize Startup
- Enhanced Sound Library
### Fixed
- Fix default devices bug

---

## [0.1.3] - 2025-05-03
### Added
- Switch from the user being able to hear their own sounds from listening device of the Cable OUTPUT to the default speakers
- Added Trello and Github links in settings
- Enhanced Sound Library
### Fixed
- Fix microphone not inputting audio when in audio playback
- Fix CaptureComboBox not saving last device when changing

---

## [0.1.4] - 2025-5-08
### Added
- Added Loop Checkbox
- Play/Pause/Stop UI and sound Optimization
### Fixed
- Fixed hen an audio is paused, if another one is played the old plays instead.

## [0.1.5] - 2025-5-17
### Added
- First version of soundboard management
### Fixed
- Fixed app data bug
- Partially fixed driver checks and drivers not installing on some devices

## [0.1.6] - 2025-5-31
### Added
- Added Audio Controls like Bass and Pitch
- Added TTS integrated with ESpeak
- Added New UI for Sounds tab
### Fixed
- Fixed pitch and bass bugs
- Fixed app not finding espeak data files
- Fixed speed formula for TTS
- Fixed UI Issues
- Allowed Minimize and Maximize


## [0.1.7] - 2025-7-??
### Added
- ?
### Fixed
- Persistant audio bug fixed
