using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BliBloup.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BliBloup.Services
{
    public sealed class VoiceEventArgs : EventArgs
    {
        public VoiceEventArgs(string label, int activeVoices)
        {
            Label = label;
            ActiveVoices = activeVoices;
        }

        public string Label { get; }

        public int ActiveVoices { get; }
    }

    /// <summary>
    /// Mixes several soundboard clips at the same time on two destinations:
    /// the default speakers and the virtual microphone mixer (VB-Cable routing).
    ///
    /// Each clip becomes a <see cref="PlaybackVoice"/> with its own reader/processing chain.
    /// Both master mixes go through a <see cref="LimiterSampleProvider"/> so simultaneous
    /// playback cannot clip. Starting a sound no longer stops the sounds already playing.
    ///
    /// All public methods must be called from the UI thread; end-of-clip callbacks arrive on
    /// the audio thread and are marshalled back through the Avalonia dispatcher.
    /// </summary>
    public sealed class ClipPlaybackEngine : IDisposable
    {
        private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        private readonly DeviceManager _deviceManager;
        private readonly object _sync = new();
        private readonly List<PlaybackVoice> _voices = new();

        private MixingSampleProvider? _speakerMixer;
        private IWavePlayer? _speakerOutput;
        private bool _loopAll;
        private bool _disposed;

        public ClipPlaybackEngine(DeviceManager deviceManager)
        {
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _deviceManager.MixerInputEnded += OnVirtualMixerInputEnded;
        }

        public event EventHandler<VoiceEventArgs>? VoiceStarted;

        public event EventHandler<VoiceEventArgs>? VoiceEnded;

        public bool IsPaused { get; private set; }

        public int ActiveVoiceCount
        {
            get
            {
                lock (_sync)
                {
                    return _voices.Count;
                }
            }
        }

        public bool HasActiveVoices => ActiveVoiceCount > 0;

        /// <summary>Human readable reason of the last failed playback attempt, for the UI.</summary>
        public string? LastError { get; private set; }

        /// <summary>True when the default-speaker mix could be opened on this machine.</summary>
        public bool IsSpeakerOutputAvailable => _speakerOutput != null;

        /// <summary>Starts a clip without stopping anything already playing.</summary>
        public bool PlaySound(SoundItem sound)
        {
            LastError = null;

            if (sound is null || _disposed)
            {
                LastError = "The audio engine is not available.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sound.Path) || !File.Exists(sound.Path))
            {
                LastError = $"Audio file not found: {sound.Path}";
                Logger.Log($"Cannot play missing audio file: {sound.Path}");
                return false;
            }

            WaveStream? virtualStream = null;
            WaveStream? speakerStream = null;

            try
            {
                virtualStream = OpenTrimmedStream(sound);
                speakerStream = OpenTrimmedStream(sound);

                return StartVoice(sound, sound.Name, virtualStream, speakerStream, null);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Logger.Log($"Failed to start clip '{sound.Name}': {ex}");
                virtualStream?.Dispose();
                speakerStream?.Dispose();
                return false;
            }
        }

        /// <summary>Plays generated audio (for example TTS output) as another mixable voice.</summary>
        public bool PlayBuffer(byte[] wavData, string label, SoundItem parameters, float volume)
        {
            if (wavData is null || wavData.Length == 0 || parameters is null || _disposed)
            {
                return false;
            }

            WaveStream? virtualStream = null;
            WaveStream? speakerStream = null;

            try
            {
                virtualStream = new LoopStream(new WaveFileReader(new MemoryStream(wavData, writable: false))) { Loop = _loopAll };
                speakerStream = new LoopStream(new WaveFileReader(new MemoryStream(wavData, writable: false))) { Loop = _loopAll };

                return StartVoice(parameters, label, virtualStream, speakerStream, volume);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Logger.Log($"Failed to start buffer '{label}': {ex}");
                virtualStream?.Dispose();
                speakerStream?.Dispose();
                return false;
            }
        }

        public void StopAll()
        {
            List<PlaybackVoice> snapshot;

            lock (_sync)
            {
                snapshot = new List<PlaybackVoice>(_voices);
                _voices.Clear();
            }

            foreach (var voice in snapshot)
            {
                voice.Cancelled = true;
                CleanupVoiceResources(voice, raiseEnded: true);
            }

            IsPaused = false;
        }

        public void PauseAll()
        {
            if (IsPaused)
            {
                return;
            }

            _speakerOutput?.Pause();

            List<PlaybackVoice> snapshot;
            lock (_sync)
            {
                snapshot = new List<PlaybackVoice>(_voices);
            }

            foreach (var voice in snapshot)
            {
                if (voice.VirtualAttached && voice.VirtualInput != null)
                {
                    _deviceManager.TryRemoveMixerInput(voice.VirtualInput);
                }
            }

            IsPaused = true;
        }

        public void ResumeAll()
        {
            if (!IsPaused)
            {
                return;
            }

            List<PlaybackVoice> snapshot;
            lock (_sync)
            {
                snapshot = new List<PlaybackVoice>(_voices);
            }

            foreach (var voice in snapshot)
            {
                if (voice.VirtualAttached && voice.VirtualInput != null)
                {
                    _deviceManager.TryAddMixerInput(voice.VirtualInput);
                }
            }

            _speakerOutput?.Play();
            IsPaused = false;
        }

        public void SetLoopAll(bool loop)
        {
            _loopAll = loop;

            List<PlaybackVoice> snapshot;
            lock (_sync)
            {
                snapshot = new List<PlaybackVoice>(_voices);
            }

            foreach (var voice in snapshot)
            {
                SetStreamLoop(voice.VirtualStream, loop);
                SetStreamLoop(voice.SpeakerStream, loop);
            }
        }

        /// <summary>Re-applies volume, EQ and pitch after the user edits them while playing.</summary>
        public void UpdateVoiceSettings(SoundItem sound)
        {
            if (sound is null)
            {
                return;
            }

            List<PlaybackVoice> snapshot;
            lock (_sync)
            {
                snapshot = new List<PlaybackVoice>(_voices);
            }

            foreach (var voice in snapshot)
            {
                if (!ReferenceEquals(voice.Sound, sound))
                {
                    continue;
                }

                try
                {
                    var volume = (float)Math.Clamp(sound.Volume / 100.0, 0, 2);
                    voice.VirtualVolume.Volume = volume;
                    voice.SpeakerVolume.Volume = volume;
                    voice.VirtualEq.UpdateFilters(sound);
                    voice.SpeakerEq.UpdateFilters(sound);
                    voice.VirtualPitch.SetPitch((float)sound.Pitch);
                    voice.SpeakerPitch.SetPitch((float)sound.Pitch);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to update live voice settings: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Re-adds the virtual microphone feeds after the routing mixer was rebuilt
        /// (playback/capture device change).
        /// </summary>
        public void ReattachVirtualInputs()
        {
            List<PlaybackVoice> snapshot;
            lock (_sync)
            {
                snapshot = new List<PlaybackVoice>(_voices);
            }

            foreach (var voice in snapshot)
            {
                if (voice.VirtualInput is null || IsPaused)
                {
                    continue;
                }

                voice.VirtualAttached = _deviceManager.TryAddMixerInput(voice.VirtualInput);
            }
        }

        private bool StartVoice(SoundItem settings, string label, WaveStream virtualStream, WaveStream speakerStream, float? volumeOverride)
        {
            var speakerReady = EnsureSpeakerOutput();

            var volume = volumeOverride ?? (float)Math.Clamp(settings.Volume / 100.0, 0, 2);
            var duration = GetDuration(virtualStream);

            var voice = new PlaybackVoice
            {
                Sound = settings,
                Label = label,
                VirtualStream = virtualStream,
                SpeakerStream = speakerStream,
                Duration = duration
            };

            try
            {
                var virtualChain = BuildChain(virtualStream, settings, volume);
                voice.VirtualEq = virtualChain.Eq;
                voice.VirtualPitch = virtualChain.Pitch;
                voice.VirtualVolume = virtualChain.Volume;

                if (speakerReady)
                {
                    var speakerChain = BuildChain(speakerStream, settings, volume);
                    voice.SpeakerEq = speakerChain.Eq;
                    voice.SpeakerPitch = speakerChain.Pitch;
                    voice.SpeakerVolume = speakerChain.Volume;
                }

                // The speaker path is attached first: it is the reference end detector.
                if (speakerReady && _speakerMixer != null)
                {
                    voice.SpeakerInput = voice.SpeakerVolume;
                    _speakerMixer.AddMixerInput(voice.SpeakerInput);
                    voice.SpeakerAttached = true;
                }
                else
                {
                    speakerStream.Dispose();
                    voice.SpeakerStream = null;
                }

                voice.VirtualInput = voice.VirtualVolume;
                voice.VirtualAttached = _deviceManager.TryAddMixerInput(voice.VirtualInput);

                if (!voice.SpeakerAttached && !voice.VirtualAttached)
                {
                    // No output at all: clean up instead of silently "playing" into nowhere.
                    LastError = "No audio output is available. Check the Windows playback device and the virtual microphone routing, then try again.";
                    Logger.Log($"No audio output available for '{label}'.");
                    CleanupVoiceResources(voice, raiseEnded: false);
                    return false;
                }

                lock (_sync)
                {
                    _voices.Add(voice);
                }

                // A voice started while playback is paused must stay silent until resume.
                if (IsPaused && voice.VirtualAttached && voice.VirtualInput != null)
                {
                    _deviceManager.TryRemoveMixerInput(voice.VirtualInput);
                }

                ScheduleSafetyCleanup(voice);

                var active = ActiveVoiceCount;
                Logger.Log($"Voice started: {label} (active={active}, trim={settings.TrimStart:0.###}-{settings.TrimEnd:0.###}s, virtual={voice.VirtualAttached}, speaker={voice.SpeakerAttached}, duration={voice.Duration:0.###}s)");
                VoiceStarted?.Invoke(this, new VoiceEventArgs(label, active));
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"This sound could not be prepared: {ex.Message}";
                Logger.Log($"Failed to build voice '{label}': {ex}");
                CleanupVoiceResources(voice, raiseEnded: false);
                return false;
            }
        }

        private static WaveStream OpenTrimmedStream(SoundItem sound)
        {
            WaveStream reader = Path.GetExtension(sound.Path).ToLowerInvariant() switch
            {
                ".mp3" => new Mp3FileReader(sound.Path),
                ".wav" => new WaveFileReader(sound.Path),
                _ => throw new InvalidOperationException("Unsupported file format")
            };

            return new TrimmedLoopStream(reader, sound.TrimStart, sound.TrimEnd, loop: false);
        }

        private static double GetDuration(WaveStream stream)
        {
            try
            {
                var bytesPerSecond = Math.Max(1, stream.WaveFormat.AverageBytesPerSecond);
                return stream.Length / (double)bytesPerSecond;
            }
            catch
            {
                return 0;
            }
        }

        private bool EnsureSpeakerOutput()
        {
            if (_speakerOutput != null)
            {
                return true;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                if (device == null)
                {
                    return false;
                }

                _speakerMixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
                _speakerMixer.MixerInputEnded += OnSpeakerMixerInputEnded;

                var limiter = new LimiterSampleProvider(_speakerMixer);
                var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, latency: 80);
                output.PlaybackStopped += (_, args) =>
                {
                    if (args.Exception != null)
                    {
                        Logger.Log($"Speaker output stopped: {args.Exception.Message}");
                    }
                };

                output.Init(limiter);
                output.Play();

                _speakerOutput = output;
                Logger.Log("Speaker mix started.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Speaker mix unavailable: {ex.Message}");
                _speakerMixer = null;
                _speakerOutput = null;
                return false;
            }
        }

        private class PlaybackChain
        {
            public EqualizerSampleProvider Eq { get; init; } = null!;

            public PitchShifter Pitch { get; init; } = null!;

            public VolumeSampleProvider Volume { get; init; } = null!;
        }

        private static PlaybackChain BuildChain(WaveStream stream, SoundItem sound, float volume)
        {
            var sampleProvider = stream.ToSampleProvider();
            var converted = AudioService.ConvertFormat(sampleProvider, MixFormat);

            var eq = new EqualizerSampleProvider(converted, sound);
            var pitch = new PitchShifter(eq);
            pitch.SetPitch((float)sound.Pitch);
            var volumeProvider = new VolumeSampleProvider(pitch) { Volume = volume };

            return new PlaybackChain
            {
                Eq = eq,
                Pitch = pitch,
                Volume = volumeProvider
            };
        }

        private void OnVoicePathEnded(PlaybackVoice voice, bool speakerPath)
        {
            // Raised by the mixers on the audio thread when they drop a finished input.
            // Never touch Avalonia state or the mixers from here.
            Logger.Log($"Voice path ended: {voice.Label} ({(speakerPath ? "speaker" : "virtual" )})");

            bool otherPathStillRunning;

            if (speakerPath)
            {
                voice.SpeakerAttached = false;
                voice.SpeakerInput = null;
                otherPathStillRunning = voice.VirtualAttached;
            }
            else
            {
                voice.VirtualAttached = false;
                voice.VirtualInput = null;
                otherPathStillRunning = voice.SpeakerAttached;
            }

            if (!otherPathStillRunning)
            {
                Dispatcher.UIThread.Post(() => CompleteVoice(voice));
            }
        }

        private void OnSpeakerMixerInputEnded(object? sender, SampleProviderEventArgs e) =>
            OnMixerInputEnded(e.SampleProvider, speakerPath: true);

        private void OnVirtualMixerInputEnded(object? sender, ISampleProvider provider) =>
            OnMixerInputEnded(provider, speakerPath: false);

        private void OnMixerInputEnded(ISampleProvider provider, bool speakerPath)
        {
            PlaybackVoice? voice = null;

            lock (_sync)
            {
                foreach (var candidate in _voices)
                {
                    if (speakerPath
                            ? ReferenceEquals(candidate.SpeakerInput, provider)
                            : ReferenceEquals(candidate.VirtualInput, provider))
                    {
                        voice = candidate;
                        break;
                    }
                }
            }

            if (voice is null)
            {
                return; // not one of our clip inputs (for example the microphone feed)
            }

            OnVoicePathEnded(voice, speakerPath);
        }

        private void CompleteVoice(PlaybackVoice voice)
        {
            if (!voice.Cancelled && !RemoveVoice(voice))
            {
                return;
            }

            CleanupVoiceResources(voice, raiseEnded: true);
        }

        private bool RemoveVoice(PlaybackVoice voice)
        {
            lock (_sync)
            {
                return _voices.Remove(voice);
            }
        }

        private void ScheduleSafetyCleanup(PlaybackVoice voice)
        {
            if (voice.Duration <= 0 || IsVoiceLooping(voice))
            {
                return;
            }

            // Keep the voice alive slightly longer than the clip: the detector normally fires
            // first, this only covers a dead output device.
            var timeout = TimeSpan.FromSeconds(Math.Min(voice.Duration + 2.0, 60 * 30));

            _ = Task.Delay(timeout).ContinueWith(_ =>
            {
                if (voice.Cancelled || voice.Ended || IsVoiceLooping(voice))
                {
                    return;
                }

                Logger.Log($"Voice safety cleanup: {voice.Label}");
                Dispatcher.UIThread.Post(() => CompleteVoice(voice));
            }, TaskScheduler.Default);
        }

        private static bool IsVoiceLooping(PlaybackVoice voice) =>
            voice.VirtualStream is TrimmedLoopStream { Loop: true } ||
            voice.VirtualStream is LoopStream { Loop: true } ||
            voice.SpeakerStream is TrimmedLoopStream { Loop: true } ||
            voice.SpeakerStream is LoopStream { Loop: true };

        private void CleanupVoiceResources(PlaybackVoice voice, bool raiseEnded)
        {
            if (voice.Ended)
            {
                return;
            }

            voice.Ended = true;

            try
            {
                if (voice.VirtualAttached && voice.VirtualInput != null)
                {
                    _deviceManager.TryRemoveMixerInput(voice.VirtualInput);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to detach virtual voice: {ex.Message}");
            }

            try
            {
                if (voice.SpeakerAttached && voice.SpeakerInput != null && _speakerMixer != null)
                {
                    _speakerMixer.RemoveMixerInput(voice.SpeakerInput);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to detach speaker voice: {ex.Message}");
            }

            voice.VirtualStream?.Dispose();
            voice.SpeakerStream?.Dispose();

            if (!raiseEnded)
            {
                return;
            }

            var active = ActiveVoiceCount;
            Logger.Log($"Voice ended: {voice.Label} (active={active})");
            VoiceEnded?.Invoke(this, new VoiceEventArgs(voice.Label, active));
        }

        private static void SetStreamLoop(WaveStream? stream, bool loop)
        {
            switch (stream)
            {
                case TrimmedLoopStream trimmed:
                    trimmed.Loop = loop;
                    break;
                case LoopStream loopStream:
                    loopStream.Loop = loop;
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _deviceManager.MixerInputEnded -= OnVirtualMixerInputEnded;
            StopAll();

            try
            {
                _speakerOutput?.Stop();
                _speakerOutput?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to dispose speaker output: {ex.Message}");
            }

            _speakerOutput = null;
            _speakerMixer = null;

            GC.SuppressFinalize(this);
        }

        private sealed class PlaybackVoice
        {
            public SoundItem Sound { get; init; } = null!;

            public string Label { get; init; } = string.Empty;

            public double Duration { get; init; }

            public WaveStream? VirtualStream { get; init; }

            public WaveStream? SpeakerStream { get; set; }

            public EqualizerSampleProvider VirtualEq { get; set; } = null!;

            public PitchShifter VirtualPitch { get; set; } = null!;

            public VolumeSampleProvider VirtualVolume { get; set; } = null!;

            public EqualizerSampleProvider SpeakerEq { get; set; } = null!;

            public PitchShifter SpeakerPitch { get; set; } = null!;

            public VolumeSampleProvider SpeakerVolume { get; set; } = null!;

            public ISampleProvider? VirtualInput { get; set; }

            public ISampleProvider? SpeakerInput { get; set; }

            public bool VirtualAttached { get; set; }

            public bool SpeakerAttached { get; set; }

            public volatile bool Cancelled;

            public volatile bool Ended;
        }
    }
}
