# SoundPad

A Windows soundboard app with a clean Fluent UI design.  
Play sounds to any output device, route them through Discord via VB-CABLE, and assign global hotkeys so your hands never leave the keyboard.

---

## Features

- **Sound library** — add any MP3/WAV/OGG/FLAC/AAC file, give it a display name and category, set per-sound volume
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
- **Update checking** — manual "Check for Updates" button; optional once-per-day startup check (off by default)
- **Tray mode** — minimise or close to the system tray; hotkeys keep working
- **Start with Windows** — one toggle to register SoundPad in the current-user Run key (no admin required)
- **Settings persistence** — devices, volume, window position, and all hotkeys survive restarts
- **Perceptual volume curve** — the volume slider feels natural (power-2 curve: 50 % UI = −12 dB)
- **Sound Editor** — non-destructive trim (Trim Start / Trim End) and Fade In / Fade Out per sound; original audio files are never modified; settings stored in sounds.json
- **Waveform timeline** — draggable Trim Start / Trim End handles, click-to-seek playhead, animated preview playhead, fade-in / fade-out gradient overlays, and numeric fields synced bidirectionally with the canvas
- **Category Manager** — create, rename, and delete custom sound categories; deleting a category with sounds prompts where to move them; chained operations resolve correctly
- **Sound row context menu** — right-click any sound: Edit, Favourite/Unfavourite, Duplicate (same audio file, same trim/fade/volume, no hotkey), Reveal in Folder, Remove

---

## Installation

1. Download **SoundPad-Setup-1.2.0.exe** from the Releases page.
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

## Favorites and Recent sounds

**Favorites** — Click the star (☆) on any sound row to mark it as a favourite. Select **Favorites** in the category filter to see only starred sounds.

**Recent** — Select **Recent** in the category filter to see sounds played in the last 7 days, ordered by most recently played.

---

## Drag-and-drop import

Drag one or more audio files (MP3, WAV, OGG, FLAC, AAC) from File Explorer directly onto the Sound Library panel. SoundPad copies the files to its app-data folder and adds them to the library immediately — no dialog needed.

---

## Library backup import / export

**Export**: Settings tab → **Export Backup** → choose a save location. SoundPad creates a ZIP containing `sounds.json` (your metadata) and a `Sounds/` folder with all audio files.

**Import**: Settings tab → **Import Backup** → select a `.zip` file. SoundPad adds any sounds not already in your library (matched by ID), copies their audio files, and clears hotkeys that conflict with your existing ones.

Backup ZIPs are self-contained and portable — you can copy a library to another machine.

---

## Sound Editor

Click **Edit** on any sound row (or right-click → Edit) to open the Sound Editor.

- **Trim Start / Trim End** — drag the green and orange-red handles on the waveform to set the play region. Only the selected region plays — in preview, in the library, and through the virtual output. The original audio file is never modified.
- **Fade In / Fade Out** — type a duration in seconds to smoothly ramp volume up at the start and down at the end of the trimmed region.
- **Waveform timeline** — the full audio waveform is drawn on a canvas. Regions outside the trim zone are dimmed. Fade zones show coloured gradient overlays so you can see the ramp at a glance.
- **Playhead** — click anywhere on the canvas to position the preview start point. During playback, a white dashed line moves in real time and stops at Trim End.
- **Numeric fields** — Trim Start, Trim End, Fade In, and Fade Out fields stay in sync with the handles. Typing a value moves the handle; dragging the handle updates the field.
- **Play Preview** — plays the trimmed, faded clip through your Monitor Output from the playhead position (or Trim Start if the playhead is outside the trim range).

All settings save to `sounds.json`. Old `sounds.json` files without trim/fade fields load normally — missing values default to no trim and no fade. Library backup ZIPs preserve trim, fade, and category data.

---

## Category Manager

Click the **Categories** button in the toolbar to open the Category Manager.

- **Create** — add a new empty category. Useful to set one up before you have sounds for it.
- **Rename** — rename any category; all sounds in that category update automatically.
- **Delete** — delete a category. If it contains sounds, choose which remaining category to move them to. Empty categories are removed immediately.

The virtual categories **All**, **Favorites**, and **Recent** cannot be created, renamed, or deleted.

---

## Sound row context menu

Right-click any sound row for quick actions:

| Action | Description |
|---|---|
| **Edit** | Opens the Sound Editor for that sound |
| **Favourite / Unfavourite** | Toggles the favourite star |
| **Duplicate** | Creates a copy with the same audio file, trim/fade, and volume — no hotkey assigned |
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

## Update checking

**Settings tab → Updates**

- **Check for Updates now** — queries the GitHub Releases API immediately and shows a banner if a newer version is available.
- **Check automatically on startup** — when enabled, runs the check once per 24 hours in the background at app startup. Disabled by default; no network requests are made unless you turn this on.

If an update is found, a message appears in the status bar with a link to the **Open Releases Page** button.

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

A `startup.log` file in this folder records app startup events and is overwritten on each launch — useful for diagnosing startup crashes.

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
