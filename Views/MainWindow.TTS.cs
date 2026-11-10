using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BliBloup.Models;
using BliBloup.Services;
using BliBloup.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BliBloup.Views
{
    public partial class MainWindow : Window
    {
        private TtsService? _ttsService;

        private void InitializeTts()
        {
            try
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    _ttsService = new TtsService(viewModel);
                    Console.WriteLine("TTS Service initialized successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TTS initialization failed: {ex.Message}");
                // Don't throw here - let the app continue without TTS if needed
                Logger.Log($"TTS Init Error: {ex}");
                UpdateStatus("TTS unavailable - check eSpeak installation");
            }
        }

        private void TextToSpeechNav_Checked(object? sender, RoutedEventArgs e)
        {
            _ttsService?.RefreshVoices();
        }
    

        private async void PreviewTts_Click(object? sender, RoutedEventArgs e)
        {
            if (_ttsService == null) return;

            try
            {
                await _ttsService.GenerateTtsAudioAsync();
                StartTtsPlayback();
            }
            catch (Exception ex)
            {
                UpdateStatus($"TTS Error: {ex.Message}");
            }
        }




        private async void SaveTtsAudio_Click(object sender, RoutedEventArgs e)
        {
            // First check if service exists
            if (_ttsService == null) return;

            // Then get the stream and check for null
            var audioStream = _ttsService.GetAudioStream();
            if (audioStream == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title = "Save TTS Audio",
                        SuggestedFileName = $"TTS_{DateTime.Now:yyyyMMddHHmmss}.wav",
                        FileTypeChoices = new[]
                        {
                    new FilePickerFileType("WAV Audio")
                    {
                        Patterns = new[] { "*.wav" }
                    }
                        }
                    });

                if (file != null)
                {
                    string filePath = file.Path.LocalPath;

                    using (var fileStream = File.Create(filePath))
                    {
                        // Reset position before copying
                        audioStream.Position = 0;
                        await audioStream.CopyToAsync(fileStream);
                    }

                    if (DataContext is MainWindowViewModel vm)
                    {
                        var newItem = new SoundItem
                        {
                            Path = filePath,
                            Name = $"TTS: {vm.TtsText?.TrimTo(20) ?? "Untitled"}",
                            Volume = vm.TtsVolume,
                            Pitch = vm.TtsPitch
                        };

                        AttachSoundItem(newItem);
                        vm.SelectedSoundboard?.Sounds.Add(newItem);
                        SaveSoundLibrary();
                        UpdateStatus("💾 TTS audio saved to soundboard!");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Save Error: {ex.Message}");
            }
        }



        private void StartTtsPlayback()
        {
            var service = _ttsService;
            if (service == null) return;

            if (DataContext is not MainWindowViewModel viewModel) return;

            try
            {
                var audioStream = service.GetAudioStream();
                if (audioStream == null) return;

                audioStream.Position = 0;
                var wavData = audioStream.ToArray();

                var parameters = new SoundItem
                {
                    Volume = viewModel.TtsVolume,
                    Pitch = viewModel.TtsPitch,
                    Bass = 0,
                    Treble = 0
                };

                var label = $"TTS: {viewModel.TtsText?.TrimTo(20) ?? "speech"}";
                StartTtsVoice(wavData, parameters, (float)(viewModel.TtsVolume / 100.0), label);
            }
            catch (Exception ex)
            {
                UpdateStatus($"TTS Playback Error: {ex.Message}");
            }
        }



        private string GetESpeakBinaryPath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(appDir, "espeak", "espeak.exe");
            }
            else
            {
                return "espeak";
            }
        }


        /*
        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // run command line??
        }
        */

        //# Debian/Ubuntu
        //sudo apt-get install espeak

        //# macOS
        //brew install espeak

    }
}