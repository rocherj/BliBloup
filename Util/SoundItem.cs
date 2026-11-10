using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BoomBx.Models
{
    public class SoundItem : INotifyPropertyChanged
    {
        public string Path { get; set; } = string.Empty;
        private string _name = string.Empty;
        private double _volume = 100;
        private string _iconPath = "avares://BoomBx/Assets/bocchi.jpg";

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
        /// Global keyboard combination that plays this sound while BoomBx is in the background.
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}