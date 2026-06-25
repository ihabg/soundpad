# SoundPad v1.13.0 — Release Checklist

Work through every item before publishing the GitHub Release.  
Check off each item as you verify it.

---

## Build verification

- [ ] `dotnet build SoundPad.App/SoundPad.App.csproj -c Release` → **0 errors / 0 warnings**
- [ ] `.\scripts\publish-release.ps1` completes without errors
- [ ] `artifacts\publish\SoundPad.App.exe` exists after publish
- [ ] `.\scripts\build-installer.ps1` completes without errors (requires Inno Setup)
- [ ] `artifacts\installer\SoundPad-Setup-1.13.0.exe` exists after installer build

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

- [ ] Run `SoundPad-Setup-1.13.0.exe` — no UAC prompt (per-user install)
- [ ] Installer wizard shows correct app name, version (1.13.0), and publisher
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

- [ ] Settings → Check for Updates (while running v1.13.0) → status bar shows "SoundPad is up to date."
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

- [ ] Right-click a pad → context menu shows: Edit, Favourite/Unfavourite, Duplicate, Color…, Reveal in Folder, Remove
- [ ] **Edit** → Sound Editor opens for that sound
- [ ] **Favourite** → star fills on the pad; filter badge updates; next right-click shows "Unfavourite"
- [ ] **Unfavourite** → star clears on the pad; next right-click shows "Favourite"
- [ ] **Duplicate** → new pad appears with same name, same color, no hotkey
- [ ] **Reveal in Folder** → File Explorer opens with audio file selected
- [ ] **Remove** → confirmation dialog; confirm → pad removed from grid

### Color Picker dialog — List View

- [ ] Right-click a sound row → **Color…** opens the Color Picker dialog (no submenu)
- [ ] Select **Red** → Apply → a 4 px red stripe appears on the left edge of that row immediately
- [ ] Select **Default** → Apply → stripe disappears; row returns to standard appearance
- [ ] Stripe remains visible while a sound is actively playing (accent background + stripe both visible)
- [ ] Stripe is not visible on rows that have no color set (PadColor = null)

### Color Picker dialog — Grid View

- [ ] Right-click a pad → **Color…** opens the Color Picker dialog (no submenu)
- [ ] Select **Blue** → Apply → pad background changes to blue immediately
- [ ] Select **Default** → Apply → pad returns to the standard card background
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

- [ ] Change category filter to anything other than "All" → attempt to drag a row or pad → status bar shows "Reorder is only available in All view with no filters and Manual sort order."
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

## Feature tests — v1.10.0 new features

### Pro Sound Editor — opening

- [ ] Right-click any sound in List View → Edit → Sound Editor opens without crash
- [ ] Right-click an Instant Replay WAV clip → Edit → Sound Editor opens without crash
- [ ] Open a sound that has never been edited → timeline shows one block covering the full audio
- [ ] Open a sound that was previously trimmed (TrimStart/TrimEnd) → timeline shows one block with the correct boundaries

### Select tool

- [ ] Press **A** (or click Select button in toolbar) → Select tool is active (button highlighted)
- [ ] Click a block → block highlights with a blue selection outline
- [ ] Click the same block again → block deselects
- [ ] Click a different block → previous selection clears, new block selected
- [ ] Click an empty area between blocks → selection clears and playhead moves to that position

### Cut tool

- [ ] Press **C** (or click Cut button in toolbar) → Cut tool is active; cursor changes to crosshair
- [ ] Hover over the timeline → yellow dashed cut preview hairline follows the mouse
- [ ] Click inside a block → block splits into two adjacent blocks at the click position
- [ ] Blocks are now two — both visible in the timeline with a thin separator line
- [ ] Click inside one of the two blocks → three blocks total
- [ ] Cutting alone does not remove any audio — total edited duration is unchanged

### Remove Block

- [ ] Press **A**, click a block to select it
- [ ] Click **Remove Block** button → selected block is deleted; remaining blocks ripple together with no gap
- [ ] Press **Delete** while a block is selected → same behaviour as Remove Block button
- [ ] Right-click a block → block is removed immediately (no prior selection needed)
- [ ] Remove Block button is disabled when no block is selected or only one block remains
- [ ] Attempting to remove the last block is blocked (button disabled / no action)

