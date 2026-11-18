# Hunter's music - the instruments and the score of all 3 songs

Everything below is measured from the driver itself. The driver (file 3, `03_musfile.bin`,
loaded at `$22EDA`) was run on the Musashi 68000 emulator that ships with the oracle
(`in/hunterdata/cpu.py`); `init` (`$22EDA`, d1 = song number) was called once and then `play`
(`$22F6E`) once per frame. Every write to the sound chip (`$DFF0A0-$DFF0DA`, i.e. AUDxLCH/LCL,
LEN, PER, VOL, DAT) and to DMACON (`$DFF096`) was logged with its frame number - that log is the
ground truth for the tables below. The format notes and `music.py` were used only to name things.

## What was produced

| file | what it is |
|---|---|
| `findings/driver.asm` | my own 68000 disassembly of the whole driver (17 817 lines, capstone) |
| `findings/instruments.txt` | the 7 samples, where they live, their loops, the driver parameters, and who uses them |
| `out/samples/sample_00..06.wav` | every sample the songs play, one WAV each, 8-bit mono at 8287 Hz |
| `out/score_0.txt`, `score_1.txt`, `score_2.txt` | per-note score, tracker-style, with the song structure at the top |
| `out/score_0.mid`, `score_1.mid`, `score_2.mid` | the same score as MIDI, one track per channel |
| `out/song_0.wav`, `song_1.wav`, `song_2.wav` | 44.1 kHz stereo mix (ch 0+3 left, 1+2 right), a by-product |
| `music_render.py`, `test_music_render.py` | the tool and its test (`python -I test_music_render.py`) |
| `out/trace_N.json` | the numbers for each song (length, loop point, peak) |

## The three songs (measured)

| song | frames | length at 50 Hz | loop | peak of the mix |
|---|---|---|---|---|
| 0 | 8065 | 161.3 s (2:41) | yes, back to frame 0; jump taken at frame 8064 | 0.500 |
| 1 | 5761 | 115.2 s (1:55) | yes, back to frame 0; jump taken at frame 5760 | 0.463 |
| 2 | 2881 | 57.6 s (0:58) | yes, back to frame 0; jump taken at frame 2880 | 0.500 |

None clipped (peak 0.50 = half of full scale, which is what two channels at volume 64 sum to).
All three end the same way: the last pattern finishes with `$FE` "stop all voices" (all four
channels silenced, `DMACON=$0F`), then a few frames later the sequence's `$FF` jump restarts the
song from frame 0. That stop happens at frames 8059, 5754 and 2857 - i.e. 5, 6 and 24 frames
before the restart. So the songs are written to be played on a loop.

Structure: each channel walks its own sequence of patterns (`out/score_N.txt` lists the pattern
number on all four channels at every change, with the frame and time). Song 0, for example:
patterns 17/3/14/19 for the first 11.8 s, then 13/... from 11.8 s, then a middle section in
patterns 2/5/73/74/8/25/26 from 25.2 s, and so on to 8064. Row length (the low 6 bits of the
pattern's first byte) is per note and is printed for every note; the driver's tempo counter
`$29(a1)` reloads from the 8-byte table at `$235E2` (`00 01 01 01 01 01 01 01`).

## Instruments - what the songs actually play

Seven samples are used; they are the driver's sample-table slots 0-6 (slots 7-27 in the 28-entry
table all point at `$7E30` and are never referenced by any instrument, so they are junk).
Nine instruments (the 16-byte table at `$2388E`) map onto those seven samples. Full table in
`findings/instruments.txt`; the short version:

| sample | file offset | length | loop start | loop len | instruments | notes: song 0 / 1 / 2 |
|---|---|---|---|---|---|---|
| 0 | `$000000` | 1446 B | 0 | 1 word (one-shot) | i0, i1 | 420 / 261 / 149 |
| 1 | `$0005A6` | 3498 B | 0 | 1 word (one-shot) | i2 (+vibrato) | 353 / 159 / 154 |
| 2 | `$001350` | 8648 B | 8392 | 128 words | i3 (+vibrato) | 140 / 12 / 25 |
| 3 | `$003518` | 3656 B | 3398 | 129 words | i4, i7 | 498 / 821 / 28 |
| 4 | `$004360` | 4410 B | 1240 | 1585 words | i5 | 123 / 131 / 27 |
| 5 | `$00549A` | 4362 B | 1214 | 1574 words | i6 | 11 / 0 / 2 |
| 6 | `$0065A4` | 6284 B | 3052 | 1616 words | i8 | 46 / 33 / 11 |

Where the samples live is a file offset, because the music file is loaded straight at `$22EDA`
(sample data base `$25B58`); the note's chip pointer is `$25B58 + offset`.

Instrument parameters, read from the driver's code (assembly first, `findings/driver.asm`):

* instrument byte +0 = sample number, +1 = envelope-table index, +2 = effect flags,
  +7 = effect step, +$E = vibrato-table index (0 = none), +$F = vibrato delay in frames.
