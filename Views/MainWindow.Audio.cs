using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BliBloup.Models;
using BliBloup.Services;
using BliBloup.ViewModels;

namespace BliBloup.Views
{
    public partial class MainWindow : Window
    {
        private AudioService? _audioService;
        private ClipPlaybackEngine? _playbackEngine;
        private string? _lastStartedVoiceName;

        private enum PlaybackState { Stopped, Playing, Paused }
        private PlaybackState _currentPlaybackState = PlaybackState.Stopped;

        private void InitializeAudioService()
        {
            try
            {
                if (DataContext == null)
                    throw new InvalidOperationException("DataContext is not initialized");

                _audioService = new AudioService(
                    (MainWindowViewModel)DataContext,
                    _settings,
                    _deviceManager
                );

                _playbackEngine = new ClipPlaybackEngine(_deviceManager);
                _playbackEngine.VoiceStarted += OnVoiceStarted;
                _playbackEngine.VoiceEnded += OnVoiceEnded;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Audio service init failed: {ex.Message}");
            }
        }

        private void OnVoiceStarted(object? sender, VoiceEventArgs e)
        {
            _lastStartedVoiceName = e.Label;
            _currentPlaybackState = PlaybackState.Playing;
            UpdateTrayNowPlaying(e.Label);
            UpdatePlayPauseButtonState();
        }

