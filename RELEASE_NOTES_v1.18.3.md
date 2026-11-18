# SoundPad v1.18.3 — Playback fixes and shared effects

## Playback and editing fixes

- Speed changes immediately update the Edit Sound timeline, duration, and playhead. A 60-second sound at 0.5× plays for 120 seconds, including its ending.
- Cuts, trims, and seeking remain aligned with the source audio when changing speed.
- Play Preview resets to the beginning every time, including with effects enabled.
- With **Interrupt previous sound** off, sounds overlap, including repeated presses of the same sound. This works in the list, pads, floating pads, and hotkeys. Stop All stops every copy.
- With interruption on, starting a new sound stops earlier playback and cancels outdated pending effect renders.

## Shared effects in both editors

The Edit Sound window and Sound Editor now share one effects panel and processing system.

- **Speed** stays separate and always visible, with its own Reset button.
- The **Effects dropdown** shows one effect's controls at a time, marks enabled effects, and summarizes active effects. Resetting Effects preserves Speed.
- Existing effects: Reverse and Normalize.
- New effects: **Pitch shift**, **Echo / delay**, **Reverb**, **Radio / telephone filter**, and **Distortion**.
- Pitch shift ranges from −12 to +12 semitones without changing duration. The other new effects have 0–100% strength controls; all default to off.
- Echo retains its one-second tail and Reverb retains its 1.5-second tail. Timelines include those tails; source cut positions remain correct. Explicit clip-local trims can limit the rendered region.
- Editor effects apply to the selected clip without changing its original library sound. Undo/Redo, split, duplicate, copy/paste, drafts, previews, exports, and rendered mixes preserve the settings.
- Effect definitions are shared, so future registered effects appear in both screens automatically.

## Upgrade and downloads

Run **SoundPad-Setup-1.18.3.exe** to update your existing installation. Existing sounds, decks, settings, and shortcuts are preserved. A portable ZIP, SHA256 checksums, and release manifest are included. The lower-memory audio cache from v1.18.1 remains in use.

## Validation

102 automated audio and workspace checks passed, covering pitch accuracy, stereo processing, safe output, full effect tails, timeline mapping, overlapping playback, persistence, and Undo/Redo. The self-contained Windows build and Inno Setup installer are packaged for this release. Clean-machine installation and extended game/Discord routing were not repeated.
