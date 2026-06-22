# SoundPad v1.1.0 — Release Checklist

Work through every item before publishing the GitHub Release.  
Check off each item as you verify it.

---

## Build verification

- [ ] `dotnet build SoundPad.App/SoundPad.App.csproj -c Release` → **0 errors / 0 warnings**
- [ ] `.\scripts\publish-release.ps1` completes without errors
- [ ] `artifacts\publish\SoundPad.App.exe` exists after publish
- [ ] `.\scripts\build-installer.ps1` completes without errors (requires Inno Setup)
- [ ] `artifacts\installer\SoundPad-Setup-1.1.0.exe` exists after installer build

---

## Debug build smoke test

Run `dotnet run --project SoundPad.App` and verify:

- [ ] App window opens cleanly (no crash, no startup error in status bar)
- [ ] Sound Library tab loads with sample sounds
- [ ] Status bar shows "Library: N sound(s) loaded"

---

## Published exe test

Run `artifacts\publish\SoundPad.App.exe` directly (not via dotnet run):

- [ ] App opens without any installer
- [ ] All UI tabs are visible and functional
- [ ] Sounds play correctly

---

## Installer test

- [ ] Run `SoundPad-Setup-1.1.0.exe` — no UAC prompt (per-user install)
- [ ] Installer wizard shows correct app name, version (1.1.0), and publisher
- [ ] App icon appears on installer wizard pages
- [ ] Installation completes to `%LocalAppData%\Programs\SoundPad`
- [ ] Start Menu shortcut created and launches the app
- [ ] Desktop shortcut created if the user ticked the option
- [ ] App launches after installer finishes

---

## Feature tests — v1.0.0 regression (run on installed version)

### Sound library
- [ ] Add a sound (browse to an MP3/WAV) → appears in library
- [ ] Edit a sound name and category → changes persist after restart
- [ ] Remove confirmation dialog shows the sound name and "Remove" button
- [ ] Remove a sound → removed from library, audio file not deleted

### Search and filter
- [ ] Type in the search box → library filters by name
- [ ] Change category filter → library filters by category

### Playback
- [ ] Click Play on a sound → plays through Monitor Output
- [ ] Per-sound volume slider → audible difference at different levels
- [ ] Stop All button → stops all currently playing sounds

### Hotkeys
- [ ] Press Ctrl+Alt+1 immediately after fresh app start → sound plays (no dialog needed)
- [ ] Press Ctrl+Alt+2, Ctrl+Alt+3, Ctrl+Alt+4 → correct sounds play
- [ ] Assign a new hotkey combo to a sound → works immediately after Save
- [ ] Clear a hotkey → combo no longer triggers that sound
- [ ] Stop All hotkey (if assigned) → stops all sounds globally
- [ ] Hotkeys fire when app is in background (another window has focus)

### Discord / VB-CABLE routing
- [ ] Set Virtual Output to CABLE Input
- [ ] Play a sound → heard in Discord (or a second audio recording device)
- [ ] Monitor Output still works simultaneously

### Microphone passthrough
- [ ] Enable Mic Passthrough → mic audio routes into Virtual Output
- [ ] Mic Volume slider adjusts mic level in the mix
- [ ] Disable Mic Passthrough → mic audio stops routing
- [ ] Passthrough survives app restart (setting persisted)

### Tray behaviour
- [ ] Minimize to Tray on → minimise hides window, tray icon visible
- [ ] Hotkeys still work while hidden in tray
- [ ] Tray icon right-click → Show / Stop All / Exit all work
- [ ] Tray icon double-click → window shown
- [ ] Close to Tray on → ✕ hides window instead of exiting
- [ ] Exit from tray → app fully exits, tray icon removed

### Start with Windows
- [ ] Toggle on → `HKCU\...\Run\SoundPad` key created
- [ ] Toggle off → key removed
- [ ] Log out and back in → SoundPad starts automatically (if toggled on)

### Settings persistence
- [ ] Set devices, mic volume, and hotkeys → restart → all settings restored
- [ ] Window position restored after restart

---

## Feature tests — v1.1.0 new features

### Favorites
- [ ] Click the star on a sound → star fills / row marked as favourite
- [ ] Select "Favorites" filter → only starred sounds shown
- [ ] Unfavourite a sound → disappears from Favorites filter
- [ ] Favourite state persists after restart

### Recent sounds
- [ ] Play a sound → it appears under "Recent" filter
- [ ] Recent filter orders sounds by most recently played (latest first)
- [ ] Sounds not played in the last 7 days do not appear in Recent
- [ ] Empty Recent state shows a helpful message