        private void OnVoiceEnded(object? sender, VoiceEventArgs e)
        {
            if (e.ActiveVoices > 0)
            {
                return;
            }

            _lastStartedVoiceName = null;
            _currentPlaybackState = PlaybackState.Stopped;
            UpdateTrayNowPlaying(null);
            UpdatePlayPauseButtonState();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedSound))
            {
                // Sound items are subscribed when they are created or loaded, so live volume/EQ
                // updates work for any playing voice, selected or not.

                // Refresh the trim editor (waveform + selection) for the new sound.
                _ = LoadTrimInfoAsync(ViewModel.SelectedSound);

                // Starting a clip no longer stops the others, and selecting another sound in the
                // list must not interrupt what is currently being mixed.
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.SelectedSoundboard))
            {
                RefreshSelectedBoardState();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsLoopingEnabled))
            {
                _playbackEngine?.SetLoopAll(ViewModel.IsLoopingEnabled);
            }
        }

        private void SoundItem_VolumeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not SoundItem soundItem)
            {
                return;
            }

            if (e.PropertyName is nameof(SoundItem.Volume) or nameof(SoundItem.Bass)
                or nameof(SoundItem.Treble) or nameof(SoundItem.Pitch))
            {
                // Live update of every playing voice that references this sound.
                _playbackEngine?.UpdateVoiceSettings(soundItem);
            }
        }

        private void PlayPauseHandler(object? sender, RoutedEventArgs e)
        {
            switch (_currentPlaybackState)
            {
                case PlaybackState.Stopped:
                    StartAudioProcessing();
                    break;
                case PlaybackState.Playing:
                    PauseAudioProcessing();
                    break;
                case PlaybackState.Paused:
                    ResumeAudioProcessing();
                    break;
            }
        }

        private void StopHandler(object? sender, RoutedEventArgs e)
        {
            StopAudioProcessing();
        }

        /// <summary>
        /// Starts playback of <paramref name="sound"/>, or of the selected sound when omitted.
        /// Sounds already playing are kept: the engine mixes every clip.
        /// </summary>
        private void StartAudioProcessing(SoundItem? sound = null)
        {
            var target = sound ?? ViewModel.SelectedSound;

            if (_playbackEngine == null)
            {
                UpdateStatus("🔇 Audio engine unavailable", true);
                return;
            }

            if (target == null)
            {
                UpdateStatus("🔇 No sound selected");
                return;
            }

            try
            {
                if (_playbackEngine.IsPaused)
                {
                    _playbackEngine.ResumeAll();
                }

                if (!_playbackEngine.PlaySound(target))
                {
                    UpdateStatus($"❌ {_playbackEngine.LastError ?? $"Could not play \"{target.Name}\"."}", true);
                    return;
                }

                _currentPlaybackState = PlaybackState.Playing;
                var active = _playbackEngine.ActiveVoiceCount;
                UpdateStatus(active > 1
                    ? $"🎵 Playing {target.Name} (+{active - 1} also playing)"
                    : $"🎵 Playing {target.Name}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"⛔ Error: {ex.Message}");
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
        }

        /// <summary>Stops every sound currently playing (the "Stop all" control and panic hotkey).</summary>
        private void StopAudioProcessing(bool updateStatus = true)
        {
            try
            {
                _playbackEngine?.StopAll();
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠ Stop error: {ex.Message}");
            }
            finally
            {
                _currentPlaybackState = PlaybackState.Stopped;
                _lastStartedVoiceName = null;
                UpdateTrayNowPlaying(null);
                UpdatePlayPauseButtonState();
                if (updateStatus)
                {
                    UpdateStatus("⏹ Playback stopped");
                }
            }
        }

        private void PauseAudioProcessing()
        {
            if (_playbackEngine == null || !_playbackEngine.HasActiveVoices)
            {
                return;
            }

            _playbackEngine.PauseAll();
            _currentPlaybackState = PlaybackState.Paused;
            UpdateStatus("⏸ Paused");
            UpdatePlayPauseButtonState();
        }

        private void ResumeAudioProcessing()
        {
            if (_playbackEngine == null || !_playbackEngine.HasActiveVoices)
            {
                return;
            }

            _playbackEngine.ResumeAll();
            _currentPlaybackState = PlaybackState.Playing;
            UpdateStatus($"🎵 Playing {_playbackEngine.ActiveVoiceCount} sound(s)");
            UpdatePlayPauseButtonState();
        }

        private void UpdatePlayPauseButtonState()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var active = _playbackEngine?.HasActiveVoices == true;

                PlayPauseButton.Content = _currentPlaybackState switch
                {
                    PlaybackState.Playing => "Pause",
                    PlaybackState.Paused => "▶ Resume",
                    _ => "▶ Play"
                };

                // The stop control stays visible at all times: a soundboard must always expose
                // an obvious way to silence it.
                StopButton.IsVisible = true;
                StopButton.IsEnabled = active || _currentPlaybackState == PlaybackState.Paused;
            });
        }

        /// <summary>Plays generated TTS audio through the same mixer as the soundboard clips.</summary>
        private void StartTtsVoice(byte[] wavData, SoundItem parameters, float volume, string label)
        {
            if (_playbackEngine == null)
            {
                UpdateStatus("🔇 Audio engine unavailable", true);
                return;
            }

            try
            {
                if (_playbackEngine.IsPaused)
                {
                    _playbackEngine.ResumeAll();
                }

                if (!_playbackEngine.PlayBuffer(wavData, label, parameters, volume))
                {
                    UpdateStatus("TTS Playback Error: could not start the audio mix", true);
                    return;
                }

                _currentPlaybackState = PlaybackState.Playing;
                UpdateStatus($"🗣️ Playing {label}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"TTS Playback Error: {ex.Message}");
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
        }

        private void MutePreviewChanged(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                return;
            }

            if (sender is CheckBox box)
            {
                sound.MutePreview = box.IsChecked == true;
            }

            SaveSoundLibrary();
            _playbackEngine?.UpdateVoiceSettings(sound);
            UpdateStatus(sound.MutePreview
                ? "Preview muted: this sound now goes only to the virtual microphone."
                : "Preview audible: this sound now also plays on your speakers.");
        }

        private void DisposeAudioEngine()
        {
            if (_playbackEngine == null)
            {
                return;
            }

            _playbackEngine.VoiceStarted -= OnVoiceStarted;
            _playbackEngine.VoiceEnded -= OnVoiceEnded;
            _playbackEngine.Dispose();
            _playbackEngine = null;
        }
    }
}
