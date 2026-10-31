using System.IO;
using NAudio.Wave;
using SoundPad.App.Audio;
using SoundPad.App.Models;
using SoundPad.App.Services;

int checks = 0;
if (args.Contains("--profile-library") || args.Contains("--legacy-profile-library"))
{
    MemoryChecks.ProfileLibrary(args.Contains("--legacy-profile-library"));
    return;
}
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
    Console.WriteLine($"PASS {description}");
}
void Equal(float[] actual, float[] expected, string description)
    => Check(actual.SequenceEqual(expected), description);

Equal(RecordingSamples.MixRecent(null, null), [], "Empty capture produces no recording");
Equal(RecordingSamples.MixRecent([.1f, .2f, .3f, .4f, .5f, .6f], null, 4), [.3f, .4f, .5f, .6f], "Clip length selects the newest stereo frames");
Equal(RecordingSamples.MixRecent([.1f, .2f, .3f, .4f], [.5f, .6f]), [.1f, .2f, .8f, 1f], "Late microphone starts align at the newest frame and mixing is clamped");
Equal(RecordingSamples.MixRecent(null, [.1f, .2f, .3f, .4f]), [.1f, .2f, .3f, .4f], "Microphone-only audio is retained when system audio is silent");
Check(RecordingSamples.MixRecent([1, 1, 1, 1], null, 3).Length == 2, "Clip cuts preserve complete stereo frames");
Equal(RecordingSamples.Peaks([-.2f, .8f, -.9f, .1f], 2), [.8f, .9f], "Waveform peaks reflect the captured samples");
MemoryChecks.Run(Check);
var manager = new FakeHotkeyManager();
var hotkeys = new HotkeyService(manager);
HotkeyBinding Binding(uint key) => new() { Modifiers = 6, Key = key, DisplayText = $"Ctrl+Alt+{key}" };
var firstSound = new SoundItem { Hotkey = Binding(49) };
var secondSound = new SoundItem { Hotkey = Binding(50) };
var stopBinding = Binding(83); var replayBinding = Binding(82); var replayToggle = Binding(84); var lockBinding = Binding(76);
int soundHits = 0, stopHits = 0, replayHits = 0, lockHits = 0;
hotkeys.HotkeyTriggered += _ => soundHits++;
hotkeys.StopAllHotkeyTriggered += () => stopHits++;
hotkeys.InstantReplayClipTriggered += () => replayHits++;
hotkeys.SoundboardLockTriggered += () => lockHits++;
hotkeys.ReregisterAll([firstSound], stopBinding, replayBinding, replayToggle, lockBinding);
int soundId = manager.Registered.Single(p => p.Value.Key == 49).Key;
int stopId = manager.Registered.Single(p => p.Value.Key == 83).Key;
int replayId = manager.Registered.Single(p => p.Value.Key == 82).Key;
manager.Raise(soundId); manager.Raise(stopId); manager.Raise(replayId);
Check(soundHits == 1 && stopHits == 1 && replayHits == 1 && manager.Registered.Count == 5, "Unlocked mode registers and dispatches normal SoundPad shortcuts");
int unregisterCount = manager.UnregisterCalls;
hotkeys.ReregisterAll([firstSound], stopBinding, replayBinding, replayToggle, lockBinding, locked: true);
Check(manager.UnregisterCalls > unregisterCount && manager.Registered.Count == 1 && manager.Registered.Single().Value.Key == 76,
    "Lock releases game-conflicting registrations instead of only muting their callbacks");
Check((manager.Registered.Single().Value.Modifiers & 0x4000) != 0, "Holding the lock shortcut cannot repeatedly toggle the state");
manager.Raise(soundId); manager.Raise(stopId); manager.Raise(replayId);
Check(soundHits == 1 && stopHits == 1 && replayHits == 1, "Already-queued sound, stop, and replay hotkeys are ignored while locked");
manager.Raise(manager.Registered.Single().Key);
Check(lockHits == 1, "The unlock shortcut still works while other shortcuts are released");
hotkeys.ReregisterAll([secondSound], stopBinding, replayBinding, replayToggle, lockBinding, locked: true);
Check(manager.Registered.Count == 1 && manager.Registered.Single().Value.Key == 76, "Changing decks cannot re-enable shortcuts while locked");
hotkeys.ReregisterAll([secondSound], stopBinding, replayBinding, replayToggle, lockBinding, locked: false);
Check(manager.Registered.Values.Any(v => v.Key == 50) && manager.Registered.Values.All(v => v.Key != 49), "Unlock restores the current deck rather than stale deck shortcuts");
manager.FailKey = 76;
var lockFailure = hotkeys.ReregisterAll([firstSound], stopBinding, replayBinding, replayToggle, lockBinding, locked: true);
Check(lockFailure.SoundboardLockFailed && manager.Registered.Count == 0, "Unavailable unlock shortcuts are reported without reclaiming game keys");
manager.FailKey = null;
hotkeys.ReregisterAll([firstSound], stopBinding, replayBinding, replayToggle, locked: true);
Check(manager.Registered.Count == 0, "Lock works without an assigned shortcut and can be controlled by the on-screen button");
var persistedLock = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(new AppSettings { SoundboardLocked = true, SoundboardLockHotkey = lockBinding }))!;
Check(persistedLock.SoundboardLocked && persistedLock.SoundboardLockHotkey?.Key == 76 && !new AppSettings().SoundboardLocked,
    "Lock state and shortcut persist, while existing installs default to unlocked");
