using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Real-time microphone voice-changer settings. Stored in the application settings and read
/// continuously by the microphone processing chain.
/// </summary>
public class VoiceChangerSettings : INotifyPropertyChanged
{
    private double _bass;
    private double _treble;
    private double _pitch = 1.0;
    private double _distortion;
    private double _echoDelayMs;
    private double _echoFeedback;
    private double _reverbMix;
    private bool _robotize;
    private bool _monitor;

    public double Bass
    {
        get => _bass;
        set => Set(ref _bass, Math.Clamp(value, -20, 20));
    }

    public double Treble
    {
        get => _treble;
        set => Set(ref _treble, Math.Clamp(value, -20, 20));
    }

    public double Pitch
    {
        get => _pitch;
        set => Set(ref _pitch, Math.Clamp(value, 0.5, 2.0));
    }

    public double Distortion
    {
        get => _distortion;
        set => Set(ref _distortion, Math.Clamp(value, 0, 100));
    }

    public double EchoDelayMs
    {
        get => _echoDelayMs;
        set => Set(ref _echoDelayMs, Math.Clamp(value, 0, 500));
    }

    public double EchoFeedback
    {
        get => _echoFeedback;
        set => Set(ref _echoFeedback, Math.Clamp(value, 0, 90));
    }

    public double ReverbMix
    {
        get => _reverbMix;
        set => Set(ref _reverbMix, Math.Clamp(value, 0, 100));
    }

    public bool Robotize
    {
        get => _robotize;
        set => Set(ref _robotize, value);
    }

    /// <summary>Play the processed voice on the default speakers for local monitoring.</summary>
    public bool Monitor
    {
        get => _monitor;
        set => Set(ref _monitor, value);
    }

    /// <summary>Neutral voice: no EQ, no effects, pitch at 1.0x. Monitoring is kept as it is.</summary>
    public void Reset()
    {
        Bass = 0;
        Treble = 0;
        Pitch = 1.0;
        Distortion = 0;
        EchoDelayMs = 0;
        EchoFeedback = 0;
        ReverbMix = 0;
        Robotize = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
