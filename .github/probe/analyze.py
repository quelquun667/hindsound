"""Measure the energy at 440 Hz and 1000 Hz in a recording.

usage: analyze.py FILE MODE
FILE: raw float32 stereo 48000 Hz, or a RIFF wav (int16 or float32).
MODE: A (only 440 Hz, 1000 Hz at least 30 dB below), B (the reverse),
      both (each above -30 dBFS), f440 (peak at 440 +- 2 Hz, level -12..0 dBFS),
      info (print only).
Exit code 0 when the criterion holds.
"""
import struct
import sys

import numpy as np


def load(path):
    data = open(path, "rb").read()
    rate, ch = 48000, 2
    if data[:4] == b"RIFF":
        pos, fmt = 12, None
        while pos + 8 <= len(data):
            cid = data[pos:pos + 4]
            sz = struct.unpack("<I", data[pos + 4:pos + 8])[0]
            if cid == b"fmt ":
                fmt = struct.unpack("<HHIIHH", data[pos + 8:pos + 24])
            if cid == b"data":
                break
            pos += 8 + sz + (sz & 1)
        tag, ch, rate, _, _, bits = fmt
        raw = data[pos + 8:pos + 8 + sz]
        isfloat = tag == 3 or (tag == 0xFFFE and bits == 32)
        width = bits // 8
    else:
        raw, isfloat, width = data, True, 4
    frame = ch * width
    raw = raw[: len(raw) // frame * frame]
    if isfloat:
        x = np.frombuffer(raw, dtype="<f4").astype(np.float64)
    else:
        x = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    return x.reshape(-1, ch).mean(axis=1), rate


def band_db(spec, freqs, win_sum, f0):
    m = (freqs > f0 - 8) & (freqs < f0 + 8)
    amp = 2 * spec[m].max() / win_sum if m.any() else 0.0
    return 20 * np.log10(max(amp, 1e-9))


def main():
    path, mode = sys.argv[1], sys.argv[2]
    x, rate = load(path)
    x = x[int(0.4 * rate):]
    if len(x) < int(0.5 * rate):
        print("n=%d too_short" % len(x))
        return 1
    w = np.hanning(len(x))
    spec = np.abs(np.fft.rfft(x * w))
    freqs = np.fft.rfftfreq(len(x), 1.0 / rate)
    a = band_db(spec, freqs, w.sum(), 440)
    b = band_db(spec, freqs, w.sum(), 1000)
    sel = (freqs > 100) & (freqs < 5000)
    peak = freqs[sel][spec[sel].argmax()] if x.any() else 0.0
    print("a_db=%.1f b_db=%.1f peak=%.1f n=%d" % (a, b, peak, len(x)))
    if mode == "A":
        return 0 if (a > -20 and a - b >= 30) else 1
    if mode == "B":
        return 0 if (b > -20 and b - a >= 30) else 1
    if mode == "both":
        return 0 if (a > -30 and b > -30) else 1
    if mode == "f440":
        return 0 if (abs(peak - 440) <= 2 and -12 <= a <= 0) else 1
    return 0


sys.exit(main())
