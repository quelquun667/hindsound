#!/bin/bash
# Probe: can one playback stream be recorded alone (parecord --monitor-stream)?
# Env: ENVNAME (label), SERVER (pulse or pipewire). Runs inside a container.
set -u
W=/tmp/work
mode=${1:-root}

if [ "$mode" = root ]; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq >/tmp/apt.log 2>&1
  pkgs="pulseaudio-utils python3 python3-numpy procps dbus util-linux"
  case "$SERVER" in
    pulse) pkgs="$pkgs pulseaudio" ;;
    pipewire) pkgs="$pkgs pipewire pipewire-bin pipewire-pulse wireplumber" ;;
  esac
  apt-get install -y -qq --no-install-recommends $pkgs >>/tmp/apt.log 2>&1 \
    || { tail -30 /tmp/apt.log; echo "RESULT env=$ENVNAME setup fail"; exit 2; }
  id tester >/dev/null 2>&1 || useradd -m -u 2000 tester
  mkdir -p $W
  chown tester $W
  exec runuser -u tester -- env ENVNAME="$ENVNAME" SERVER="$SERVER" bash /probe/probe-app.sh inner
fi

if [ "$mode" = inner ]; then
  export XDG_RUNTIME_DIR=/tmp/xdg-tester
  mkdir -p -m 700 "$XDG_RUNTIME_DIR"
  exec dbus-run-session -- bash /probe/probe-app.sh body
fi

# ---- body ----
cd $W
fails=0
emit() { echo "RESULT env=$ENVNAME $1 $2 $3"; }
AN=""
check() { # name file mode
  AN=$(python3 -I /probe/analyze.py "$2" "$3" 2>&1)
  if [ $? -eq 0 ]; then emit "$1" pass "$AN"; return 0; fi
  emit "$1" fail "$AN"; return 1
}
wait_ready() { local i; for i in $(seq 40); do pactl info >/dev/null 2>&1 && return 0; sleep 0.5; done; return 1; }
idx_of() { pactl list sink-inputs | awk -v n="$1" '/^Sink Input #/{id=substr($3,2)} index($0,"media.name = \"" n "\""){print id; exit}'; }
wait_idx() { local i x; for i in $(seq 30); do x=$(idx_of "$1"); [ -n "$x" ] && { echo "$x"; return; }; sleep 0.2; done; }
prop_of() { pactl list sink-inputs | awk -v i="$1" -v k="$2" '/^Sink Input #/{cur=substr($3,2)} cur==i && index($0,k " = "){s=$0; sub(/^[^"]*"/,"",s); sub(/"[^"]*$/,"",s); print s; exit}'; }
play() { # name file rate channels
  ( while :; do cat "$2"; done | pacat --playback --stream-name="$1" --rate="$3" --channels="$4" --format=s16le 2>>$W/pacat.err ) &
}
stop_players() { pkill pacat 2>/dev/null; pkill paplay 2>/dev/null; pkill parecord 2>/dev/null; sleep 0.7; }
rec_stream() { # idx out [device]
  if [ -n "${3:-}" ]; then
    timeout -k 3 -s INT 2.5 parecord -d "$3" --monitor-stream="$1" --format=float32le --rate=48000 --channels=2 --raw "$2" 2>"$2.err"
  else
    timeout -k 3 -s INT 2.5 parecord --monitor-stream="$1" --format=float32le --rate=48000 --channels=2 --raw "$2" 2>"$2.err"
  fi
}

python3 -I /probe/gen.py $W

echo "== start server ($SERVER)"
if [ "$SERVER" = pulse ]; then
  pulseaudio -n -D --exit-idle-time=-1 --log-target=file:$W/server.log \
    -L "module-null-sink sink_name=probe" -L module-native-protocol-unix
else
  pipewire >$W/pw.log 2>&1 &
  sleep 1
  wireplumber >$W/wp.log 2>&1 &
  sleep 1
  pipewire-pulse >$W/pp.log 2>&1 &