### Playback after block editing

- [ ] Cut a block, remove one of the resulting pieces → play the sound from the main library → removed region is skipped; audio plays continuously across the cut point
- [ ] Export as MP3 on a sound with removed blocks → exported file skips the removed region seamlessly

### Block-edge trimming

- [ ] In Select tool, hover near the left edge of any block → cursor changes to resize arrow (SizeWE)
- [ ] Hover near the right edge of any block → cursor changes to resize arrow
- [ ] Drag left edge of a block → block shrinks/grows at its start; waveform updates live
- [ ] Drag right edge of a block → block shrinks/grows at its end; waveform updates live
- [ ] For first block: dragging left edge updates Trim Start text field in real time
- [ ] For last block: dragging right edge updates Trim End text field in real time
- [ ] Block cannot be trimmed below 20 ms duration (drag stops before zero)

### Undo

- [ ] Undo button is disabled on editor open (nothing to undo)
- [ ] Cut a block → Undo button enables
- [ ] Click Undo → cut is reversed; single block restored
- [ ] Press Ctrl+Z → same result as Undo button
- [ ] Remove a block → Undo → block restored in its original position
- [ ] Trim a block edge → release → Undo → edge reverts to pre-drag position
- [ ] Undo multiple times in sequence → each operation reverses in LIFO order

### Zoom slider

- [ ] Zoom slider visible in editor toolbar, default 1×
- [ ] Drag slider to 2× → timeline is twice as wide; horizontal scrollbar appears in the waveform border
- [ ] Drag slider to 10× → timeline scales to 10×; scrollbar allows full navigation
- [ ] Cut a block while zoomed → cut lands at the correct source-audio position (not misaligned)
- [ ] Trim a block edge while zoomed → trim lands at the correct source-audio position
- [ ] Drag back to 1× → timeline returns to fit the viewport; no scrollbar

### Draggable playhead arrow

- [ ] White dashed playhead line visible on timeline
- [ ] White triangle handle visible at the bottom of the playhead line
- [ ] Click the timeline → playhead jumps to that position
- [ ] Hover over the triangle handle → cursor changes to Hand
- [ ] Drag the triangle handle left/right → playhead line moves in real time
- [ ] Release → playhead stays at the new position
- [ ] Click Play Preview after dragging → preview starts from the dragged position
- [ ] Drag playhead while zoomed → position remains correct on the visual and audio timeline

### Snap cut to playhead

- [ ] "Snap cut to playhead" checkbox is visible and checked by default (ON)
- [ ] With Snap ON, Cut tool active: drag playhead to a precise position; move mouse close to the playhead line (within ~10 px) → cut preview hairline snaps to playhead position and turns bright yellow
- [ ] Click while snapped → block splits exactly at playhead position (not just approximately)
- [ ] Move mouse far from playhead → hairline returns to mouse position (normal semi-transparent yellow); click cuts at mouse
- [ ] Snap does not fire if the playhead is in a different block than the mouse
- [ ] Uncheck "Snap cut to playhead" (Snap OFF) → clicking near the playhead cuts at the exact mouse position; no snapping occurs

### Fade In / Fade Out

- [ ] Set Fade In = 1s → Play Preview → audio ramps up from silence over the first second of the first block
- [ ] Set Fade Out = 1s → Play Preview → audio ramps to silence over the last second of the last block
- [ ] Fades apply to the joined multi-block output (not per-block)
- [ ] Fade In / Fade Out survive Save → reopen → still applied during playback

### Save and persistence

- [ ] Click Save after cutting and removing blocks → dialog closes
- [ ] Reopen Edit for the same sound → timeline shows the saved blocks
- [ ] Play the sound from the main library → plays only the kept blocks (removed regions skipped)
- [ ] Export as MP3 on the edited sound → exported audio contains only kept blocks

### Backup / import round-trip

- [ ] Edit a sound (cut + remove blocks), save, then export a backup ZIP
- [ ] Import that backup on a clean library → sound loads with the same block structure
- [ ] Blocks play correctly after import

