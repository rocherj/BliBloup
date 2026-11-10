using BoomBx.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.CoreAudioApi;
using System.Collections.ObjectModel;
using System.Speech.Synthesis;
using VoiceInfo = BoomBx.Models.VoiceInfo;

namespace BoomBx.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {

        public MainWindowViewModel()
        {
            _appVersion = $"Version {AppVersionHelper.GetInformationalVersion()}";
            PlaybackDevices = new ObservableCollection<MMDevice>();
            CaptureDevices = new ObservableCollection<MMDevice>();
        }

        [ObservableProperty]
        private string? _appVersion;

        [ObservableProperty]
        private Soundboard? _selectedSoundboard;

        [ObservableProperty]
        private SoundItem? _selectedSound;

        [ObservableProperty]
        private bool _isLoopingEnabled;

        [ObservableProperty]
        private string? _selectedFilePath;

        [ObservableProperty]
        private string _playPauseHotkey = "F9";

        [ObservableProperty]
        private string _stopHotkey = "F10";
        
        [ObservableProperty]
        private string _pauseHotkey = "F11";

        [ObservableProperty]
        private string _panicHotkey = "Esc";

        [ObservableProperty]
        private bool _hotkeysEnabled = true;

        /// <summary>Short "Ctrl+Alt+1 → Vine boom" style feedback for the last triggered hotkey.</summary>
        [ObservableProperty]
        private string? _lastHotkeyDisplay;

        /// <summary>Multi-line list of duplicate / already-owned combinations, empty when clean.</summary>
        [ObservableProperty]
        private string? _hotkeyWarnings;

        [ObservableProperty]
        private bool _hasHotkeyWarnings;

        // --- Trim editor state (left panel bindings) ---

        [ObservableProperty]
        private double _clipDurationSeconds = 1;

        [ObservableProperty]
        private double _trimStartSeconds;

        [ObservableProperty]
        private double _trimEndSeconds = 1;

        [ObservableProperty]
        private float[]? _clipPeaks;

        [ObservableProperty]
        private string _trimStartText = "0:00.0";

        [ObservableProperty]
        private string _trimEndText = "0:00.0";

        [ObservableProperty]
        private string _clipDurationText = "0:00.0";

        [ObservableProperty]
        private string _trimSelectionText = string.Empty;

        public ObservableCollection<Soundboard> Soundboards { get; } = new();

        partial void OnSelectedSoundChanged(SoundItem? value)
        {
            SelectedFilePath = value?.Path ?? string.Empty;
        }

        [ObservableProperty]
        private string _ttsText = "";

        [ObservableProperty]
        private double _ttsVolume = 100;

        [ObservableProperty]
        private double _ttsPitch = 1.0;

        [ObservableProperty]
        private double _ttsSpeed = 0.5;

        [ObservableProperty]
        private ObservableCollection<VoiceInfo> _availableVoices = [];

        [ObservableProperty]
        private VoiceInfo? _selectedVoice;
        
        [ObservableProperty]
        private ObservableCollection<MMDevice> _playbackDevices = new();

        [ObservableProperty]
        private ObservableCollection<MMDevice> _captureDevices = new();

        [ObservableProperty]
        private MMDevice? _selectedPlaybackDevice;

        [ObservableProperty]
        private MMDevice? _selectedCaptureDevice;
    }
}