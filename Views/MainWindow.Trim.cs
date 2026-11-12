using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BliBloup.Models;
using BliBloup.Services;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace BliBloup.Views
{
    /// <summary>
    /// Trim editor: waveform preview, start/end selection, audible preview and baking the
    /// selection into a file (copy or overwrite, the latter after confirmation).
    ///
    /// The trim itself is non destructive metadata on <see cref="SoundItem"/>; playback
    /// applies it immediately, saving is an explicit action.
    ///
    /// The waveform control enforces the minimum selection length while a handle is dragged,
    /// and committed values are debounced before they reach the model.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Smallest trimmed length and the gap the two handles can never cross.</summary>
        private const double MinimumSelectionSeconds = 0.1;

        private readonly AudioTrimService _trimService = new();
        private bool _syncingTrim;
        private DispatcherTimer? _trimCommitTimer;

        private async Task LoadTrimInfoAsync(SoundItem? sound)
        {
            CancelPendingTrimCommit();

            if (sound is null)
            {
                ResetTrimDisplay();
                return;
            }

            try
            {
                var info = await _trimService.ReadClipInfoAsync(sound.Path);

                if (!ReferenceEquals(ViewModel.SelectedSound, sound))
                {
                    return; // selection changed while the file was being analysed
                }

                ApplyTrimDisplay(sound, info.DurationSeconds, info.Peaks);
            }
            catch (Exception ex)
            {
                Logger.Log($"Trim preview unavailable for '{sound.Path}': {ex.Message}");

                if (!ReferenceEquals(ViewModel.SelectedSound, sound))
                {
                    return;
                }

                ResetTrimDisplay();
            }
        }

        private void ApplyTrimDisplay(SoundItem sound, double durationSeconds, float[]? peaks)
        {
            double duration = Math.Max(MinimumSelectionSeconds, durationSeconds);

            double start = Math.Clamp(sound.TrimStart, 0, Math.Max(0, duration - MinimumSelectionSeconds));
            double end = sound.TrimEnd > 0.01
                ? Math.Clamp(sound.TrimEnd, Math.Min(duration, start + MinimumSelectionSeconds), duration)
                : duration;

            if (end < start + MinimumSelectionSeconds)
            {
                start = Math.Max(0, end - MinimumSelectionSeconds);
            }

            _syncingTrim = true;
            try
            {
                ViewModel.ClipDurationSeconds = duration;
                ViewModel.ClipPeaks = peaks;
                ViewModel.ClipDurationText = FormatTime(duration);
                ViewModel.TrimStartSeconds = start;
                ViewModel.TrimEndSeconds = end;
                ViewModel.TrimStartText = FormatTime(start);
                ViewModel.TrimEndText = FormatTime(end);
                ViewModel.TrimSelectionText = BuildSelectionText(start, end);

            }
            finally
            {
                _syncingTrim = false;
            }
        }

        private void ResetTrimDisplay()
        {
            _syncingTrim = true;
            try
            {
                ViewModel.ClipDurationSeconds = 1;
                ViewModel.ClipPeaks = null;
                ViewModel.ClipDurationText = "—";
                ViewModel.TrimStartSeconds = 0;
                ViewModel.TrimEndSeconds = 1;
                ViewModel.TrimStartText = "—";
                ViewModel.TrimEndText = "—";
                ViewModel.TrimSelectionText = "Select a sound to edit its trim.";

            }
            finally
            {
                _syncingTrim = false;
            }
        }

        private void TrimWaveform_SelectionChanged(object? sender, EventArgs e)
        {
            if (_syncingTrim)
            {
                return;
            }

            ScheduleTrimCommit();
        }

        /// <summary>
        /// Debounced: dragging a slider fires dozens of notifications; the metadata write and the
        /// formatted labels are coalesced into one update shortly after the mouse stops.
        /// </summary>
        private void ScheduleTrimCommit()
        {
            _trimCommitTimer ??= CreateTrimCommitTimer();
            _trimCommitTimer.Stop();
            _trimCommitTimer.Start();
        }

        private DispatcherTimer CreateTrimCommitTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                CommitTrim();
            };
            return timer;
        }

        private void CancelPendingTrimCommit() => _trimCommitTimer?.Stop();

        private void FlushTrimCommit()
        {
            CancelPendingTrimCommit();
            CommitTrim();
        }

        private void CommitTrim()
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                return;
            }

            double duration = Math.Max(MinimumSelectionSeconds, ViewModel.ClipDurationSeconds);

            _syncingTrim = true;
            try
            {
                double start = Math.Clamp(ViewModel.TrimStartSeconds, 0, Math.Max(0, duration - MinimumSelectionSeconds));
                double end = Math.Clamp(ViewModel.TrimEndSeconds, Math.Min(duration, start + MinimumSelectionSeconds), duration);

                ViewModel.TrimStartSeconds = start;
                ViewModel.TrimEndSeconds = end;
                ViewModel.TrimStartText = FormatTime(start);
                ViewModel.TrimEndText = FormatTime(end);
                ViewModel.TrimSelectionText = BuildSelectionText(start, end);

                sound.TrimStart = start;
                sound.TrimEnd = end >= duration - 0.02 ? 0 : end;

                // Keep the trimmed range even if the user closes the app without another save.
                SaveSoundLibrary();
            }
            finally
            {
                _syncingTrim = false;
            }
        }

        private static string BuildSelectionText(double start, double end) =>
            $"Playing {FormatTime(start)} → {FormatTime(end)} ({FormatTime(Math.Max(0, end - start))})";

        private void ResetTrim_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                return;
            }

            CancelPendingTrimCommit();
            sound.TrimStart = 0;
            sound.TrimEnd = 0;
            ApplyTrimDisplay(sound, ViewModel.ClipDurationSeconds, ViewModel.ClipPeaks);
            SaveSoundLibrary();
            UpdateStatus($"Trim reset for \"{sound.Name}\".");
        }

        private void PreviewTrim_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                UpdateStatus("Select a sound first.", true);
                return;
            }

            FlushTrimCommit();

            // Isolate the preview so the selection can be judged on its own. This preview is
            // local-only: the virtual microphone path is deliberately not attached, so Discord or
            // OBS do not hear the trim tests.
            StopAudioProcessing(updateStatus: false);
            StartAudioProcessing(sound, previewOnly: true);
        }

        private async void SaveTrimCopy_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                UpdateStatus("Select a sound first.", true);
                return;
            }

            try
            {
                FlushTrimCommit();
                UpdateStatus("💾 Saving trimmed copy…");

                var result = await _trimService.SaveTrimmedAsync(
                    sound.Path,
                    ViewModel.TrimStartSeconds,
                    EffectiveTrimEnd(),
                    overwrite: false);

                var copy = new SoundItem
                {
                    Path = result.Path,
                    Name = sound.Name + " (trimmed)",
                    Volume = sound.Volume,
                    Bass = sound.Bass,
                    Treble = sound.Treble,
                    Pitch = sound.Pitch,
                    IconPath = sound.IconPath,
                    MutePreview = sound.MutePreview,
                    Distortion = sound.Distortion,
                    EchoDelayMs = sound.EchoDelayMs,
                    EchoFeedback = sound.EchoFeedback,
                    ReverbMix = sound.ReverbMix,
                    Robotize = sound.Robotize
                };
                copy.PropertyChanged += SoundItem_VolumeChanged;
                AttachSoundItem(copy);

                ViewModel.SelectedSoundboard?.Sounds.Add(copy);
                SaveSoundLibrary();
                LoadAndRegisterHotkeys(announce: false);

                UpdateStatus(result.FormatChanged
                    ? "💾 Saved as a WAV copy (MP3 encoder unavailable)."
                    : "💾 Trimmed copy saved to the soundboard.");

                ViewModel.SelectedSound = copy;
                _ = PcmAudioCache.Instance.PreloadAsync(new[] { copy.Path });
            }
            catch (Exception ex)
            {
                Logger.Log($"Trim save failed: {ex}");
                UpdateStatus($"Save failed: {ex.Message}", true);
            }
        }

        private async void SaveTrimOverwrite_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                UpdateStatus("Select a sound first.", true);
                return;
            }

            try
            {
                FlushTrimCommit();

                var box = MessageBoxManager.GetMessageBoxStandard(
                    "Overwrite audio file",
                    $"Replace the original file permanently?\n\n{sound.Path}\n\n" +
                    $"Only {FormatTime(ViewModel.TrimStartSeconds)} → {FormatTime(ViewModel.TrimEndSeconds)} will remain. " +
                    "This cannot be undone.",
                    ButtonEnum.YesNo);

                var answer = await box.ShowAsync();

                if (answer != ButtonResult.Yes)
                {
                    UpdateStatus("Overwrite cancelled.");
                    return;
                }

                // Release the file before rewriting it.
                StopAudioProcessing(updateStatus: false);

                var result = await _trimService.SaveTrimmedAsync(
                    sound.Path,
                    ViewModel.TrimStartSeconds,
                    EffectiveTrimEnd(),
                    overwrite: true);

                sound.TrimStart = 0;
                sound.TrimEnd = 0;
                _trimService.Invalidate(result.Path);
                PcmAudioCache.Instance.Unload(sound.Path);
                _ = PcmAudioCache.Instance.PreloadAsync(new[] { sound.Path });
                SaveSoundLibrary();
                await LoadTrimInfoAsync(sound);

                UpdateStatus($"💾 Original overwritten: {result.Path}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Trim overwrite failed: {ex}");
                UpdateStatus($"Overwrite failed: {ex.Message}", true);
            }
        }

        /// <summary>Zero means "until the end of the file" in the model.</summary>
        private double EffectiveTrimEnd()
        {
            double duration = ViewModel.ClipDurationSeconds;
            return ViewModel.TrimEndSeconds >= duration - 0.02 ? 0 : ViewModel.TrimEndSeconds;
        }

        private static string FormatTime(double seconds)
        {
            var safe = Math.Max(0, seconds);
            var time = TimeSpan.FromSeconds(safe);
            return time.TotalHours >= 1
                ? time.ToString(@"h\:mm\:ss\.f")
                : time.ToString(@"m\:ss\.f");
        }
    }
}
