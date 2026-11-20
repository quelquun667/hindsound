#!/usr/bin/env bash
# DIAG: records one playback stream with parecord, with its defaults and with the settings of the app, while another plays.
set -u
T="$RUNNER_TEMP/probe"
mkdir -p "$T"
python3 - "$T" <<'PY'
import sys, struct, math
d = sys.argv[1]
def tone(name, hz, sec):
    n = 48000 * sec
    data = b''.join(struct.pack('<hh', s, s) for s in (int(0.5 * 32767 * math.sin(2 * math.pi * hz * i / 48000)) for i in range(n)))
    with open(d + '/' + name, 'wb') as f:
        f.write(b'RIFF' + struct.pack('<I', 36 + len(data)) + b'WAVEfmt ' + struct.pack('<IHHIIHH', 16, 1, 2, 48000, 192000, 4, 16) + b'data' + struct.pack('<I', len(data)) + data)
tone('app.wav', 700, 14)
tone('other.wav', 2300, 16)
PY
cat > "$T/level.py" <<'PY'
import sys, struct, math
path = sys.argv[1]
raw = open(path, 'rb').read()
n = len(raw) // 8
x = struct.unpack('<%df' % (2 * n), raw[:8 * n])
def level(start, frames, hz):
    c = 2 * math.cos(2 * math.pi * hz / 48000)
    p = q = 0.0
    for i in range(frames):
        cur = x[2 * (start + i)] + c * p - q
        q = p
        p = cur
    return 2 * math.sqrt(max(p * p + q * q - c * p * q, 0)) / frames
B = 4800
lv = [level(s, B, 700) for s in range(0, n - B + 1, B)]
print(path.split('/')[-1] + ': ' + str(n) + ' frames, 700 Hz per 100 ms: ' + ' '.join('%.2f' % v for v in lv))
PY
MODULE=$(pactl load-module module-null-sink sink_name=probe_out)
paplay --device=probe_out "$T/other.wav" &
OTHER=$!
sleep 1
paplay --device=probe_out "$T/app.wav" &
APP=$!
sleep 1
IDX=$(pactl list sink-inputs | awk -v pid="$APP" '/^Sink Input #/{i=substr($3,2)} /application.process.id = /{gsub(/"/,"",$3); if ($3==pid) print i}')
echo "probe: app pid $APP, sink input $IDX"
pactl list sinks | grep -E "Name: probe_out|Latency"
rec() {
  name=$1
  shift
  timeout 2 parecord --raw --format=float32le --rate=48000 --channels=2 --device=probe_out.monitor --monitor-stream="$IDX" "$@" "$T/$name.raw"
  python3 "$T/level.py" "$T/$name.raw"
  pactl list sinks | grep -A40 "Name: probe_out" | grep -E "Latency"
}
rec default
rec app --latency-msec=20 --no-move
rec latency20 --latency-msec=20
kill "$OTHER"
sleep 0.5
rec app-alone --latency-msec=20 --no-move
rec default-alone
kill "$APP"
wait
pactl unload-module "$MODULE"
