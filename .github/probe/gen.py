"""Generate the test sounds in the directory given as argument."""
import math
import struct
import sys
import wave

d = sys.argv[1]
SEC = 10


def pcm(freq, rate, ch):
    out = bytearray()
    for i in range(rate * SEC):
        s = int(0.5 * 32767 * math.sin(2 * math.pi * freq * i / rate))
        out += struct.pack("<h", s) * ch
    return bytes(out)


for name, freq, rate, ch in (("A48", 440, 48000, 2), ("B48", 1000, 48000, 2), ("A44m", 440, 44100, 1)):
    open("%s/%s.raw" % (d, name), "wb").write(pcm(freq, rate, ch))
w = wave.open(d + "/A.wav", "wb")
w.setnchannels(2)
w.setsampwidth(2)
w.setframerate(48000)
w.writeframes(pcm(440, 48000, 2))
w.close()
