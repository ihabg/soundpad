# SoundPad

A Windows soundboard app with a clean Fluent UI design.  
Play sounds to any output device, route them through Discord via VB-CABLE, and assign global hotkeys so your hands never leave the keyboard.

---

## Features

- **Sound library** — add any MP3/WAV file, give it a display name and category, set per-sound volume
- **Dual output** — Monitor Output (hear it yourself) and Virtual Output (send to Discord/VB-CABLE) run simultaneously
- **Global hotkeys** — assign Ctrl+Alt+1…N (or any combo) to sounds; they fire even when the app is minimised or hidden
- **Stop All hotkey** — a single global key stops every playing sound instantly
- **Microphone passthrough** — your mic audio is mixed into the virtual output so Discord hears both you and the sounds
- **Tray mode** — minimise or close to the system tray; hotkeys keep working
- **Start with Windows** — one toggle to register SoundPad in the current-user Run key (no admin required)
- **Settings persistence** — devices, volume, window position, and all hotkeys survive restarts
- **Perceptual volume curve** — the volume slider feels natural (power-2 curve: 50 % UI = −12 dB)

---

## Installation

1. Download **SoundPad-Setup-1.0.0.exe** from the Releases page.
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

1. Download and install **VB-CABLE Virtual Audio Device** from [vb-audio.com](https://vb-audio.com/Cable/).
2. Restart your PC after installing VB-CABLE.
3. In SoundPad → **Settings** tab:
   - Set **Virtual Output** to `CABLE Input (VB-Audio Virtual Cable)`.
   - Set **Monitor Output** to your real speakers/headphones.
4. In Discord → **Voice & Video** → **Input Device**: choose `CABLE Output (VB-Audio Virtual Cable)`.
5. Press a sound hotkey — you will hear it in your headphones (Monitor Output) and Discord will hear it through CABLE.

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
