# SoundPad v1.0.0 — Release Checklist

Work through every item before publishing the GitHub Release.  
Check off each item as you verify it.

---

## Build verification

- [ ] `dotnet build SoundPad.App/SoundPad.App.csproj -c Release` → **0 errors / 0 warnings**
- [ ] `.\scripts\publish-release.ps1` completes without errors
- [ ] `artifacts\publish\SoundPad.App.exe` exists after publish
- [ ] `.\scripts\build-installer.ps1` completes without errors (requires Inno Setup)
- [ ] `artifacts\installer\SoundPad-Setup-1.0.0.exe` exists after installer build

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

- [ ] Run `SoundPad-Setup-1.0.0.exe` — no UAC prompt (per-user install)
- [ ] Installer wizard shows correct app name, version, and publisher
- [ ] App icon appears on installer wizard pages
- [ ] Installation completes to `%LocalAppData%\Programs\SoundPad`
- [ ] Start Menu shortcut created and launches the app
- [ ] Desktop shortcut created if the user ticked the option
- [ ] App launches after installer finishes

---

## Feature tests (run on the installed version)

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
  git tag v1.0.0
  git push origin v1.0.0
  ```
- [ ] Create GitHub Release from tag `v1.0.0`
- [ ] Add release notes summarising features
- [ ] Upload `artifacts\installer\SoundPad-Setup-1.0.0.exe` as a release asset
- [ ] Verify the download link works and the installer runs cleanly

---

## Post-release

- [ ] Note in README / release notes that the app is unsigned and SmartScreen may warn
- [ ] (Future) Obtain EV code-signing certificate to eliminate SmartScreen prompt