### Regression: old sounds unaffected

- [ ] Open the Sound Editor on a sound that has never had blocks edited → works exactly as before (single block spanning full trim range)
- [ ] Edit Trim Start / Trim End via text fields on an un-blocked sound → trims correctly
- [ ] Old sounds play without any change to their audio output after upgrading to v1.10.1

### Regression: v1.9 and earlier features unaffected

- [ ] Instant Replay clips can still be saved and played back
- [ ] Export as MP3 still works for sounds with no block edits
- [ ] Mini Mode opens, shows pads, and plays sounds
- [ ] Grid drag-and-drop reorder still works
- [ ] External file-drop import still works
- [ ] Hotkeys (sound, Stop All, Instant Replay) still fire correctly
- [ ] Mic passthrough is unaffected
- [ ] Monitor and Virtual output routing are unaffected
- [ ] In-app updater still functions

---

## Feature tests — v1.11.0 new features

### Pro Sound Editor — time ruler

- [ ] Open the Sound Editor on any sound → time ruler is visible above the waveform canvas
- [ ] At 1× zoom, ruler tick marks and timestamps align with the waveform timeline
- [ ] Zoom to 5× → ruler tick spacing adjusts; timestamps match the zoomed positions
- [ ] Zoom to 10× → ruler is still readable and correctly aligned
- [ ] Scroll the waveform horizontally while zoomed → ruler scrolls in sync with the waveform

### Pro Sound Editor — zoom / scroll (Phase 2 improvements)

- [ ] Zoom to any level → playhead remains visible in the viewport (timeline scrolls to keep it in view)
- [ ] Scroll and cut a block at a non-zero scroll offset → cut lands at the correct source-audio position

### Pro Sound Editor — redo

- [ ] Perform an edit (cut, remove, trim, paste, reorder) → Undo reverses it
- [ ] After undoing, click the **Redo** button → edit is re-applied
- [ ] After undoing, press **Ctrl+Y** → edit is re-applied
- [ ] After undoing, press **Ctrl+Shift+Z** → edit is re-applied
- [ ] Redo button is disabled when there is nothing to redo
- [ ] Performing a new edit after undoing clears the redo stack (Redo button disables)

### Pro Sound Editor — Copy / Paste block

- [ ] Select a block → press **Ctrl+C** → no visible change (block is copied internally)
- [ ] Press **Ctrl+V** → a new block is appended to the end of the timeline
- [ ] Pasted block has the same source audio boundaries as the copied block
- [ ] Play from the library → pasted block plays in sequence after the other blocks
- [ ] Export as MP3 → pasted block is included in the exported audio at the correct position
- [ ] Undo → pasted block is removed; Redo → pasted block is restored

### Pro Sound Editor — block drag / reorder

- [ ] In Select mode, drag a block to a new position → visual placeholder shows the drop target
- [ ] Release → timeline updates to the new block order
- [ ] Play from the library → audio plays in the reordered block sequence
- [ ] Export as MP3 → exported audio follows the reordered block order
- [ ] Undo → reorder is reversed; Redo → reorder is re-applied
- [ ] Drag a block to the first position → it becomes the first block played and exported
- [ ] Drag a block to the last position → it becomes the last block played and exported

### Pro Sound Editor — selected block info

- [ ] No block selected → block info area is empty or shows a placeholder
- [ ] Select a block → toolbar shows the block's start time, end time, and duration
- [ ] Trim a block edge → info updates in real time to reflect the new boundaries
- [ ] Reorder blocks → selected block info updates to reflect the new positions
- [ ] Deselect the block → info clears

### Pro Sound Editor — Spacebar preview play/pause

- [ ] With the Sound Editor open and no text field focused, press **Spacebar** → preview plays from the playhead position
- [ ] Press **Spacebar** again while playing → preview pauses
- [ ] Press **Spacebar** again while paused → preview resumes from the paused position
- [ ] Click **Stop Preview** → stops preview; next Spacebar starts from the playhead position
- [ ] Type in the **Trim Start** or **Trim End** field → Spacebar does not trigger play/pause while focused

