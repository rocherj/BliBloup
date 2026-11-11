using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;

namespace BliBloup.Services
{
    /// <summary>Decoded PCM data for one sound, ready to be wrapped in a RawSourceWaveStream.</summary>
    public sealed record PcmAudioData(byte[] Data, WaveFormat Format);

    /// <summary>
    /// Keeps a decoded 16-bit PCM copy of each sound in memory. Files are decoded once (at library
    /// load or when a sound is added) so triggering a sound only reads from a MemoryStream instead
    /// of opening and decoding the file again.
    ///
    /// The cache is an LRU bounded by <see cref="DefaultMaxBytes"/>; entries for deleted sounds are
    /// removed explicitly by the UI. Entries are also invalidated when the file timestamp or size
    /// changes, which covers trimming a file in place.
    /// </summary>
    public sealed class PcmAudioCache
    {
        public const long DefaultMaxBytes = 256L * 1024 * 1024;

        public static PcmAudioCache Instance { get; } = new();

        private readonly long _maxBytes;
        private readonly object _gate = new();
        private readonly Dictionary<string, LinkedListNode<Entry>> _entries =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<Entry> _lru = new();
        private long _totalBytes;

        private PcmAudioCache()
            : this(DefaultMaxBytes)
        {
        }

        internal PcmAudioCache(long maxBytes)
        {
            _maxBytes = Math.Max(16L * 1024 * 1024, maxBytes);
        }

        public (int Count, long Bytes) Stats
        {
            get
            {
                lock (_gate)
                {
                    return (_entries.Count, _totalBytes);
                }
            }
        }

        /// <summary>Returns the cached data, or null when the sound is not cached or the file changed.</summary>
        public PcmAudioData? TryGet(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }

            FileInfo info;

            try
            {
                info = new FileInfo(fullPath);
            }
            catch
            {
                return null;
            }

            if (!info.Exists)
            {
                return null;
            }

            lock (_gate)
            {
                if (!_entries.TryGetValue(fullPath, out var node))
                {
                    return null;
                }

                var entry = node.Value;

                if (entry.LastWriteUtc != info.LastWriteTimeUtc || entry.FileLength != info.Length)
                {
                    Remove(node);
                    return null;
                }

                // Touch: the most recently played sounds stay in memory the longest.
                _lru.Remove(node);
                _lru.AddFirst(node);
                return new PcmAudioData(entry.Data, entry.Format);
            }
        }

        /// <summary>
        /// Returns the cached data or decodes the file synchronously. A miss should be rare: the
        /// library is preloaded when it is loaded and every import starts a background decode.
        /// </summary>
        public PcmAudioData GetOrLoad(string path)
        {
            var cached = TryGet(path);

            if (cached is not null)
            {
                return cached;
            }

            string fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);

            if (!info.Exists)
            {
                throw new FileNotFoundException("The audio file could not be found.", fullPath);
            }

            var (data, format) = Decode(fullPath);
            Add(fullPath, info, data, format);
            return new PcmAudioData(data, format);
        }

        /// <summary>Decodes the given files in the background, ignoring duplicates and failures.</summary>
        public Task PreloadAsync(IEnumerable<string> paths)
        {
            var list = paths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            if (list.Length == 0)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                foreach (var path in list)
                {
                    try
                    {
                        GetOrLoad(path);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"PCM preload failed for '{path}': {ex.Message}");
                    }
                }

                var (count, bytes) = Stats;
                Logger.Log($"PCM cache ready: {count} sound(s), {bytes / (1024.0 * 1024.0):0.0} MB.");
            });
        }

        public void Unload(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            lock (_gate)
            {
                if (_entries.TryGetValue(fullPath, out var node))
                {
                    Remove(node);
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _lru.Clear();
                _totalBytes = 0;
            }
        }

        private void Add(string fullPath, FileInfo info, byte[] data, WaveFormat format)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(fullPath, out var existing))
                {
                    Remove(existing);
                }

                // A single huge file would evict the whole library: keep it on disk instead.
                if (data.LongLength > _maxBytes / 2)
                {
                    Logger.Log($"PCM cache skipped '{Path.GetFileName(fullPath)}': {data.Length / (1024.0 * 1024.0):0.0} MB is too large.");
                    return;
                }

                var node = new LinkedListNode<Entry>(new Entry
                {
                    Path = fullPath,
                    Data = data,
                    Format = format,
                    FileLength = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc
                });

                _lru.AddFirst(node);
                _entries[fullPath] = node;
                _totalBytes += data.LongLength;

                while (_totalBytes > _maxBytes && _lru.Last is { } last)
                {
                    Logger.Log($"PCM cache evicted '{Path.GetFileName(last.Value.Path)}' " +
                               $"({last.Value.Data.Length / (1024.0 * 1024.0):0.0} MB), " +
                               $"memory budget {_maxBytes / (1024.0 * 1024.0):0} MB.");
                    Remove(last);
                }

                Logger.Log($"PCM cache loaded '{Path.GetFileName(fullPath)}' " +
                           $"({format.SampleRate} Hz, {format.Channels} ch, {data.Length / 1024} KB).");
            }
        }

        private void Remove(LinkedListNode<Entry> node)
        {
            _lru.Remove(node);
            _entries.Remove(node.Value.Path);
            _totalBytes -= node.Value.Data.LongLength;
        }

        private static (byte[] Data, WaveFormat Format) Decode(string path)
        {
            using var reader = new AudioFileReader(path);

            var format = new WaveFormat(
                Math.Max(8000, reader.WaveFormat.SampleRate),
                16,
                Math.Max(1, reader.WaveFormat.Channels));

            using var memory = new MemoryStream();

            // Decode in one-second chunks; the float buffer comes from AudioFileReader.
            int chunkSamples = Math.Max(2048, format.SampleRate * format.Channels);
            var floats = new float[chunkSamples];
            var bytes = new byte[chunkSamples * 2];

            int read;

            while ((read = reader.Read(floats, 0, floats.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    short sample = (short)Math.Clamp(floats[i] * 32767f, -32768f, 32767f);
                    bytes[2 * i] = (byte)sample;
                    bytes[2 * i + 1] = (byte)(sample >> 8);
                }

                memory.Write(bytes, 0, read * 2);
            }

            return (memory.ToArray(), format);
        }

        private sealed class Entry
        {
            public string Path { get; init; } = string.Empty;
            public byte[] Data { get; init; } = Array.Empty<byte>();
            public WaveFormat Format { get; init; } = new WaveFormat(44100, 16, 2);
            public long FileLength { get; init; }
            public DateTime LastWriteUtc { get; init; }
        }
    }
}