if (args.Contains("--native-hotkeys"))
{
    Exception? nativeHotkeyError = null;
    var nativeThread = new Thread(() =>
    {
        var owner = new System.Windows.Window();
        var game = new System.Windows.Window();
        try
        {
            using var appManager = new SoundPad.App.Hotkeys.HotkeyManager(owner);
            using var gameManager = new SoundPad.App.Hotkeys.HotkeyManager(game);
            var nativeService = new HotkeyService(appManager);
            var soundKey = new HotkeyBinding { Modifiers = 7, Key = 0x87 }; // Ctrl+Alt+Shift+F24
            var toggleKey = new HotkeyBinding { Modifiers = 7, Key = 0x86 }; // Ctrl+Alt+Shift+F23
            var nativeSound = new SoundItem { Hotkey = soundKey };
            var active = nativeService.ReregisterAll([nativeSound], null, soundboardLockHotkey: toggleKey);
            Check(active.FailedSoundIds.Count == 0 && !active.SoundboardLockFailed, "Windows registers the sound and lock shortcuts");
            Check(!gameManager.Register(1, soundKey.Modifiers, soundKey.Key), "Windows confirms the active sound shortcut is reserved by SoundPad");
            nativeService.ReregisterAll([nativeSound], null, soundboardLockHotkey: toggleKey, locked: true);
            Check(gameManager.Register(1, soundKey.Modifiers, soundKey.Key), "A competing Windows app can claim the sound key after locking");
            Check(!gameManager.Register(2, toggleKey.Modifiers, toggleKey.Key), "Windows keeps only the unlock shortcut reserved while locked");
            gameManager.UnregisterAll();
            var restored = nativeService.ReregisterAll([nativeSound], null, soundboardLockHotkey: toggleKey);
            Check(restored.FailedSoundIds.Count == 0 && !gameManager.Register(1, soundKey.Modifiers, soundKey.Key), "Unlock reclaims the sound shortcut through the real Windows registration API");
        }
        catch (Exception ex) { nativeHotkeyError = ex; }
        finally { owner.Close(); game.Close(); }
    });
    nativeThread.SetApartmentState(ApartmentState.STA);
    nativeThread.Start();
    nativeThread.Join();
    if (nativeHotkeyError is not null) throw new Exception("Native hotkey integration checks failed", nativeHotkeyError);
}
float[] ring = [1, 2, 3, 4, 5, 6];
int position = 4;
long written = 4;
RecordingSamples.FillSilence(ring, ref position, ref written, 8);
Equal(ring, [0, 0, 3, 4, 0, 0], "Silent gaps clear the correct samples across a ring-buffer wrap");
Check(position == 2 && written == 8, "Silent capture advances the recording timeline");
RecordingSamples.FillSilence(ring, ref position, ref written, 20);
Check(ring.All(s => s == 0) && position == 2 && written == 20, "Long silence clears old audio instead of replaying stale samples");

