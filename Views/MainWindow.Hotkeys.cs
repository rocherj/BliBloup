using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BoomBx.Models;
using BoomBx.Services;

namespace BoomBx.Views
{
    /// <summary>
    /// Global hotkeys: registration of transport actions and per-clip playback, key capture,
    /// conflict reporting, enable/disable handling and the tray indicator updates.
    ///
    /// Every action is posted to the UI thread: the hook itself never touches Avalonia state.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string PlayPauseHotkeyBoxName = "PlayPauseHotkeyTextBox";
        private const string StopHotkeyBoxName = "StopHotkeyTextBox";
        private const string PauseHotkeyBoxName = "PauseHotkeyTextBox";
        private const string PanicHotkeyBoxName = "PanicHotkeyTextBox";

        private GlobalHotkeyManager? _hotkeyManager;
        private TextBox? _currentlySettingHotkey;
        private SoundItem? _currentlySettingClip;
        private bool _hotkeyCaptureActive;

        private void InitializeHotkeys()
        {
            _hotkeyManager?.Dispose();

            _hotkeyManager = new GlobalHotkeyManager();
            _hotkeyManager.HotkeyTriggered += OnHotkeyTriggered;

            if (!_hotkeyManager.TryStart(out var startError))
            {
                UpdateStatus($"Global hotkeys unavailable: {startError}", true);
            }

            ViewModel.HotkeysEnabled = _settings.HotkeysEnabled;
            _hotkeyManager.IsEnabled = _settings.HotkeysEnabled;

            Logger.Log(_hotkeyManager.IsRunning
                ? "Global hotkey hook started."
                : "Global hotkey hook is not running.");

            LoadAndRegisterHotkeys();
            UpdateTrayHotkeysState();
        }

        /// <summary>
        /// Rebuilds every binding: the four transport actions plus one combination per clip.
        /// Duplicates and combinations owned by other applications are reported, not swallowed.
        /// </summary>
        private void LoadAndRegisterHotkeys(bool announce = true)
        {
            if (_hotkeyManager is null)
            {
                return;
            }

            var bindings = new List<GlobalHotkeyManager.HotkeyBinding>();
            var conflicts = new List<string>();
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Register(string owner, string? raw, Action action)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                if (!HotkeyText.TryParse(raw, out var gesture, out var parseError) || gesture is null)
                {
                    conflicts.Add($"{owner}: invalid hotkey \"{raw}\" ({parseError})");
                    return;
                }

                if (!HotkeyText.IsAssignable(gesture, out var reason))
                {
                    conflicts.Add($"{owner}: {reason}");
                    return;
                }

                var canonical = HotkeyText.Format(gesture);

                if (owners.TryGetValue(canonical, out var firstOwner))
                {
                    conflicts.Add($"{owner}: \"{canonical}\" is already used by {firstOwner}");
                    return;
                }

                owners[canonical] = owner;
                bindings.Add(new GlobalHotkeyManager.HotkeyBinding(
                    gesture,
                    owner,
                    () => Dispatcher.UIThread.Post(action)));
            }

            Register("Play / Pause", _settings.PlayPauseHotkey, () => PlayPauseHandler(null, null!));
            Register("Stop", _settings.StopHotkey, () => StopAudioProcessing());
            Register("Pause", _settings.PauseHotkey, () => PauseAudioProcessing());
            Register("Emergency stop", _settings.PanicHotkey, PerformPanicStop);

            foreach (var sound in GetAllSounds())
            {
                if (string.IsNullOrWhiteSpace(sound.Hotkey))
                {
                    continue;
                }

                var clip = sound;
                Register($"Clip \"{clip.Name}\"", clip.Hotkey, () => StartAudioProcessing(clip));
            }

            // Another application that registered the same combination keeps receiving it too:
            // the sound would fire twice. Report it instead of failing silently.
            var windowHandle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

            foreach (var binding in bindings)
            {
                var availability = HotkeyConflictDetector.Probe(windowHandle, binding.Gesture);

                if (availability == HotkeyAvailability.UsedByAnotherApplication)
                {
                    conflicts.Add($"\"{HotkeyText.Format(binding.Gesture)}\" ({binding.Label}) is already registered by another application");
                }
                else if (availability == HotkeyAvailability.ReservedByWindows)
                {
                    conflicts.Add($"\"{HotkeyText.Format(binding.Gesture)}\" ({binding.Label}) is reserved by Windows and may not fire");
                }
            }