fi
if ! wait_ready; then
  echo "server did not start"; tail -20 $W/*.log 2>/dev/null
  emit setup fail "server did not start"; exit 2
fi
[ "$SERVER" = pipewire ] && pactl load-module module-null-sink sink_name=probe >/dev/null
sleep 1
pactl set-default-sink probe
echo "== pactl info"
pactl info | grep -E 'Server (Name|Version)|Default Sink|Sample'
emit server info "$(pactl info | grep -E 'Server (Name|Version)' | tr '\n' ';')"

# ---- T1 ----
echo "== T1"
play probeA A48.raw 48000 2; play probeB B48.raw 48000 2
ia=$(wait_idx probeA); ib=$(wait_idx probeB)
echo "indices A=$ia B=$ib"
sleep 1
timeout -k 3 -s INT 2.5 parecord -d probe.monitor --format=float32le --rate=48000 --channels=2 --raw wit.raw 2>wit.err
check T1witness wit.raw both
DEV=""; t1=0
rec_stream "$ia" t1.raw; cat t1.raw.err
if check T1 t1.raw A; then t1=1; fi
rec_stream "$ia" t1d.raw probe.monitor; cat t1d.raw.err
if check T1_d_monitor t1d.raw A; then t1=1; [ -z "$DEV" ] && DEV=probe.monitor; fi
if [ -f t1.raw ] && python3 -I /probe/analyze.py t1.raw A >/dev/null 2>&1; then DEV=""; fi
[ $t1 -eq 1 ] || fails=$((fails+1))
stop_players

# ---- T2 ----
echo "== T2"
play probeA A44m.raw 44100 1
ia=$(wait_idx probeA); sleep 1
rec_stream "$ia" t2.raw "$DEV"; cat t2.raw.err
check T2 t2.raw f440
stop_players

# ---- T3 ----
echo "== T3"
play probeA A48.raw 48000 2
ia=$(wait_idx probeA); sleep 1
( s=$(date +%s.%N)
  timeout -k 3 -s INT 8 parecord ${DEV:+-d $DEV} --monitor-stream="$ia" --format=float32le --rate=48000 --channels=2 --raw t3.raw 2>t3.err
  echo "$? $(echo "$(date +%s.%N) - $s" | bc 2>/dev/null || date +%s.%N)" >t3.rc ) &
rp=$!
sleep 1.5
pkill -f -- "--stream-name=probeA"
wait $rp
bytes=$(stat -c %s t3.raw)
echo "parecord stderr:"; cat t3.err
emit T3 info "rc_and_elapsed=$(cat t3.rc) (rc 124 = still running after 8 s timeout) bytes=$bytes (384000 per second of float32 stereo 48k) err=$(tr '\n' '|' <t3.err)"
stop_players

# ---- T4 ----
echo "== T4"
play probeA A48.raw 48000 2; play probeB B48.raw 48000 2
ia=$(wait_idx probeA); ib=$(wait_idx probeB); sleep 1
rec_stream "$ia" t4a.raw "$DEV" & p1=$!
rec_stream "$ib" t4b.raw "$DEV" & p2=$!
wait $p1 $p2
cat t4a.raw.err t4b.raw.err
ok4=0
check T4a t4a.raw A && ok4=$((ok4+1))
check T4b t4b.raw B && ok4=$((ok4+1))
[ $ok4 -eq 2 ] && emit T4 pass "both" || { emit T4 fail "ok=$ok4"; fails=$((fails+1)); }
stop_players

# ---- T5 ----
echo "== T5"
printf '#!/bin/sh\npaplay --stream-name=probeA "$1"\ntrue\n' >child.sh
sh child.sh $W/A.wav &
shpid=$!
ia=$(wait_idx probeA)
appid=$(prop_of "$ia" application.process.id)
chain=""; p=$appid; ok5=fail
for i in 1 2 3 4 5 6; do
  [ -z "$p" ] || [ "$p" = 0 ] && break
  chain="$chain $p($(cat /proc/$p/comm 2>/dev/null))"
  [ "$p" = "$shpid" ] && ok5=pass
  p=$(sed 's/.*) //' /proc/$p/stat 2>/dev/null | awk '{print $2}')
done
emit T5 $ok5 "application.process.id=$appid shell_pid=$shpid chain=[$chain ]"
stop_players

# ---- T6 ----
if [ "$SERVER" = pipewire ]; then
  echo "== T6"
  play probeA A48.raw 48000 2
  ia=$(wait_idx probeA); sleep 1
  read nid nserial < <(pw-dump | python3 -I /probe/pwnode.py probeA)
  echo "node id=$nid serial=$nserial"
  ok6=fail
  for tgt in "$nid" "$nserial"; do
    [ -n "$tgt" ] || continue
    timeout -k 3 -s INT 2.5 pw-record --target "$tgt" --rate 48000 --channels 2 --format f32 t6.wav 2>t6.err; cat t6.err
    if check "T6_target_$tgt" t6.wav A; then ok6=pass; break; fi
  done
  emit T6 $ok6 "id=$nid serial=$nserial"
  stop_players
fi

echo "== logs"
tail -5 $W/*.log 2>/dev/null
echo "FAILS=$fails"
[ $fails -eq 0 ]
