#!/usr/bin/env bash
# Throwaway probe of the macOS port, run by .github/workflows/mac-probe.yml on a GitHub macOS runner. Not part of the app.
#
# Each point it establishes is printed as one line "PROBE <name> <result>", and gathered in $W/probe.log for the summary.
# The phases run in this order, one step of the workflow each; a failed point is reported and the phase goes on:
#   terrain    macOS version, architecture, SIP, the TCC databases (read, write, entries), the audio devices
#   blackhole  the BlackHole 2ch loopback driver, made the default output and input
#   build      the Core Audio probe (native/capture.m) built twice: build 1 and build 2, different binaries
#   sign       a throwaway code signing certificate in a temporary keychain; four .app: ad hoc and certificate, build 1
#              and 2; their designated requirements, codesign --verify, spctl, and whether build 2 satisfies build 1's
#   audio      stage 1, from the plain program: which way of starting the IO returns (output and input IOProc, AudioQueue
#              input, a global tap through three aggregates and three readers); stage 2, from the .app with the first
#              way that delivered data: global tap without and with permission, one process, the microphone, then
#              build 2 in place of build 1 with no new permission; certificate, then ad hoc
#   summary    all the PROBE lines
#   cleanup    deletes the temporary keychain and the certificate files
# Every test runs in a process of its own with a limit per step: a hang ends that test only, with a sample of its stacks.
# The window, pasteboard, hotkey, login item and font points were proven by the first version of this probe (commit
# 498b7bd) and are no longer run.
# The bash of macOS is 3.2: no associative arrays, no mapfile, no empty "${array[@]}" under set -u.
set -uo pipefail

phase="${1:?usage: probe.sh terrain|blackhole|build|sign|audio|summary|cleanup}"
here="$(cd "$(dirname "$0")" && pwd)"
W="${RUNNER_TEMP:-/tmp}/mac-probe"
mkdir -p "$W"

readonly SYS_TCC='/Library/Application Support/com.apple.TCC/TCC.db'
readonly USER_TCC="$HOME/Library/Application Support/com.apple.TCC/TCC.db"
readonly ADHOC_ID='com.hindsound.probe.adhoc'
readonly CERT_ID='com.hindsound.probe.cert'
readonly INSTALL_DIR="$HOME/Applications"

probe() {
  local name="$1"
  shift
  echo "PROBE $name $*" | tee -a "$W/probe.log"
}

oneline() { tr '\n\t' '  ' | sed -e 's/  */ /g' -e 's/ $//'; }

tcc_db() { if [ "$1" = system ]; then echo "$SYS_TCC"; else echo "$USER_TCC"; fi; }

# The designated requirement of a signed bundle, as text.
dr_of() { codesign -d -r- "$1" 2>&1 | sed -n 's/^#* *designated => //p' | head -n 1; }

# --- terrain ------------------------------------------------------------------------------------------------------------

terrain() {
  probe terrain.sw_vers "$(sw_vers | oneline)"
  probe terrain.uname_m "$(uname -m)"
  probe terrain.csrutil "$(csrutil status 2>&1 | oneline)"
  probe terrain.user "$(id -un) uid=$(id -u) console_owner=$(stat -f %Su /dev/console) bash=$BASH_VERSION"
  probe terrain.sudo "$(sudo -n true 2>&1 && echo passwordless || echo refused)"
  local which db
  for which in system user; do
    db="$(tcc_db "$which")"
    probe "terrain.tcc_${which}_read" "$(sudo sqlite3 "$db" 'select count(*) from access;' 2>&1 | oneline)"
    probe "terrain.tcc_${which}_write" "$(sudo sqlite3 "$db" "insert or replace into access (service, client, client_type, auth_value, auth_reason, auth_version, flags) values ('kTCCServiceProbeWriteTest', 'com.hindsound.probe.writetest', 0, 0, 4, 1, 0); select 'inserted=' || count(*) from access where service = 'kTCCServiceProbeWriteTest'; delete from access where service = 'kTCCServiceProbeWriteTest'; select 'after_delete=' || count(*) from access where service = 'kTCCServiceProbeWriteTest';" 2>&1 | oneline)"
    probe "terrain.tcc_${which}_services" "$(sudo sqlite3 "$db" "select service || '=' || count(*) from access group by service;" 2>&1 | oneline)"
    echo "--- $which TCC.db: schema and entries"
    sudo sqlite3 "$db" '.schema access' 2>&1
    sudo sqlite3 -separator ' | ' "$db" 'select service, client, client_type, auth_value, auth_reason, length(csreq) from access order by service, client;' 2>&1
  done
  echo "--- system_profiler SPAudioDataType (before BlackHole)"
  system_profiler SPAudioDataType 2>&1
  probe terrain.audio_devices "$(system_profiler SPAudioDataType 2>/dev/null | grep -E '^        [^ ].*:$' | sed -e 's/^ *//' -e 's/:$//' | paste -sd ',' -)"
  probe terrain.display "$(system_profiler SPDisplaysDataType 2>/dev/null | grep -E 'Resolution|Display Type|Retina|UI Looks like' | oneline)"
}

