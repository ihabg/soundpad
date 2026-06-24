# SoundPad v1.9.0 — Release Checklist

Work through every item before publishing the GitHub Release.  
Check off each item as you verify it.

---

## Build verification

- [ ] `dotnet build SoundPad.App/SoundPad.App.csproj -c Release` → **0 errors / 0 warnings**
- [ ] `.\scripts\publish-release.ps1` completes without errors
- [ ] `artifacts\publish\SoundPad.App.exe` exists after publish
- [ ] `.\scripts\build-installer.ps1` completes without errors (requires Inno Setup)
- [ ] `artifacts\installer\SoundPad-Setup-1.9.0.exe` exists after installer build

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

- [ ] Run `SoundPad-Setup-1.9.0.exe` — no UAC prompt (per-user install)
- [ ] Installer wizard shows correct app name, version (1.9.0), and publisher
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

## Feature tests — v1.2.0 new features

### Sound Editor — waveform and timeline
- [ ] Open Edit Sound on any clip → waveform renders in the canvas (coloured bars fill the area)
- [ ] Drag the green Trim Start handle → handle moves, Trim Start field updates in real time
- [ ] Drag the orange-red Trim End handle → handle moves, Trim End field updates in real time
- [ ] Trim Start handle cannot be dragged past Trim End; Trim End cannot be dragged before Trim Start
- [ ] Type a value in Trim Start field → green handle moves to the new position on the canvas
- [ ] Type a value in Trim End field → orange-red handle moves to the new position on the canvas
- [ ] Click on the waveform canvas (not on a handle) → white dashed playhead moves to that position
- [ ] Click Play Preview → audio plays from the playhead position (or Trim Start if playhead is outside trim range)
- [ ] Playhead line animates left-to-right during preview and stops exactly at Trim End
- [ ] Click Stop Preview while playing → audio stops; playhead stays at the stopped position
- [ ] Close the Edit Sound dialog while preview is playing → audio stops immediately, no hung audio
- [ ] Fade In = 0.5 s, start preview from Trim Start → audio ramps up from silence over 0.5 s
- [ ] Fade In = 0.5 s, start preview from mid-track playhead → no fade-in ramp (starts at full volume)
- [ ] Fade Out = 0.5 s → audio ramps to silence during the last 0.5 s of the trimmed clip
- [ ] Fade-in / fade-out also apply during main library playback (not just in-editor preview)
- [ ] Duration label shows the correct total duration of the sound

### Sound Editor — validation
- [ ] Trim Start > Trim End → Save blocked: "Trim Start must be less than Trim End."
- [ ] Trim Start set to a value greater than the sound duration (Trim End left empty) → Save blocked
- [ ] Trim End > sound duration → Save blocked: "Trim End cannot exceed the sound duration."
- [ ] Non-numeric or negative value in any field → Save blocked with the field-name error
- [ ] All fields empty → Save allowed (no trim, no fade — same as default)

### Sound Editor — persistence
- [ ] Save trim/fade values → restart app → Edit Sound shows the saved values
- [ ] Trimmed sound plays only the trimmed region (not the full file) after restart
- [ ] Faded sound applies fade-in/fade-out correctly after restart
- [ ] Sound with no trim/fade saved plays in full from start to end (no regression)

### Library backup — trim/fade/category data
- [ ] Export a library containing trimmed and faded sounds → ZIP created
- [ ] Import that ZIP on a clean library → sounds load with trim/fade/category intact
- [ ] Trim/fade values play correctly after import
- [ ] Import an older backup ZIP (without trim/fade fields in sounds.json) → no crash; sounds load with no trim/fade applied

### Category Manager
- [ ] Click **Categories** button in toolbar → Category Manager dialog opens
- [ ] Create "TestCat" → appears in list and is automatically selected
- [ ] Rename "TestCat" to "Sound FX" → renamed in list and still selected
- [ ] Assign a sound to "Sound FX" via Edit Sound → sound appears under "Sound FX" filter in main window
- [ ] Delete "Sound FX" (with sounds) → move-to dropdown appears; choose another category → Done → sounds now appear under chosen category
- [ ] Delete an empty category → no move-to prompt; category removed immediately
- [ ] Chained delete in one session (delete A → move to B, then delete B → move to C) → Done → sounds originally in A land in C, not in a ghost category B
- [ ] All, Favorites, Recent do not appear as rename or delete targets
- [ ] Category list refreshes correctly in the main window filter after closing the dialog

