"""soundfile (file 4): sample player code + samples + effect table. See re/formats/soundfile.md.

Loaded at $1135C (buffer $831A); `jsr` to its start is the player init ($845C).
Tables (addresses at the load address):
  $1164A  28 longs: sample offset from $118DC
  $116BA  6 bytes per sample: length (words), loop offset, repeat length (1 = one-shot)
  $1175C  envelopes (3-byte steps, read by $114EC-$1156E)
  $1178C  21 effects x 16 bytes: sample, envelope, period word, duration
  $118DC  sample data (8-bit signed)
"""
import struct
import wave
from dataclasses import dataclass

LOAD = 0x1135C
PAL_CLOCK = 3546895
SAMPLE_OFFS, SAMPLE_INFO, ENVELOPES, EFFECTS, DATA = 0x1164A, 0x116BA, 0x1175C, 0x1178C, 0x118DC
SLOTS = 28


@dataclass
class Sample:
    index: int
    offset: int
    length_words: int
    loop_offset: int
    repeat_words: int
    pcm: bytes          # signed 8-bit

    @property
    def loops(self):
        return self.repeat_words > 1


@dataclass
class Effect:
    index: int
    sample: int
    envelope: int
    period: int
    duration: int


def _o(a):
    return a - LOAD


def samples(d):
    """Real samples only: slots whose data lies inside the file (12 of 28)."""
    offs = struct.unpack_from(">%dI" % SLOTS, d, _o(SAMPLE_OFFS))
    out = []
    for i in range(SLOTS):
        ln, lo, rep = struct.unpack_from(">3H", d, _o(SAMPLE_INFO) + 6 * i)
        start = _o(DATA) + offs[i]
        if ln and start + 2 * ln <= len(d):
            out.append(Sample(i, offs[i], ln, lo, rep, d[start:start + 2 * ln]))
    return out


def sample_area_size(d):
    return len(d) - _o(DATA)


def effects(d):
    n = (DATA - EFFECTS) // 16
    out = []
    for i in range(n):
        e = d[_o(EFFECTS) + 16 * i:_o(EFFECTS) + 16 * i + 16]
        out.append(Effect(i, e[0], e[1], struct.unpack_from(">H", e, 2)[0], e[4]))
    return out


def envelopes(d):
    return d[_o(ENVELOPES):_o(EFFECTS)]


def write_wav(pcm, rate, path):
    w = wave.open(path, "wb")
    w.setnchannels(1)
    w.setsampwidth(1)
    w.setframerate(rate)
    w.writeframes(bytes((b + 128) & 255 for b in pcm))   # WAV 8-bit is unsigned
    w.close()