# --- blackhole ----------------------------------------------------------------------------------------------------------

blackhole() {
  export HOMEBREW_NO_AUTO_UPDATE=1 HOMEBREW_NO_INSTALL_CLEANUP=1 HOMEBREW_NO_ENV_HINTS=1
  brew install --cask blackhole-2ch > "$W/brew-blackhole.log" 2>&1
  local rc=$?
  tail -n 15 "$W/brew-blackhole.log"
  probe blackhole.brew_install "rc=$rc"
  brew install switchaudio-osx > "$W/brew-switchaudio.log" 2>&1
  probe blackhole.switchaudio_install "rc=$?"
  probe blackhole.driver_files "$(ls /Library/Audio/Plug-Ins/HAL 2>&1 | oneline)"
  sudo killall coreaudiod
  probe blackhole.coreaudiod_restart "rc=$?"
  sleep 5
  probe blackhole.devices "$(SwitchAudioSource -a -f cli 2>&1 | oneline)"
  local type
  for type in output input system; do
    SwitchAudioSource -t "$type" -s 'BlackHole 2ch' > /dev/null 2>&1
    rc=$?
    probe "blackhole.default_$type" "rc=$rc now=$(SwitchAudioSource -t "$type" -c 2>&1)"
  done
  # Does the IO of BlackHole run at all: afplay of a 1 s tone ends after about 1 s if it does, and hangs if it does not.
  python3 -c "import wave; f = wave.open('$W/beep.wav', 'wb'); f.setnchannels(1); f.setsampwidth(2); f.setframerate(48000); f.writeframes(bytes(96000)); f.close()"
  local start=$SECONDS pid
  afplay "$W/beep.wav" &
  pid=$!
  ( sleep 10; kill "$pid" 2> /dev/null ) &
  wait "$pid"
  rc=$?
  probe blackhole.afplay_1s "rc=$rc seconds=$((SECONDS - start)) (rc 143 = killed after 10 s: no IO)"
  echo "--- system_profiler SPAudioDataType (after BlackHole)"
  system_profiler SPAudioDataType 2>&1
}

# --- build --------------------------------------------------------------------------------------------------------------

build() {
  local v
  for v in 1 2; do
    clang -fobjc-arc -Wall -arch arm64 -mmacosx-version-min=14.4 -DPROBE_BUILD="$v"       -framework Foundation -framework CoreAudio -framework AudioToolbox -framework AVFoundation       -o "$W/capture-v$v" "$here/native/capture.m" 2>&1
    probe "native.build_v$v" "rc=$? sdk=$(xcrun --show-sdk-version 2>&1) clang=$(clang --version | head -n 1)"
  done
  probe native.v1_v2_differ "$(cmp -s "$W/capture-v1" "$W/capture-v2" && echo no || echo yes)"
}

# --- sign ---------------------------------------------------------------------------------------------------------------

