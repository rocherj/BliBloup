using System;
using NAudio.Wave;

namespace BliBloup.Services
{
    /// <summary>
    /// Simple peak limiter used on the master outputs (speaker mix and the virtual microphone
    /// mix). Several clips played together can sum well past full scale; this keeps the mixed
    /// signal under the ceiling instead of letting it clip.
    ///
    /// Instant attack with a smoothed release: transparent on quiet material, gently
    /// compresses loud transients. No look-ahead, so it is a "basic" safety limiter rather
    /// than a mastering compressor.
    /// </summary>
    public sealed class LimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _ceiling;
        private readonly float _releaseCoefficient;
        private float _gain = 1f;

        public LimiterSampleProvider(
            ISampleProvider source,
            float ceiling = 0.98f,
            double releaseMilliseconds = 80.0)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _ceiling = Math.Clamp(ceiling, 0.1f, 1f);

            var sampleRate = Math.Max(1, source.WaveFormat.SampleRate);
            _releaseCoefficient = Coefficient(releaseMilliseconds, sampleRate);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>Current linear gain applied by the limiter (1 = no reduction).</summary>
        public float CurrentGain => _gain;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            int channels = WaveFormat.Channels;

            if (channels <= 0)
            {
                return read;
            }

            for (int i = 0; i + channels <= read; i += channels)
            {
                float peak = 0f;

                for (int channel = 0; channel < channels; channel++)
                {
                    float absolute = Math.Abs(buffer[offset + i + channel]);
                    if (absolute > peak)
                    {
                        peak = absolute;
                    }
                }

                float targetGain = peak > _ceiling ? _ceiling / peak : 1f;

                // Instant attack guarantees the ceiling is never exceeded; the release smooths
                // the return to unity so the gain change stays inaudible.
                if (targetGain < _gain)
                {
                    _gain = targetGain;
                }
                else
                {
                    _gain += (targetGain - _gain) * _releaseCoefficient;
                }

                for (int channel = 0; channel < channels; channel++)
                {
                    buffer[offset + i + channel] *= _gain;
                }
            }

            return read;
        }

        private static float Coefficient(double milliseconds, int sampleRate)
        {
            var samples = Math.Max(1.0, milliseconds * sampleRate / 1000.0);
            return 1f - (float)Math.Exp(-1.0 / samples);
        }
    }
}
