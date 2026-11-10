using System;
using System.Collections.ObjectModel;
using BliBloup.Models;

namespace BliBloup.Services
{
    /// <summary>
    /// Pure list operations for the soundboard, kept separate from the window so they can be
    /// unit tested without a UI.
    /// </summary>
    public static class SoundboardEditor
    {
        /// <summary>
        /// Moves a sound within the collection. Returns false for invalid indices or a no-op.
        /// Dropping onto a later item inserts after it, which matches the drag direction.
        /// </summary>
        public static bool MoveSound(ObservableCollection<SoundItem> sounds, int fromIndex, int toIndex)
        {
            if (sounds is null)
            {
                return false;
            }

            if (fromIndex < 0 || fromIndex >= sounds.Count)
            {
                return false;
            }

            if (toIndex < 0 || toIndex >= sounds.Count || toIndex == fromIndex)
            {
                return false;
            }

            var item = sounds[fromIndex];
            sounds.RemoveAt(fromIndex);
            sounds.Insert(Math.Clamp(toIndex, 0, sounds.Count), item);
            return true;
        }

        /// <summary>
        /// Clones a sound (file, icon, mix settings, trim) for a duplicate. The hotkey is
        /// deliberately cleared: two clips cannot share the same global combination.
        /// </summary>
        public static SoundItem DuplicateSound(SoundItem source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new SoundItem
            {
                Path = source.Path,
                Name = BuildDuplicateName(source.Name),
                IconPath = source.IconPath,
                Volume = source.Volume,
                Bass = source.Bass,
                Treble = source.Treble,
                Pitch = source.Pitch,
                TrimStart = source.TrimStart,
                TrimEnd = source.TrimEnd,
                Hotkey = string.Empty
            };
        }

        private static string BuildDuplicateName(string? name) =>
            string.IsNullOrWhiteSpace(name) ? "Copy" : name + " (copy)";
    }
}