### Pro Sound Editor — keyboard shortcuts do not trigger while typing

- [ ] Click in the **Trim Start** field, type a value → pressing **A** or **C** does not switch tools; pressing **Delete** does not remove a block
- [ ] Click in the **Trim End** field → same behavior
- [ ] Ctrl+Z while a text field is focused → does not trigger block-level Undo (or only undoes text input within the field)
- [ ] Spacebar while a text field is focused → does not trigger play/pause

### Instant Replay clips — still open / edit / export

- [ ] Save an Instant Replay clip → it appears in the active deck
- [ ] Right-click the clip → Edit → Pro Sound Editor opens without crash
- [ ] Cut and remove a block on the IR clip → Save → clip plays correctly with the edit applied
- [ ] Export the IR clip as MP3 → exported audio reflects the block edits

### Sound colors — Color Picker dialog (submenu replaced in v1.12.0)

- [ ] Right-click a sound → **Color…** opens the Color Picker dialog (no submenu)
- [ ] Select a preset (e.g. Red) → Apply → row stripe (List View) or pad background (Grid View) updates immediately
- [ ] Select Default → Apply → color is cleared; row/pad returns to standard appearance
- [ ] Colors persist after app restart

### Regression — v1.11.0

- [ ] Export as MP3 still works for sounds with and without block edits
- [ ] Pro Sound Editor Phase 1 features (Cut, Remove Block, trim edges, Undo, zoom, playhead, snap) still work
- [ ] Mini Mode opens, shows pads, and plays sounds correctly
- [ ] Instant Replay save, playback, and hotkeys still work
- [ ] Hotkeys (sound, Stop All, Instant Replay) still fire correctly
- [ ] Mic passthrough is unaffected
- [ ] Monitor and Virtual output routing are unaffected
- [ ] In-app updater still functions

---

## Feature tests — v1.12.0 new features

### Color Picker dialog — opening

- [ ] Right-click any sound in **List View** → context menu shows **Color…** (single item, no submenu)
- [ ] Right-click any pad in **Grid View** → context menu shows **Color…** (single item, no submenu)
- [ ] Click **Color…** → Color Picker dialog opens (420 px wide, all controls visible including Cancel and Apply buttons)
- [ ] Dialog title bar shows "Sound Color" with a color icon
- [ ] Dialog opens centered over the main window

### Color Picker dialog — initial state

- [ ] Open **Color…** on a sound with no color → Default preset is highlighted with an accent ring; HEX box is empty; Apply is enabled
- [ ] Open **Color…** on a sound with a preset color (e.g. Red `#E53935`) → Red preset is highlighted; HEX box shows `#E53935`; RGB sliders and boxes show the correct values; Apply is enabled
- [ ] Open **Color…** on a sound with a custom color (e.g. `#ABCDEF`) → no preset highlighted; HEX box shows `#ABCDEF`; RGB sliders and boxes reflect the color; Apply is enabled

### Color Picker dialog — preset selection

- [ ] Click **Red** → ring highlights Red; HEX box shows `#E53935`; RGB boxes show 229, 57, 53; preview swatch turns red; Apply enabled
- [ ] Click **Blue** → ring moves to Blue; HEX box shows `#039BE5`; preview swatch turns blue
- [ ] Click **Default** → ring highlights Default; HEX box clears; RGB boxes clear; preview swatch shows the card default background; Apply enabled
- [ ] Click a preset when another preset is already selected → ring moves to the new preset; old ring disappears

### Color Picker dialog — HEX input

- [ ] Type `#FFAA00` in the HEX box → RGB boxes update to 255, 170, 0; sliders move; preview swatch turns amber; Apply enabled
- [ ] Type `FFAA00` (no `#`) → treated as valid; dialog normalizes and accepts the color
- [ ] Type a partial value like `#FFA` → Apply disabled; error "Enter a valid 6-digit HEX color" visible
- [ ] Type `#GGGGGG` (invalid hex chars) → Apply disabled; error visible
- [ ] Clear the HEX box entirely (with no preset active) → Apply disabled; no error shown
- [ ] Type the exact HEX of a preset (e.g. `#039BE5`) → that preset's ring highlights automatically

