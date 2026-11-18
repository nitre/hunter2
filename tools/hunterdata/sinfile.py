"""sinfile (file 7): sine and cosine tables in Hunter's own floating-point format.

2048 bytes, loaded at $847A into buffer $83DA; the second 1024 bytes run on into buffer $83E2.
Users ($2C16-$2C30, $5646-$5662, $568C-$56A8) index both with an angle byte * 4
(256 steps per turn) and read one long. See re/formats/sinfile.md.

Each long is two words: exponent E (unsigned) then mantissa M (signed), value = M * 2**-E.
  $83DA = sine, $83E2 = cosine.
"""
import struct

STEPS = 256


def decode(b):
    """-> (sin, cos): two lists of 256 (exponent, mantissa) pairs, exactly as stored."""
    pairs = [struct.unpack_from(">Hh", b, 4 * i) for i in range(2 * STEPS)]
    return pairs[:STEPS], pairs[STEPS:]


def value(pair):
    e, m = pair
    return m * 2.0 ** -e


def encode(sin, cos):
    return b"".join(struct.pack(">Hh", e, m) for e, m in sin + cos)
