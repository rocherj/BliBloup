using Avalonia.Controls;
using Avalonia.Interactivity;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using System.Threading;
using Avalonia.Themes.Fluent;
using Avalonia.Platform.Storage;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.ComponentModel;
using BliBloup.ViewModels;
using BliBloup.Models;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Reflection;
using Avalonia.Media;
using System.Text.Json.Serialization;
using Avalonia.Input;
using BliBloup.Services;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace BliBloup.Views
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings = new();
        private static readonly int[] CaptureBufferOptions = { 10, 20, 30, 40, 50, 75, 100, 150 };
        private bool _initializingCaptureSettings;
        private readonly SoundboardStore _soundboardStore = new();

        private bool _installationDismissed;
        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
        private Func<Task>? _closeSplashAction;

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }


        public void SetCloseSplashAction(Func<Task> action)
        {
            _closeSplashAction = action;
        }

        public async Task CloseSplashAsync()
        {
            if (_closeSplashAction != null)
            {
                await _closeSplashAction();
            }
        }


        public new void Show()
        {
            if (!IsVisible)
            {
                Dispatcher.UIThread.Post(() => base.Show());
            }
        }

        public MainWindow()
        {
            Console.SetOut(new SplashTextWriter());
            Console.WriteLine("[1] MainWindow constructor started");

            this.Styles.Add(new FluentTheme());

            InitializeComponent();

            DataContext = new MainWindowViewModel();
            InitializeSoundboardEditing();

            _settings = SettingsManager.LoadSettings();
            InitializeCaptureSettingsUi();

            _deviceManager = new DeviceManager(
                (MainWindowViewModel)DataContext!,
                _settings,
                Dispatcher.UIThread
            );

            InitializeAudioService();
            InitializeTts();
            this.Closing += (s, e) =>
            {
                _ttsService?.Cleanup();
            };

            this.ShowInTaskbar = true;
            this.WindowState = WindowState.Normal;
            this.Opacity = 1;
            this.IsVisible = false;
            Console.WriteLine("[3] Window properties set");
        }

        private void DebugEmbeddedResources()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();

                Console.WriteLine($"=== DEBUG: Found {resourceNames.Length} embedded resources ===");
                Logger.Log($"Found {resourceNames.Length} embedded resources:");

                foreach (var resourceName in resourceNames.OrderBy(n => n))
                {
                    Console.WriteLine($"Resource: {resourceName}");
                    Logger.Log($"Resource: {resourceName}");
                }

                // Check specifically for eSpeak resources
                var espeakResources = resourceNames.Where(n => n.Contains("espeak", StringComparison.OrdinalIgnoreCase)).ToArray();
                Console.WriteLine($"=== Found {espeakResources.Length} eSpeak-related resources ===");
                Logger.Log($"Found {espeakResources.Length} eSpeak-related resources:");

                foreach (var resource in espeakResources)
                {
                    Console.WriteLine($"eSpeak Resource: {resource}");
                    Logger.Log($"eSpeak Resource: {resource}");
                }

                Console.WriteLine("=== End Resource Debug ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error debugging resources: {ex.Message}");
                Logger.Log($"Error debugging resources: {ex.Message}");
            }
        }


        public async Task InitializeAsync()
        {
            try
            {
                Console.WriteLine("[2] Starting async initialization");
                await LoadIconAsync();


                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DataContext is INotifyPropertyChanged npc)
                    {
                        npc.PropertyChanged += ViewModel_PropertyChanged;
                    }
                });
                Console.WriteLine("[2] Async initialization complete");
                DebugEmbeddedResources();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Initialization failed: {ex}");
                throw;
            }
        }

        private void ShowFatalError(string message)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "Fatal Error",
                    message + "\n\nApplication will exit.",
                    ButtonEnum.Ok                
                    );
                
                await box.ShowAsync();
                Environment.Exit(1);
            });
        }

        public async Task StartMainInitialization()
        {
            try
            {
                Console.WriteLine("[4] Starting main initialization");
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MainWindow_LoadedAsync();
                    InitializeHotkeys();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Main initialization failed: {ex}");
                throw;
            }
        }

        private async Task LoadIconAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    var uri = new Uri("avares://BliBloup/soundboard2.png");
                    using var stream = AssetLoader.Open(uri);
                    var testImage = new Bitmap(stream);
                    Console.WriteLine("Default icon loaded successfully");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading default icon: {ex}");
            }
        }

        private async Task MainWindow_LoadedAsync()
        {
            try
            {
                
                LoadSoundLibrary();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    this.IsVisible = true;
                    //this.CanResize = true;
                    this.InvalidateMeasure();
                    this.InvalidateArrange();
                });

                Console.WriteLine("[5] MainWindow_LoadedAsync started");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var needsInstallation = !_installationDismissed && await Task.Run(() => CheckVBCableInstallation());

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MainUI.IsVisible = true;
                    InstallationPanel.IsVisible = needsInstallation;
                    InstallationMessage.Text = needsInstallation
                        ? "VB-Cable not detected. Please install to continue full functionality."
                        : "";
                });

                await _deviceManager.InitializeAsync();
                _audioService?.InitializeDevices();

                if (ViewModel.PlaybackDevices.Count == 0)
                {
                    UpdateStatus("No audio output device detected. BliBloup will stay silent until a playback device is available.", true);
                }
                else if (ViewModel.CaptureDevices.Count == 0)
                {
                    UpdateStatus("No microphone detected. Virtual microphone routing needs an input device.", true);
                }

                Console.WriteLine("[11] Initialization complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Initialization failed: {ex}");
                Logger.Log($"Init Error: {ex}");
                await CloseAfterDelay(2000);
            }
        }

        private bool CheckVBCableInstallation()
        {
            Console.WriteLine("[Check] Starting device check");
            using var enumerator = new MMDeviceEnumerator();

            bool hasInput = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Any(d => d.FriendlyName.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasOutput = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Any(d => d.FriendlyName.IndexOf("CABLE Output", StringComparison.OrdinalIgnoreCase) >= 0);

            return !(hasInput && hasOutput);
        }

        private async Task ShowInstallationUI()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallationPanel.IsVisible = true;
                MainUI.IsVisible = false;
            });
        }

        private async Task ShowMainUI()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallationPanel.IsVisible = false;
                MainUI.IsVisible = true;
            });
        }

        private async void OnInstallClicked(object? sender, RoutedEventArgs e)
        {
            Button? installButton = sender as Button;
            if (installButton == null) return;

            try
            {
                installButton.IsEnabled = false;
                StatoMessage.Text = string.Empty;
                ShowProgress("Starting installation...");
                InstallationPanel.IsVisible = false;

                var success = await RunInstallerAsync();

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    InstallationPanel.IsVisible = !success;
                    MainUI.IsVisible = true;

                    if (success)
                    {
                        await _deviceManager.InitializeAsync();
                    }
                });

                if (!success && CheckVBCableInstallation())
                {
                    await _deviceManager.InitializeAsync();
                    UpdateStatus("Manual installation required - Download from vb-audio.com", true);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Installation error: {ex.Message}");
                _ = CloseAfterDelay(5000);
            }
            finally
            {
                HideProgress();
                installButton.IsEnabled = true;
            }
        }

        private void OnExitClicked(object? sender, RoutedEventArgs e)
        {
            _installationDismissed = true;
            InstallationPanel.IsVisible = false;
            StatoMessage.Text = "Some features may require VB-Cable";
        }

        private void UpdateStatus(string message, bool isError = false)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatMessage.Text = message ?? string.Empty;
                StatusMessage.Text = message ?? string.Empty;
                ProgressStatus.Text = message ?? string.Empty;
                InstallationMessageT.Text = message ?? string.Empty;

                StatoMessage.Text = message ?? string.Empty;
                StatoMessage.Foreground = isError
                    ? new SolidColorBrush(Colors.OrangeRed)
                    : new SolidColorBrush(Colors.LightGray);

                if (!isError)
                {
                    Task.Delay(5000).ContinueWith(_ =>
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (StatoMessage.Text == message)
                                StatoMessage.Text = string.Empty;
                        }));
                }
            });
        }

        private async Task CloseAfterDelay(int milliseconds)
        {
            await Task.Delay(milliseconds);
            await SafeShutdown();
        }

        private async Task SafeShutdown()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var lifetime = Application.Current?.ApplicationLifetime
                        as IClassicDesktopStyleApplicationLifetime;
                    lifetime?.Shutdown(0);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Shutdown error: {ex}");
            }
        }

        private async Task<bool> RunInstallerAsync()
        {
            string tempDir = "";
            string logPath = Path.Combine(Path.GetTempPath(), "vb_cable_install.log");

            try
            {
                ShowProgress("Preparing installer...");
                UpdateStatus("Starting VB-Cable installation...");

                if (File.Exists(logPath)) File.Delete(logPath);

                var assembly = Assembly.GetExecutingAssembly();
                const string resourceName = "BliBloup.InstallScripts.InstallVBCable.bat";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    UpdateStatus("Error: Missing installation components", true);
                    return false;
                }

                tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                var scriptPath = Path.Combine(tempDir, "InstallVBCable.bat");

                await using (var fileStream = File.Create(scriptPath))
                {
                    await stream.CopyToAsync(fileStream);
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    Verb = "runas",
                    UseShellExecute = true, // Required for admin elevation
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = tempDir
                };

                using var process = new Process { StartInfo = processInfo };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Installation failed: {ex.Message}", true);
                    return false;
                }

                var timeout = TimeSpan.FromMinutes(3);
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    process.Kill();
                    UpdateStatus("Installation timed out - Please try again", true);
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    var errorMessage = process.ExitCode switch
                    {
                        1 => "Download failed - Check internet connection",
                        2 => "File extraction failed - Antivirus might be blocking",
                        3 => "Driver installation failed - Run as Administrator",
                        _ => $"Installation error (Code: {process.ExitCode})"
                    };

                    UpdateStatus(errorMessage, true);
                    return false;
                }

                for (int i = 0; i < 20; i++) //extended to 20 seconds until everything stable
                {
                    if (!CheckVBCableInstallation())
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    UpdateStatus("VB-Cable installed successfully!");
                    await _deviceManager.InitializeAsync();
                    return true;
                }

                UpdateStatus("Installation completed but verification failed", true);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Critical error: {ex.Message}", true);
                return false;
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { /* Ignore cleanup errors */ }
                HideProgress();
            }
        }

        private void InitializeCaptureSettingsUi()
        {
            CaptureBufferComboBox.ItemsSource = CaptureBufferOptions.Select(ms => $"{ms} ms").ToList();

            int index = Array.IndexOf(CaptureBufferOptions, Math.Clamp(_settings.CaptureBufferMilliseconds, 10, 150));
            if (index < 0)
            {
                index = Array.IndexOf(CaptureBufferOptions, 40);
            }

            _initializingCaptureSettings = true;
            try
            {
                CaptureBufferComboBox.SelectedIndex = index;
                ExclusiveCaptureBox.IsChecked = _settings.CaptureExclusiveMode;
                CaptureExclusiveWarning.IsVisible = _settings.CaptureExclusiveMode;
                ProAudioBox.IsChecked = _settings.CaptureProAudioPriority;
            }
            finally
            {
                _initializingCaptureSettings = false;
            }
        }

        public void CaptureBufferChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_initializingCaptureSettings || CaptureBufferComboBox.SelectedIndex < 0)
            {
                return;
            }

            _settings.CaptureBufferMilliseconds = CaptureBufferOptions[CaptureBufferComboBox.SelectedIndex];
            SettingsManager.SaveSettings(_settings);
            _audioService?.RestartPersistentAudioRouting();
            _playbackEngine?.ReattachVirtualInputs();
            UpdateStatus($"Microphone buffer set to {_settings.CaptureBufferMilliseconds} ms.");
        }

        public void ExclusiveCaptureChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializingCaptureSettings)
            {
                return;
            }

            _settings.CaptureExclusiveMode = ExclusiveCaptureBox.IsChecked == true;
            CaptureExclusiveWarning.IsVisible = _settings.CaptureExclusiveMode;
            SettingsManager.SaveSettings(_settings);
            _audioService?.RestartPersistentAudioRouting();
            _playbackEngine?.ReattachVirtualInputs();
            UpdateStatus(_settings.CaptureExclusiveMode
                ? "Exclusive capture enabled: the microphone is now reserved for BliBloup."
                : "Shared capture enabled: other applications can use the microphone.");
        }

        public void ProAudioPriorityChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializingCaptureSettings)
            {
                return;
            }

            _settings.CaptureProAudioPriority = ProAudioBox.IsChecked == true;
            SettingsManager.SaveSettings(_settings);
            _audioService?.RestartPersistentAudioRouting();
            _playbackEngine?.ReattachVirtualInputs();
            UpdateStatus(_settings.CaptureProAudioPriority
                ? "Pro Audio thread priority requested for the capture thread."
                : "Pro Audio thread priority disabled.");
        }

        public void PlaybackDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _audioService?.HandlePlaybackDeviceChanged(device);
                // The routing mixer was rebuilt: re-attach the sounds still playing.
                _playbackEngine?.ReattachVirtualInputs();
            }
        }

        public void CaptureDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _audioService?.HandleCaptureDeviceChanged(device);
                _playbackEngine?.ReattachVirtualInputs();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            DisposeHotkeys();
            DisposeAudioEngine();
            _audioService?.Dispose();
            base.OnClosed(e);
        }

        private bool CheckDriverPresence(string installerName)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var hasInput = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .Any(d => d.FriendlyName.Contains("CABLE Input"));

                var hasOutput = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .Any(d => d.FriendlyName.Contains("CABLE Output"));

                return hasInput && hasOutput;
            }
            catch
            {
                return false;
            }
        }

        private void ShowProgress(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressStatus.Text = message;
                ProgressBarContainer.IsVisible = true;
            });
        }

        private void HideProgress()
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressBarContainer.IsVisible = false;
            });
        }

        public async void AddToLibrary(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard == null)
            {
                UpdateStatus("Select a soundboard first.", true);
                return;
            }

            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Audio Files",
                AllowMultiple = true,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Audio Files") { Patterns = new[] { "*.mp3", "*.wav" } }
                }
            });

            var added = 0;
            var addedPaths = new List<string>();

            foreach (var file in files)
            {
                var path = file.Path.LocalPath;

                if (ViewModel.SelectedSoundboard.Sounds
                    .Any(existing => string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var newItem = new SoundItem
                {
                    Path = path,
                    Name = System.IO.Path.GetFileNameWithoutExtension(path)
                };

                AttachSoundItem(newItem);
                ViewModel.SelectedSoundboard.Sounds.Add(newItem);
                addedPaths.Add(path);
                added++;
            }

            if (added > 0)
            {
                SaveSoundLibrary();
                LoadAndRegisterHotkeys(announce: false);
                UpdateStatus(added == 1 ? "Added 1 sound." : $"Added {added} sounds.");
                _ = PcmAudioCache.Instance.PreloadAsync(addedPaths);
            }
        }

        public async void RemoveFromLibrary(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard is not { } board || ViewModel.SelectedSound is not { } sound)
            {
                return;
            }

            var confirmed = await ConfirmAsync(
                "Remove sound",
                $"Remove \"{sound.Name}\" from the soundboard?\n\nThe audio file itself is kept on disk.",
                "Remove");

            if (!confirmed)
            {
                return;
            }

            board.Sounds.Remove(sound);
            ReleaseCachedAudio(sound.Path);
            SaveSoundLibrary();
            LoadAndRegisterHotkeys(announce: false);
            UpdateStatus($"Removed \"{sound.Name}\".");
        }

        /// <summary>Drops the decoded PCM buffer once no remaining sound uses the same file.</summary>
        private void ReleaseCachedAudio(string path)
        {
            bool stillUsed = ViewModel.Soundboards
                .SelectMany(board => board.Sounds)
                .Any(sound => string.Equals(sound.Path, path, StringComparison.OrdinalIgnoreCase));

            if (!stillUsed)
            {
                PcmAudioCache.Instance.Unload(path);
            }
        }

        private void AttachSoundItem(SoundItem sound)
        {
            sound.PropertyChanged -= SoundItem_VolumeChanged;
            sound.PropertyChanged += SoundItem_VolumeChanged;
            sound.PropertyChanged -= SoundItem_PropertyChanged;
            sound.PropertyChanged += SoundItem_PropertyChanged;
        }

        private static async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo);
            var answer = await box.ShowAsync();
            return answer == ButtonResult.Yes;
        }

        private async void ChangeIconClicked(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound) return;

            Logger.Log($"Thumbnail picker opened for '{sound.Name}'.");

            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Sound Thumbnail",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } }
                }
            });

            if (files.Count == 0 || files[0].TryGetLocalPath() is not string localPath)
            {
                return;
            }

            try
            {
                // Downscale to at most 256x256 and re-encode, so huge pictures cannot slow the UI.
                var result = ThumbnailService.CreateThumbnail(
                    localPath,
                    BliBloupPaths.IconsDirectory,
                    Guid.NewGuid().ToString("N"));

                // Store the full path: thumbnails survive a changed data directory.
                sound.IconPath = result.Path;
                SaveSoundLibrary();

                var sizeKb = Math.Max(1, result.Bytes / 1024);
                UpdateStatus($"🖼️ Thumbnail updated for \"{sound.Name}\" ({result.Width}×{result.Height}px, {sizeKb} KB).");
            }
            catch (Exception ex)
            {
                Logger.Log($"Thumbnail import failed: {ex}");
                UpdateStatus($"Could not use that image: {ex.Message}", true);
            }
        }

        /// <summary>Returns the sound thumbnail to the bundled default.</summary>
        private void ResetThumbnail_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound is not { } sound)
            {
                return;
            }

            sound.IconPath = "avares://BliBloup/soundboard2.png";
            SaveSoundLibrary();
            UpdateStatus($"Thumbnail reset to default for \"{sound.Name}\".");
        }
        private void SaveChangesClicked(object? sender, RoutedEventArgs e)
        {
            SaveSoundLibrary();
            UpdateStatus("Changes saved successfully!");
        }

        private void SaveSoundLibrary()
        {
            try
            {
                // Atomic write + previous version kept as soundboards.json.bak.
                _soundboardStore.Save(ViewModel.Soundboards);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error saving sound library: {ex}");
                UpdateStatus($"Could not save the sound library: {ex.Message}", true);
            }
        }

        private void LoadSoundLibrary()
        {
            try
            {
                var result = _soundboardStore.Load();

                ViewModel.Soundboards.Clear();

                foreach (var board in result.Soundboards)
                {
                    foreach (var sound in board.Sounds)
                    {
                        AttachSoundItem(sound);
                    }

                    ViewModel.Soundboards.Add(board);
                }

                if (!ViewModel.Soundboards.Any())
                {
                    ViewModel.Soundboards.Add(new Soundboard { Name = "Default" });
                }

                // Always keep a board selected so the library does not look empty at startup.
                if (ViewModel.SelectedSoundboard is null)
                {
                    ViewModel.SelectedSoundboard = ViewModel.Soundboards[0];
                }

                RefreshSelectedBoardState();

                // Clip hotkeys are stored with the library, so re-register them after a reload.
                LoadAndRegisterHotkeys(announce: false);

                // Decode the whole library once, in the background: the first trigger of a sound
                // should not have to read and decode the file.
                _ = PcmAudioCache.Instance.PreloadAsync(
                    ViewModel.Soundboards.SelectMany(board => board.Sounds).Select(sound => sound.Path));

                switch (result.Note)
                {
                    case LibraryLoadNote.MigratedFromLegacy:
                        // Persist straight away so the readable format replaces the old file.
                        SaveSoundLibrary();
                        UpdateStatus("Library migrated to the new format.");
                        break;

                    case LibraryLoadNote.RestoredFromBackup:
                        UpdateStatus("The library file was unreadable: previous backup restored.", true);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading sound library: {ex}");
                Logger.Log($"Error loading sound library: {ex}");
                UpdateStatus($"Could not load the sound library: {ex.Message}", true);

                if (!ViewModel.Soundboards.Any())
                {
                    ViewModel.Soundboards.Add(new Soundboard { Name = "Default" });
                    ViewModel.SelectedSoundboard = ViewModel.Soundboards[0];
                }
            }
        }

        private void SoundItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Removed automatic SaveSoundLibrary call to wait for explicit save
        }

        public void PlayThroughDeviceHandler(object? sender, RoutedEventArgs e)
        {
            try
            {
                StartAudioProcessing();
                StatusMessage.Text = "Routing audio through virtual microphone...";
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"Error: {ex.Message}";
            }
        }
        


        private void SoundboardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Soundboard selected)
            {
                ViewModel.SelectedSoundboard = selected;
            }
        }

        public async void AddSoundboard(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog { Prompt = "Enter soundboard name:" };
            var result = await dialog.ShowDialog<string>(this);

            if (!string.IsNullOrWhiteSpace(result))
            {
                var soundboard = new Soundboard { Name = result };
                ViewModel.Soundboards.Add(soundboard);
                ViewModel.SelectedSoundboard = soundboard;
                SaveSoundLibrary();
            }
        }

        public async void RenameSoundboard(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard == null) return;

            var dialog = new InputDialog
            {
                Title = "Rename Soundboard",
                Prompt = "Enter new name:",
                InputText = ViewModel.SelectedSoundboard.Name ?? string.Empty
            };

            var newName = await dialog.ShowDialog<string>(this);

            if (!string.IsNullOrWhiteSpace(newName))
            {
                ViewModel.SelectedSoundboard.Name = newName;
                SaveSoundLibrary();
            }
        }

        public void RemoveSoundboard(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard != null &&
                ViewModel.Soundboards.Count > 1)
            {
                ViewModel.Soundboards.Remove(ViewModel.SelectedSoundboard);
                ViewModel.SelectedSoundboard = ViewModel.Soundboards.FirstOrDefault();
                SaveSoundLibrary();
            }
        }

        //to whoever reading this, i will clean up and optimize i promise LATER
        private void OpenGitHub(object? sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/rocherj",
                UseShellExecute = true
            });
        }
    }
}