### Color Picker dialog — RGB inputs

- [ ] Drag the Red slider → Red text box updates; HEX box updates; preview swatch updates; Apply enabled
- [ ] Type `255` in the Red box → Red slider moves to 255; HEX and swatch update
- [ ] Type `300` in any RGB box → Apply disabled; error "R, G, and B must each be a number from 0 to 255" visible
- [ ] After typing `300` then clearing the box (with Default still active) → error disappears; Apply re-enables
- [ ] Type valid values in all three RGB boxes → Apply enabled; error cleared
- [ ] RGB values that match a preset (e.g. R=3, G=155, B=229 → `#039BE5`) → Blue preset ring highlights automatically

### Color Picker dialog — Cancel and Escape

- [ ] Click **Cancel** → dialog closes; sound color unchanged; status bar unchanged
- [ ] Press **Escape** → same as Cancel
- [ ] Cancel on a sound that had Red → reopen → Red is still highlighted (no change was saved)

### Color Picker dialog — Apply

- [ ] Select **Red** → Apply → dialog closes; List View row shows 4 px red stripe immediately
- [ ] Select **Blue** on a Grid View pad → Apply → pad background turns blue immediately
- [ ] Select **Default** → Apply → color cleared; row/pad returns to standard appearance
- [ ] Enter custom HEX `#FFAA00` → Apply → custom color saved and visible
- [ ] Press **Enter** when Apply is enabled → same as clicking Apply

### Color persistence

- [ ] Apply a preset color → restart app → color still applied
- [ ] Apply a custom HEX color → restart app → custom color still applied
- [ ] Apply Default (clear) → restart app → color remains cleared

### Duplicate sound keeps color

- [ ] Assign color `#E53935` to a sound → right-click → Duplicate → duplicate has the same red stripe/pad color
- [ ] Duplicate has no hotkey (existing behavior preserved)

### Backup / import preserves custom color

- [ ] Assign a custom HEX color to a sound → Settings → Export Backup → ZIP created
- [ ] Import that ZIP on a clean library → custom color is present on the imported sound
- [ ] Import an old backup (pre-v1.12, no custom colors) → import succeeds; sounds load with default appearance (no crash)

### Mini Mode shows custom colors

- [ ] Open Mini Mode → pads with assigned colors show those colors (preset and custom HEX)
- [ ] Assign a new color via Color Picker → Apply → Mini Mode pad reflects the new color immediately (after FilterSoundsPanel rebuild)

### Regression — other context actions unaffected

- [ ] **Edit** → Sound Editor opens correctly for colored and uncolored sounds
- [ ] **Favourite / Unfavourite** → works correctly alongside colors
- [ ] **Duplicate** → creates copy; see duplicate test above
- [ ] **Reveal in Folder** → File Explorer opens with file selected
- [ ] **Export as MP3…** → export works for colored sounds; color has no effect on audio output
- [ ] **Remove** → confirmation dialog appears; confirm removes sound; color data is not left behind

### Regression — pro features unaffected by v1.12

- [ ] Pro Sound Editor opens, cuts, removes blocks, undoes/redoes correctly
- [ ] Instant Replay clips save, play, and open in the Sound Editor
- [ ] Sound hotkeys, Stop All hotkey, Instant Replay hotkeys all fire correctly
- [ ] Mic passthrough is unaffected
- [ ] Monitor and Virtual output routing are unaffected
- [ ] In-app updater still functions

---

## Feature tests — v1.13.0 new features

### Tags — Edit Sound dialog

- [ ] Right-click a sound → Edit → Tags field is visible between Category and Volume
- [ ] Tags field shows placeholder text "meme, game, voice"
- [ ] Enter `meme, voice, game` → Save → Edit the same sound again → Tags field pre-fills with the saved tags
- [ ] Tags are trimmed (` meme ` → `meme`) and deduplicated case-insensitively (`Meme, meme` → `meme`)
- [ ] Leave Tags empty → Save → Edit again → Tags field is empty (null stored, no crash)

### Tags — Duplicate

