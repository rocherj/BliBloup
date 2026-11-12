using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

/// <summary>
/// Pitch shifter with a true bypass at 1.0x.
///
/// The SMB shifter keeps an internal buffer that gets out of shape when the factor comes back to
/// exactly 1.0, which produced a metallic reverb-like tail. At 1.0 the source is now read
/// directly and a fresh shifter is created the next time a different factor is requested.
/// Pitch changes and bypass transitions are softly ramped to avoid clicks.
/// </summary>
public class PitchShifter : ISampleProvider
{
    private const float BypassEpsilon = 0.001f;
    private const int FadeMilliseconds = 8;
    private const float SmoothingSeconds = 0.03f;

    private readonly ISampleProvider _source;
    private SmbPitchShiftingSampleProvider? _shifter;
    private float _target = 1f;
    private float _current = 1f;
    private bool _bypassing = true;
    private int _fadeRemaining;
    private readonly int _fadeSamples;

    public PitchShifter(ISampleProvider source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (source.WaveFormat == null) throw new ArgumentException("Source has no WaveFormat", nameof(source));

        _source = source.ToStereo();
        WaveFormat = _source.WaveFormat;
        _fadeSamples = Math.Max(1, WaveFormat.SampleRate * FadeMilliseconds / 1000);
    }

    public WaveFormat WaveFormat { get; }

    public float PitchFactor => _target;

    public void SetPitch(float factor)
    {
        _target = Math.Clamp(factor, 0.5f, 2f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        // Move the working factor gently towards the requested one.
        float difference = _target - _current;
        if (Math.Abs(difference) < 0.0005f)
        {
            _current = _target;
        }
        else
        {
            float blend = Math.Min(1f, count / (float)(WaveFormat.SampleRate * SmoothingSeconds));
            _current += difference * Math.Max(0.05f, blend);
        }

        bool bypass = Math.Abs(_current - 1f) < BypassEpsilon;

        if (bypass != _bypassing)
        {
            _bypassing = bypass;
            _fadeRemaining = _fadeSamples;

            if (!bypass)
            {
                // Never reuse a shifter whose internal buffers were left in a corrupted state.
                _shifter = new SmbPitchShiftingSampleProvider(_source)
                {
                    PitchFactor = _current
                };
            }
        }

        int read;

        if (_bypassing)
        {
            read = _source.Read(buffer, offset, count);
        }
        else
        {
            _shifter ??= new SmbPitchShiftingSampleProvider(_source)
            {
                PitchFactor = _current
            };

            _shifter.PitchFactor = _current;
            read = _shifter.Read(buffer, offset, count);
        }

        if (_fadeRemaining > 0)
        {
            int fade = _fadeRemaining;
            int total = _fadeSamples;

            for (int i = 0; i < read && fade > 0; i++)
            {
                float gain = 1f - fade / (float)total;
                buffer[offset + i] *= gain;
                fade--;
            }

            _fadeRemaining = fade;
        }

        return read;
    }
}
