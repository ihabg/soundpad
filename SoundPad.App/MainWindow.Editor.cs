using System.Windows;
using SoundPad.App.Audio;
using SoundPad.App.Controls;
using SoundPad.App.Models;
using SoundPad.App.Services;

namespace SoundPad.App;

public partial class MainWindow
{
    private SoundEditorWorkspace? _soundEditor;
    private bool _closeAfterEditorSave;

    private SoundEditorWorkspace EnsureSoundEditor()
    {
        if (_soundEditor is null)
        {
            _soundEditor = new SoundEditorWorkspace(_cachedSounds, _library, () => _monitorEngine)
            {
                CaptureSaveTarget = CaptureEditorSaveTarget
            };
            _soundEditor.SaveFinished += () =>
            {
                if (_closeAfterEditorSave && !_soundEditor.IsSaving)
                {
                    _closeAfterEditorSave = false;
                    Dispatcher.BeginInvoke(new Action(Close));
                }
            };
            SoundEditorHost.Content = _soundEditor;
        }
        else _soundEditor.UpdateSources(_cachedSounds, _library);
        return _soundEditor;
    }

    private Action<SoundItem, CachedSound> CaptureEditorSaveTarget()
    {
        // Remember the destination when Save is clicked, even if the user
        // switches decks while rendering is in progress.
        var deck = _activeDeck;
        return (item, sound) =>
        {
            if (!_decks.Contains(deck)) throw new InvalidOperationException("The destination deck was removed. Choose a deck and save again.");
            deck.Sounds.Add(item);
            try { DeckService.Save(_decks); }
            catch { deck.Sounds.Remove(item); throw; }
            if (deck == _activeDeck)
            {
                _cachedSounds[item.Id] = sound;
                RefreshCategoryFilter();
                FilterSoundsPanel();
                ActiveDeckChanged?.Invoke(deck);
                _soundEditor?.UpdateSources(_cachedSounds, _library);
            }
            StatusText.Text = $"Saved mix {item.DisplayName} to {deck.Name}.";
        };
    }

    private void OpenSoundComposer(SoundItem item)
    {
        if (!_cachedSounds.ContainsKey(item.Id))
        {
            StatusText.Text = "This sound could not be loaded into the editor.";
            return;
        }
        var editor = EnsureSoundEditor();
        MainTabControl.SelectedItem = SoundEditorTab;
        StopRecordingPreview();
        editor.AddSource(item);
        editor.Focus();
    }
}