make_app() { # build bundle_id version app
  local build="$1" id="$2" version="$3" app="$4"
  rm -rf "$app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Frameworks"
  cp "$W/capture-v$build" "$app/Contents/MacOS/HindsoundProbe"
  sed -e "s/@ID@/$id/" -e "s/@VERSION@/$version/" "$here/Info.plist.in" > "$app/Contents/Info.plist"
}

codesign_one() { # identity file
  if [ "$1" = - ]; then
    codesign --force --timestamp=none --sign - "$2"
  else
    codesign --force --timestamp=none --sign "$1" --keychain "$W/probe.keychain-db" "$2"
  fi
}

sign_app() { # app identity
  local app="$1" identity="$2" rc=0 file
  while IFS= read -r file; do
    codesign_one "$identity" "$file" || rc=1
  done < <(find "$app/Contents/Frameworks" "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/HindsoundProbe")
  codesign_one "$identity" "$app" || rc=1
  return $rc
}

sign() {
  local pw kc="$W/probe.keychain-db" rc hash
  pw="$(uuidgen)"
  cat > "$W/cert.cnf" <<'EOF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = Hindsound Probe Throwaway
[ext]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
subjectKeyIdentifier = hash
EOF
  /usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -keyout "$W/cert.key" -out "$W/cert.pem" -days 2 \
    -config "$W/cert.cnf" > "$W/cert.log" 2>&1
  probe cert.create "rc=$? $(/usr/bin/openssl version)"
  /usr/bin/openssl pkcs12 -export -inkey "$W/cert.key" -in "$W/cert.pem" -out "$W/cert.p12" -passout "pass:$pw" \
    -name 'Hindsound Probe Throwaway' >> "$W/cert.log" 2>&1
  probe cert.p12 "rc=$?"
  security create-keychain -p "$pw" "$kc" && security set-keychain-settings "$kc" && security unlock-keychain -p "$pw" "$kc"
  probe cert.keychain "rc=$?"
  # shellcheck disable=SC2046
  security list-keychains -d user -s "$kc" $(security list-keychains -d user | tr -d '"')
  security import "$W/cert.p12" -k "$kc" -P "$pw" -T /usr/bin/codesign >> "$W/cert.log" 2>&1
  probe cert.import "rc=$?"
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$pw" "$kc" > /dev/null 2>> "$W/cert.log"
  probe cert.partition_list "rc=$?"
  cat "$W/cert.log"
  probe cert.find_identity "$(security find-identity -p codesigning "$kc" 2>&1 | oneline)"
  hash="$(/usr/bin/openssl x509 -in "$W/cert.pem" -noout -fingerprint -sha1 | sed -e 's/.*=//' -e 's/://g')"
  echo "$hash" > "$W/cert.sha1"

  local v kind app out
  for v in 1 2; do
    make_app "$v" "$ADHOC_ID" "$v" "$W/apps/adhoc-v$v/HindsoundProbe.app"
    sign_app "$W/apps/adhoc-v$v/HindsoundProbe.app" - > "$W/sign-adhoc-v$v.log" 2>&1
    probe "sign.adhoc_v$v" "rc=$? $(oneline < "$W/sign-adhoc-v$v.log")"
    make_app "$v" "$CERT_ID" "$v" "$W/apps/cert-v$v/HindsoundProbe.app"
    sign_app "$W/apps/cert-v$v/HindsoundProbe.app" "$hash" > "$W/sign-cert-v$v.log" 2>&1
    probe "sign.cert_v$v" "rc=$? $(oneline < "$W/sign-cert-v$v.log")"
  done
  for kind in adhoc cert; do
    for v in 1 2; do
      app="$W/apps/$kind-v$v/HindsoundProbe.app"
      probe "sign.${kind}_v$v.dr" "$(dr_of "$app")"
      probe "sign.${kind}_v$v.cdhash" "$(codesign -dvvv "$app" 2>&1 | grep -E '^(CDHash|Signature|Authority|TeamIdentifier|Identifier)=' | oneline)"
      out="$(codesign --verify --deep --strict --verbose=2 "$app" 2>&1)"
      probe "sign.${kind}_v$v.verify" "rc=$? $(echo "$out" | oneline)"
      out="$(spctl -a -vv "$app" 2>&1)"
      probe "sign.${kind}_v$v.spctl" "rc=$? $(echo "$out" | oneline)"
    done
    # What TCC checks at launch: does build 2 satisfy the requirement recorded for build 1?
    out="$(codesign --verify -R "=$(dr_of "$W/apps/$kind-v1/HindsoundProbe.app")" "$W/apps/$kind-v2/HindsoundProbe.app" 2>&1)"
    probe "sign.$kind.v2_satisfies_v1_dr" "rc=$? $(echo "$out" | oneline)"
  done
}

