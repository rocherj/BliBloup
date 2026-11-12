using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BliBloup.Models;

namespace BliBloup.Services
{
    public enum LibraryLoadNote
    {
        None,

        /// <summary>The file used the old ReferenceHandler.Preserve format and was translated.</summary>
        MigratedFromLegacy,

        /// <summary>The main file was missing or unreadable and the previous backup was used.</summary>
        RestoredFromBackup
    }

    public sealed record LibraryLoadResult(ObservableCollection<Soundboard> Soundboards, LibraryLoadNote Note);

    /// <summary>
    /// Reads and writes the soundboard library.
    ///
    /// Format v2 is plain, readable JSON (camelCase, no reference metadata). Writing is atomic:
    /// the new document goes to a temporary file, is flushed to disk, then replaces the current
    /// one through File.Replace, which keeps the previous version as "soundboards.json.bak".
    /// A small ring of timestamped copies is kept under "backups/" as a second safety net.
    ///
    /// Legacy libraries (the old "$id"/"$values" documents) are still loaded and are rewritten
    /// in the new format on the next save.
    /// </summary>
    public sealed class SoundboardStore
    {
        public const int CurrentVersion = 2;
        public const string LibraryFileName = "soundboards.json";
        private const string DefaultIcon = "avares://BliBloup/soundboard2.png";
        /// <summary>
        /// Placeholder thumbnails shipped by previous versions (both the old brand and the
        /// intermediate soundboard.png) are upgraded to the bundled default automatically.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> LegacyDefaultIcons =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "avares://BliBloup/Assets/bocchi.jpg",
                "avares://BliBloup/soundboard.png",
                "avares://BoomBx/Assets/bocchi.jpg",
                "avares://BoomBx/soundboard.png"
            };
        private const int MaxTimestampedBackups = 5;

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Keep hotkeys and paths human readable ("Ctrl+Alt+1" instead of "Ctrl\u002BAlt\u002B1").
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions LegacyReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReferenceHandler = ReferenceHandler.Preserve
        };

        private readonly string _directory;

        public SoundboardStore(string? directory = null)
        {
            _directory = directory ?? BliBloupPaths.AppDataDirectory;
        }

        public string DirectoryPath => _directory;

        public string LibraryPath => Path.Combine(_directory, LibraryFileName);

        public string BackupPath => LibraryPath + ".bak";

        public string BackupsDirectory => Path.Combine(_directory, "backups");

        public LibraryLoadResult Load()
        {
            Directory.CreateDirectory(_directory);

            if (File.Exists(LibraryPath))
            {
                try
                {
                    var result = TryLoadFile(LibraryPath);
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Library load failed, trying the backup: {ex.Message}");
                }
            }

            if (File.Exists(BackupPath))
            {
                try
                {
                    var result = TryLoadFile(BackupPath);
                    if (result != null)
                    {
                        return result with { Note = LibraryLoadNote.RestoredFromBackup };
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Library backup load failed: {ex.Message}");
                }
            }

            return new LibraryLoadResult(new ObservableCollection<Soundboard>(), LibraryLoadNote.None);
        }

        private static LibraryLoadResult? TryLoadFile(string path)
        {
            var json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            if (LooksLikeLegacyFormat(json))
            {
                var legacy = JsonSerializer.Deserialize<ObservableCollection<Soundboard>>(json, LegacyReadOptions);
                if (legacy is null)
                {
                    return null;
                }

                SanitizeInPlace(legacy);
                return new LibraryLoadResult(legacy, LibraryLoadNote.MigratedFromLegacy);
            }

            var file = JsonSerializer.Deserialize<LibraryFile>(json, ReadOptions);
            if (file?.Soundboards is null)
            {
                return null;
            }

            var boards = new ObservableCollection<Soundboard>();

            foreach (var boardDto in file.Soundboards)
            {
                if (boardDto is null)
                {
                    continue;
                }

                boards.Add(ToModel(boardDto));
            }

            return new LibraryLoadResult(boards, LibraryLoadNote.None);
        }

        private static bool LooksLikeLegacyFormat(string json) =>
            json.Contains("\"$id\"", StringComparison.Ordinal) ||
            json.Contains("\"$values\"", StringComparison.Ordinal);

        /// <summary>
        /// Writes the library atomically. Never leaves a partially written file behind: the
        /// document is serialized to a temp file first and then swapped in.
        /// </summary>
        public void Save(IEnumerable<Soundboard> soundboards)
        {
            Directory.CreateDirectory(_directory);

            var document = new LibraryFile
            {
                Version = CurrentVersion,
                SavedAtUtc = DateTime.UtcNow,
                Soundboards = soundboards.Select(ToDto).ToList()
            };

            var tempPath = LibraryPath + ".tmp";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, WriteOptions);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                if (File.Exists(LibraryPath))
                {
                    ReplaceWithBackup(tempPath, LibraryPath, BackupPath);
                }
                else
                {
                    File.Move(tempPath, LibraryPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best effort */ }
                }
            }

            RotateTimestampedBackup();
        }

        private static void ReplaceWithBackup(string tempPath, string targetPath, string backupPath)
        {
            try
            {
                File.Replace(tempPath, targetPath, backupPath, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // Fallback for file systems where Replace is not available: still keep a backup,
                // then swap the files.
                Logger.Log($"Atomic replace unavailable, using copy fallback: {ex.Message}");
                File.Copy(targetPath, backupPath, overwrite: true);
                File.Move(tempPath, targetPath, overwrite: true);
            }
        }

        private void RotateTimestampedBackup()
        {
            try
            {
                if (!File.Exists(BackupPath))
                {
                    return;
                }

                Directory.CreateDirectory(BackupsDirectory);

                var target = Path.Combine(
                    BackupsDirectory,
                    $"soundboards-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");

                if (!File.Exists(target))
                {
                    File.Copy(BackupPath, target);
                }

                var oldBackups = Directory
                    .GetFiles(BackupsDirectory, "soundboards-*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Skip(MaxTimestampedBackups)
                    .ToList();

                foreach (var old in oldBackups)
                {
                    try { File.Delete(old); } catch { /* best effort */ }
                }
            }
            catch (Exception ex)
            {
                // Backups must never break a save.
                Logger.Log($"Could not rotate library backups: {ex.Message}");
            }
        }

        private static LibraryFileDto ToDto(Soundboard board) => new()
        {
            Name = board.Name,
            Sounds = board.Sounds.Select(ToDto).ToList()
        };

        private static SoundDto ToDto(SoundItem sound) => new()
        {
            Path = sound.Path,
            Name = sound.Name,
            IconPath = sound.IconPath,
            Volume = sound.Volume,
            Bass = sound.Bass,
            Treble = sound.Treble,
            Pitch = sound.Pitch,
            Hotkey = sound.Hotkey,
            TrimStart = sound.TrimStart,
            TrimEnd = sound.TrimEnd,
            MutePreview = sound.MutePreview,
            Distortion = sound.Distortion,
            EchoDelayMs = sound.EchoDelayMs,
            EchoFeedback = sound.EchoFeedback,
            ReverbMix = sound.ReverbMix,
            Robotize = sound.Robotize
        };

        private static Soundboard ToModel(LibraryFileDto dto)
        {
            var board = new Soundboard
            {
                Name = string.IsNullOrWhiteSpace(dto.Name) ? "Soundboard" : dto.Name
            };

            foreach (var soundDto in dto.Sounds ?? Enumerable.Empty<SoundDto>())
            {
                if (soundDto is null)
                {
                    continue;
                }

                board.Sounds.Add(ToModel(soundDto));
            }

            return board;
        }

        private static SoundItem ToModel(SoundDto dto)
        {
            var path = dto.Path ?? string.Empty;

            var item = new SoundItem
            {
                Path = path,
                Name = string.IsNullOrWhiteSpace(dto.Name) ? Path.GetFileNameWithoutExtension(path) : dto.Name,
                IconPath = NormalizeIconPath(dto.IconPath),
                Volume = Clamp(dto.Volume, 0, 100, 100),
                Bass = Clamp(dto.Bass, -20, 20, 0),
                Treble = Clamp(dto.Treble, -20, 20, 0),
                Pitch = Clamp(dto.Pitch, 0.5, 2, 1),
                Hotkey = dto.Hotkey ?? string.Empty,
                TrimStart = Math.Max(0, dto.TrimStart),
                TrimEnd = Math.Max(0, dto.TrimEnd),
                MutePreview = dto.MutePreview,
                Distortion = Math.Clamp(dto.Distortion, 0, 100),
                EchoDelayMs = Math.Clamp(dto.EchoDelayMs, 0, 500),
                EchoFeedback = Math.Clamp(dto.EchoFeedback, 0, 90),
                ReverbMix = Math.Clamp(dto.ReverbMix, 0, 100),
                Robotize = dto.Robotize
            };

            if (item.TrimEnd > 0 && item.TrimEnd <= item.TrimStart)
            {
                item.TrimEnd = 0;
            }

            return item;
        }

        /// <summary>Clamps values from a hand-edited or corrupt file into the supported range.</summary>
        private static void SanitizeInPlace(ObservableCollection<Soundboard> boards)
        {
            foreach (var board in boards)
            {
                if (board is null)
                {
                    continue;
                }

                if (board.Sounds is null)
                {
                    board.Sounds = new ObservableCollection<SoundItem>();
                }

                if (string.IsNullOrWhiteSpace(board.Name))
                {
                    board.Name = "Soundboard";
                }

                foreach (var sound in board.Sounds)
                {
                    if (sound is null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(sound.Name))
                    {
                        sound.Name = Path.GetFileNameWithoutExtension(sound.Path ?? string.Empty);
                    }

                    sound.IconPath = NormalizeIconPath(sound.IconPath);

                    sound.Volume = Clamp(sound.Volume, 0, 100, 100);
                    sound.Bass = Clamp(sound.Bass, -20, 20, 0);
                    sound.Treble = Clamp(sound.Treble, -20, 20, 0);
                    sound.Pitch = Clamp(sound.Pitch, 0.5, 2, 1);
                    sound.TrimStart = Math.Max(0, sound.TrimStart);
                    sound.TrimEnd = Math.Max(0, sound.TrimEnd);

                    if (sound.TrimEnd > 0 && sound.TrimEnd <= sound.TrimStart)
                    {
                        sound.TrimEnd = 0;
                    }
                }
            }
        }

        /// <summary>
        /// Empty or legacy default icons (the old bocchi.jpg placeholder) are upgraded to the new
        /// default thumbnail; custom paths are kept as they are.
        /// </summary>
        private static string NormalizeIconPath(string? iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return DefaultIcon;
            }

            if (LegacyDefaultIcons.Contains(iconPath))
            {
                return DefaultIcon;
            }

            // Custom thumbnails imported before the rebranding live under the old folder: map them
            // to the migrated copy when it exists.
            var legacyIcons = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BoomBx",
                "icons");

            if (Path.IsPathRooted(iconPath) &&
                iconPath.StartsWith(legacyIcons, StringComparison.OrdinalIgnoreCase))
            {
                var mapped = Path.Combine(BliBloupPaths.IconsDirectory, Path.GetFileName(iconPath));

                if (File.Exists(mapped))
                {
                    return mapped;
                }
            }

            // A custom file that was deleted or moved falls back to the default thumbnail.
            if (Path.IsPathRooted(iconPath) && !File.Exists(iconPath))
            {
                Logger.Log($"Thumbnail file is missing, using the default: {iconPath}");
                return DefaultIcon;
            }

            // Very old libraries stored only the file name relative to the icons folder.
            if (!Path.IsPathRooted(iconPath) &&
                !iconPath.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(BliBloupPaths.IconsDirectory, iconPath);

                if (!File.Exists(candidate))
                {
                    return DefaultIcon;
                }
            }

            return iconPath;
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (!double.IsFinite(value))
            {
                return fallback;
            }

            return Math.Clamp(value, min, max);
        }

        // --- Serialization contracts (new readable format) ---

        private sealed class LibraryFile
        {
            public int Version { get; set; } = CurrentVersion;

            public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

            public List<LibraryFileDto> Soundboards { get; set; } = new();
        }

        private sealed class LibraryFileDto
        {
            public string? Name { get; set; }

            public List<SoundDto>? Sounds { get; set; }
        }

        private sealed class SoundDto
        {
            public string? Path { get; set; }

            public string? Name { get; set; }

            public string? IconPath { get; set; }

            public double Volume { get; set; } = 100;

            public double Bass { get; set; }

            public double Treble { get; set; }

            public double Pitch { get; set; } = 1;

            public string? Hotkey { get; set; }

            public double TrimStart { get; set; }

            public double TrimEnd { get; set; }

            public bool MutePreview { get; set; }

            public double Distortion { get; set; }

            public double EchoDelayMs { get; set; }

            public double EchoFeedback { get; set; }

            public double ReverbMix { get; set; }

            public bool Robotize { get; set; }
        }
    }
}
