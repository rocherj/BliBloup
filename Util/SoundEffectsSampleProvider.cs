using System;
using BliBloup.Models;
using NAudio.Wave;

/// <summary>
/// Lightweight real-time effects applied after the EQ and pitch shifter:
/// soft-clipping distortion, a circular delay/echo, a small three-comb reverb and a ring
/// modulator for the robot voice. Every parameter is ramped per buffer so moving a slider while
/// a sound plays does not click, and every effect is fully bypassed when its setting is zero.
/// Settings are read from the live <see cref="SoundItem"/>, which also makes changes apply to
/// voices that are already playing.
/// </summary>
public sealed class SoundEffectsSampleProvider : ISampleProvider
{
    private const float MaxEchoSeconds = 0.5f;
    private const float EchoWet = 0.45f;
    private const float ReverbFeedback = 0.6f;
    private const float RobotFrequency = 60f;

    private readonly ISampleProvider _source;
    private readonly SoundItem _sound;
    private readonly int _channels;
    private readonly int _sampleRate;

    private readonly float[] _echoBuffer;
    private int _echoIndex;

    private readonly float[][] _reverbBuffers;
    private readonly int[] _reverbLengths;
    private readonly int[] _reverbPositions;

    private float _distortion;
    private float _echoDelaySamples;
    private float _echoFeedback;
    private float _echoWet;
    private float _reverbWet;
    private float _robotDepth;
    private float _robotPhase;

    public SoundEffectsSampleProvider(ISampleProvider source, SoundItem sound)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sound = sound ?? throw new ArgumentNullException(nameof(sound));
        WaveFormat = source.WaveFormat;

        _channels = Math.Max(1, WaveFormat.Channels);
        _sampleRate = Math.Max(8000, WaveFormat.SampleRate);

        _echoBuffer = new float[Math.Max(_channels * 2, (int)(MaxEchoSeconds * _sampleRate) * _channels + _channels)];
        _reverbLengths = new[]
        {
            Math.Max(1, (int)(0.0297f * _sampleRate)),
            Math.Max(1, (int)(0.0371f * _sampleRate)),
            Math.Max(1, (int)(0.0411f * _sampleRate))
        };

        _reverbBuffers = new float[_reverbLengths.Length][];
        _reverbPositions = new int[_reverbLengths.Length];

        for (int i = 0; i < _reverbLengths.Length; i++)
        {
            _reverbBuffers[i] = new float[_reverbLengths[i] * _channels];
        }
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        // Snapshot the live settings once per buffer: the UI can then change sliders while
        // this buffer is processed without the parameters changing halfway through it.
        float distortionTarget = (float)(Math.Clamp(_sound.Distortion, 0, 100) / 100.0);
        float delayTarget = (float)(Math.Clamp(_sound.EchoDelayMs, 0, 500) / 1000.0 * _sampleRate);
        float feedbackTarget = (float)(Math.Clamp(_sound.EchoFeedback, 0, 90) / 100.0);
        float reverbTarget = (float)(Math.Clamp(_sound.ReverbMix, 0, 100) / 100.0) * 0.7f;
        float robotTarget = _sound.Robotize ? 1f : 0f;

        int read = _source.Read(buffer, offset, count);

        if (read <= 0)
        {
            return read;
        }

        int frames = read / _channels;

        if (frames <= 0)
        {
            return read;
        }

        bool echoActive = delayTarget >= _sampleRate * 0.005f && feedbackTarget > 0.001f;
        float echoWetTarget = echoActive ? EchoWet : 0f;

        bool echoProcessing = _echoWet > 0.0001f || echoWetTarget > 0.0001f;
        bool reverbProcessing = _reverbWet > 0.0001f || reverbTarget > 0.0001f;

        bool anyEffectActive = distortionTarget > 0.0001f
                               || echoProcessing
                               || reverbProcessing
                               || robotTarget > 0.0001f
                               || _distortion > 0.0001f
                               || _robotDepth > 0.0001f;

        if (!anyEffectActive)
        {
            // Complete bypass: no allocation, no per-sample math.
            return read;
        }

