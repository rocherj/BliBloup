using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BoomBx.Services
{
    public sealed record AudioClipInfo(double DurationSeconds, float[] Peaks);

    public sealed record TrimSaveResult(string Path, bool Overwritten, bool FormatChanged);

    /// <summary>
    /// Reads waveform/duration information and writes a trimmed copy of an audio file.
    ///
    /// The trim stored on a <c>SoundItem</c> is non destructive; this service implements the
    /// explicit "save" actions that bake the selection into a real file (copy or overwrite).
    /// </summary>
    public sealed class AudioTrimService
    {
        public const int PeakCount = 480;

        private readonly Dictionary<string, (DateTime WriteTimeUtc, AudioClipInfo Info)> _cache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        /// <summary>Duration and normalized peaks used by the waveform preview.</summary>
        public Task<AudioClipInfo> ReadClipInfoAsync(string path) => Task.Run(() => ReadClipInfo(path));

        public void Invalidate(string path)
        {
            lock (_gate)
            {
                _cache.Remove(path);
            }
        }

        private AudioClipInfo ReadClipInfo(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("Audio file not found.", path);
            }

            var writeTime = File.GetLastWriteTimeUtc(path);

            lock (_gate)
            {
                if (_cache.TryGetValue(path, out var cached) && cached.WriteTimeUtc == writeTime)
                {
                    return cached.Info;
                }
            }

            AudioClipInfo info;

            using (var reader = new AudioFileReader(path))
            {
                info = BuildInfo(reader);
            }

            lock (_gate)
            {
                _cache[path] = (writeTime, info);
            }

            return info;
        }

        private static AudioClipInfo BuildInfo(AudioFileReader reader)
        {
            double duration = reader.TotalTime.TotalSeconds;
            var peaks = new float[PeakCount];

            if (duration <= 0.001)
            {
                return new AudioClipInfo(0, peaks);
            }

            int channels = reader.WaveFormat.Channels;
            int sampleRate = reader.WaveFormat.SampleRate;
            double secondsPerPeak = duration / PeakCount;
            long framesPerPeak = Math.Max(1, (long)(sampleRate * secondsPerPeak));

            var buffer = new float[4096];
            long frame = 0;
            int read;

            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i + channels <= read; i += channels)
                {
                    float peak = 0f;

                    for (int channel = 0; channel < channels; channel++)
                    {
                        float absolute = Math.Abs(buffer[i + channel]);
                        if (absolute > peak)
                        {
                            peak = absolute;
                        }
                    }

                    int bucket = (int)Math.Min(PeakCount - 1, frame / framesPerPeak);
                    if (peak > peaks[bucket])
                    {
                        peaks[bucket] = peak;
                    }

                    frame++;
                }
            }

            return new AudioClipInfo(duration, peaks);
        }

        /// <summary>
        /// Writes the [start, end) region of <paramref name="sourcePath"/> to a new file.
        /// When <paramref name="overwrite"/> is true the original path is replaced (MP3 is
        /// re-encoded, WAV is rewritten); otherwise a "(trimmed)" copy is created.
        /// </summary>
        public Task<TrimSaveResult> SaveTrimmedAsync(string sourcePath, double startSeconds, double endSeconds, bool overwrite) =>
            Task.Run(() => SaveTrimmed(sourcePath, startSeconds, endSeconds, overwrite));

        private TrimSaveResult SaveTrimmed(string sourcePath, double startSeconds, double endSeconds, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Audio file not found.", sourcePath);
            }

            // Defensive: the UI already clamps, but a NaN/infinity must never reach the encoder.
            if (!double.IsFinite(startSeconds) || startSeconds < 0)
            {
                startSeconds = 0;
            }

            if (!double.IsFinite(endSeconds) || endSeconds < 0)
            {
                endSeconds = 0;
            }

            double duration;

            using (var probe = new AudioFileReader(sourcePath))
            {
                duration = probe.TotalTime.TotalSeconds;
            }

            double start = Math.Clamp(startSeconds, 0, Math.Max(0, duration - 0.01));
            double end = endSeconds <= 0 ? duration : Math.Clamp(endSeconds, 0, duration);

            if (end - start < 0.1)
            {
                throw new InvalidOperationException("The selection is shorter than 100 ms; extend it before saving.");
            }

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            bool isMp3 = extension == ".mp3";

            if (overwrite)
            {
                if (isMp3 && !OperatingSystem.IsWindows())
                {
                    throw new PlatformNotSupportedException("Overwriting an MP3 requires the Windows Media Foundation encoder; use \"Save as copy\" instead.");
                }

                var tempPath = Path.Combine(
                    Path.GetDirectoryName(sourcePath) ?? string.Empty,
                    $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.tmp{(isMp3 ? ".mp3" : ".wav")}");

                try
                {
                    if (isMp3)
                    {
                        EncodeMp3(sourcePath, start, end, tempPath);
                    }
                    else
                    {
                        WriteWav(sourcePath, start, end, tempPath);
                    }

                    File.Move(tempPath, sourcePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* best effort */ }
                    }
                }

                Invalidate(sourcePath);
                return new TrimSaveResult(sourcePath, Overwritten: true, FormatChanged: false);
            }

            if (!isMp3)
            {
                var copyPath = BuildCopyPath(sourcePath, extension);
                WriteWav(sourcePath, start, end, copyPath);
                return new TrimSaveResult(copyPath, Overwritten: false, FormatChanged: false);
            }

            // MP3 copy: re-encode when Media Foundation is available, otherwise fall back to WAV.
            var mp3CopyPath = BuildCopyPath(sourcePath, ".mp3");

            try
            {
                EncodeMp3(sourcePath, start, end, mp3CopyPath);
                return new TrimSaveResult(mp3CopyPath, Overwritten: false, FormatChanged: false);
            }
            catch (Exception ex)
            {
                Logger.Log($"MP3 encode failed, falling back to WAV copy: {ex.Message}");
            }

            var wavCopyPath = BuildCopyPath(sourcePath, ".wav");
            WriteWav(sourcePath, start, end, wavCopyPath);
            return new TrimSaveResult(wavCopyPath, Overwritten: false, FormatChanged: true);
        }

        private static ISampleProvider CreateTrimmedProvider(string sourcePath, double start, double end, out AudioFileReader reader)
        {
            reader = new AudioFileReader(sourcePath);

            return new OffsetSampleProvider(reader)
            {
                SkipOver = TimeSpan.FromSeconds(start),
                Take = TimeSpan.FromSeconds(end - start)
            };
        }

        private static void WriteWav(string sourcePath, double start, double end, string destination)
        {
            var provider = CreateTrimmedProvider(sourcePath, start, end, out var reader);

            try
            {
                WaveFileWriter.CreateWaveFile16(destination, provider);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private static void EncodeMp3(string sourcePath, double start, double end, string destination)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("MP3 encoding requires Windows Media Foundation.");
            }

            var provider = CreateTrimmedProvider(sourcePath, start, end, out var reader);

            try
            {
                var pcm = new SampleToWaveProvider16(provider);
                MediaFoundationEncoder.EncodeToMp3(pcm, destination, 192000);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private static string BuildCopyPath(string sourcePath, string extension)
        {
            var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var candidate = Path.Combine(directory, $"{name} (trimmed){extension}");
            int index = 2;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"{name} (trimmed {index}){extension}");
                index++;
            }

            return candidate;
        }
    }
}
