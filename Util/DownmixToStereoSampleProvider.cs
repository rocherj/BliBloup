using System;
using NAudio.Wave;

public class DownmixToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _sourceBuffer = Array.Empty<float>();

    public DownmixToStereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        if (_sourceChannels < 2)
            throw new ArgumentException("Source must have at least 2 channels for downmixing");
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int outputFrames = count / 2;

        if (outputFrames <= 0)
        {
            return 0;
        }

        int requestedSourceSamples = outputFrames * _sourceChannels;

        // The source has more channels than the stereo destination, so it must not be read
        // directly into the smaller output buffer.
        if (_sourceBuffer.Length < requestedSourceSamples)
        {
            _sourceBuffer = new float[requestedSourceSamples];
        }

        int samplesRead = _source.Read(_sourceBuffer, 0, requestedSourceSamples);
        int numFrames = samplesRead / _sourceChannels;

        for (int i = 0; i < numFrames; i++)
        {
            float left = 0;
            float right = 0;
            int sourceOffset = i * _sourceChannels;

            for (int ch = 0; ch < _sourceChannels; ch++)
            {
                float sample = _sourceBuffer[sourceOffset + ch];
                if (ch == 0) left = sample;        // First channel to left
                else if (ch == 1) right = sample;  // Second channel to right
                else
                {
                    // Additional channels split evenly between left and right
                    left += sample * 0.5f;
                    right += sample * 0.5f;
                }
            }

            buffer[offset + i * 2] = left;      // Left channel
            buffer[offset + i * 2 + 1] = right; // Right channel
        }

        return numFrames * 2;
    }
}