# --- audio --------------------------------------------------------------------------------------------------------------

readonly STEP_LIMIT=12
readonly CAPTURE_SECONDS=2
# The ways to read a global tap, in order of preference: aggregate (null: main sub-device the Null Audio Device, whose
# input is silent; taponly: the tap alone; out: main sub-device the default output, as AudioCap, but BlackHole's input
# then also brings the tone) and reader.
readonly COMBOS='null:ioproc null:auhal null:queue taponly:ioproc taponly:auhal taponly:queue out:ioproc'
log_shown=0

install_app() { # source destination
  rm -rf "$2"
  mkdir -p "$(dirname "$2")"
  ditto "$1" "$2"
}

make_sounds() {
  python3 - "$W" <<'EOF'
import array, math, sys, wave
folder, rate, seconds = sys.argv[1], 48000, 40
tone = array.array('h')
for i in range(rate * seconds):
    v = int(0.5 * 32767 * math.sin(2 * math.pi * 1000 * i / rate))
    tone.append(v)
    tone.append(v)
for name, data in (('tone.wav', tone.tobytes()), ('silence.wav', bytes(rate * seconds * 4))):
    with wave.open(f'{folder}/{name}', 'wb') as f:
        f.setnchannels(2)
        f.setsampwidth(2)
        f.setframerate(rate)
        f.writeframes(data)
EOF
  probe audio.sounds "rc=$? $(ls -l "$W/tone.wav" "$W/silence.wav" 2>&1 | awk '{print $5, $NF}' | oneline)"
}

tcc_rows() { # tag
  local which
  for which in system user; do
    probe "$1.tcc_rows_$which" "$(sudo sqlite3 "$(tcc_db "$which")" "select service || ':' || client || ':auth=' || auth_value || ':reason=' || auth_reason || ':csreq=' || ifnull(length(csreq), 0) from access where client like 'com.hindsound.probe%';" 2>&1 | oneline)"
  done
}

# Writes the permission of system audio capture and microphone for a bundle id, with the requirement of the given build.
grant() { # tag app id
  local tag="$1" app="$2" id="$3" req hex which service rc=0
  req="$(dr_of "$app")"
  csreq -r "=$req" -b "$W/csreq-$tag.bin" || rc=1
  hex="$(xxd -p "$W/csreq-$tag.bin" | tr -d '\n')"
  for which in system user; do
    for service in kTCCServiceAudioCapture kTCCServiceMicrophone; do
      sudo sqlite3 "$(tcc_db "$which")" "insert or replace into access (service, client, client_type, auth_value, auth_reason, auth_version, csreq, flags, last_modified) values ('$service', '$id', 0, 2, 3, 1, X'$hex', 0, CAST(strftime('%s','now') AS INTEGER));" || rc=1
    done
  done
  sudo killall tccd 2> /dev/null
  killall tccd 2> /dev/null
  sleep 2
  probe "$tag.grant" "rc=$rc id=$id requirement=$req"
  tcc_rows "$tag"
}

