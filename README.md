# SoundPad

A Windows soundboard app with a clean Fluent UI design.  
Play sounds to any output device, route them through Discord via VB-CABLE, and assign global hotkeys so your hands never leave the keyboard.

---

## Features

- **Sound library** — add any MP3/WAV/OGG/FLAC/AAC file, give it a display name, category, and tags; set per-sound volume; search by name, category, or tag; filter by tag; sort by Manual order, Name A–Z, Name Z–A, Newest, Oldest, Category, or Favorites first; Manual order enables drag-and-drop reorder
- **Profiles / Decks** — organize sounds into named decks; create, rename, duplicate, and delete decks; the active deck persists across restarts; sounds, categories, and hotkeys are all per-deck; switching decks stops active sound effects but does not interrupt mic passthrough; the Stop All hotkey remains global
- **Grid / Pad View** — toggle between List and Grid view using the toolbar buttons; List View shows the full editing table and is best for managing sounds; Grid View shows large clickable pads (name, category, hotkey, and favorite star) optimized for quick soundboard use during Discord sessions or gaming; active pads highlight and show a ▶ indicator; hotkey presses highlight the correct pad; Stop All clears all pad highlights; search and category filters work in Grid View; deck switching refreshes the grid; right-click any pad for the same actions as List View; selected view persists across restarts; drag pads or rows to reorder sounds within the active deck — order persists after restart and affects both views; Grid View pad size is configurable (Small / Medium / Large); Compact Grid Mode hides category badges and favorite stars for a denser layout
- **Mini Mode / Floating Soundboard** — click **Mini** in the toolbar to open a compact always-on-top floating window showing every pad in the active deck; play or stop sounds directly from Mini Mode; active state syncs bidirectionally with the main window — playing from either UI highlights the pad in both; Stop All works from either window; deck switching and active deck renames update Mini Mode automatically; the pin button toggles always-on-top; closing Mini Mode hides it without exiting the app; Mini Mode window size and position persist across restarts; hotkeys continue to work while Mini Mode has keyboard focus; designed for Discord and gaming use where the main window is hidden in the tray
- **Sound colors** — right-click any sound in List View or Grid View → **Color…** to open the Color Picker dialog; choose one of 9 preset colors (Red, Orange, Yellow, Green, Blue, Purple, Pink, Gray, or Default to clear), or enter a custom HEX code like `#FFAA00`, or adjust RGB sliders (0–255); a live preview swatch updates as you type; Apply saves, Cancel discards; in List View the color appears as a 4 px vertical accent stripe on the left edge of the row; in Grid View the color fills the pad background; colors are stored per-sound as `PadColor`; old decks, settings, and backups without `PadColor` load correctly with the default appearance
- **Favorites** — star any sound to pin it in the Favorites filter
- **Recent sounds** — filter to the last 7 days of played sounds, ordered by most recently played
- **Drag-and-drop import** — drag audio files directly onto the Sound Library panel to add them instantly
- **Library backup** — export your entire sound library (sounds + metadata) to a ZIP, and import it on another machine
- **Dual output** — Monitor Output (hear it yourself) and Virtual Output (send to Discord/VB-CABLE) run simultaneously
- **Active sound controls** — each playing sound shows a live Stop button on its row; the row highlights while active
- **Playback mode** — choose between **Mix** (all sounds play simultaneously) or **Interrupt** (each new sound stops the previous one)
- **Global hotkeys** — assign Ctrl+Alt+1…N (or any combo) to sounds; they fire even when the app is minimised or hidden
- **Stop All hotkey** — a single global key stops every playing sound instantly
- **Microphone passthrough** — your mic audio is mixed into the virtual output so Discord hears both you and the sounds
- **Discord / Game Routing Wizard** — step-by-step guide with live status dots and auto-detect for VB-CABLE / Voicemeeter
- **Audio Performance Presets** — choose between Stable (300 ms), Balanced (100 ms), and Low Latency (60 ms) buffer sizes
- **In-app updater** — when a new release is detected, a panel appears with release notes and a **Download & Install** button; the installer downloads to a safe temp path, verifies the file, asks for confirmation, and launches the installer — the app never overwrites its own running executable; auto-check only notifies and never downloads automatically
- **Tray mode** — minimise or close to the system tray; hotkeys keep working
- **Start with Windows** — one toggle to register SoundPad in the current-user Run key (no admin required)
- **Settings persistence** — devices, volume, window position, and all hotkeys survive restarts
- **Perceptual volume curve** — the volume slider feels natural (power-2 curve: 50 % UI = −12 dB)
- **Pro Sound Editor** — CapCut-style block timeline; Select (A) and Cut (C) tools; cut splits a block without removing audio; remove blocks to ripple remaining audio together; drag block edges to trim; drag blocks to reorder; Undo (Ctrl+Z) and Redo (Ctrl+Y / Ctrl+Shift+Z); Copy (Ctrl+C) and Paste (Ctrl+V) blocks; time ruler above waveform; selected block info; Spacebar preview play/pause; zoom slider (1×–10×); draggable playhead arrow; snap cut to playhead; Fade In / Fade Out applied to the joined output; non-destructive — original files never modified; segments saved in decks.json and backups; Instant Replay clips use the same editor
- **Audio Effects** — per-sound non-destructive effects applied at playback time: **Reverse** the audio, **Normalize** to peak volume, and **Playback Speed** (0.5×–2.0×, vinyl-style — pitch shifts with speed); effects stack with trim, fade, volume, and block segments; changes are saved per-sound in decks.json and preserved in backups; processed audio is cached after first render for instant replays; effects apply identically during library playback, in-editor preview, and Export as MP3; Reset Effects button restores all three to defaults; true pitch-shifting without speed change is planned for a future release
- **Category Manager** — create, rename, and delete custom sound categories; deleting a category with sounds prompts where to move them; chained operations resolve correctly
- **Sound row context menu** — right-click any sound row or pad card: Edit, Favourite/Unfavourite, Duplicate (same audio file, same trim/fade/volume/effects, no hotkey), Color… (opens Color Picker dialog), Reveal in Folder, Remove

