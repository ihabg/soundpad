# Shared audio effects

The Edit Sound window and Sound Editor inspector use the same
`Controls/AudioEffectsPanel` and `Audio/AudioEffectCatalog` definitions. The
current effects are Reverse, Normalize, Pitch shift, Radio / telephone,
Distortion, Echo / delay, and Reverb. Speed (0.5–2×) is an always-visible
playback control with its own Reset button, separate from the Effects dropdown.
Resetting Effects preserves the chosen speed.

## Add an effect once

Add an `AudioEffectDefinition` to `AudioEffectCatalog.Definitions`, including its
stable ID, display name, default/range, toggle-or-slider control, explanatory
hint, and sample-processing function. Both panels generate its control
automatically. Mark a definition `IsPlaybackControl` to show it separately from
the Effects dropdown. All previews, normal playback, recording trim rendering, mix
rendering, and exports use this processing pipeline. If the effect changes
length, also supply its `OutputDuration` function so timeline durations update.
Mark `AddsTail` when the duration change is an appended decay rather than a
change in source playback rate, so source cuts keep their correct positions.

Pitch ranges from −12 to +12 semitones and preserves duration. Radio,
Distortion, Echo, and Reverb use 0–100% strength with 0% meaning off. Echo adds
four decaying repeats at 250-ms intervals, retaining a one-second tail. Reverb
uses damped comb filters and all-pass diffusion with a 1.5-second tail. Stereo
channels have independent processing state. Pitch uses the NAudio SMB phase
vocoder, flushing and compensating its FFT latency to preserve the sound ending.
No new packages or online processing are required.

Values are persisted by stable effect ID. Unrecognized values are retained for
forward compatibility. Existing reverse/normalize/speed properties continue to
load from older libraries; new settings take precedence when present.

## Editor behavior

Select a clip to edit its effects in the right inspector. Changes affect that
clip, not its original library sound. A clip with no overrides inherits its
source effects. Both panels use a dropdown to show one effect's controls at a
time; a summary and checkmarks identify enabled effects. Selecting another
effect does not disable any others. Overrides are copied independently when duplicating, copying,
pasting, or splitting clips, and are included in Undo/Redo and local drafts.
Speed changes preserve the trimmed source region and rescale its timeline
length. A slider drag or Reset is one undo operation.
Explicit clip-local trims still limit the rendered region, including effect
tails; untrimmed clips retain their complete tails.

Library edits and recording trims keep their settings through saves and
backups. Saving a rendered mix to a deck creates audio with its effects already
applied, so playback does not apply them twice.
