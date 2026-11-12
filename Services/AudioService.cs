using Avalonia.Threading;
using BliBloup.Models;
using BliBloup.ViewModels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace BliBloup.Services
{
    public class AudioService : IDisposable
    {
        private bool _isRestarting;
        private readonly object _restartLock = new();

        private readonly AppSettings _settings;
        private readonly MainWindowViewModel _viewModel;
        private readonly DeviceManager _deviceManager;

        private WasapiCapture? _micCapture;
        private IWavePlayer? _persistentOutput;
        private MixingSampleProvider? _persistentMixer;
        private bool _mmcssApplied;

        private EqualizerSampleProvider? _micEq;
        private PitchShifter? _micPitch;
        private SoundEffectsSampleProvider? _micFx;
        private TeeSampleProvider? _micTee;
        private IWavePlayer? _micMonitorOutput;
        private BufferedWaveProvider? _micMonitorBuffer;
        private readonly SoundItem _micSound = new() { Name = "Microphone" };
                
        public bool IsUsingVirtualOutput => 
        _deviceManager.SelectedPlaybackDevice?.FriendlyName?.Contains("CABLE Input") == true;

        /// <summary>Raised after the persistent routing mixer has been rebuilt.</summary>
        public event EventHandler? RoutingRestarted;

        public AudioService(MainWindowViewModel viewModel, AppSettings settings, DeviceManager deviceManager)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            _viewModel = viewModel;
            _settings = settings;
            _deviceManager = deviceManager;
            _settings.VoiceChanger ??= new VoiceChangerSettings();
            _settings.VoiceChanger.PropertyChanged += HandleVoiceChangerSettingsChanged;
        }

        public void InitializeDevices()
        {
            StartPersistentAudioRouting();
        }

        public void HandlePlaybackDeviceChanged(MMDevice? device)
        {
            _deviceManager.SetPlaybackDevice(device);
            RestartPersistentAudioRouting();
        }

        private void HandleCaptureError(object? sender, StoppedEventArgs e)
        {
            if (e.Exception == null) return;
            
            Console.WriteLine($"Capture error: {e.Exception.Message}");
            ScheduleAudioRestart();
        }

        private void HandleOutputError(object? sender, StoppedEventArgs e)
        {
            if (e.Exception == null) return;
            
            Console.WriteLine($"Output error: {e.Exception.Message}");
            ScheduleAudioRestart();
        }

        private void ScheduleAudioRestart()
        {
            lock (_restartLock)
            {
                if (_isRestarting) return;
                _isRestarting = true;
            }

            // Restart on UI thread after delay
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await Task.Delay(1000);
                    Console.WriteLine("Attempting audio restart...");
                    RestartPersistentAudioRouting();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Restart failed: {ex.Message}");
                }
                finally
                {
                    lock (_restartLock) _isRestarting = false;
                }
            });
        }


        public void HandleCaptureDeviceChanged(MMDevice? device)
        {
            _deviceManager.SetCaptureDevice(device);
            RestartPersistentAudioRouting();
        }

        public void RestartPersistentAudioRouting()
        {
            StopPersistentAudioRouting();
            StartPersistentAudioRouting();
        }

        private void StartPersistentAudioRouting()
        {
            if (_deviceManager.SelectedPlaybackDevice == null || _deviceManager.SelectedCaptureDevice == null)
                return;

            try
            {
                StopPersistentAudioRouting();

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                _persistentMixer = new MixingSampleProvider(targetFormat)
                {
                    ReadFully = true
                };
                _deviceManager.SetPersistentMixer(_persistentMixer);

                _mmcssApplied = false;
                _micCapture = CreateMicCapture(_settings.CaptureExclusiveMode);
                var micProvider = new WaveInProvider(_micCapture);
                var micSampleProvider = ConvertFormat(micProvider.ToSampleProvider(), targetFormat);

                // Voice changer: EQ, pitch and the shared effect chain live on the microphone
                // branch only. Soundboard clips still enter the persistent mixer separately.
                ApplyVoiceChangerSettings();
                _micEq = new EqualizerSampleProvider(micSampleProvider, _micSound);
                _micPitch = new PitchShifter(_micEq);
                _micPitch.SetPitch((float)_micSound.Pitch);
                _micFx = new SoundEffectsSampleProvider(_micPitch, _micSound);
                _micTee = new TeeSampleProvider(_micFx);

                if (_settings.VoiceChanger.Monitor)
                {
                    StartMicMonitor();
                }

                _persistentMixer.AddMixerInput(_micTee);

                Logger.Log($"Voice changer active: pitch={_micSound.Pitch:0.##}, bass={_micSound.Bass:0.#}/{_micSound.Treble:0.#} dB, " +
                           $"distortion={_micSound.Distortion:0}%, echo={_micSound.EchoDelayMs:0}ms, " +
                           $"reverb={_micSound.ReverbMix:0}%, robot={_micSound.Robotize}.");

                _persistentOutput = new WasapiOut(
                    _deviceManager.SelectedPlaybackDevice,
                    AudioClientShareMode.Shared,
                    false,
                    100
                );
                _persistentOutput.PlaybackStopped += HandleOutputError;

                // Master limiter for the virtual microphone mix (microphone + clips): several
                // sounds playing together must not clip before reaching the game/Discord.
                _persistentOutput.Init(new LimiterSampleProvider(_persistentMixer));
                try
                {
                    _micCapture.StartRecording();
                }
                catch (Exception ex) when (_settings.CaptureExclusiveMode)
                {
                    // Some drivers only expose their format in shared mode: keep the app usable.
                    Logger.Log($"Exclusive capture unavailable ({ex.Message}); falling back to shared mode.");
                    DisposeMicCapture();
                    _mmcssApplied = false;
                    _micCapture = CreateMicCapture(exclusive: false);
                    _micCapture.StartRecording();
                }

                _persistentOutput.Play();
                RoutingRestarted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting persistent routing: {ex}");
            }
        }

        private void StopPersistentAudioRouting()
        {
            StopMicMonitor();
            _micEq = null;
            _micPitch = null;
            _micFx = null;
            _micTee = null;
            DisposeMicCapture();

            if (_persistentOutput != null)
            {
                _persistentOutput.PlaybackStopped -= HandleOutputError;
                _persistentOutput.Stop();
                _persistentOutput.Dispose();
                _persistentOutput = null;
            }

            _persistentMixer?.RemoveAllMixerInputs();
        }


        private WasapiCapture CreateMicCapture(bool exclusive)
        {
            var device = _deviceManager.SelectedCaptureDevice
                ?? throw new InvalidOperationException("No capture device is selected.");

            int bufferMilliseconds = Math.Clamp(_settings.CaptureBufferMilliseconds, 10, 250);

            // NAudio 2.2.1 does not expose IAudioClient3, so the shared path uses event-driven
            // WASAPI with the smallest buffer the device accepts. That is the lowest-latency
            // shared mode available through the library while other applications can still use
            // the microphone.
            var capture = new WasapiCapture(
                device,
                useEventSync: true,
                audioBufferMillisecondsLength: bufferMilliseconds)
            {
                ShareMode = exclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared
            };

            capture.DataAvailable += HandleMicDataAvailable;
            capture.RecordingStopped += HandleCaptureError;

            Logger.Log($"Microphone capture: {capture.ShareMode} mode, buffer {bufferMilliseconds} ms, " +
                       $"Pro Audio priority requested={_settings.CaptureProAudioPriority}.");

            return capture;
        }

        private void HandleMicDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_mmcssApplied || !_settings.CaptureProAudioPriority)
            {
                return;
            }

            // The first callback always runs on the WASAPI capture thread.
            _mmcssApplied = true;
            MmcssThread.TryEnableProAudio();
        }

        private void DisposeMicCapture()
        {
            if (_micCapture is null)
            {
                return;
            }

            _micCapture.DataAvailable -= HandleMicDataAvailable;
            _micCapture.RecordingStopped -= HandleCaptureError;

            try
            {
                _micCapture.StopRecording();
            }
            catch
            {
                // The device may already be gone; disposal below still releases the COM objects.
            }

            _micCapture.Dispose();
            _micCapture = null;
            _mmcssApplied = false;
        }

        /// <summary>Forces a neutral voice and clears the microphone pitch-shifter state.</summary>
        public void ResetVoiceChanger()
        {
            ApplyVoiceChangerSettings();
            _micPitch?.Reset();
        }

        private void ApplyVoiceChangerSettings()
        {
            var voice = _settings.VoiceChanger;

            _micSound.Bass = voice.Bass;
            _micSound.Treble = voice.Treble;
            _micSound.Pitch = voice.Pitch;
            _micSound.Distortion = voice.Distortion;
            _micSound.EchoDelayMs = voice.EchoDelayMs;
            _micSound.EchoFeedback = voice.EchoFeedback;
            _micSound.ReverbMix = voice.ReverbMix;
            _micSound.Robotize = voice.Robotize;

            _micEq?.UpdateFilters(_micSound);

            if (_micPitch is not null)
            {
                if (Math.Abs(voice.Pitch - 1.0) < 0.0001)
                {
                    _micPitch.Reset();
                }
                else
                {
                    _micPitch.SetPitch((float)voice.Pitch);
                }
            }
        }

        private void HandleVoiceChangerSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VoiceChangerSettings.Monitor))
            {
                if (_micCapture is not null)
                {
                    RestartPersistentAudioRouting();
                }

                return;
            }

            ApplyVoiceChangerSettings();
        }

        private void StartMicMonitor()
        {
            if (_micTee is null)
            {
                return;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var selected = _deviceManager.SelectedPlaybackDevice;

                if (selected is not null &&
                    (string.Equals(selected.ID, device.ID, StringComparison.OrdinalIgnoreCase) ||
                     device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Log("Voice monitoring skipped: the default output is the virtual cable.");
                    return;
                }

                var format = _micTee.WaveFormat;
                _micMonitorBuffer = new BufferedWaveProvider(format)
                {
                    BufferDuration = TimeSpan.FromSeconds(1),
                    DiscardOnBufferOverflow = true,
                    ReadFully = true
                };

                _micTee.Sink = _micMonitorBuffer;

                _micMonitorOutput = new WasapiOut(
                    device,
                    AudioClientShareMode.Shared,
                    useEventSync: false,
                    latency: 100);

                _micMonitorOutput.PlaybackStopped += HandleOutputError;
                _micMonitorOutput.Init(_micMonitorBuffer);
                _micMonitorOutput.Play();

                Logger.Log($"Voice monitoring enabled on '{device.FriendlyName}'.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Voice monitoring unavailable: {ex.Message}");
                StopMicMonitor();
            }
        }

        private void StopMicMonitor()
        {
            if (_micTee is not null)
            {
                _micTee.Sink = null;
            }

            if (_micMonitorOutput is not null)
            {
                _micMonitorOutput.PlaybackStopped -= HandleOutputError;

                try
                {
                    _micMonitorOutput.Stop();
                }
                catch
                {
                    // Device may already be gone.
                }

                _micMonitorOutput.Dispose();
                _micMonitorOutput = null;
            }

            _micMonitorBuffer = null;
        }

        public static ISampleProvider ConvertFormat(ISampleProvider input, WaveFormat targetFormat)
        {
            if (input.WaveFormat.Channels != targetFormat.Channels)
            {
                if (targetFormat.Channels == 2)
                {
                    input = input.WaveFormat.Channels switch
                    {
                        1 => new MonoToStereoSampleProvider(input),
                        > 2 => new DownmixToStereoSampleProvider(input),
                        _ => input
                    };
                }
                else if (targetFormat.Channels == 1)
                {
                    input = input.WaveFormat.Channels switch
                    {
                        2 => new StereoToMonoSampleProvider(input),
                        > 2 => throw new NotSupportedException(
                            "Downmixing to mono from multi-channel not implemented"),
                        _ => input
                    };
                }
            }

            if (input.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                input = new WdlResamplingSampleProvider(input, targetFormat.SampleRate);
            }

            return input;
        }

        public void Dispose()
        {
            _settings.VoiceChanger.PropertyChanged -= HandleVoiceChangerSettingsChanged;
            StopPersistentAudioRouting();
            _persistentOutput?.Dispose();
            _micCapture?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}