using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BliBloup.Models
{
    public class SoundItem : INotifyPropertyChanged
    {
        public string Path { get; set; } = string.Empty;
        private string _name = string.Empty;
        private double _volume = 100;
        private string _iconPath = "avares://BliBloup/soundboard2.png";

        public string IconPath
        {
            get => _iconPath;
            set
            {
                if (_iconPath != value)
                {
                    _iconPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name ?? System.IO.Path.GetFileNameWithoutExtension(Path);
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                OnPropertyChanged();
            }
        }

        private double _bass;
        public double Bass
        {
            get => _bass;
            set
            {
                _bass = Math.Clamp(value, -20, 20);
                OnPropertyChanged();
            }
        }

        private double _treble;
        public double Treble
        {
            get => _treble;
            set
            {
                _treble = Math.Clamp(value, -20, 20);
                OnPropertyChanged();
            }
        }

        private double _pitch = 1.0;
        public double Pitch
        {
            get => _pitch;
            set
            {
                _pitch = Math.Clamp(value, 0.5, 2.0);
                OnPropertyChanged();
            }
        }

        private string _hotkey = string.Empty;

        /// <summary>
        /// Global keyboard combination that plays this sound while BliBloup is in the background.
        /// Stored in canonical text form (see <c>HotkeyText.Format</c>); empty means unassigned.
        /// </summary>
        public string Hotkey
        {
            get => _hotkey;
            set
            {
                var normalized = value ?? string.Empty;
                if (_hotkey != normalized)
                {
                    _hotkey = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasHotkey));
                }
            }
        }

        public bool HasHotkey => !string.IsNullOrWhiteSpace(_hotkey);

        private double _trimStart;

        /// <summary>
        /// Non destructive trim: playback starts this many seconds into the file.
        /// Applied live, so the original audio file stays untouched until the user saves.
        /// </summary>
        public double TrimStart
        {
            get => _trimStart;
            set
            {
                var normalized = Math.Max(0, value);
                if (Math.Abs(_trimStart - normalized) > 0.0001)
                {
                    _trimStart = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasTrim));
                }
            }
        }

        private double _trimEnd;

        /// <summary>
        /// End of the trimmed region in seconds. Zero means "until the end of the file".
        /// </summary>
        public double TrimEnd
        {
            get => _trimEnd;
            set
            {
                var normalized = Math.Max(0, value);
                if (Math.Abs(_trimEnd - normalized) > 0.0001)
                {
                    _trimEnd = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasTrim));
                }
            }
        }

        public bool HasTrim => _trimStart > 0.001 || _trimEnd > 0.001;

        private bool _mutePreview;

        /// <summary>
        /// Keep this sound in the virtual microphone mix but silence its local speaker/headphone
        /// copy. Useful to check what goes to Discord/OBS without hearing the clip.
        /// </summary>
        public bool MutePreview
        {
            get => _mutePreview;
            set
            {
                if (_mutePreview != value)
                {
                    _mutePreview = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _distortion;

        /// <summary>Soft-clipping amount, 0 (bypassed) to 100.</summary>
        public double Distortion
        {
            get => _distortion;
            set
            {
                var normalized = Math.Clamp(value, 0, 100);
                if (Math.Abs(_distortion - normalized) > 0.0001)
                {
                    _distortion = normalized;
                    OnPropertyChanged();
                }
            }
        }

        private double _echoDelayMs;

        /// <summary>Echo delay in milliseconds, 0 (bypassed) to 500.</summary>
        public double EchoDelayMs
        {
            get => _echoDelayMs;
            set
            {
                var normalized = Math.Clamp(value, 0, 500);
                if (Math.Abs(_echoDelayMs - normalized) > 0.0001)
                {
                    _echoDelayMs = normalized;
                    OnPropertyChanged();
                }
            }
        }

        private double _echoFeedback;

        /// <summary>Echo feedback in percent, 0 (no repeats) to 90.</summary>
        public double EchoFeedback
        {
            get => _echoFeedback;
            set
            {
                var normalized = Math.Clamp(value, 0, 90);
                if (Math.Abs(_echoFeedback - normalized) > 0.0001)
                {
                    _echoFeedback = normalized;
                    OnPropertyChanged();
                }
            }
        }

        private double _reverbMix;

        /// <summary>Reverb dry/wet mix, 0 (bypassed) to 100.</summary>
        public double ReverbMix
        {
            get => _reverbMix;
            set
            {
                var normalized = Math.Clamp(value, 0, 100);
                if (Math.Abs(_reverbMix - normalized) > 0.0001)
                {
                    _reverbMix = normalized;
                    OnPropertyChanged();
                }
            }
        }

        private bool _robotize;

        /// <summary>Enables the ring-modulator robot voice effect.</summary>
        public bool Robotize
        {
            get => _robotize;
            set
            {
                if (_robotize != value)
                {
                    _robotize = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}