# The frames of a sample that tell where a thread waits.
stacks() { # sample-file
  if [ ! -s "$1" ]; then
    echo "(no sample written)"
    return
  fi
  echo "--- stacks (sample): audio, TCC and wait frames"
  grep -E 'AudioDeviceStart|AudioOutputUnitStart|AudioQueueStart|HALC_|HALB_|HAL_|AudioObject|AudioHardware|AUHAL|TCC|tcc_|semaphore|mach_msg|_dispatch_sync|pthread_cond|xpc_connection_send_message_with_reply_sync|runTest|readIOProc|readAUHAL|readQueue|tapTest|startTone|outIOProc' "$1" \
    | sed -E -e 's/^[ +!:|]*//' -e 's/ +\[0x[0-9a-f,]+\]//' -e 's/  +/ /g' | awk '!seen[$0]++' | head -n 60
}

# The recent log of coreaudiod, tccd and the probe; at most three times per run, as log show is slow.
recent_log() { # since
  [ "$log_shown" -ge 3 ] && return
  log_shown=$((log_shown + 1))
  echo "--- log since $1: coreaudiod, tccd, the probe"
  perl -e 'alarm 45; exec @ARGV' log show --start "$1" --style compact \
    --predicate 'process == "coreaudiod" OR process == "tccd" OR process == "HindsoundProbe" OR process == "capture-v1"' 2> /dev/null \
    | grep -iE 'tap|tcc|AudioCapture|aggregate|start|IOProc|error|fail|denied|prompt|timeout|wait' | cut -c1-260 | tail -n 40
}

# Runs one test in a process of its own while afplay plays the 1 kHz tone and the silence (their pids replace the words
# TONE and SILENT in the arguments). mode: cli (the plain program, a child of this shell; the runner is then the process
# TCC asks about) or open (the .app through Launch Services: its own responsible process for TCC). A test that hangs
# prints "stuck", and its stacks and the recent log are printed.
run_case() { # tag mode app hal-runloop test [args]
  local tag="$1" mode="$2" app="$3" halrl="$4" out="$W/run-$1.out" tone silent pid arg since watchdog
  shift 4
  rm -f "$out" "$out.err" "$out.sample"
  afplay "$W/tone.wav" &
  tone=$!
  afplay "$W/silence.wav" &
  silent=$!
  sleep 1
  local args=()
  for arg in "$@"; do
    case "$arg" in
      TONE) args+=("$tone") ;;
      SILENT) args+=("$silent") ;;
      *) args+=("$arg") ;;
    esac
  done
  since="$(date '+%Y-%m-%d %H:%M:%S')"
  echo "=== $tag ($mode, hal run loop $halrl: ${args[*]})"
  if [ "$mode" = open ]; then
    open -n -W --stdout "$out" --stderr "$out.err" "$app" --args "$tag" "$STEP_LIMIT" "$CAPTURE_SECONDS" "$out.sample" \
      "$halrl" "${args[@]}" &
  else
    "$W/capture-v1" "$tag" "$STEP_LIMIT" "$CAPTURE_SECONDS" "$out.sample" "$halrl" "${args[@]}" > "$out" 2> "$out.err" &
  fi
  pid=$!
  ( sleep 60; kill "$pid"; pkill -f 'HindsoundProbe.app/Contents/MacOS/HindsoundProbe' ) > /dev/null 2>&1 &
  watchdog=$!
  wait "$pid"
  kill "$watchdog" 2> /dev/null
  pkill -f 'HindsoundProbe.app/Contents/MacOS/HindsoundProbe' 2> /dev/null
  kill "$tone" "$silent" 2> /dev/null
  wait "$tone" "$silent" 2> /dev/null
  grep '^PROBE ' "$out" 2> /dev/null | tee -a "$W/probe.log"
  grep -q '^PROBE ' "$out" 2> /dev/null || probe "$tag.no_output" "the process wrote nothing"
  [ -s "$out.err" ] && sed 's/^/stderr: /' "$out.err" | tail -n 20
  if grep -q "^PROBE $tag.stuck " "$out" 2> /dev/null; then
    stacks "$out.sample"
    recent_log "$since"
  fi
  return 0
}

works() { # tag: did its capture deliver data (sound or silence)
  grep -qE '\.result (SIGNAL|SILENCE_EXACT_ZERO|SILENCE_NEAR_ZERO) ' "$W/run-$1.out" 2> /dev/null
}

