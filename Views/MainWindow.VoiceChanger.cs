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
            SettingsManager.SaveSettings(_settings);
        }

        private void ApplyVoicePreset(string label, Action<VoiceChangerSettings> apply)
        {
            apply(_settings.VoiceChanger);
            SettingsManager.SaveSettings(_settings);
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