### Right-click context menu
- [ ] Right-click a sound row → context menu shows: Edit, Favourite / Unfavourite, Duplicate, Reveal in Folder, Remove
- [ ] **Edit** → Sound Editor dialog opens for that sound
- [ ] **Favourite** → star fills on the row; next right-click shows "Unfavourite"
- [ ] **Unfavourite** → star clears; next right-click shows "Favourite"
- [ ] **Duplicate** → new sound appears at the bottom of the library with the same name, same file, same trim/fade/volume, and **no hotkey assigned**
- [ ] **Reveal in Folder** → File Explorer opens with the audio file highlighted
- [ ] **Reveal in Folder** on a sound whose file has been manually deleted → no crash, nothing opens
- [ ] **Remove** → confirmation dialog shows the sound name; confirm → sound removed; audio file on disk not deleted

---

## Feature tests — v1.3.0 new features

### Update check — already on latest

- [ ] Settings → Check for Updates (while running v1.9.0) → status bar shows "SoundPad is up to date."
- [ ] No update panel appears when already on latest
- [ ] CheckUpdatesButton re-enables immediately after result

### Update available panel (simulate by temporarily lowering csproj version to 1.0.0, rebuild, run)

- [ ] Check for Updates → update panel appears below the Updates card
- [ ] Panel shows the version number (e.g. "Version v1.6.0 is available")
- [ ] Panel shows the release title when it differs from the tag
- [ ] Panel shows a truncated excerpt of release notes when the GitHub release body is non-empty
- [ ] Panel does not show release notes area when the release body is empty
- [ ] Download & Install button is visible when the release has a matching `SoundPad-Setup-*.exe` asset
- [ ] Open Release Page button is always visible in the panel
- [ ] Later button is always visible in the panel
- [ ] Auto-check on startup shows the same panel (notify only — no download starts automatically)

### Download & Install