audio() {
  make_sounds
  local method='' combo agg reader kind id app

  # Stage 1, from the plain program: does any start return, and which way of reading a tap delivers data.
  run_case s1_perm cli - null perm
  run_case s1_out_ioproc cli - null out_ioproc
  run_case s1_in_ioproc cli - null in_ioproc
  run_case s1_in_queue cli - null in_queue
  for combo in $COMBOS; do
    agg="${combo%%:*}"
    reader="${combo##*:}"
    run_case "s1_${agg}_${reader}" cli - null tap global "$agg" "$reader"
    if [ -z "$method" ] && works "s1_${agg}_${reader}"; then method="$combo"; fi
  done
  run_case s1_taponly_ioproc_mainrl cli - main tap global taponly ioproc
  probe audio.method_cli "${method:-none}"

  # Stage 2, from the .app: certificate first (the update test that matters), then ad hoc.
  for kind in cert adhoc; do
    if [ "$kind" = adhoc ]; then id="$ADHOC_ID"; else id="$CERT_ID"; fi
    app="$INSTALL_DIR/HindsoundProbe-$kind.app"
    install_app "$W/apps/$kind-v1/HindsoundProbe.app" "$app"
    run_case "${kind}_v1_noauth_perm" open "$app" null perm
    if [ -n "$method" ]; then
      run_case "${kind}_v1_noauth_excl" open "$app" null tap global_excl_self "${method%%:*}" "${method##*:}"
    fi
    [ "$kind" = cert ] && run_case "${kind}_v1_noauth_mic" open "$app" null in_ioproc
    grant "${kind}_v1" "$app" "$id"
    run_case "${kind}_v1_auth_perm" open "$app" null perm
    if [ -z "$method" ]; then
      for combo in $COMBOS; do
        run_case "${kind}_v1_auth_find_${combo%%:*}_${combo##*:}" open "$app" null tap global_excl_self "${combo%%:*}" "${combo##*:}"
        if works "${kind}_v1_auth_find_${combo%%:*}_${combo##*:}"; then
          method="$combo"
          break
        fi
      done
      probe "audio.method_app_$kind" "${method:-none}"
    fi
    if [ -n "$method" ]; then
      agg="${method%%:*}"
      reader="${method##*:}"
      run_case "${kind}_v1_auth_excl" open "$app" null tap global_excl_self "$agg" "$reader"
      if [ "$kind" = cert ]; then
        run_case "${kind}_v1_auth_global" open "$app" null tap global "$agg" "$reader"
        run_case "${kind}_v1_auth_proc_tone" open "$app" null tap proc "$agg" "$reader" TONE
        run_case "${kind}_v1_auth_proc_silent" open "$app" null tap proc "$agg" "$reader" SILENT
        run_case "${kind}_v1_auth_mic" open "$app" null in_ioproc
      fi
    fi
    # Build 2 in place of build 1, same path and bundle id, and no new permission.
    install_app "$W/apps/$kind-v2/HindsoundProbe.app" "$app"
    probe "${kind}_v2.installed" "$(dr_of "$app")"
    run_case "${kind}_v2_perm" open "$app" null perm
    if [ -n "$method" ]; then
      run_case "${kind}_v2_excl" open "$app" null tap global_excl_self "${method%%:*}" "${method##*:}"
    fi
    tcc_rows "${kind}_end"
  done
}

# --- summary and cleanup ------------------------------------------------------------------------------------------------

summary() {
  echo "=== All PROBE lines"
  cat "$W/probe.log" 2> /dev/null
}

cleanup() {
  security delete-keychain "$W/probe.keychain-db" 2> /dev/null
  rm -f "$W/cert.key" "$W/cert.pem" "$W/cert.p12" "$W/cert.sha1"
  pkill afplay 2> /dev/null
  echo "Keychain and certificate files removed."
}

case "$phase" in
  terrain | blackhole | build | sign | audio | summary | cleanup) "$phase" ;;
  *) echo "Unknown phase: $phase" >&2; exit 2 ;;
esac
exit 0