- [ ] Assign tags to a sound → right-click → Duplicate → duplicate has the same tags
- [ ] Edit the duplicate's tags → original sound's tags are unchanged

### Search

- [ ] Type a sound's name in the search box → only matching sounds shown
- [ ] Type a category name in the search box → only sounds in that category shown
- [ ] Type a tag in the search box → only sounds with that tag shown
- [ ] Clear button (×) inside the search box resets the filter immediately → all sounds return
- [ ] Search is case-insensitive
- [ ] Empty search box → all sounds shown (no filtering)

### Tag Filter

- [ ] No sounds have tags → Tag Filter combo box is hidden
- [ ] Add tags to a sound → Tag Filter combo box appears in the filter bar
- [ ] Tag Filter shows "Any Tag" plus each unique tag across all sounds in the deck
- [ ] Select a tag → only sounds with that tag are shown
- [ ] Select "Any Tag" → all sounds shown again
- [ ] Remove all tags from all sounds → Tag Filter combo box hides automatically

### Sort

- [ ] Sort box defaults to **Manual order** on a fresh install
- [ ] Sort box defaults to **Manual order** when loading old `settings.json` without `LibrarySortOrder`
- [ ] Select **Name A–Z** → sounds sorted alphabetically by name
- [ ] Select **Name Z–A** → sounds sorted reverse-alphabetically by name
- [ ] Select **Newest first** → sounds sorted by creation date, most recently added first
- [ ] Select **Oldest first** → sounds sorted by creation date, oldest first
- [ ] Select **Category** → sounds sorted alphabetically by category; within each category, sorted alphabetically by name
- [ ] Select **Favorites first** → starred sounds appear before unstarred; within each group, sorted alphabetically by name
- [ ] Close and reopen the app → sort selection is restored from `settings.json`

### Recent disables Sort

- [ ] Select **Recent** category filter → Sort box grays out (disabled)
- [ ] While Recent is active → sounds are ordered by most recently played (latest first)
- [ ] Select any other category filter → Sort box re-enables

### Drag reorder — extended guard

- [ ] Set sort to anything other than Manual → attempt to drag a row or pad → status bar shows "Reorder is only available in All view with no filters and Manual sort order."
- [ ] Set tag filter to anything other than Any Tag → attempt to drag → same status bar message
- [ ] Set category to All, search empty, tag Any Tag, sort Manual → drag reorder works normally

### Search / filter / sort in both views

- [ ] Switch to Grid View → search box, category filter, tag filter, and sort box all work identically
- [ ] Filter sounds in List View, switch to Grid View → same filtered result shown

### Backup — tags preserved

- [ ] Add tags to several sounds → Settings → Export Backup → ZIP created
- [ ] Import that ZIP on a clean library → tags are present on all imported sounds
- [ ] Import an old backup (pre-v1.13, no tags in JSON) → import succeeds; sounds load with no tags, no crash

### Regression — existing features unaffected by v1.13

- [ ] Play, stop, favorites, recent, categories, add, remove, duplicate, reveal, export MP3 all work
- [ ] Edit sound saves name, category, volume, trim, fade, segments correctly alongside tags
- [ ] Pro Sound Editor opens and saves correctly
- [ ] Instant Replay saves clips, clips appear in library, hotkeys work
- [ ] Mini Mode opens, shows pads, syncs playback state
- [ ] Hotkeys (sound, Stop All, Instant Replay) fire correctly
- [ ] Routing Wizard and device selection work
- [ ] Backup export/import works for all pre-existing fields
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
  git tag v1.13.0
  git push origin v1.13.0
  ```
- [ ] Create GitHub Release from tag `v1.13.0`
- [ ] Add release notes summarising v1.13.0 Search, Tags, and Better Library UX changes
- [ ] Upload `artifacts\installer\SoundPad-Setup-1.13.0.exe` as a release asset
- [ ] Verify the download link works and the installer runs cleanly

---

## Post-release

- [ ] Note in release notes that the app is unsigned and SmartScreen may warn
- [ ] (Future) Obtain EV code-signing certificate to eliminate SmartScreen prompt
- [ ] (Future) Ship a built-in SoundPad virtual audio driver to remove VB-CABLE dependency
