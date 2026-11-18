"""musfile (file 3): Martin Walker's music player + songs + instruments. See re/formats/musfile.md.

Loaded at $22EDA (buffer $8322). Init $22EDA (d1 = song), play $22F6E. Tables at load address:
  $23652  note -> period words (a3)           $236FC  28 longs: sample offset from $25B58
  $2376C  6 bytes per sample (len words, loop offset, repeat words; 1 = one-shot)
  $2388E  instruments, 16 bytes (a5): +0 sample, +2, +7, +$F parameters
  $2391E  pattern offset words -> patterns at $239B4 (75 patterns)
  $23D9A  songs, 16 bytes = 4 channel words (sequence offset from $23DCA) + 8 setting bytes
  $23DCA  sequences: < $FD pattern (bit 6 = flag), $FD tempo byte, $FE loop, $FF stop ($234A8)
Pattern event ($23002-$23146): byte A (bits 0-5 duration, bit 6 effect, bit 7 = extra byte follows:
positive -> instrument, else effect value), then note byte (bit 7 flag, bits 0-6 note); $FF ends.
"""
import struct
from dataclasses import dataclass

LOAD = 0x22EDA
PERIODS, SAMPLE_OFFS, SAMPLE_INFO, INSTRUMENTS = 0x23652, 0x236FC, 0x2376C, 0x2388E
PATTERN_OFFS, PATTERNS, SONGS, SEQUENCES, SAMPLE_DATA = 0x2391E, 0x239B4, 0x23D9A, 0x23DCA, 0x25B58
SLOTS = 28


def _o(a):
    return a - LOAD


@dataclass
class Event:
    duration: int
    effect: bool
    extra: int        # None if no extra byte
    note: int
    note_flag: bool


def samples(d):
    """(slot, pcm, repeat_words) for the slots an instrument plays (+0 byte of each instrument: slots
    0-6). The other 21 slots all point at the same junk offset and no instrument uses them."""
    offs = struct.unpack_from(">%dI" % SLOTS, d, _o(SAMPLE_OFFS))
    used = sorted({ins[0] for ins in instruments(d)})
    out = []
    for i in used:
        ln, lo, rep = struct.unpack_from(">3H", d, _o(SAMPLE_INFO) + 6 * i)
        start = _o(SAMPLE_DATA) + offs[i]
        if ln and start + 2 * ln <= len(d):
            out.append((i, d[start:start + 2 * ln], rep))
    return out


def periods(d):
    return list(struct.unpack_from(">%dH" % ((SAMPLE_OFFS - PERIODS) // 2), d, _o(PERIODS)))


def instruments(d):
    n = (PATTERN_OFFS - INSTRUMENTS) // 16
    return [d[_o(INSTRUMENTS) + 16 * i:_o(INSTRUMENTS) + 16 * (i + 1)] for i in range(n)]


def pattern(d, n):
    off = struct.unpack_from(">H", d, _o(PATTERN_OFFS) + 2 * n)[0]
    if off == 0xFFFF:          # unused slot
        return None
    p = _o(PATTERNS) + off
    events = []
    while d[p] != 0xFF:
        a = d[p]
        p += 1
        extra = None
        if a & 0x80:
            extra = d[p]
            p += 1
        note = d[p]
        p += 1
        events.append(Event(a & 0x3F, bool(a & 0x40), extra, note & 0x7F, bool(note & 0x80)))
    return events


def pattern_count():
    return (PATTERNS - PATTERN_OFFS) // 2


def sequence(d, off):
    """Items of one channel sequence, read like $234A8:
    ("play", repeat, pattern) - byte -> $20 repeat count, next byte -> $24 pattern;
    ("block", n)              - bit 6 set: repeat the following block n (bits 0-5) times;
    ("tempo", v)              - $FD then v -> $0(a1);
    ("stop", pos) / ("jump", pos) - $FE / $FF then position byte; $FE stops all channels
                                ($22FD2), $FF continues from pos (the song loops)."""
    base, p, items = _o(SEQUENCES) + off, 0, []
    while True:
        b = d[base + p]
        if b == 0xFD:
            items.append(("tempo", d[base + p + 1])); p += 2
        elif b >= 0xFE:
            items.append(("stop" if b == 0xFE else "jump", d[base + p + 1]))
            return items
        elif b & 0x40:
            items.append(("block", b & 0x3F)); p += 1
        else:
            items.append(("play", b, d[base + p + 1])); p += 2


def songs(d):
    """3 songs x 4 channel sequences. Init copies the song's 16 bytes to $8(a1); $234A8 reads
    the channel's sequence offset as word $8(a1, channel*2); the last 8 bytes are settings."""
    return [[sequence(d, struct.unpack_from(">H", d, _o(SONGS) + 16 * s + 2 * c)[0]) for c in range(4)]
            for s in range((SEQUENCES - SONGS) // 16)]