        if (_echoWet <= 0.0001f && echoWetTarget > 0.0001f)
        {
            // Enabling the echo: start at the requested delay instead of ramping the delay from
            // zero, so the first repeat arrives at the time the user selected.
            _echoDelaySamples = Math.Clamp(delayTarget, _channels, _echoBuffer.Length - _channels * 2);
            _echoFeedback = feedbackTarget;
        }

        float inverseFrames = 1f / frames;
        float distortionStep = (distortionTarget - _distortion) * inverseFrames;
        float delayStep = (Math.Clamp(delayTarget, _channels, _echoBuffer.Length - _channels * 2) - _echoDelaySamples) * inverseFrames;
        float feedbackStep = (feedbackTarget - _echoFeedback) * inverseFrames;
        float echoWetStep = (echoWetTarget - _echoWet) * inverseFrames;
        float reverbStep = (reverbTarget - _reverbWet) * inverseFrames;
        float robotStep = (robotTarget - _robotDepth) * inverseFrames;
        float phaseIncrement = 2f * MathF.PI * RobotFrequency / _sampleRate;

        int combCount = _reverbLengths.Length;

        for (int frame = 0; frame < frames; frame++)
        {
            _distortion = Math.Clamp(_distortion + distortionStep, 0f, 1f);
            _echoDelaySamples = Math.Clamp(_echoDelaySamples + delayStep, _channels, _echoBuffer.Length - _channels * 2);
            _echoFeedback = Math.Clamp(_echoFeedback + feedbackStep, 0f, 0.9f);
            _echoWet = Math.Clamp(_echoWet + echoWetStep, 0f, EchoWet);
            _reverbWet = Math.Clamp(_reverbWet + reverbStep, 0f, 1f);
            _robotDepth = Math.Clamp(_robotDepth + robotStep, 0f, 1f);

            int delayFrames = Math.Max(1, (int)MathF.Round(_echoDelaySamples));
            int writeBase = _echoIndex;
            float robotSine = _robotDepth > 0.0001f ? MathF.Sin(_robotPhase) : 0f;

            for (int channel = 0; channel < _channels; channel++)
            {
                int index = offset + frame * _channels + channel;
                float sample = buffer[index];

                if (_distortion > 0.0001f)
                {
                    float drive = 1f + _distortion * 8f;
                    float shaped = MathF.Tanh(sample * drive) / MathF.Tanh(drive);
                    sample = sample * (1f - _distortion) + shaped * _distortion;
                }

                if (_robotDepth > 0.0001f)
                {
                    sample *= (1f - _robotDepth) + _robotDepth * robotSine;
                }

                if (echoProcessing)
                {
                    // Capture the signal even while the wet gain is still ramping in, otherwise
                    // the first echoes of a freshly enabled effect would be lost.
                    int readIndex = (writeBase - delayFrames * _channels) % _echoBuffer.Length;
                    if (readIndex < 0)
                    {
                        readIndex += _echoBuffer.Length;
                    }

                    readIndex += channel;
                    float delayed = _echoBuffer[readIndex];
                    _echoBuffer[writeBase + channel] = sample + delayed * _echoFeedback;
                    sample += delayed * _echoWet;
                }

                if (reverbProcessing)
                {
                    // Same as the echo: keep feeding the comb filters while the wet mix ramps.
                    float wetSum = 0f;

                    for (int comb = 0; comb < combCount; comb++)
                    {
                        int bufferIndex = _reverbPositions[comb] * _channels + channel;
                        float delayed = _reverbBuffers[comb][bufferIndex];
                        _reverbBuffers[comb][bufferIndex] = sample + delayed * ReverbFeedback;
                        wetSum += delayed;
                    }

                    sample += wetSum / combCount * _reverbWet;
                }

                buffer[index] = sample;
            }

            for (int comb = 0; comb < combCount; comb++)
            {
                _reverbPositions[comb]++;

                if (_reverbPositions[comb] >= _reverbLengths[comb])
                {
                    _reverbPositions[comb] = 0;
                }
            }

            _echoIndex += _channels;

            if (_echoIndex >= _echoBuffer.Length)
            {
                _echoIndex = 0;
            }

            _robotPhase += phaseIncrement;

            if (_robotPhase > 2f * MathF.PI)
            {
                _robotPhase -= 2f * MathF.PI;
            }
        }

        return read;
    }
}
