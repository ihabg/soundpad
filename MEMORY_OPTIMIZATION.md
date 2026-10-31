# Audio memory optimization

Long sounds now use a temporary decoded float-PCM cache once they exceed 4 MiB.
Short effects remain entirely in memory; long sounds retain their first second
in RAM. Playback reads already-decoded samples with independent file offsets,
so simultaneous outputs do not share a seek cursor. Cache files are deleted by
their owning file handles when released, including process exit. Source audio,
sample rate, quality, effects, and recording buffer duration are unchanged.

Decoding and waveform generation use bounded blocks rather than growing a
full-file list and copying it. Short replay saves snapshot only the requested
duration. Finished mixer inputs release their playback references automatically.

## Measurements on the existing 36-sound library

| Measurement | Previous | Optimized |
|---|---:|---:|
| Decoded audio retained in RAM | 423.6 MiB | 27.3 MiB |
| Isolated loader working set | 771.9 MiB | 103.5 MiB |
| Isolated loader private bytes | 804.5 MiB | 71.8 MiB |
| Total managed allocations during load | 1741.1 MiB | 444.7 MiB |
| Load time | 5.36 s | 4.96 s |

The full optimized WPF preview measured 301 MiB working set / 253 MiB private
bytes with all 36 sounds loaded and one-minute Instant Replay enabled. The
installed app measured 923–1216 MiB during this session. Process figures vary
with use and garbage collection; loader figures above compare the same source
library in separate processes. No forced garbage collection or working-set
trimming was used.

Eight concurrent 20-ms cache-read blocks measured 0.055 ms at p99 and 0.105 ms
maximum in the local warm-cache benchmark. This checks cache overhead, not a
guarantee about every storage device or heavy concurrent disk workload. Large
mix renders still allocate output buffers while rendering.

## Repeat validation

- `dotnet run --project SoundPad.Tests -c Release`
- Add `-- --native-hotkeys` for Windows shortcut integration checks.
- The compiled tests accept `--profile-library` and `--legacy-profile-library`
  for opt-in, read-only comparisons using the current active deck. They print
  aggregate measurements, never sound names or paths.

Regression checks cover exact samples, concurrent reads, seeks, fades,
reverse/normalization, file decoding across the cache threshold, waveform
buckets, and bounded replay snapshots. The temporary cache uses local storage;
it does not upload or change library audio.
