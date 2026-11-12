using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BliBloup.Services;

namespace BliBloup.Views
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer? _voiceSaveTimer;

        private void VoiceChanger_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            _voiceSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _voiceSaveTimer.Tick -= SaveVoiceSettings;
            _voiceSaveTimer.Tick += SaveVoiceSettings;
            _voiceSaveTimer.Stop();
            _voiceSaveTimer.Start();
        }

        private void SaveVoiceSettings(object? sender, EventArgs e)
        {
            _voiceSaveTimer?.Stop();

            try
            {
                SettingsManager.SaveSettings(_settings);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save voice changer settings: {ex}");
                UpdateStatus($"Could not save voice settings: {ex.Message}", true);
            }
        }

        private void ApplyVoicePreset(string label, Action<VoiceChangerSettings> apply)
        {
            apply(_settings.VoiceChanger);
            SaveVoiceSettings(null, EventArgs.Empty);
            UpdateStatus($"Voice preset applied: {label}.");
        }

        private void ChipmunkPreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Chipmunk", voice =>
            {
                voice.Pitch = 1.5;
                voice.Bass = -6;
                voice.Treble = 8;
                voice.Distortion = 0;
                voice.EchoDelayMs = 0;
                voice.EchoFeedback = 0;
                voice.ReverbMix = 0;
                voice.Robotize = false;
            });

        private void DemonPreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Demon", voice =>
            {
                voice.Pitch = 0.8;
                voice.Bass = 15;
                voice.Treble = -8;
                voice.Distortion = 0;
                voice.EchoDelayMs = 0;
                voice.EchoFeedback = 0;
                voice.ReverbMix = 0;
                voice.Robotize = false;
            });

        private void RobotPreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Robot", voice =>
            {
                voice.Pitch = 1.0;
                voice.Bass = 0;
                voice.Treble = 0;
                voice.Distortion = 15;
                voice.EchoDelayMs = 0;
                voice.EchoFeedback = 0;
                voice.ReverbMix = 0;
                voice.Robotize = true;
            });

        private void PhonePreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Phone / Radio", voice =>
            {
                voice.Pitch = 1.0;
                voice.Bass = 0;
                voice.Treble = 0;
                voice.Distortion = 35;
                voice.EchoDelayMs = 60;
                voice.EchoFeedback = 15;
                voice.ReverbMix = 0;
                voice.Robotize = false;
            });

        private void CavePreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Cave", voice =>
            {
                voice.Pitch = 1.0;
                voice.Bass = 0;
                voice.Treble = 0;
                voice.Distortion = 0;
                voice.EchoDelayMs = 200;
                voice.EchoFeedback = 25;
                voice.ReverbMix = 80;
                voice.Robotize = false;
            });

        private void AlienPreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Alien / Insect", voice =>
            {
                voice.Pitch = 1.35;
                voice.Bass = -8;
                voice.Treble = 12;
                voice.Distortion = 10;
                voice.EchoDelayMs = 0;
                voice.EchoFeedback = 0;
                voice.ReverbMix = 15;
                voice.Robotize = false;
            });

        private void MonsterPreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Monster / Ogre", voice =>
            {
                voice.Pitch = 0.6;
                voice.Bass = 20;
                voice.Treble = -10;
                voice.Distortion = 25;
                voice.EchoDelayMs = 0;
                voice.EchoFeedback = 0;
                voice.ReverbMix = 20;
                voice.Robotize = false;
            });

        private void MegaphonePreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Megaphone", voice =>
            {
                voice.Pitch = 1.0;
                voice.Bass = -15;
                voice.Treble = 5;
                voice.Distortion = 45;
                voice.EchoDelayMs = 80;
                voice.EchoFeedback = 15;
                voice.ReverbMix = 0;
                voice.Robotize = false;
            });

        private void GhostPreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Ghost / Spirit", voice =>
            {
                voice.Pitch = 1.15;
                voice.Bass = 0;
                voice.Treble = 8;
                voice.Distortion = 0;
                voice.EchoDelayMs = 300;
                voice.EchoFeedback = 40;
                voice.ReverbMix = 75;
                voice.Robotize = false;
            });

        private void UnderwaterPreset_Click(object? sender, RoutedEventArgs e) =>
            ApplyVoicePreset("Underwater", voice =>
            {
                voice.Pitch = 0.9;
                voice.Bass = 15;
                voice.Treble = -20;
                voice.Distortion = 5;
                voice.EchoDelayMs = 150;
                voice.EchoFeedback = 35;
                voice.ReverbMix = 30;
                voice.Robotize = false;
            });

        private void ResetVoice_Click(object? sender, RoutedEventArgs e)
        {
            _settings.VoiceChanger.Reset();
            _audioService?.ResetVoiceChanger();
            SettingsManager.SaveSettings(_settings);
            UpdateStatus("Voice changer reset: neutral pitch, EQ and effects.");
        }

        private void VoiceMonitorChanged(object? sender, RoutedEventArgs e)
        {
            UpdateStatus(_settings.VoiceChanger.Monitor
                ? "Voice monitoring enabled on the default speakers."
                : "Voice monitoring disabled.");
        }
    }
}