string directory = Path.GetFullPath(Path.Combine("SoundPad.Tests", "bin", "TestData", "recordings-" + Guid.NewGuid()));
Directory.CreateDirectory(directory);
try
{
    string audio = Path.Combine(directory, "test.wav");
    using (var writer = new WaveFileWriter(audio, CachedSound.TargetFormat))
    {
        var samples = new float[48000 * 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)Math.Sin(i * .05) * .25f;
        writer.WriteSamples(samples, 0, samples.Length);
    }
    var renderMethod = typeof(SoundPad.App.MainWindow).GetMethod("RenderRecording", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var preview = (CachedSound)renderMethod.Invoke(null, [new SoundItem { FilePath = audio }])!;
    Check(preview.TotalSamples == 96000 && preview.BuildPeaks(100).Any(s => s > .1f), "Recording preview rendering preserves duration and audible samples");
    var source = new SoundItem { DisplayName = "Editor source", FilePath = audio };
    var mixTrack = new MixTrack
    {
        Label = "Voice track", Volume = .8f,
        Clips = [new MixClip { SoundItemId = source.Id, StartOffsetSeconds = .5, LocalTrimStartSeconds = .2, LocalTrimEndSeconds = .8, FadeInSeconds = .05 }]
    };
    string draftPath = Path.Combine(directory, "Editor", "draft.json");
    SoundEditorProjectService.Save(new SoundEditorProject { Name = "My editable mix", Tracks = [mixTrack], Sources = [source], Zoom = 125, PlayheadSeconds = .7 }, draftPath);
    var project = SoundEditorProjectService.Load(draftPath)!;
    Check(project.Name == "My editable mix" && project.Zoom == 125 && project.PlayheadSeconds == .7 && project.Sources[0].FilePath == audio,
        "Editor drafts retain the mix name, sources, zoom, and playhead across restart");
    var restoredClip = project.Tracks[0].Clips[0];
    Check(restoredClip.StartOffsetSeconds == .5 && restoredClip.LocalTrimStartSeconds == .2 && restoredClip.LocalTrimEndSeconds == .8 && restoredClip.FadeInSeconds == .05,
        "Editor drafts retain editable clip positions, trims, fades, and track volume");
    var mixed = MixRenderer.Render(project.Tracks, new Dictionary<Guid, SoundItem> { [source.Id] = source }, new Dictionary<Guid, CachedSound> { [source.Id] = preview });
    Check(mixed.Length == 105600 && mixed.Take(48000).All(s => s == 0) && mixed.Skip(48000).Any(s => Math.Abs(s) > .01f),
        "A restored multitrack project renders the expected trimmed audio at its timeline offset");
    SoundEditorProjectService.Save(new SoundEditorProject { Tracks = Enumerable.Range(0, 9).Select(_ => new MixTrack()).ToList() }, draftPath);
    try { SoundEditorProjectService.Load(draftPath); throw new Exception("Expected invalid track count to be rejected"); }
    catch (InvalidDataException) { checks++; Console.WriteLine("PASS Invalid editor projects are rejected before entering the workspace"); }
    var clip = new RecordingClip
    {
        Sound = new SoundItem { DisplayName = "Saved moment", FilePath = audio, Category = "Clips", Segments = [new AudioSegment(.2, .8)] },
        DurationSeconds = .6,
        Source = "Synthetic test audio", Peaks = [.1f, .5f, .2f]
    };
    RecordingLibraryService.Save([clip], directory);
    var loaded = RecordingLibraryService.Load(directory);
    Check(loaded.Count == 1 && loaded[0].Sound.DisplayName == "Saved moment" && loaded[0].DurationSeconds == .6, "Recording history survives a restart");
    Check(loaded[0].Sound.Segments?.Single().StartSeconds == .2 && loaded[0].Peaks.SequenceEqual(clip.Peaks), "Non-destructive trims and waveform metadata persist");
    loaded[0].Sound.DisplayName = "Renamed clip";
    RecordingLibraryService.Save(loaded, directory);
    Check(RecordingLibraryService.Load(directory)[0].Sound.DisplayName == "Renamed clip" && !File.Exists(Path.Combine(directory, "recordings.json.tmp")), "Atomic history replacement updates metadata");
    string deckCopy = Path.Combine(directory, "deck-copy.bin");
    File.Copy(audio, deckCopy);
    RecordingLibraryService.Delete(loaded, loaded[0], directory);
    Check(!File.Exists(audio) && RecordingLibraryService.Load(directory).Count == 0 && File.Exists(deckCopy), "Deleting a recording removes audio and history while preserving deck copies");
    File.Copy(deckCopy, audio);
    File.Delete(Path.Combine(directory, "recordings.json"));
    Directory.CreateDirectory(Path.Combine(directory, "recordings.json"));
    try
    {
        RecordingLibraryService.Delete([clip], clip, directory);
        throw new Exception("Deletion should fail when history cannot be saved");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    Check(File.Exists(audio), "Failed history updates restore the recording file");
    Directory.Delete(Path.Combine(directory, "recordings.json"));
    File.WriteAllText(Path.Combine(directory, "recordings.json"), "broken metadata");
    var recovered = RecordingLibraryService.Load(directory);
    Check(recovered.Count == 1 && Math.Abs(recovered[0].DurationSeconds - 1) < .001, "Original audio recovers after damaged metadata");
    Check(Directory.GetFiles(directory, "recordings.json.bak-*").Length == 1, "Damaged metadata is preserved for recovery");
    File.Delete(audio);
    RecordingLibraryService.Save([clip], directory);
    Check(RecordingLibraryService.Load(directory).Count == 0, "Missing audio does not leave unusable history entries");
}
finally { Directory.Delete(directory, recursive: true); }
Console.WriteLine($"{checks} audio and workspace checks passed.");

sealed class FakeHotkeyManager : SoundPad.App.Hotkeys.IHotkeyManager
{
    public event Action<int>? HotkeyPressed;
    public Dictionary<int, (uint Modifiers, uint Key)> Registered { get; } = new();
    public uint? FailKey { get; set; }
    public int UnregisterCalls { get; private set; }
    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        if (virtualKey == FailKey) return false;
        Registered[id] = (modifiers, virtualKey);
        return true;
    }
    public void UnregisterAll() { Registered.Clear(); UnregisterCalls++; }
    public void Raise(int id) => HotkeyPressed?.Invoke(id);
}