* All nine instruments have **flags $00**, so the per-note effect code (`$23264-$23362`:
  arpeggio, volume slide, portamento) is never taken in these three songs.
* Volume comes from an **envelope**: the driver walks 3-byte steps (frames between changes,
  volume delta, repeats before the next step) and clamps at 0-64 (`$233A8-$2341C`). Which
  instrument uses which entry is in `findings/instruments.txt`; the entries themselves are
  printed there as raw bytes. What I measured in the chip log is the result: every note's volume
  moves one step per frame (e.g. song 0's first ch1 note: 21,22,23,24,... up to 48), no note
  holds a constant volume, and no note was silent.
* **Vibrato** only on instruments 2 and 3 (samples 1 and 2): vibrato-table index 6, after a
  32-frame delay. It is a triangle: the period moves by the step in the table at `$235AC`
  (count) / `$235BF` (period step), flipping direction each time the count runs out
  (`$231A4-$2323A`). In song 0, channel 3's first note shows it: period 421 -> 427, +1 per frame.
* The final chip volume is the envelope value times the driver's master volume (`$6(a1)`, set to
  $40 = 64 at init) divided by 64 (`$2348E-$2349A`). No master fades were seen in these songs.

## How a note is played (what the log shows)

The driver rewrites LC/LEN/PER/VOL for all four channels **every frame** (`$2346E-$2349A`); the
chip sees about 22 writes per frame. A *new note* is marked by a write to DMACON with a clear bit
for that channel (`$0001/$0002/$0004/$0008`, from `$2312A`), which is followed inside the same
frame by PER = `$7E` (a deliberate inaudible period while the pointer is set up), then
LC = sample start + LEN = the sample's full length, and then at the end of the frame
LC = start + loop offset, LEN = loop length. The score is built from those markers: 1591 notes in
song 0, 1417 in song 1, 396 in song 2 (e.g. song 0: 690 on channel 0, 489 on channel 1,
341 on channel 2, 71 on channel 3).

Pitch: the score names each period with the standard ProTracker table (finetune 0, 428 = C-2).
The driver's own period table at `$23652` is **not** the ProTracker one and is slightly detuned -
every period I measured sits 16-18 cents flat of the nearest ProTracker note (424 vs 428 = C-2,
282 vs 285 = G-2, and so on), so that is the driver's own tuning, not a mistake in the naming.
Periods off the table are flagged in `score_N.txt`; the raw period is printed for every note, so
nothing is lost.

Also visible in the log and not baked into the notes: per-frame period changes inside a note
(vibrato, listed per note in `score_N.txt`) and per-frame volume changes (the envelope, listed
as `volume envelope 40,41,42,...`).

## MIDI

`score_N.mid` is format 1, one track per channel plus a tempo track. Division is 1000 ticks per
quarter and the tempo is 1 000 000 us per quarter, i.e. 1000 ticks = 1 second, so 20 ticks =
1 frame = 20 ms exactly: the file carries the driver's real 50 Hz frame timing, and every note
starts and ends on its measured frame. Velocity is the driver's start volume scaled 0-64 to
0-127.

## Check (the mix)

`out/song_N.wav` is rendered from the same write log: 44.1 kHz stereo, channel 0+3 on the left
and 1+2 on the right. Peaks 0.500 / 0.463 / 0.500, no clipping. I could not do the online
comparison (no web access in this run), so there is no reference recording to compare against.

One caveat, stated plainly: the driver rewrites the channel pointer every frame, which a real
Paula would apply as soon as the channel's current buffer runs out. The render therefore models
Paula as "the pointer written now takes effect at the next buffer boundary, the length reloads at
the boundary" - under that model a one-shot sample plays once and then holds its 1-word loop, and
a looping sample loops from its loop point seamlessly, which is what the driver's design implies.
Rendering with a model where the pointer jumps mid-buffer instead truncates every note to about
one frame of audio and turns the mix into a 50 Hz buzz; I did not use that model. The score and
the instrument table do not depend on this choice - they come from the writes and the driver's
own memory, not from the render.

## Lessons

* The one thing that unlocked the whole job was finding the driver's *own* note-start marker in
  the disassembly (the per-channel DMACON clear at `$2312A`) rather than guessing from the chip
  writes. Without it every frame looks like four new notes, because the driver rewrites the
  pointer, length, period and volume every single frame.
* Restarting the emulator once per song is cheap (0.2 s for a 2:41 song) once the log hook is in
  the custom-register write path - much cheaper than trying to model the format from the notes.
* Detecting the song loop by hashing the whole player state did not work (some counters never
  repeat exactly); comparing the (pattern number, sequence position) pair for all four channels
  and requiring real progress first did.
* `music.py` matched the driver everywhere it was checked (instrument table count, sample layout,
  sequence flags); its "repeat = 1 means one-shot" reading is what the driver's one-shot samples
  actually are.
