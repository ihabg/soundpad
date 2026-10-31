# SoundPad v1.18.1 — Lower memory usage

This update reduces audio memory use while preserving audio quality and responsive playback.

- Short sounds stay fully loaded in RAM. Longer sounds use a temporary, already-decoded float-PCM cache and keep their first second ready in memory.
- Audio loading and waveforms use bounded blocks instead of full-file temporary copies.
- Saving a short replay copies only the requested duration rather than the entire replay buffer.
- Finished playback entries release their audio references automatically.
- Audio samples, effects, recording duration, and library files retain their existing behavior.

## Measured results

With the same 36-sound library, retained decoded audio fell from 423.6 MiB to 27.3 MiB. The full optimized preview used about 301 MiB compared with 923–1216 MiB observed for the previous app. Actual usage varies with the library, recordings, and editing activity.

Loading was slightly faster in the local comparison. Eight simultaneous 20-ms cache-read blocks stayed below 1 ms in local warm-cache tests. Storage performance can vary, and large mix renders still need temporary output buffers.

## Downloads and upgrade

Run **SoundPad-Setup-1.18.1.exe** to update. Your sounds, decks, settings, and shortcuts are preserved. A portable ZIP, SHA256 checksums, and release manifest are also included.

The temporary audio cache uses local storage and is deleted when its file handles are released, including app exit. It does not modify or upload your original sounds.

## Validation

44 automated audio and workspace checks cover exact samples, concurrent reads, seeking, fades, effects, waveforms, replay snapshots, and persistence. The optimized app was also measured with the existing sound library and Instant Replay enabled. Full clean-machine installation and extended game/Discord routing were not repeated.