### Drag-and-drop import
- [ ] Drag an MP3 from File Explorer onto the Sound Library panel → added to library
- [ ] Drag multiple audio files at once → all added
- [ ] Drag a non-audio file → nothing added, no crash
- [ ] Dropped sounds play correctly through Monitor Output

### Library backup — Export
- [ ] Settings tab → Export Backup → save dialog opens
- [ ] ZIP is created at the chosen path
- [ ] ZIP contains `sounds.json` and a `Sounds/` folder with audio files

### Library backup — Import
- [ ] Settings tab → Import Backup → file picker opens
- [ ] Importing a valid ZIP adds new sounds to the library
- [ ] Duplicate sounds (same ID) are skipped with a count in the status bar
- [ ] Hotkeys that conflict with existing ones are cleared on import
- [ ] Imported sounds play correctly

### Active sound controls
- [ ] Start playing a sound → its row highlights with accent colour
- [ ] Play button on that row changes to a Stop button
- [ ] Click the row's Stop button → only that sound stops; others continue
- [ ] Sound finishes naturally → row reverts to default colour and Play button
- [ ] Stop All → all rows revert to default colour and Play button

### Playback mode — Mix (default)
- [ ] Play two sounds rapidly → both play simultaneously
- [ ] Stop All stops both

### Playback mode — Interrupt
- [ ] Settings → Behavior → enable Interrupt Previous Sound
- [ ] Play a sound, then play another → first sound stops, second plays
- [ ] Interrupt setting persists after restart

### Audio Performance Presets
- [ ] Default preset on fresh install is **Balanced**
- [ ] Select **Stable** → restart → Stable is still selected (not reset to Balanced)
- [ ] Select **Low Latency** → restart → Low Latency is still selected
- [ ] Changing preset stops any playing sounds and recreates engines
- [ ] Mic passthrough is automatically restarted after preset change (if it was active)
- [ ] Rollback: if engine creation fails, previous preset is restored

### Discord / Game Routing Wizard
- [ ] Wizard status dots show correct green/amber state for current device selection
- [ ] No Virtual Output selected → "No virtual cable" warning banner visible
- [ ] Virtual = Monitor → conflict warning visible
- [ ] VB-CABLE present → "Use Recommended Setup" button visible; clicking it selects the cable
- [ ] Test Virtual Output button plays a tone to the virtual device for ~1.5 seconds
- [ ] Test tone stops when Stop All is pressed
- [ ] Running the test tone again replaces the previous tone (no overlap)
- [ ] Wizard updates when device selection changes

### Update checking — Manual
- [ ] Settings → Check for Updates → status bar shows result within a few seconds
- [ ] With no internet → status bar shows "Could not check for updates"

### Update checking — Automatic startup check
- [ ] Auto-check is **off** by default → no network request on startup
- [ ] Enable auto-check → on next launch (24 h elapsed) a check runs silently
- [ ] Auto-check result updates the status bar only if an update is available
- [ ] LastUpdateCheckUtc is saved so the check does not repeat within 24 h

### Startup settings restoration
- [ ] All behavior toggles (Interrupt, Auto-Update, Minimize to Tray, Close to Tray) are restored correctly on startup with no spurious saves
- [ ] Audio Performance Preset is restored to the saved value on every launch

---

## Uninstall test

- [ ] Uninstall via Settings → Apps
- [ ] App files removed from `%LocalAppData%\Programs\SoundPad`
- [ ] Start Menu shortcut removed
- [ ] `%AppData%\SoundPad` user data folder **still exists** (intentionally preserved)
- [ ] No leftover entries in Settings → Apps after uninstall

---

## Git / GitHub release

- [ ] All changes committed on `main` with 0 modified files
- [ ] Create and push Git tag:  
  ```
  git tag v1.1.0
  git push origin v1.1.0
  ```
- [ ] Create GitHub Release from tag `v1.1.0`
- [ ] Add release notes summarising v1.1.0 features
- [ ] Upload `artifacts\installer\SoundPad-Setup-1.1.0.exe` as a release asset
- [ ] Verify the download link works and the installer runs cleanly

---

## Post-release

- [ ] Note in release notes that the app is unsigned and SmartScreen may warn
- [ ] (Future) Obtain EV code-signing certificate to eliminate SmartScreen prompt
- [ ] (Future) Ship a built-in SoundPad virtual audio driver to remove VB-CABLE dependency