            _hotkeyManager.SetBindings(bindings);
            Logger.Log($"Global hotkeys: {bindings.Count} binding(s), {conflicts.Count} conflict(s), enabled={ViewModel.HotkeysEnabled}.");

            ViewModel.PlayPauseHotkey = DisplayHotkey(_settings.PlayPauseHotkey);
            ViewModel.StopHotkey = DisplayHotkey(_settings.StopHotkey);
            ViewModel.PauseHotkey = DisplayHotkey(_settings.PauseHotkey);
            ViewModel.PanicHotkey = DisplayHotkey(_settings.PanicHotkey);

            ApplyHotkeyWarnings(conflicts);

            if (announce)
            {
                if (conflicts.Count > 0)
                {
                    UpdateStatus($"Hotkeys loaded with {conflicts.Count} conflict(s). See Settings › Hotkeys.", true);
                }
                else
                {
                    UpdateStatus("Hotkeys loaded successfully.", false);
                }
            }
        }

        private void ApplyHotkeyWarnings(IReadOnlyList<string> conflicts)
        {
            ViewModel.HasHotkeyWarnings = conflicts.Count > 0;
            ViewModel.HotkeyWarnings = conflicts.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, conflicts.Select(conflict => "⚠ " + conflict));

            if (conflicts.Count > 0)
            {
                Logger.Log("Hotkey conflicts: " + string.Join(" | ", conflicts));
            }
        }

        private void OnHotkeyTriggered(object? sender, GlobalHotkeyManager.HotkeyTriggeredEventArgs e)
        {
            Logger.Log($"Hotkey triggered: {e.Gesture} → {e.Label}");

            // Raised on the hook thread: everything UI bound must go through the dispatcher.
            Dispatcher.UIThread.Post(() =>
            {
                ViewModel.LastHotkeyDisplay = string.IsNullOrEmpty(e.Gesture)
                    ? e.Label
                    : $"{e.Gesture} → {e.Label}";

                UpdateStatus($"⌨ {e.Gesture} → {e.Label}");
            });
        }

        private void PerformPanicStop()
        {
            if (_currentPlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            StopAudioProcessing(updateStatus: false);
            Logger.Log("Panic stop: playback stopped.");
            UpdateStatus("🛑 Panic stop — playback stopped");
        }

        private IEnumerable<SoundItem> GetAllSounds() =>
            ViewModel.Soundboards.SelectMany(board => board.Sounds);

        #region Hotkey capture

        private void SetHotkey_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not TextBox textBox)
            {
                return;
            }

            BeginHotkeyCapture(textBox, null);
        }

        private void SetClipHotkey_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } clip)
            {
                UpdateStatus("Select a sound first.", true);
                return;
            }

            BeginHotkeyCapture(ClipHotkeyTextBox, clip);
        }

        private void ClearClipHotkey_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } clip || string.IsNullOrWhiteSpace(clip.Hotkey))
            {
                return;
            }

            clip.Hotkey = string.Empty;
            SaveSoundLibrary();
            LoadAndRegisterHotkeys(announce: false);
            UpdateStatus($"Hotkey cleared for \"{clip.Name}\".");
        }

        private void BeginHotkeyCapture(TextBox textBox, SoundItem? clip)
        {
            EndHotkeyCapture();

            _currentlySettingHotkey = textBox;
            _currentlySettingClip = clip;
            _hotkeyCaptureActive = true;

            // Pause global hotkeys so the combination being pressed cannot trigger playback.
            if (_hotkeyManager != null)
            {
                _hotkeyManager.IsEnabled = false;
            }

            KeyDown += Hotkey_KeyDown;
            Deactivated += MainWindow_Deactivated;

            textBox.Text = "Press a combination…";
            textBox.Focus();

            UpdateStatus(clip is null
                ? "Press a key combination (Esc cancels)."
                : $"Press a key combination for \"{clip.Name}\" (Esc cancels).");
        }

        private void EndHotkeyCapture()
        {
            if (!_hotkeyCaptureActive && _currentlySettingHotkey is null)
            {
                return;
            }

            var textBox = _currentlySettingHotkey;
            var clip = _currentlySettingClip;

            _hotkeyCaptureActive = false;
            _currentlySettingHotkey = null;
            _currentlySettingClip = null;

            KeyDown -= Hotkey_KeyDown;
            Deactivated -= MainWindow_Deactivated;

            if (_hotkeyManager != null)
            {
                _hotkeyManager.IsEnabled = ViewModel.HotkeysEnabled;
            }

            if (textBox != null)
            {
                textBox.Text = clip is null
                    ? DisplayHotkey(GetGlobalHotkeySetting(textBox.Name))
                    : DisplayHotkey(clip.Hotkey);
            }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (!_hotkeyCaptureActive)
            {
                return;
            }

            EndHotkeyCapture();
            UpdateStatus("Hotkey capture cancelled (window lost focus).", true);
        }

        private void Hotkey_KeyDown(object? sender, KeyEventArgs e)
        {
            if (!_hotkeyCaptureActive || _currentlySettingHotkey is null)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EndHotkeyCapture();
                UpdateStatus("Hotkey capture cancelled.");
                return;
            }

            // Wait for a real key: modifiers arrive first and are not a complete combination.
            if (HotkeyText.IsModifierKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;

            var gesture = new KeyGesture(e.Key, e.KeyModifiers);

            if (!HotkeyText.IsAssignable(gesture, out var reason))
            {
                EndHotkeyCapture();
                UpdateStatus($"⚠ {reason}", true);
                return;
            }

            var canonical = HotkeyText.Format(gesture);
            var textBox = _currentlySettingHotkey;
            var clip = _currentlySettingClip;

            var duplicateOwner = FindHotkeyOwner(canonical, textBox, clip);

            if (duplicateOwner is not null)
            {
                EndHotkeyCapture();
                UpdateStatus($"⚠ \"{canonical}\" is already used by {duplicateOwner}.", true);
                return;
            }

            if (clip is not null)
            {
                clip.Hotkey = canonical;
                SaveSoundLibrary();
                EndHotkeyCapture();
                LoadAndRegisterHotkeys(announce: false);
                UpdateStatus($"Hotkey {canonical} assigned to \"{clip.Name}\".");
                return;
            }

            ApplyGlobalHotkey(textBox.Name, canonical);
            EndHotkeyCapture();
            LoadAndRegisterHotkeys(announce: false);
            UpdateStatus($"Hotkey set to {canonical} for {DescribeGlobalHotkey(textBox.Name)}.");
        }

        private void ApplyGlobalHotkey(string? textBoxName, string canonical)
        {
            switch (textBoxName)
            {
                case PlayPauseHotkeyBoxName:
                    _settings.PlayPauseHotkey = canonical;
                    break;
                case StopHotkeyBoxName:
                    _settings.StopHotkey = canonical;
                    break;
                case PauseHotkeyBoxName:
                    _settings.PauseHotkey = canonical;
                    break;
                case PanicHotkeyBoxName:
                    _settings.PanicHotkey = canonical;
                    break;
                default:
                    return;
            }

            SettingsManager.SaveSettings(_settings);
        }

        private string GetGlobalHotkeySetting(string? textBoxName) => textBoxName switch
        {
            PlayPauseHotkeyBoxName => _settings.PlayPauseHotkey,
            StopHotkeyBoxName => _settings.StopHotkey,
            PauseHotkeyBoxName => _settings.PauseHotkey,
            PanicHotkeyBoxName => _settings.PanicHotkey,
            _ => string.Empty
        };

        private static string DescribeGlobalHotkey(string? textBoxName) => textBoxName switch
        {
            PlayPauseHotkeyBoxName => "play / pause",
            StopHotkeyBoxName => "stop",
            PauseHotkeyBoxName => "pause",
            PanicHotkeyBoxName => "emergency stop",
            _ => "hotkey"
        };

        private string? FindHotkeyOwner(string canonical, TextBox? targetBox, SoundItem? targetClip)
        {
            if (!string.Equals(targetBox?.Name, PlayPauseHotkeyBoxName, StringComparison.Ordinal) &&
                string.Equals(DisplayHotkey(_settings.PlayPauseHotkey), canonical, StringComparison.OrdinalIgnoreCase))
            {
                return "play / pause";
            }

            if (!string.Equals(targetBox?.Name, StopHotkeyBoxName, StringComparison.Ordinal) &&
                string.Equals(DisplayHotkey(_settings.StopHotkey), canonical, StringComparison.OrdinalIgnoreCase))
            {
                return "stop";
            }

            if (!string.Equals(targetBox?.Name, PauseHotkeyBoxName, StringComparison.Ordinal) &&
                string.Equals(DisplayHotkey(_settings.PauseHotkey), canonical, StringComparison.OrdinalIgnoreCase))
            {
                return "pause";
            }

            if (!string.Equals(targetBox?.Name, PanicHotkeyBoxName, StringComparison.Ordinal) &&
                string.Equals(DisplayHotkey(_settings.PanicHotkey), canonical, StringComparison.OrdinalIgnoreCase))
            {
                return "emergency stop";
            }

            foreach (var sound in GetAllSounds())
            {
                if (ReferenceEquals(sound, targetClip))
                {
                    continue;
                }

                if (string.Equals(DisplayHotkey(sound.Hotkey), canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return $"clip \"{sound.Name}\"";
                }
            }

            return null;
        }

        private static string DisplayHotkey(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            return HotkeyText.TryParse(raw, out var gesture, out _) && gesture is not null
                ? HotkeyText.Format(gesture)
                : raw;
        }

        #endregion

        #region Enable / disable and tray

        private void HotkeysEnabledBox_Click(object? sender, RoutedEventArgs e)
        {
            ApplyHotkeysEnabled(ViewModel.HotkeysEnabled);
        }

        /// <summary>Toggles global hotkeys (mixed in from the tray menu).</summary>
        public void ToggleHotkeysFromTray()
        {
            ApplyHotkeysEnabled(!ViewModel.HotkeysEnabled);
        }

        private void ApplyHotkeysEnabled(bool enabled)
        {
            ViewModel.HotkeysEnabled = enabled;
            _settings.HotkeysEnabled = enabled;
            SettingsManager.SaveSettings(_settings);

            if (_hotkeyManager != null)
            {
                _hotkeyManager.IsEnabled = enabled && !_hotkeyCaptureActive;
            }

            UpdateTrayHotkeysState();
            UpdateStatus(enabled ? "Global hotkeys enabled." : "Global hotkeys disabled.");
        }

        private void UpdateTrayHotkeysState() =>
            (Application.Current as App)?.SetTrayHotkeysEnabled(ViewModel.HotkeysEnabled);

        /// <summary>Stops every sound from the tray menu without focusing the window.</summary>
        public void StopAllSoundsFromTray()
        {
            if (_currentPlaybackState == PlaybackState.Stopped)
            {
                return;
            }

            StopAudioProcessing(updateStatus: false);
            UpdateStatus("⏹ Playback stopped from the tray.");
        }

        private static void UpdateTrayNowPlaying(string? soundName) =>
            (Application.Current as App)?.SetNowPlaying(soundName);

        private void DisposeHotkeys()
        {
            if (_hotkeyManager is null)
            {
                return;
            }

            EndHotkeyCapture();
            _hotkeyManager.HotkeyTriggered -= OnHotkeyTriggered;
            _hotkeyManager.Dispose();
            _hotkeyManager = null;
        }

        #endregion

        #region Tests

        /// <summary>Runs a transport action once, used by the "Test" buttons of the hotkey section.</summary>
        private void TestHotkey_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string action)
            {
                return;
            }

            switch (action)
            {
                case "PlayPause":
                    PlayPauseHandler(null, null!);
                    break;
                case "Stop":
                    StopAudioProcessing();
                    break;
                case "Pause":
                    PauseAudioProcessing();
                    break;
                case "Panic":
                    PerformPanicStop();
                    break;
            }

            ViewModel.LastHotkeyDisplay = $"Test → {action}";
        }

        #endregion
    }
}