- [ ] Click Download & Install → download progress bar appears and updates as bytes arrive
- [ ] Status text shows "Downloading… X.X MB / Y.Y MB" while download is in progress
- [ ] Download & Install button is disabled during the download
- [ ] Later button is disabled during the download
- [ ] Check for Updates button is disabled during the download
- [ ] Cancel button appears in the progress panel during the download
- [ ] File is downloaded to `%TEMP%\SoundPad\Updates\` (verify in File Explorer during download)
- [ ] App never writes to its own install directory during the download

### Cancel download

- [ ] Click Cancel during an active download → download stops, progress panel hides, all buttons re-enable
- [ ] No error dialog appears after a clean cancel
- [ ] Can start another download immediately after cancelling (retry works)
- [ ] Partially downloaded file in `%TEMP%\SoundPad\Updates\` is overwritten on next attempt (no conflict)

### Download complete — user clicks No

- [ ] Download completes → "Install now?" confirmation dialog appears
- [ ] Click No → dialog dismisses, progress panel hides, all buttons re-enable
- [ ] App remains open and functional
- [ ] Can click Download & Install again without restarting the app

### Download complete — user clicks Yes

- [ ] Download completes → "Install now?" dialog appears
- [ ] Click Yes → installer launches (SmartScreen / UAC if applicable)
- [ ] App closes after installer is successfully launched
- [ ] Installer proceeds independently; app is no longer running

### Installer launch failure

- [ ] If the downloaded installer cannot be launched (e.g. file renamed/deleted before clicking Yes):
  - Error dialog appears with the file path
  - App remains open
  - Download & Install, Later, and Check for Updates buttons all re-enable
  - Open Release Page button remains usable

### Open Release Page fallback

- [ ] Click Open Release Page from inside the update panel → correct GitHub release URL opens in browser
- [ ] Click Open Releases Page from the top-level button (before any check) → GitHub releases page opens
- [ ] Open Release Page works even when Download & Install is hidden (no-asset release)

### No installer asset in release

- [ ] When the GitHub release has no `SoundPad-Setup-*.exe` asset:
  - Download & Install button is hidden
  - A "No installer file found" message is shown
  - Open Release Page button remains visible and functional

### Auto-check — notify only, no auto-download

- [ ] With auto-check enabled and an update available, app starts → update panel appears
- [ ] No download begins automatically on startup
- [ ] Download only starts when the user explicitly clicks Download & Install
- [ ] Auto-check does not run again within the same 24-hour window

### Safety constraints

- [ ] App executable is never overwritten while running
- [ ] Downloads always land in `%TEMP%\SoundPad\Updates\` regardless of asset filename
- [ ] A malformed asset filename (e.g. containing path separators) does not escape the temp directory
- [ ] Closing the app during an active download shuts down cleanly (no hung process, no crash)

---

## Feature tests — v1.4.0 new features

### Migration from v1.3 and earlier

- [ ] Fresh install on a machine with an existing `sounds.json` (no `decks.json`) → app launches without error
- [ ] `decks.json` is created containing a single **General** deck with all sounds from `sounds.json`
- [ ] Original `sounds.json` is renamed to `sounds.json.v1.3.bak_<timestamp>` and kept in `%AppData%\SoundPad\`
- [ ] All sounds in the General deck play correctly after migration
- [ ] Settings and hotkeys survived the migration unchanged

### Deck bar UI

- [ ] Deck bar is visible above the Sound Library panel on the Sound Library tab
- [ ] Active deck chip is highlighted
- [ ] Clicking a different chip switches the active deck immediately
- [ ] Deck bar scrolls horizontally if there are many decks (chips do not wrap or get clipped)

### Create deck

- [ ] Click **+** in the deck bar → New Deck dialog opens with text field focused and text selected
- [ ] Type a name → Enter key confirms (OK button also works)
- [ ] Escape key cancels without creating a deck
- [ ] New deck chip appears at the end of the deck bar and becomes active
- [ ] New deck starts with an empty sound list
- [ ] Duplicate name → error message shown inline; dialog stays open

### Rename deck

- [ ] Right-click a deck chip → Rename → dialog opens pre-filled with current name, text selected
- [ ] Enter new name → deck chip updates immediately
- [ ] Renamed deck's sounds are unchanged
- [ ] Duplicate name → error shown inline; dialog stays open

### Duplicate deck

- [ ] Right-click a deck chip → Duplicate → new deck appears with "Copy of <name>"
- [ ] Duplicate contains the same sounds and categories as the original
- [ ] Duplicate has **no hotkeys** assigned (all cleared to avoid Win32 conflicts)
- [ ] Original deck is unchanged

### Delete deck

- [ ] Right-click a deck chip → Delete → confirmation dialog shows the deck name
- [ ] Confirm → deck removed; active deck switches to the adjacent remaining deck
- [ ] The last remaining deck has no Delete option (or Delete is greyed out / blocked)
- [ ] Cancelling the confirmation leaves the deck intact

### Active deck persistence across restarts

- [ ] Switch to a non-default deck → close and reopen app → same deck is active
- [ ] Sound library shows the correct sounds for the restored active deck

### Per-deck sound isolation

- [ ] Add a sound while on Deck A → sound does not appear in Deck B
- [ ] Remove a sound while on Deck A → sound remains in Deck B
- [ ] Drag-and-drop import adds sounds to the active deck only

### Per-deck categories

- [ ] Open Category Manager on Deck A → create "FX" category
- [ ] Switch to Deck B → "FX" does not appear in Deck B's category list
- [ ] Category Manager on Deck B → "FX" is not listed

### Per-deck hotkeys

- [ ] Assign Ctrl+Alt+1 to Sound X in Deck A
- [ ] Switch to Deck B → Ctrl+Alt+1 is unassigned (no sound plays or a different sound plays)
- [ ] Assign Ctrl+Alt+1 to Sound Y in Deck B → no conflict error
- [ ] Switch back to Deck A → Ctrl+Alt+1 plays Sound X again
- [ ] Hotkeys re-register automatically when switching decks (no restart required)

### Stop All hotkey remains global

- [ ] Assign a Stop All hotkey
- [ ] Play a sound, switch to a different deck → Stop All hotkey still stops the sound

### Deck switch stops sounds but not mic passthrough

- [ ] Enable mic passthrough and play a sound
- [ ] Switch to a different deck → sound stops; mic passthrough continues uninterrupted

### Sound Editor in non-General decks

- [ ] Switch to a non-default deck, add a sound, open Sound Editor → trim/fade work correctly
- [ ] Saved trim/fade values persist after restart in the correct deck

### Favorites and Recent are per-deck

- [ ] Star a sound in Deck A → switch to Deck B → starred sound does not appear in Deck B's Favorites filter
- [ ] Play a sound in Deck A → switch to Deck B → sound does not appear in Deck B's Recent filter

### Backup export — deck data included

- [ ] Settings tab → Export Backup → ZIP is created
- [ ] ZIP contains `decks.json` with all decks and their metadata
- [ ] ZIP contains `sounds.json` (flat list across all decks, for backward compatibility)
- [ ] ZIP contains `Sounds/` folder with all audio files

### Import — old backup (sounds.json only)

- [ ] Import a v1.3 backup ZIP (contains only `sounds.json`, no `decks.json`)
- [ ] Sounds are added to the **currently active deck**
- [ ] Sounds already present in any deck (matched by ID) are skipped; count shown in status bar
- [ ] Conflicting hotkeys are cleared; cleared count shown in status bar
- [ ] Imported sounds play correctly in the active deck

### Import — new backup (decks.json)

- [ ] Import a v1.4 backup ZIP (contains `decks.json`)
- [ ] Decks from the backup are merged by name: sounds added to matching existing deck, or a new deck is created
- [ ] Sounds already present in any deck (matched by ID) are skipped
- [ ] Newly added deck chips appear in the deck bar after import

### Corrupt decks.json recovery

- [ ] Write garbage text into `%AppData%\SoundPad\decks.json`, restart app
- [ ] App launches without crash
- [ ] A timestamped backup of the corrupt file appears (e.g. `decks.json.bak_<timestamp>`)
- [ ] App falls back to migrating `sounds.json` (if present) or creating a fresh General deck

### Empty decks.json recovery

- [ ] Write `[]` into `decks.json`, restart app
- [ ] App launches without crash
- [ ] Timestamped backup created; app falls back to migration or fresh General deck

### Null Sounds / CustomCategories fields

- [ ] Hand-edit `decks.json`: set `"Sounds": null` on one deck, `"CustomCategories": null` on another
- [ ] App loads without NullReferenceException
- [ ] Both fields are treated as empty lists; app is fully functional

---

## Feature tests — v1.5.0 new features

### List / Grid view toggle

- [ ] Toolbar shows **List View** (highlighted) and **Grid View** buttons
- [ ] Click **Grid View** → button highlights; List View button de-highlights
- [ ] Click **List View** → button highlights; Grid View button de-highlights
- [ ] Toggle does not affect which deck is active or what sounds are loaded

### Grid View — layout

- [ ] Grid View renders pad cards in a horizontal wrap layout
- [ ] Each pad shows: sound name (center), category badge (bottom left), hotkey (top right, if set), favorite star (top left)
- [ ] Column header (NAME / CATEGORY / HOTKEY …) is hidden in Grid View
- [ ] Sounds area container has fully rounded corners and a visible border on all 4 sides in Grid View
- [ ] Switching back to List View restores the column header and the original area border style (square top, no top border line, connects to the header)

### View persistence

- [ ] Switch to Grid View → close and reopen app → Grid View is still active
- [ ] Switch to List View → close and reopen app → List View is still active
- [ ] Fresh install (no `settings.json`) defaults to List View

### Grid View — playback

- [ ] Click a pad → sound plays; pad background highlights with accent colour and ▶ indicator appears
- [ ] Click the same pad again while playing → sound stops; pad returns to its color/default background; ▶ indicator disappears
- [ ] Sound finishes naturally → pad deactivates automatically (background and indicator reset)

### Grid View — active state sync

- [ ] Press a hotkey for a sound visible in Grid View → correct pad highlights immediately
- [ ] Click **Stop All** → all active pads deactivate simultaneously; ▶ indicators disappear
- [ ] Switch from Grid to List while a sound is playing → List row for that sound highlights correctly
- [ ] Switch from List to Grid while a sound is playing → pad for that sound highlights correctly

### Grid View — search and filter

- [ ] Type in the search box while in Grid View → only matching pads shown; count badge updates
- [ ] Select a category filter while in Grid View → only pads in that category shown
- [ ] Select "Favorites" filter → only starred pads shown
- [ ] Select "Recent" filter → only recently played pads shown
- [ ] Empty result state shows a helpful message in both views

### Grid View — deck switching

- [ ] Switch to a different deck while in Grid View → grid refreshes to show new deck's sounds
- [ ] Any playing sounds stop when switching decks (in Grid View or List View)

### Grid View — right-click context menu

- [ ] Right-click a pad → context menu shows: Edit, Favourite/Unfavourite, Duplicate, Color, Reveal in Folder, Remove
- [ ] **Edit** → Sound Editor opens for that sound
- [ ] **Favourite** → star fills on the pad; filter badge updates; next right-click shows "Unfavourite"
- [ ] **Unfavourite** → star clears on the pad; next right-click shows "Favourite"
- [ ] **Duplicate** → new pad appears with same name, same color, no hotkey
- [ ] **Reveal in Folder** → File Explorer opens with audio file selected
- [ ] **Remove** → confirmation dialog; confirm → pad removed from grid

### Color menu — List View

- [ ] Right-click a sound row → Color submenu shows 9 options with color swatches
- [ ] Select **Red** → a 4 px red stripe appears on the left edge of that row immediately
- [ ] Select **Default** → stripe disappears; row returns to standard appearance
- [ ] Stripe remains visible while a sound is actively playing (accent background + stripe both visible)
- [ ] Stripe is not visible on rows that have no color set (PadColor = null)

### Color menu — Grid View

- [ ] Right-click a pad → Color submenu shows 9 options with color swatches
- [ ] Select **Blue** → pad background changes to blue immediately
- [ ] Select **Default** → pad returns to the standard card background
- [ ] While a sound is playing, the accent highlight overrides the pad color; ▶ indicator still shown; color restores when sound stops

### Color persistence

- [ ] Assign a color to a sound in List View → switch to Grid View → pad shows the same color
- [ ] Assign a color to a sound in Grid View → switch to List View → row shows the stripe
- [ ] Close and reopen the app → colors are preserved on both rows and pads
- [ ] Duplicate a colored sound → duplicate has the same color

### Backup — PadColor

- [ ] Export a backup containing sounds with colors → ZIP created
- [ ] Import that backup on a clean library → colors are preserved on the imported sounds
- [ ] Import an **old backup** (pre-v1.5, no `PadColor` fields) → import succeeds; sounds load with default appearance (no color, no crash)
- [ ] Import an old `sounds.json`-only backup → sounds load with default appearance

### File-drop import in Grid View

- [ ] While in Grid View, drag an audio file onto the sounds area → new pad appears immediately
- [ ] Dropped sound plays correctly

### Regression — no v1.4 regressions

- [ ] Sound Editor (Edit sound dialog) opens correctly in both views
- [ ] Deck create / rename / duplicate / delete all work correctly after adding colors
- [ ] Hotkeys registered on deck switch still fire correctly in Grid View
- [ ] Mic passthrough unaffected by view switch
- [ ] Tray icon and Stop All hotkey work while in Grid View
- [ ] In-app updater panel still appears and functions correctly

---

## Feature tests — v1.6.0 new features

### Drag-and-drop reorder — List View

- [ ] In List View with "All" selected and search box empty, drag a sound row by its non-interactive area → 2 px drop indicator line appears between rows as the cursor moves
- [ ] Drop the sound at a new position → order updates immediately in the panel
- [ ] Restart the app → sounds appear in the new order (order persisted to `decks.json`)
- [ ] Switch to Grid View after reorder → Grid View shows the same new order

### Drag-and-drop reorder — Grid View

- [ ] In Grid View with "All" selected and search box empty, drag a pad card → card-sized placeholder appears at the insert position
- [ ] Drop the pad at a new position → grid updates immediately
- [ ] Restart the app → pads appear in the new order
- [ ] Switch to List View after reorder → List View shows the same new order

### Reorder guards

- [ ] Change category filter to anything other than "All" → attempt to drag a row or pad → status bar shows "Reorder is only available in All view with no search filter."
- [ ] Type text in the search box → attempt to drag → same status bar message
- [ ] Drop indicator does not appear when reorder is blocked
- [ ] Clearing the filter / search box restores reorder capability without restart

### Reorder vs. interactive controls

- [ ] Click the **Play/Stop** button on a List View row → sound plays/stops; no drag initiated
- [ ] Click the **Volume slider** on a List View row → slider adjusts; no drag initiated
- [ ] Click the **Hotkey** button on a List View row → hotkey dialog opens; no drag initiated
- [ ] Click a pad card in Grid View without dragging → sound plays or stops; no reorder occurs
- [ ] Drag only starts after the cursor moves past the system drag threshold (not on a quick click-and-release)

### Reorder does not change sound properties

- [ ] Reorder sounds, then verify: hotkeys still fire on the reordered sounds
- [ ] Reorder sounds, then verify: pad colors remain correct after reorder
- [ ] Reorder sounds, then press **Stop All** → all active states clear correctly
- [ ] Reorder sounds, then switch decks → deck switch works without error; return to original deck shows the reordered order

### External file-drop regression

- [ ] While in List View, drag an audio file from File Explorer onto the sounds area → file imported and new row appears at the bottom
- [ ] While in Grid View, drag an audio file onto the sounds area → new pad appears at the bottom
- [ ] Drag a non-audio file onto the sounds area → nothing added, no crash
- [ ] During an external file drop, drop indicator (2 px line / placeholder) does not appear — only the accent border highlight shows

### Pad size — Small / Medium / Large

- [ ] Switch to Grid View → **Pad Size** combo box is visible in the toolbar
- [ ] Select **Small** → all pads resize to 120 × 100 px immediately
- [ ] Select **Medium** → all pads resize to 160 × 130 px immediately (this is the default)
- [ ] Select **Large** → all pads resize to 210 × 170 px immediately
- [ ] Pad Size combo is **not** visible in List View
- [ ] Close and reopen the app → pad size is restored to the last selected value
- [ ] Fresh install (no `settings.json`) → pad size defaults to Medium

### Compact Grid Mode

- [ ] Switch to Grid View → **Compact** button is visible in the toolbar
- [ ] Click **Compact** → category badges and favourite stars disappear; sound name font increases; Compact button highlights (Primary appearance)
- [ ] Click **Compact** again → category badges and favourite stars reappear; Compact button returns to Secondary appearance
- [ ] Compact button is **not** visible in List View
- [ ] Close and reopen the app → compact mode is restored to the last state
- [ ] A sound actively playing through a compact mode toggle continues playing and its pad re-highlights correctly after the rebuild

### Pad size and compact — interaction

- [ ] Toggle compact mode while Large pads are selected → layout is compact at Large size
- [ ] Change pad size while compact is on → pads resize; compact state is preserved
- [ ] Drop indicator placeholder matches the current pad size during a drag (not always Medium)

### Backup — order preserved

- [ ] Reorder sounds in the active deck, then export a backup ZIP
- [ ] Import that backup on a clean library → sounds appear in the same reordered sequence
- [ ] Old backup ZIPs (created before v1.6) import correctly; order follows `sounds.json` / `decks.json` as written

---

## Feature tests — v1.7.0 new features

### Mini Mode — open and basic display

- [ ] Click **Mini** button in the toolbar → Mini Mode floating window opens
- [ ] Mini window shows all pads from the currently active deck
- [ ] Mini pads appear in the same order as the main List and Grid views
- [ ] Mini pads respect PadColor — a sound with a color set in the main window shows that color in Mini Mode
- [ ] Mini window header shows the active deck name

### Mini Mode — playback sync

- [ ] Click a Mini pad → sound plays; that pad highlights with accent overlay and ▶ indicator
- [ ] Click the same Mini pad while playing → sound stops; pad returns to normal appearance
- [ ] Play a sound from the **main window** → the corresponding Mini pad highlights simultaneously
- [ ] Play a sound from **Mini Mode** → the corresponding row/card in the main window highlights simultaneously
- [ ] Press a **global hotkey** → Mini pad for that sound highlights correctly
- [ ] Sound finishes naturally → Mini pad deactivates automatically; main window row/card also clears

### Mini Mode — Stop All

- [ ] Click ⏹ Stop All in Mini Mode footer → all sounds stop; all Mini pads and all main-window rows/cards deactivate simultaneously
- [ ] Click Stop All in the **main window** → all Mini pads deactivate simultaneously
- [ ] Press the **Stop All global hotkey** → all Mini pads deactivate simultaneously

### Mini Mode — deck sync

- [ ] Switch decks in the main window → Mini Mode header updates to the new deck name and pads rebuild immediately
- [ ] Rename the **active deck** in the main window → Mini Mode header updates to the new name immediately
- [ ] Rename a **non-active deck** → Mini Mode is unaffected
- [ ] Delete the active deck → Mini Mode rebuilds with the replacement deck automatically (no crash)
- [ ] Switch to an **empty deck** → Mini Mode shows no pads and does not crash

### Mini Mode — window behavior

- [ ] Drag Mini window by its header → repositions freely anywhere on screen
- [ ] Click the **📌 pin button** → always-on-top toggles; pin button highlights (Primary) when active, Secondary when inactive
- [ ] Click the **✕ close button** → Mini window hides; the main app remains running
- [ ] Click **Mini** button again → Mini window reappears (was hidden, not destroyed)
- [ ] Resize Mini window → pads wrap to fill the new width; scrollbar appears if pads overflow vertically

### Mini Mode — settings persistence

- [ ] Open Mini, drag it to a custom position, close it, reopen → restores to the same position
- [ ] Toggle always-on-top on, close and reopen app → always-on-top is still on; pin button is highlighted
- [ ] Toggle always-on-top off, close and reopen app → always-on-top is still off
- [ ] Resize Mini, close and reopen app → window size is restored
- [ ] Manually set `"MiniWindowLeft": -3000` in `settings.json`, reopen Mini → window clamps back onto the visible screen (not off-screen)
- [ ] Fresh install (no `settings.json`) → Mini opens at a default WPF position; no crash

### Mini Mode — app exit

- [ ] Open Mini Mode, then exit the app via the main window ✕ → app exits cleanly; no hung process remains
- [ ] Open Mini Mode, then exit via the tray icon → app exits cleanly
- [ ] Mini window position and always-on-top state are saved before the app exits

### Mini Mode — tray interaction

- [ ] Enable **Minimize to Tray**; minimize the main window → Mini Mode remains visible on screen
- [ ] Enable **Close to Tray**; click ✕ on the main window → main window hides to tray; Mini Mode remains visible
- [ ] Double-click tray icon to restore main window → Mini Mode is unaffected

### Mini Mode — hotkeys

- [ ] Open Mini Mode so it has keyboard focus; press a sound hotkey → sound plays (hotkey fires globally)
- [ ] Open Mini Mode so it has keyboard focus; press the Stop All hotkey → all sounds stop

### Regression — v1.6 features unaffected by v1.7 changes

- [ ] Drag-and-drop reorder in List View still works correctly
- [ ] Drag-and-drop reorder in Grid View still works correctly
- [ ] External file-drop import (dragging audio files from File Explorer) still works in both views
- [ ] Grid pad size (Small / Medium / Large) still works and persists
- [ ] Compact Grid Mode still works and persists
- [ ] Pad colors still display correctly in both List and Grid views

---

## Feature tests — v1.8.0 new features

### Instant Replay — default state

- [ ] Fresh install (no `settings.json`) → Instant Replay is **OFF** by default
- [ ] Include microphone is **OFF** by default
- [ ] No audio capture occurs until Instant Replay is toggled ON

### Instant Replay — capture device

- [ ] Capture Device drop-down loads all available render endpoints
- [ ] Selecting a device and restarting the app → same device is still selected
- [ ] Leaving the device on "Default" captures from the Windows default playback endpoint

### Instant Replay — signal indicator

- [ ] Toggle Instant Replay ON with audio playing → signal indicator shows **Capturing audio**
- [ ] Toggle Instant Replay ON with no audio playing → indicator shows **No signal (waiting for audio)**
- [ ] Selecting a wrong/inactive capture device while audio plays → indicator shows no-signal + device-meter-active warning
- [ ] Indicator clears automatically when Instant Replay is turned OFF

### Instant Replay — settings locked while ON

- [ ] Capture Device combo is disabled while Instant Replay is ON
- [ ] Buffer Length combo is disabled while Instant Replay is ON
- [ ] Include microphone checkbox is disabled while Instant Replay is ON
- [ ] Microphone drop-down is disabled while Instant Replay is ON
- [ ] Locked-hint text is visible while Instant Replay is ON
- [ ] All controls re-enable when Instant Replay is turned OFF

### Instant Replay — buffer length

- [ ] Buffer Length combo offers 1, 2, 3, 4, 5 minutes
- [ ] Selected buffer length persists after restart
- [ ] Changing buffer length while OFF takes effect on next Enable

### Instant Replay — system-only clip (mic OFF)

- [ ] Enable Instant Replay, play audio for 10+ seconds, click Save Clip → clip appears in the active deck
- [ ] Clip file is a valid WAV at `%AppData%\SoundPad\Sounds\Clip YYYY-MM-DD HH-MM-SS.wav`
- [ ] Clip plays back with audible audio (not silence)
- [ ] Saved clip opens in the Sound Editor immediately after saving
- [ ] Clip name defaults to `Clip YYYY-MM-DD HH-MM-SS`
- [ ] Sound Editor allows rename, trim, fade, and hotkey assignment on the clip

### Instant Replay — clip in active deck

- [ ] Saved clip appears at the bottom of the current active deck in List View
- [ ] Saved clip appears as a pad in Grid View
- [ ] Saved clip plays correctly from the library
- [ ] Saved clip is included in the next backup export

### Instant Replay — Mini Mode refresh

- [ ] Open Mini Mode before saving a clip → after Save Clip, the new clip pad appears in Mini Mode without reopening it

### Instant Replay — wrong capture device (no signal)

- [ ] Select a capture device that is not producing audio
- [ ] Enable Instant Replay, wait, then Save Clip → status bar shows warning about no system audio signal alongside "Clip saved: ..."
- [ ] Clip file is still saved (silent WAV) — no crash

### Instant Replay — optional microphone

- [ ] Tick Include microphone, select a mic device, enable Instant Replay → status bar shows IR is ON
- [ ] Mic device drop-down lists all available WaveIn devices
- [ ] Selected mic device and Include state persist after restart
- [ ] Enable Instant Replay + mic, speak into mic, play audio, Save Clip → WAV contains both system audio and voice
- [ ] Clip plays back with both system audio and mic audio audible

### Instant Replay — mic OFF does not record microphone

- [ ] Include microphone is unchecked → Save Clip → clip contains only system audio (verify with audio editor if needed)
- [ ] Instant Replay OFF → no mic recording occurs regardless of Include microphone state

### Instant Replay — mic failure graceful handling

- [ ] Select a mic device that cannot be opened (e.g. disconnect it before enabling IR)
- [ ] Toggle Instant Replay ON → status bar shows "Instant Replay is ON — mic capture failed: ..." (system capture continues)
- [ ] Save Clip while mic failed → clip saves with system audio only; status bar shows "(Microphone had no signal; saved system audio only.)" appended to "Clip saved: ..."
- [ ] No crash; app remains fully functional

### Instant Replay — hotkeys

- [ ] Assign a Save Clip hotkey in Settings → hotkey fires and saves a clip while app is in background
- [ ] Assign a Toggle Instant Replay hotkey → hotkey turns IR ON and OFF
- [ ] Both hotkeys persist after restart
- [ ] Both hotkeys remain active after switching decks
- [ ] Both hotkeys fire while Mini Mode has keyboard focus

### Instant Replay — app exit while ON

- [ ] Enable Instant Replay (and optionally mic), then exit the app → app exits cleanly within a few seconds; no hung process

### Instant Replay — settings backward compatibility

- [ ] Manually delete all Instant Replay fields from `settings.json`, restart app → app loads without crash; IR defaults to OFF

### Regression — no v1.7 regressions introduced by v1.8

- [ ] Mini Mode opens, plays sounds, syncs with main window, and refreshes on deck switch
- [ ] Grid drag-and-drop reorder still works in both List and Grid views
- [ ] External file-drop import still works in both views
- [ ] Mic Passthrough is unaffected (can be enabled simultaneously with IR mic capture)
- [ ] Output routing (Monitor + Virtual) is unaffected
- [ ] Sound Editor opens for existing sounds (not just clips)
- [ ] In-app updater still functions

---

## Feature tests — v1.9.0 new features

### Export as MP3 — context menu presence

- [ ] Right-click any sound in **List View** → context menu shows "Export as MP3…" between "Reveal in Folder" and the separator above "Remove"
- [ ] Right-click any sound in **Grid View** → same menu item present at the same position
- [ ] Mini Mode pads have no context menu — no action needed there

### Export as MP3 — dialog behavior

- [ ] Click "Export as MP3…" → Save As dialog opens with the sound's display name as the default filename, `.mp3` extension, "MP3 Audio (*.mp3)|*.mp3" filter
- [ ] Click Cancel in the Save As dialog → nothing happens, status bar unchanged, no file created
- [ ] Choose a path and click Save → status bar shows "Exporting MP3…" briefly then "Exported MP3: filename.mp3"

### Export as MP3 — source file types

- [ ] Export an imported **MP3** file → valid MP3 produced (re-rendered, not a file copy)
- [ ] Export an imported **WAV** file → valid MP3 produced
- [ ] Export an Instant Replay **WAV clip** (saved from Instant Replay) → valid MP3 produced

### Export as MP3 — edits applied

- [ ] Export a sound with **TrimStart=2s, TrimEnd=5s** → MP3 is ~3 seconds; content matches the trimmed region
- [ ] Export a sound with **FadeIn=1s** → audible fade-in ramp at the start of the exported MP3
- [ ] Export a sound with **FadeOut=1s** → audible fade-out ramp at the end of the exported MP3
- [ ] Export a sound with **Volume=50%** → exported MP3 is noticeably quieter than a 100% export of the same sound
- [ ] Export a sound with all four edits applied simultaneously → all edits reflected in the MP3

### Export as MP3 — library integrity

- [ ] After export: sound still appears in the library with the same name, category, and settings
- [ ] After export: no duplicate sound added to the library
- [ ] After export: original audio file on disk is unchanged (verify via Reveal in Folder → check file date/size)

### Export as MP3 — duplicate export guard

- [ ] Trigger export on a sound; immediately right-click the same sound again → "Export as MP3…" is greyed out while export is in progress
- [ ] After the export finishes, right-click again → "Export as MP3…" is enabled again

### Export as MP3 — error handling

- [ ] Export to a **read-only directory** (e.g. `C:\Windows\`) → status bar shows "Export failed: …", no crash
- [ ] After a failed export: no partial file left at the chosen output path (temp file cleaned up)
- [ ] After a failed export: if a file already existed at the output path, it is still intact

### Export as MP3 — output file quality

- [ ] Open exported MP3 in an external media player (Windows Media Player, VLC, etc.) → file plays correctly
- [ ] Exported MP3 duration matches the trimmed length (or full length if no trim)
- [ ] Exported MP3 has no leading/trailing silence beyond what trim/fade settings produce

### Regression — no v1.8 regressions introduced by v1.9 changes

- [ ] Sound Editor (Edit Sound dialog) opens and saves trim/fade correctly
- [ ] Instant Replay clips can still be saved and played back
- [ ] Mini Mode opens, shows pads, and plays sounds correctly
- [ ] Grid drag-and-drop reorder still works
- [ ] External file-drop import still works in both views
- [ ] Hotkeys (sound, Stop All, Instant Replay) still fire correctly
- [ ] Mic passthrough is unaffected
- [ ] Monitor and Virtual output routing are unaffected
- [ ] In-app updater still functions

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
  git tag v1.9.0
  git push origin v1.9.0
  ```
- [ ] Create GitHub Release from tag `v1.9.0`
- [ ] Add release notes summarising v1.9.0 features
- [ ] Upload `artifacts\installer\SoundPad-Setup-1.9.0.exe` as a release asset
- [ ] Verify the download link works and the installer runs cleanly

---

## Post-release

- [ ] Note in release notes that the app is unsigned and SmartScreen may warn
- [ ] (Future) Obtain EV code-signing certificate to eliminate SmartScreen prompt
- [ ] (Future) Ship a built-in SoundPad virtual audio driver to remove VB-CABLE dependency
