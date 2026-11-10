using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using BoomBx.Models;
using BoomBx.Services;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace BoomBx.Views
{
    /// <summary>
    /// Trim editor: waveform preview, start/end selection, audible preview and baking the
    /// selection into a file (copy or overwrite, the latter after confirmation).
    ///
    /// The trim itself is non destructive metadata on <see cref="SoundItem"/>; playback
    /// applies it immediately, saving is an explicit action.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const double MinimumSelectionSeconds = 0.05;

        private readonly AudioTrimService _trimService = new();
        private bool _syncingTrim;

        private async Task LoadTrimInfoAsync(SoundItem? sound)
        {
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

            _syncingTrim = true;
            try
            {
                ViewModel.ClipDurationSeconds = duration;
                ViewModel.ClipPeaks = peaks;
                ViewModel.ClipDurationText = FormatTime(duration);
                ViewModel.TrimStartSeconds = Math.Clamp(sound.TrimStart, 0, Math.Max(0, duration - MinimumSelectionSeconds));

                double end = sound.TrimEnd > 0.01
                    ? Math.Clamp(sound.TrimEnd, ViewModel.TrimStartSeconds + MinimumSelectionSeconds, duration)
                    : duration;

                ViewModel.TrimEndSeconds = end;
                ViewModel.TrimStartText = FormatTime(ViewModel.TrimStartSeconds);
                ViewModel.TrimEndText = FormatTime(end);
            }
            finally
            {
                _syncingTrim = false;
            }

            UpdateTrimSelectionText();
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

        private void TrimStartSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncingTrim)
            {
                return;
            }

            double duration = Math.Max(MinimumSelectionSeconds, ViewModel.ClipDurationSeconds);
            double start = Math.Clamp(e.NewValue, 0, duration - MinimumSelectionSeconds);
            double end = ViewModel.TrimEndSeconds;

            if (start > end - MinimumSelectionSeconds)
            {
                end = Math.Min(duration, start + MinimumSelectionSeconds);
            }

            ApplyTrimValues(start, end);
        }

        private void TrimEndSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncingTrim)
            {
                return;
            }

            double duration = Math.Max(MinimumSelectionSeconds, ViewModel.ClipDurationSeconds);
            double end = Math.Clamp(e.NewValue, MinimumSelectionSeconds, duration);
            double start = ViewModel.TrimStartSeconds;

            if (end < start + MinimumSelectionSeconds)
            {
                start = Math.Max(0, end - MinimumSelectionSeconds);
            }

            ApplyTrimValues(start, end);
        }

        private void ApplyTrimValues(double start, double end)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                return;
            }

            double duration = Math.Max(MinimumSelectionSeconds, ViewModel.ClipDurationSeconds);
            start = Math.Clamp(start, 0, duration - MinimumSelectionSeconds);
            end = Math.Clamp(end, start + MinimumSelectionSeconds, duration);

            _syncingTrim = true;
            try
            {
                ViewModel.TrimStartSeconds = start;
                ViewModel.TrimEndSeconds = end;
                ViewModel.TrimStartText = FormatTime(start);
                ViewModel.TrimEndText = FormatTime(end);
            }
            finally
            {
                _syncingTrim = false;
            }

            sound.TrimStart = start;
            sound.TrimEnd = end >= duration - 0.02 ? 0 : end;

            UpdateTrimSelectionText();
        }

        private void UpdateTrimSelectionText()
        {
            if (ViewModel.SelectedSound is null)
            {
                return;
            }

            double length = Math.Max(0, ViewModel.TrimEndSeconds - ViewModel.TrimStartSeconds);
            ViewModel.TrimSelectionText =
                $"Playing {FormatTime(ViewModel.TrimStartSeconds)} → {FormatTime(ViewModel.TrimEndSeconds)} ({FormatTime(length)})";
        }

        private void ResetTrim_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                return;
            }

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

            // Isolate the preview so the selection can be judged on its own.
            StopAudioProcessing(updateStatus: false);
            StartAudioProcessing(sound);
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
                    IconPath = sound.IconPath
                };
                copy.PropertyChanged += SoundItem_VolumeChanged;

                ViewModel.SelectedSoundboard?.Sounds.Add(copy);
                SaveSoundLibrary();
                LoadAndRegisterHotkeys(announce: false);

                UpdateStatus(result.FormatChanged
                    ? "💾 Saved as a WAV copy (MP3 encoder unavailable)."
                    : "💾 Trimmed copy saved to the soundboard.");

                ViewModel.SelectedSound = copy;
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