---

## Installation

1. Download **SoundPad-Setup-1.15.0.exe** from the Releases page.
2. Run the installer. No administrator password is needed — it installs per-user to  
   `%LocalAppData%\Programs\SoundPad`.
3. Optionally tick **Create a Desktop shortcut** during setup.
4. Click **Launch SoundPad** at the end of the wizard, or find it in the Start Menu.

> **Windows SmartScreen warning** — SoundPad is not yet code-signed. If Windows shows  
> "Windows protected your PC", click **More info → Run anyway**. Code signing is a  
> planned future step.

---

## Discord / VB-CABLE routing

SoundPad can send sounds directly into Discord (or any other voice chat) without a physical cable.

> **Note:** SoundPad does not include its own virtual audio driver yet. You need to install a third-party virtual cable (VB-CABLE or Voicemeeter) for Discord / game microphone routing.

1. Download and install **VB-CABLE Virtual Audio Device** from [vb-audio.com](https://vb-audio.com/Cable/).
2. Restart your PC after installing VB-CABLE.
3. In SoundPad → **Settings** tab:
   - Set **Virtual Output** to `CABLE Input (VB-Audio Virtual Cable)`.
   - Set **Monitor Output** to your real speakers/headphones.
4. In Discord → **Voice & Video** → **Input Device**: choose `CABLE Output (VB-Audio Virtual Cable)`.
5. Press a sound hotkey — you will hear it in your headphones (Monitor Output) and Discord will hear it through CABLE.

---

## Discord / Game Routing Wizard

The **Settings** tab includes a Routing Wizard that removes the guesswork from the setup above.

- **Live status dots** show Monitor Output, Virtual Output, and Mic Passthrough state at a glance.
- If no virtual cable is selected, a warning banner appears with a **Use Recommended Setup** button that auto-selects the best detected device (VB-CABLE, Voicemeeter, etc.).
- **Test Virtual Output** plays a 1.5-second 440 Hz tone directly to the virtual device so you can confirm Discord hears it before playing a real sound.
- Warning banners also flag same-device conflicts (Monitor = Virtual) and mic-with-no-device conditions.

---

## Microphone passthrough

When mic passthrough is enabled, your microphone is mixed into the virtual output alongside sounds, so Discord hears both your voice and SoundPad audio without you switching inputs.

1. Connect a microphone.
2. In SoundPad → **Settings** tab:
   - Enable **Mic Passthrough**.
   - Select your microphone from the **Microphone** drop-down.
   - Adjust **Mic Volume** so your voice is at a comfortable level relative to the sounds.
3. VB-CABLE must be set as the Virtual Output (see above).
4. Discord's input must be set to `CABLE Output`.

> Passthrough adds roughly 20 ms of latency. This is intentional to keep the buffer stable  
> while still being imperceptible in voice chat.

---

## Profiles / Decks

Decks let you organize your sound library into independent sets — one deck per game, stream, or scenario.

The deck bar appears at the top of the Sound Library panel as a row of chips. Click a chip to switch to that deck. The active chip is highlighted.

| Action | How |
|---|---|
| **Create deck** | Click **+** in the deck bar, enter a name |
| **Rename deck** | Right-click a deck chip → Rename |
| **Duplicate deck** | Right-click a deck chip → Duplicate (copies all sounds and categories; hotkeys are cleared to avoid conflicts) |
| **Delete deck** | Right-click a deck chip → Delete (the last remaining deck cannot be deleted) |

**What is per-deck:**
- Sounds — adding, removing, and editing sounds only affects the active deck
- Categories — Category Manager changes apply to the active deck only
- Hotkeys — only the active deck's hotkeys are registered with Windows; the same combo can be reused across decks without conflict
- Favorites and Recent — starred sounds and play history are scoped to each deck

**What is global:**
- Stop All hotkey — stops all sounds regardless of which deck is active
- Mic passthrough — switching decks does not affect passthrough; it continues uninterrupted
- Audio device selection and performance preset

**Migration from v1.3 and earlier:**
On first launch after upgrading, SoundPad automatically migrates your existing `sounds.json` into a new **General** deck. The original `sounds.json` is renamed to a timestamped backup (e.g. `sounds.json.v1.3.bak_20260623120000`) and kept in `%AppData%\SoundPad\`. No sounds are lost.

---

## Grid / Pad View

SoundPad offers two views for the sound library, toggled by the **List** and **Grid** buttons in the toolbar.

### List View (default)

The full editing table with name, category, hotkey, volume slider, and created date columns. Best for managing, editing, and organising sounds.

### Grid View

Large clickable pads arranged in a wrap layout — ideal for quick soundboard use during a Discord session or while gaming.

Each pad shows:
- Sound name (center, bold)
- Category badge (bottom left)
- Hotkey label (top right, if assigned)
- Favorite star (top left)
- ▶ playing indicator (bottom right, while active)

**Interaction in Grid View:**
- **Click a pad** — plays the sound; click again to stop it
- **Right-click a pad** — same context menu as List View (Edit, Favourite/Unfavourite, Duplicate, Color, Reveal in Folder, Remove)
- **Active pad** — highlights with an accent-colour background and shows the ▶ indicator
- **Hotkey press** — highlights the correct pad exactly as clicking would
- **Stop All** — clears all pad highlights immediately
- **Search and category filter** — both work in Grid View and refresh the pad layout
- **Deck switching** — switching decks reloads the grid with the new deck's sounds

### Drag-and-drop reorder

Sounds can be reordered by dragging within the active deck. Reorder works in both List View and Grid View.

- **List View** — drag any row by its non-interactive area (not the Play button, Volume slider, or Hotkey button). A 2 px drop indicator line shows the insert position.
- **Grid View** — drag any pad card. A card-sized placeholder shows the insert position. The placeholder matches the current pad size.
- **Order persists** — the new order is saved to `decks.json` immediately and is reflected in both views after the drop.
- **Reorder is blocked** when a category filter other than **All** is active, the search box is non-empty, the tag filter is set to anything other than **Any Tag**, or the sort order is not **Manual order**. A message appears in the status bar explaining why.
- **Deck.Sounds list order is the source of truth.** Reorder does not change sound IDs, hotkeys, colors, categories, trim/fade settings, or file paths.
- External file-drop import (dragging audio files from File Explorer) is unaffected — it works in all views and filter states.

### Pad size

The **Pad Size** combo box appears in the toolbar when Grid View is active.

| Size | Dimensions |
|---|---|
| Small | 120 × 100 px |
| Medium | 160 × 130 px (default) |
| Large | 210 × 170 px |

The selected size is saved to `settings.json` and restored on restart.

### Compact Grid Mode

Click the **Compact** button in the toolbar (Grid View only) to toggle compact mode.

- **Compact on** — hides the category badge and favorite star; increases the sound name font size for maximum name visibility in a dense layout
- **Compact off** — full layout with category badge, favorite star, hotkey, and sound name

Compact mode is saved to `settings.json` and restored on restart.

### View persistence

The last selected view (List or Grid), pad size, and compact mode are all saved to `settings.json` and restored on restart. Old `settings.json` files without these fields default to List View, Medium pads, and compact off.

---

## Mini Mode / Floating Soundboard

Click the **Mini** button in the toolbar to open a compact floating soundboard window alongside the main app.

Mini Mode is designed for Discord and gaming use — keep it visible while the main window is minimised to the tray.

### What Mini Mode shows

Mini Mode displays every pad in the currently active deck as a compact 110 × 90 px card. Cards use the same pad color as Grid View. No search filtering or category filtering is applied — all sounds in the deck are always visible.

### Playback

- **Click a Mini pad** — plays the sound; click again while playing to stop it
- **Active state syncs both ways** — playing from Mini highlights the pad in Mini and the row/card in the main window; playing from the main window highlights the pad in Mini
- **Stop All** — click ⏹ Stop All in Mini Mode's footer, or use the main window's Stop All button or global hotkey; all highlights clear in both UIs simultaneously
- **Sound finishes naturally** — Mini pad deactivates automatically, matching the main window

### Deck and rename sync

Switching decks or renaming the active deck in the main window updates Mini Mode immediately. The header shows the active deck name and updates live.

### Window behavior

| Control | Behavior |
|---|---|
| **Drag the header** | Moves the Mini window freely |
| **📌 Pin button** | Toggles always-on-top; highlighted when active |
| **✕ Close button** | Hides Mini Mode — the app keeps running |
| **Resize edges** | Pads wrap to fill the window |
| **Reopen** | Click **Mini** in the toolbar again |

### Settings persistence

Mini Mode window size, position, and always-on-top state are saved to `settings.json` and restored on next open. If the saved position is off-screen (e.g. a secondary monitor was disconnected), the window is clamped back onto the visible screen.

### Architecture

MainWindow remains the single owner of all playback and audio state. Mini Mode is UI-only and does not create a second audio engine. All play, stop, and active-state changes go through MainWindow's existing `PlayLibraryItem`, `StopSoundById`, and `StopAllSounds` methods. Active state propagates via the `PlaybackStateChanged` event fired from `UpdateRowState`. Deck changes propagate via the `ActiveDeckChanged` event. Old `settings.json` files without Mini Mode fields load correctly — all new fields default to sensible values (`MiniAlwaysOnTop = true`, `MiniOpenOnStartup = false`, position = null).

---

## Instant Replay Audio Clipper

The **Instant Replay** feature records a rolling buffer of your system audio — so when a perfect moment happens, you can save the last N minutes as a clip with a single button or hotkey.

Instant Replay is **OFF by default**. No audio is captured unless you explicitly enable it.

### Enabling Instant Replay

1. Open the **Settings** tab → find the **Instant Replay** card.
2. Toggle **Instant Replay** ON.
3. The status bar confirms the capture device and audio format, or shows a signal warning if no audio is detected.

### Capture device

- The **Capture Device** drop-down selects which system render endpoint to record (e.g. headset, speakers, or a specific audio interface).
- Leaving it on the default captures from the Windows default playback device.
- The selected device persists across restarts.
- The **Signal** indicator shows one of three states:
  - **Capturing audio** — ring buffer is filling normally
  - **No signal (waiting for audio)** — device is open but no audio data has arrived yet
  - **No signal (device meter active but capture silent)** — audio is playing on the device but the capture is reading silence; try selecting a different Capture Device

### Buffer length

Choose from **1 to 5 minutes** of rolling history. The ring buffer holds this many minutes of audio at all times; older audio is overwritten continuously as new audio arrives.

All Instant Replay settings (capture device, buffer length, microphone) are locked while Instant Replay is ON and unlock when you turn it OFF.

### Saving a clip

Click **Save Clip** in the Settings card, or press the **Save Clip hotkey** (assignable in Settings). SoundPad:

1. Snapshots the last N minutes from the ring buffer
2. Writes a WAV file to `%AppData%\SoundPad\Sounds\`
3. Adds the clip to the active deck as `Clip YYYY-MM-DD HH-MM-SS`
4. Opens the **Sound Editor** immediately so you can rename, trim, fade, or assign a hotkey

Clips are saved as **WAV** for reliability and instant playback. MP3 export is planned for a later version.

Once in the library, clips behave exactly like any other sound — they can be played, colored, hotkeyed, duplicated, trimmed, faded, and included in backups. Mini Mode refreshes automatically when a clip is saved.

### Optional microphone capture

To include your voice in clips:

1. Tick **Include microphone** in the Instant Replay card.
2. Select your microphone from the **Microphone** drop-down.
3. Adjust **Mic Volume** (0–200 %).
4. Save a clip — SoundPad mixes system audio and mic audio sample-by-sample into a single WAV.

Microphone capture is **OFF by default** (privacy default). No microphone is ever recorded unless **Include microphone** is enabled AND Instant Replay is ON. Enabling mic capture does not affect the existing **Mic Passthrough** feature — both can be active simultaneously.

If the mic produces no signal at save time, the clip is saved with system audio only and the status bar explains why.

### Hotkeys

Two hotkeys can be assigned in the **Settings** tab:

| Hotkey | Action |
|---|---|
| **Save Clip** | Instantly snapshot the ring buffer and save a clip |
| **Toggle Instant Replay** | Turn Instant Replay ON or OFF without opening Settings |

Both hotkeys fire globally (even when the app is minimised or hidden in the tray), survive deck switches, and persist across restarts.

### Privacy

- No audio is captured while Instant Replay is OFF.
- No microphone is recorded unless Include microphone is enabled.
- No audio is routed anywhere — Instant Replay only writes to a local WAV file on Save Clip.
- Old `settings.json` files without Instant Replay fields load correctly — all fields default to OFF.

---

## Export as MP3

Right-click any sound in **List View** or **Grid View** and choose **Export as MP3…** to save an external MP3 file of that sound.

### What gets exported

Export renders the version you actually hear in SoundPad — not just a copy of the original file:

- **Block segments (v1.10+)** — only the kept blocks are included; removed blocks are skipped entirely
- **Trim Start / Trim End** — only the trimmed region is exported (for sounds without block data)
- **Audio Effects (v1.14+)** — Reverse, Normalize, and Playback Speed are applied before the fade and volume stages
- **Fade In / Fade Out** — volume ramps are baked into the exported audio
- **Volume** — per-sound volume (including the perceptual power-2 curve) is applied

Even if the source file is already an MP3, SoundPad re-renders it through the edit pipeline so the exported file always reflects your edits.

### Export behavior

- A **Save As** dialog lets you choose the output file name and location.
- The default file name is the sound's display name, sanitised for Windows.
- Encoding uses the **Windows Media Foundation** built-in MP3 encoder (no external tools or bundled binaries required).
- Export bitrate: **192 kbps**.
- A safe temp-file flow is used: SoundPad encodes to a temporary file first, then replaces the final file only after a successful encode. If encoding fails, the temp file is deleted and the original final path is not touched.
- While an export is in progress, the **Export as MP3…** menu item for that sound is greyed out. It re-enables when the export finishes.
- Status bar shows **"Exporting MP3…"** while running and **"Exported MP3: filename.mp3"** on success.

### What is not affected

- The sound in the SoundPad library is **not modified**.
- No duplicate sound is added to the library.
- The original audio file on disk is **not modified**.

### Works for

- Any imported sound (MP3, WAV, OGG, FLAC, AAC)
- Instant Replay WAV clips saved from the Instant Replay feature

### Future

Export as WAV, batch export, and selectable bitrate are planned for a later version.

---

## Sound / Pad colors

Right-click any sound (in List View or Grid View) and choose **Color…** to open the Color Picker dialog.

### Color Picker dialog

| Control | Description |
|---|---|
| **Preset colors** | Click a swatch to select Default, Red, Orange, Yellow, Green, Blue, Purple, Pink, or Gray |
| **HEX input** | Type a 6-digit hex code like `#FFAA00` (the `#` prefix is optional) |
| **RGB sliders / inputs** | Drag or type R, G, B values from 0–255; the HEX box and preview swatch update live |
| **Live preview** | Swatch at the bottom of the dialog shows the selected color as you choose or type |
| **Apply** | Saves the color and closes the dialog; disabled until a valid color is chosen or entered |
| **Cancel / Escape** | Closes without changing the color |

**Preset color values:**

| Color | Hex |
|---|---|
| Default | — (removes color; row/pad returns to the standard background) |
| Red | `#E53935` |
| Orange | `#F4511E` |
| Yellow | `#F9AB00` |
| Green | `#0F9D58` |
| Blue | `#039BE5` |
| Purple | `#7B1FA2` |
| Pink | `#D81B60` |
| Gray | `#546E7A` |

**How colors appear:**
- **List View** — a 4 px vertical accent stripe on the left edge of the row. The active/playing highlight covers the row background but the stripe remains visible alongside it.
- **Grid View** — the color fills the pad card background. When the sound is active, the accent highlight replaces the color; the ▶ indicator makes the active state clear.

Colors are stored per sound as `PadColor` in `decks.json`. Old decks, settings files, and backup ZIPs without `PadColor` load correctly — missing values default to the standard appearance. Duplicating a sound copies its color. Backup export preserves colors; old backup imports without `PadColor` restore with default colors.

---

## Favorites and Recent sounds

**Favorites** — Click the star (☆) on any sound row to mark it as a favourite. Select **Favorites** in the category filter to see only starred sounds.

**Recent** — Select **Recent** in the category filter to see sounds played in the last 7 days, ordered by most recently played.

---

## Sound Library — Search, Tags, and Sort

### Search

A search box runs across the top of the Sound Library filter bar. Typing filters the library in real time — matching any of the sound's **name**, **category**, or **tags**. A **Clear** (×) button inside the search box resets it instantly.

Search works in both List View and Grid View.

### Tags

Every sound can carry a list of free-form tags. Assign tags via **Edit Sound** — the Tags field accepts a comma-separated list (e.g. `meme, voice, game`). Tags are trimmed and deduplicated case-insensitively on save.

Once any sound in the active deck has tags, a **Tag Filter** combo box appears in the filter bar. Select a tag to show only sounds that carry it; select **Any Tag** to remove the tag filter. The Tag Filter is hidden automatically when no sounds in the deck have tags.

- Tags are stored in `decks.json` and included in backup ZIPs.
- Old decks and backup ZIPs without tags load correctly — missing tags default to none.
- Duplicating a sound copies its tags. Editing the duplicate's tags does not affect the original.

### Sort

The **Sort** combo box in the filter bar controls the display order of sounds:

| Option | Order |
|---|---|
| **Manual order** (default) | The order sounds appear in the deck — drag rows or pads to reorder |
| **Name A–Z** | Alphabetical by display name |
| **Name Z–A** | Reverse alphabetical by display name |
| **Newest first** | By creation date, most recently added first |
| **Oldest first** | By creation date, oldest first |
| **Category** | Alphabetical by category, then by display name within each category |
| **Favorites first** | Starred sounds first, then alphabetical by display name |

The selected sort is saved to `settings.json` and restored on restart. Old `settings.json` files without this field default to Manual order.

**Manual order** is the only sort that allows drag-and-drop reorder. Any other sort order blocks drag reorder — the status bar explains why.

**Recent** overrides the sort box — it is always sorted by most recently played, and the Sort box is disabled while Recent is selected.

---

## Drag-and-drop import

Drag one or more audio files (MP3, WAV, OGG, FLAC, AAC) from File Explorer directly onto the Sound Library panel. SoundPad copies the files to its app-data folder and adds them to the library immediately — no dialog needed.

---

## Library backup import / export

**Export**: Settings tab → **Export Backup** → choose a save location. SoundPad creates a ZIP containing:
- `decks.json` — all decks and their sound metadata (v1.4+ format)
- `sounds.json` — flat list of all sounds across all decks (included for backward compatibility)
- `Sounds/` — all audio files

**Import**: Settings tab → **Import Backup** → select a `.zip` file.

- **New format (ZIP contains `decks.json`)** — decks are merged into your library by name. Sounds already in any deck (matched by ID) are skipped. New decks are created automatically. Hotkeys that conflict within the destination deck are cleared.
- **Old format (ZIP contains only `sounds.json`)** — sounds are added to the currently active deck. Sounds already in any deck (matched by ID) are skipped. Conflicting hotkeys are cleared.

Backup ZIPs are self-contained and portable — you can copy a library to another machine.

---

## Pro Sound Editor

Click **Edit** on any sound row (or right-click → Edit) to open the Pro Sound Editor.

Audio editing is **non-destructive** — the original audio file is never modified. All edits are stored in `decks.json` and preserved in library backups. Old sounds (no segment data) load and play exactly as before.

### Block timeline

The editor displays a compact CapCut-style block timeline. Each block represents a kept region of the original audio. Blocks are packed together with no gaps — this is the edited/played timeline. Removed blocks leave no visual hole; remaining blocks ripple left.

### Time ruler

A time ruler runs above the waveform canvas and shows absolute timestamps for the current view. The ruler automatically adjusts its tick spacing to match the current zoom level and scrolls in sync with the waveform so the position under the playhead is always readable.

### Tools

| Tool | Shortcut | What it does |
|---|---|---|
| **Select** | A | Click a block to select it; click the timeline to reposition the playhead |
| **Cut** | C | Click inside a block to split it into two at that point — no audio is removed |

### Cutting and removing blocks

1. Press **C** to switch to Cut mode.
2. Click anywhere inside a block — it splits into two adjacent blocks. No audio is deleted; both blocks remain in the timeline.
3. Press **A** to switch back to Select mode.
4. Click a block to select it (it highlights with a blue outline).
5. Click **Remove Block** (or press **Delete**) to delete the selected block. The remaining blocks ripple together with no visual gap.
6. Right-click any block to remove it directly.

Playback and Export as MP3 both join the remaining blocks seamlessly — removed regions are skipped automatically.

### Trimming block edges

In **Select** mode, hover near the left or right edge of any block. The cursor changes to a resize arrow. Drag to trim that block's boundary:

- **Left edge** → adjusts the block's `StartSeconds` (first block left edge also updates Trim Start)
- **Right edge** → adjusts the block's `EndSeconds` (last block right edge also updates Trim End)
- Minimum block duration: 20 ms

### Playhead

The white dashed vertical line marks the current preview start position.

- **Click the timeline** to jump the playhead to that position.
- **Drag the white triangle handle** at the bottom of the canvas for precise positioning. Dragging stops any running preview.

### Snap cut to playhead

When **Snap cut to playhead** is checked (default: ON) and the Cut tool is active, moving the mouse within ~10 px of the playhead snaps the cut preview hairline to the playhead position (the hairline turns bright yellow). Clicking then splits the block exactly at the playhead. Turn Snap OFF to always cut at the exact mouse position.

### Undo / Redo

Every edit (cut, remove block, trim drag, paste, and reorder) can be undone or redone:

- Click the **Undo** button in the editor toolbar, or press **Ctrl+Z**.
- Click the **Redo** button in the editor toolbar, or press **Ctrl+Y** / **Ctrl+Shift+Z**.

The undo/redo stacks are per-session and are not persisted after the dialog closes.

### Copy / Paste block

With a block selected in **Select** mode:

- Press **Ctrl+C** to copy the selected block.
- Press **Ctrl+V** to paste it as a new block appended to the end of the timeline.

Pasted blocks play and export in the same sequence as all other blocks. Paste is undoable.

### Block drag / reorder

In **Select** mode, drag any block horizontally to move it to a new position in the timeline. A visual placeholder shows the drop target.

- Reordering affects both playback and **Export as MP3** — audio is rendered in the reordered sequence.
- Reorder is undoable.

### Selected block info

When a block is selected, the editor toolbar shows the block's **start time**, **end time**, and **duration**. The display updates in real time as you trim block edges or reorder blocks.

### Zoom

The **Zoom** slider scales the timeline from 1× to 10×. When zoomed, the timeline scrolls horizontally. All coordinate mapping stays correct under any zoom level.

### Fade In / Fade Out

Fade In and Fade Out are applied to the full joined output:

- **Fade In** ramps from silence at the very start of the first block.
- **Fade Out** ramps to silence at the very end of the last block.
- Fades are applied identically during Preview, library playback, and Export as MP3.

### Preview

Click **Play Preview** (or press **Spacebar**) to hear all kept blocks joined together, with fades applied, starting from the playhead position. Press **Spacebar** again (or click **Stop Preview**) to pause. Plays through the Monitor Output device.

### Backward compatibility

- Sounds edited before v1.10 (trim/fade only, no block data) load and play correctly without any migration.
- `decks.json` files without the `Segments` field deserialize to a single full-range block — identical playback to before.
- Library backup ZIPs preserve segment data; old backups without segments import cleanly with default (full-range) behaviour.

---

## Audio Effects

Click **Edit** on any sound to open the Sound Editor. The **EFFECTS** section at the bottom of the dialog lets you apply non-destructive per-sound effects.

### Available effects

| Effect | Description |
|---|---|
| **Reverse** | Plays the audio backwards. Stereo L/R pairing is preserved correctly. |
| **Normalize** | Boosts (or reduces) volume so the loudest peak reaches 0 dB. Useful for quiet recordings. |
| **Playback Speed** | Adjusts speed from 0.5× (half speed) to 2.0× (double speed). Pitch shifts with speed — slower sounds lower, faster sounds higher (vinyl-style). |

### How effects work

- Effects are **non-destructive** — the original audio file is never modified.
- Effects are applied in order: extract block segments → reverse → normalize → speed change.
- The result is cached after the first render; replaying a sound with effects is instant.
- The cache is invalidated automatically when you edit and save the sound.
- Effects **stack** with all other edits: trim, block segments, fade in/out, and per-sound volume all apply on top of the effects output.
- A **Reset Effects** button in the dialog restores Reverse, Normalize, and Speed to their defaults (off, off, 1.0×).

### Persistence and portability

- Effect settings are saved in `decks.json` per sound and preserved across restarts.
- **Backup export** includes effect settings; importing a backup restores them.
- **Duplicate** copies the source sound's effect settings to the duplicate.

### Editor preview

While the Sound Editor is open, clicking **Play Preview** renders effects using the current (unsaved) control values — so you can hear exactly what the saved result will sound like before clicking Save.

### Export as MP3

**Export as MP3** applies the same effects pipeline, so the exported file matches what you hear in SoundPad.

### Vinyl-style pitch note

Speed change uses a resampling approach: playing at 2× speed also raises pitch by one octave; 0.5× lowers it by one octave. True pitch-shifting without speed change is planned for a future release.

---

## Category Manager

Click the **Categories** button in the toolbar to open the Category Manager.

- **Create** — add a new empty category. Useful to set one up before you have sounds for it.
- **Rename** — rename any category; all sounds in that category update automatically.
- **Delete** — delete a category. If it contains sounds, choose which remaining category to move them to. Empty categories are removed immediately.

The virtual categories **All**, **Favorites**, and **Recent** cannot be created, renamed, or deleted.

---

## Sound row context menu

Right-click any sound row (List View) or pad card (Grid View) for quick actions:

| Action | Description |
|---|---|
| **Edit** | Opens the Sound Editor for that sound |
| **Favourite / Unfavourite** | Toggles the favourite star |
| **Duplicate** | Creates a copy with the same audio file, trim/fade, volume, effects (Reverse/Normalize/Speed), color, and tags — no hotkey assigned |
| **Color…** | Opens the Color Picker dialog — choose a preset, enter a custom HEX or adjust RGB sliders, preview live, then Apply; Default removes the color |
| **Reveal in Folder** | Opens File Explorer with the source audio file selected |
| **Remove** | Removes the sound from the library; the audio file is not deleted |

---

## Active sound controls

While a sound is playing, its row highlights with an accent-coloured background and the Play button becomes a **Stop** button. Click it to stop that sound individually without affecting anything else playing.

The **Stop All** button (and its global hotkey) stops every active sound at once.

---

## Playback mode

**Settings tab → Behavior → Playback Mode**

| Mode | Behaviour |
|---|---|
| **Mix** (default) | Every Play adds a new sound to the mix — multiple sounds can play at once |
| **Interrupt previous** | Starting a new sound automatically stops the previous one |

The chosen mode persists across restarts.

---

## Audio Performance Presets

**Settings tab → Audio Performance**

| Preset | Buffer | When to use |
|---|---|---|
| **Stable** | 300 ms | Most reliable; use if you hear drop-outs on older hardware |
| **Balanced** | 100 ms | Recommended for most systems (default) |
| **Low Latency** | 60 ms | Fastest response; may crackle on slower machines |

Changing the preset recreates the audio engines and stops any playing sounds. Your mic passthrough is automatically restarted after the change.

---

## In-app updater

**Settings tab → Updates**

- **Check for Updates** — queries the GitHub Releases API immediately. If a newer version is available, an update panel appears below the Updates card.
- **Automatic check on startup** — when enabled, runs silently once per 24 hours at startup. Disabled by default; no network requests are made unless you opt in. Automatic checks only notify — they never download anything automatically.

### Update available panel

When an update is detected, a panel shows the version number, release title, and a truncated release notes excerpt. The panel offers three actions:

| Button | What it does |
|---|---|
| **Download & Install** | Downloads the installer to a safe temp path and prompts you before launching it |
| **Open Release Page** | Opens the GitHub release in your browser — always available as a fallback |
| **Later** | Dismisses the panel for this session |

### Download & Install

- The installer (`SoundPad-Setup-*.exe`) is downloaded from the official GitHub release asset, streamed in chunks with a live progress bar.
- The download is saved to `%TEMP%\SoundPad\Updates\` — never to the application directory or next to the running executable.
- You can cancel the download at any time with the **Cancel** button.
- After a successful download, SoundPad asks: **"Install now?"** You must confirm before anything happens.
- If you click **Yes**, SoundPad launches the installer and closes itself. The installer runs normally and may trigger a Windows SmartScreen prompt (per-user install, no UAC required).
- If you click **No**, the download is kept and you can start again without re-downloading.
- If the installer cannot be launched, SoundPad stays open, shows an error with the file path, and keeps the **Open Release Page** button available.
- If the release has no matching installer asset, the **Download & Install** button is hidden and a message directs you to **Open Release Page** instead.

> **SoundPad never overwrites its own running executable.** The installer is a separate process that replaces the files after SoundPad exits normally.

---

## Custom hotkeys

1. In the **Sound Library** tab, click the hotkey badge on any sound row (e.g. `Ctrl+Alt+1`).
2. The **Hotkey Capture** dialog opens. Press the key combination you want.
3. Click **Save**. The hotkey is registered globally immediately.
4. To clear a hotkey, open the same dialog and click **Clear**.

The **Stop All Sounds** hotkey is set separately in the **Settings** tab.

---

## Tray behaviour

| Setting | Behaviour |
|---|---|
| **Minimize to Tray** off | Window minimises normally to the taskbar |
| **Minimize to Tray** on | Window hides to the tray; hotkeys keep working |
| **Close to Tray** off | Clicking ✕ exits the app |
| **Close to Tray** on | Clicking ✕ hides to the tray; hotkeys keep working |

Right-click the tray icon for **Show SoundPad**, **Stop All Sounds**, and **Exit**.

---

## Start with Windows

Toggle **Start with Windows** in the Settings tab.  
SoundPad adds or removes an entry in  
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`  
so it launches automatically when you log in — no admin rights needed.

---

## App data location

All user data (sound library, settings, imported audio files) is stored in:

```
%AppData%\SoundPad\
```

Typically: `C:\Users\<you>\AppData\Roaming\SoundPad\`

This folder is **not** touched by the installer or uninstaller, so your library survives uninstall + reinstall.

Key files written here:

| File / Folder | Contents |
|---|---|
| `decks.json` | All decks, sounds, categories, and hotkeys (v1.4+) |
| `settings.json` | Device selection, toggles, window position, active deck |
| `Sounds/` | Audio files imported into the library |
| `Logs/` | Rolling daily log files (see Performance and stability below) |

A `startup.log` file in this folder records app startup events and is overwritten on each launch — useful for diagnosing startup crashes.

---

## Performance and stability

### Rolling daily logs

SoundPad writes a dated log file to `%AppData%\SoundPad\Logs\soundpad-YYYY-MM-DD.log` on every run. The last 10 days of logs are kept; older files are pruned automatically on startup. Logs record the session header (version, OS, .NET runtime, machine name), device creation, playback and export errors, backup import/export results, and any unhandled exceptions.

The `startup.log` in `%AppData%\SoundPad\` is a separate file that captures very early startup events (before the rolling logger is ready) and is overwritten on each launch.

### Diagnostics card

**Settings tab → Diagnostics** provides two quick actions:

| Button | What it does |
|---|---|
| **Open Logs Folder** | Opens `%AppData%\SoundPad\Logs\` in File Explorer |
| **Export Diagnostics** | Saves a plain-text snapshot of the current session to your Desktop, including app version, OS version, selected audio devices (Monitor, Virtual, Mic), Instant Replay state, deck and sound counts, cache counts, and the last 50 log lines |

The diagnostics snapshot never includes sound file paths or sound display names — only counts and device names.

### Large file guard

Audio files larger than 200 MB are rejected when added to the library. A clear error message is shown in the status bar. This prevents accidental OOM crashes from loading multi-hour recordings into RAM.

### Save safety

Deck data (`decks.json`) and settings (`settings.json`) are now written through safe wrappers that catch IO errors and log them instead of crashing. Volume slider changes are debounced — the file is written once 500 ms after you stop dragging, not on every tick.

### Search debounce

Typing in the search box is debounced at 150 ms for better responsiveness on large libraries. Clearing the search box applies immediately (no debounce delay).

### Shutdown safety

Instant Replay capture is disposed on a background thread at app exit to avoid a potential STA-thread deadlock on Windows audio capture APIs.

### No audio behavior changes

v1.15.0 contains no intentional changes to audio routing, playback, or effect rendering. All features from v1.14.0 and earlier are preserved.

---

## Uninstalling

**Via Settings:** Settings → Apps → Installed apps → SoundPad → Uninstall.  
**Via Control Panel:** Control Panel → Programs → Uninstall a program → SoundPad.

The uninstaller removes the application files. Your sound library and settings in  
`%AppData%\SoundPad` are kept. Delete that folder manually if you want a clean slate.

---

## Building from source

Requirements: .NET 10 SDK, Windows 10/11.

```powershell
# Run from the repo root
dotnet build SoundPad.App/SoundPad.App.csproj

# Publish a self-contained single-file exe
.\scripts\publish-release.ps1

# Publish + build the Inno Setup installer (requires Inno Setup 6 or 7)
.\scripts\build-installer.ps1
```

---

## Code signing

SoundPad is currently **unsigned**. Windows SmartScreen will warn the first time users run  
the installer or exe. Code signing with an EV certificate is a planned future step and will  
eliminate the SmartScreen prompt entirely.
