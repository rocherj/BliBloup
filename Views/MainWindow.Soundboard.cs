using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BoomBx.Models;
using BoomBx.Services;

namespace BoomBx.Views
{
    /// <summary>
    /// Soundboard management: drag and drop reordering, and the quick actions available directly
    /// on each sound tile (edit hotkey, duplicate, rename, delete with confirmation).
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string SoundDragFormat = "application/x-boombx-sound";

        private Point? _soundDragStart;
        private SoundItem? _soundDragCandidate;
        private bool _soundDragInProgress;

        private void InitializeSoundboardEditing()
        {
            DragDrop.SetAllowDrop(SoundsListBox, true);

            // handledEventsToo: the ListBox items consume pointer/touch events for selection,
            // so a plain XAML handler would never see the press that starts a drag.
            SoundsListBox.AddHandler(InputElement.PointerPressedEvent, SoundsList_PointerPressed,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            SoundsListBox.AddHandler(InputElement.PointerMovedEvent, SoundsList_PointerMoved,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            SoundsListBox.AddHandler(DragDrop.DragOverEvent, SoundsList_DragOver,
                RoutingStrategies.Bubble, handledEventsToo: true);
            SoundsListBox.AddHandler(DragDrop.DropEvent, SoundsList_Drop,
                RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private void SoundsList_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(SoundsListBox).Properties.IsLeftButtonPressed)
            {
                return;
            }

            // Buttons inside the tile (hotkey / delete) must not start a drag.
            if (FindAncestor<Button>(e.Source as Visual) != null)
            {
                return;
            }

            _soundDragStart = e.GetPosition(SoundsListBox);
            _soundDragCandidate = FindSoundItem(e.Source as Visual);
        }

        private async void SoundsList_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_soundDragInProgress || _soundDragCandidate is null || _soundDragStart is null)
            {
                return;
            }

            if (!e.GetCurrentPoint(SoundsListBox).Properties.IsLeftButtonPressed)
            {
                _soundDragCandidate = null;
                return;
            }

            var position = e.GetPosition(SoundsListBox);
            var delta = position - _soundDragStart.Value;

            if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8)
            {
                return;
            }

            var candidate = _soundDragCandidate;
            _soundDragCandidate = null;
            _soundDragInProgress = true;

            try
            {
                var data = new DataObject();
                data.Set(SoundDragFormat, candidate);
                Logger.Log($"Sound drag started: {candidate.Name}");
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                Logger.Log($"Sound drag failed: {ex.Message}");
            }
            finally
            {
                _soundDragInProgress = false;
            }
        }

        private void SoundsList_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains(SoundDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void SoundsList_Drop(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains(SoundDragFormat) ||
                e.Data.Get(SoundDragFormat) is not SoundItem source ||
                ViewModel.SelectedSoundboard is not { } board)
            {
                return;
            }

            var target = FindSoundItem(e.Source as Visual);
            int fromIndex = board.Sounds.IndexOf(source);
            int toIndex = target is not null ? board.Sounds.IndexOf(target) : board.Sounds.Count - 1;

            Logger.Log($"Sound drop: {source.Name} -> {target?.Name ?? "end"}");
            if (SoundboardEditor.MoveSound(board.Sounds, fromIndex, toIndex))
            {
                SaveSoundLibrary();
                UpdateStatus($"Moved \"{source.Name}\" in the list.");
            }

            e.Handled = true;
        }

        private void EditHotkeyFromList_Click(object? sender, RoutedEventArgs e)
        {
            if (GetSoundFromAction(sender) is not { } sound)
            {
                return;
            }

            BeginHotkeyCapture(null, sound);
        }

        private async void DeleteSoundFromList_Click(object? sender, RoutedEventArgs e)
        {
            Logger.Log($"Delete action: tag={((sender as Control)?.Tag as SoundItem)?.Name ?? "null"}, selected={ViewModel.SelectedSound?.Name ?? "null"}, board={ViewModel.SelectedSoundboard?.Name ?? "null"}");
            if (GetSoundFromAction(sender) is not { } sound ||
                ViewModel.SelectedSoundboard is not { } board)
            {
                return;
            }

            var confirmed = await ConfirmAsync(
                "Delete sound",
                $"Remove \"{sound.Name}\" from the soundboard?\n\nThe audio file itself is kept on disk.",
                "Delete");

            if (!confirmed)
            {
                return;
            }

            board.Sounds.Remove(sound);

            if (ReferenceEquals(ViewModel.SelectedSound, sound))
            {
                ViewModel.SelectedSound = null;
            }

            SaveSoundLibrary();
            LoadAndRegisterHotkeys(announce: false);
            UpdateStatus($"Deleted \"{sound.Name}\".");
        }

        private void DuplicateSound_Click(object? sender, RoutedEventArgs e)
        {
            Logger.Log($"Duplicate action: tag={((sender as Control)?.Tag as SoundItem)?.Name ?? "null"}, selected={ViewModel.SelectedSound?.Name ?? "null"}, board={ViewModel.SelectedSoundboard?.Name ?? "null"}");
            if (GetSoundFromAction(sender) is not { } sound ||
                ViewModel.SelectedSoundboard is not { } board)
            {
                return;
            }

            var copy = SoundboardEditor.DuplicateSound(sound);
            AttachSoundItem(copy);

            int index = board.Sounds.IndexOf(sound);
            board.Sounds.Insert(Math.Clamp(index + 1, 0, board.Sounds.Count), copy);

            SaveSoundLibrary();
            LoadAndRegisterHotkeys(announce: false);
            ViewModel.SelectedSound = copy;
            UpdateStatus($"Duplicated \"{sound.Name}\".");
        }

        private async void RenameSound_Click(object? sender, RoutedEventArgs e)
        {
            Logger.Log($"Rename action: tag={((sender as Control)?.Tag as SoundItem)?.Name ?? "null"}, selected={ViewModel.SelectedSound?.Name ?? "null"}");
            if (GetSoundFromAction(sender) is not { } sound)
            {
                return;
            }

            var dialog = new InputDialog
            {
                Title = "Rename sound",
                Prompt = "Enter a new name:",
                InputText = sound.Name
            };

            var result = await dialog.ShowDialog<string>(this);

            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            sound.Name = result.Trim();
            SaveSoundLibrary();
            UpdateStatus($"Renamed sound to \"{sound.Name}\".");
        }

        /// <summary>
        /// Resolves the target sound of an action: tile buttons carry the clip in Tag, while the
        /// buttons of the right panel implicitly act on the selected sound.
        /// </summary>
        private SoundItem? GetSoundFromAction(object? sender) =>
            (sender as Control)?.Tag as SoundItem ?? ViewModel.SelectedSound;

        private static SoundItem? FindSoundItem(Visual? source)
        {
            var listBoxItem = FindAncestor<ListBoxItem>(source);
            return (listBoxItem?.DataContext as SoundItem);
        }

        private static T? FindAncestor<T>(Visual? visual) where T : Visual
        {
            while (visual != null)
            {
                if (visual is T match)
                {
                    return match;
                }

                visual = visual.GetVisualParent();
            }

            return null;
        }


    }
}
