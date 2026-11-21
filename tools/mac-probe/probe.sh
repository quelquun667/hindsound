#!/usr/bin/env bash
# Throwaway probe of the macOS port, run by .github/workflows/mac-probe.yml on a GitHub macOS runner. Not part of the app.
#
# Each point it establishes is printed as one line "PROBE <name> <result>", and gathered in $W/probe.log for the summary.
# The phases run in this order, one step of the workflow each; a failed point is reported and the phase goes on:
#   terrain    macOS version, architecture, SIP, the TCC databases (read, write, entries), the audio devices
#   blackhole  the BlackHole 2ch loopback driver, made the default output and input
#   build      SDL 3.2.30 from its pinned source (as tools/linux/build-sdl3.sh), the Objective-C audio library, the C#
#              probe published for osx-arm64 twice (build 1 and build 2: different binaries)
#   sign       a throwaway code signing certificate in a temporary keychain; four .app: ad hoc and certificate, build 1
#              and 2; their designated requirements, codesign --verify, spctl, and whether build 2 satisfies build 1's
#   ui         the window, pasteboard, hotkey, login item and font fallback points, and a screenshot of the window
#   audio      the capture matrix: without permission, with permission written into TCC, then build 2 in place of
#              build 1 with no new permission; ad hoc and certificate
#   summary    all the PROBE lines
#   cleanup    deletes the temporary keychain and the certificate files
# The bash of macOS is 3.2: no associative arrays, no mapfile, no empty "${array[@]}" under set -u.
set -uo pipefail

phase="${1:?usage: probe.sh terrain|blackhole|build|sign|ui|audio|summary|cleanup}"
here="$(cd "$(dirname "$0")" && pwd)"
W="${RUNNER_TEMP:-/tmp}/mac-probe"
mkdir -p "$W"

readonly SYS_TCC='/Library/Application Support/com.apple.TCC/TCC.db'
readonly USER_TCC="$HOME/Library/Application Support/com.apple.TCC/TCC.db"
readonly ADHOC_ID='com.hindsound.probe.adhoc'
readonly CERT_ID='com.hindsound.probe.cert'
readonly INSTALL_DIR="$HOME/Applications"
readonly SDL_VERSION='3.2.30'
readonly SDL_URL="https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VERSION/SDL3-$SDL_VERSION.tar.gz"
readonly SDL_SHA256='4c3b09330d866dc52eb65b66259a6684ad387252ca8c57901b3a2b534eb42e3d'

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
  local sdl="$W/sdl" rc sha config
  rm -rf "$sdl"
  mkdir -p "$sdl"
  curl --fail --silent --show-error --location --retry 3 --output "$sdl/sdl.tar.gz" "$SDL_URL"
  sha="$(shasum -a 256 "$sdl/sdl.tar.gz" | cut -d' ' -f1)"
  if [ "$sha" != "$SDL_SHA256" ]; then
    probe sdl.archive "BAD sha256=$sha expected=$SDL_SHA256"
  else
    probe sdl.archive "ok sha256=$sha"
    tar -xzf "$sdl/sdl.tar.gz" -C "$sdl"
    cmake -S "$sdl/SDL3-$SDL_VERSION" -B "$sdl/build" -DCMAKE_BUILD_TYPE=Release \
      -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST_LIBRARY=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF -DSDL_INSTALL_DOCS=OFF \
      -DCMAKE_OSX_ARCHITECTURES=arm64 -DCMAKE_OSX_DEPLOYMENT_TARGET=14.0 > "$sdl/configure.log" 2>&1
    rc=$?
    [ $rc -eq 0 ] || tail -n 40 "$sdl/configure.log"
    cmake --build "$sdl/build" --parallel 4 > "$sdl/build.log" 2>&1
    rc=$?
    [ $rc -eq 0 ] || tail -n 40 "$sdl/build.log"
    probe sdl.build "rc=$rc"
    config="$(find "$sdl/build" -name SDL_build_config.h | head -n 1)"
    probe sdl.cocoa "$(grep -E '^#define SDL_VIDEO_DRIVER_COCOA( |$)' "$config" 2>&1 | oneline)"
    cp -L "$sdl/build/libSDL3.0.dylib" "$W/libSDL3.0.dylib"
    probe sdl.lib "$(file -b "$W/libSDL3.0.dylib" 2>&1) $(otool -D "$W/libSDL3.0.dylib" 2>&1 | tail -n 1)"
  fi

  clang -fobjc-arc -Wall -dynamiclib -arch arm64 -mmacosx-version-min=14.2 \
    -framework Foundation -framework CoreAudio -framework AVFoundation \
    -install_name @rpath/libprobeaudio.dylib -o "$W/libprobeaudio.dylib" "$here/native/probe_audio.m" 2>&1
  probe native.build "rc=$? sdk=$(xcrun --show-sdk-version 2>&1) clang=$(clang --version | head -n 1)"
  clang -arch arm64 -mmacosx-version-min=14.2 -o "$W/probe_audio_cli" "$here/native/probe_audio_cli.c"     -L"$W" -lprobeaudio -Wl,-rpath,"$W" 2>&1
  probe native.build_cli "rc=$?"

  local v
  for v in 1 2; do
    rm -rf "$W/publish-v$v"
    dotnet publish "$here/HindsoundProbe.csproj" -c Release -r osx-arm64 --self-contained \
      -p:PublishSingleFile=true -p:ProbeBuild="$v" -o "$W/publish-v$v" > "$W/publish-v$v.log" 2>&1
    rc=$?
    [ $rc -eq 0 ] || tail -n 40 "$W/publish-v$v.log"
    probe "dotnet.publish_v$v" "rc=$rc files=$(ls "$W/publish-v$v" 2>&1 | oneline)"
  done
  probe dotnet.v1_v2_differ "$(cmp -s "$W/publish-v1/HindsoundProbe" "$W/publish-v2/HindsoundProbe" && echo no || echo yes)"
}

# --- sign ---------------------------------------------------------------------------------------------------------------

make_app() { # publish_dir bundle_id version app
  local publish="$1" id="$2" version="$3" app="$4"
  rm -rf "$app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Frameworks"
  cp -R "$publish"/. "$app/Contents/MacOS/"
  cp "$W/libSDL3.0.dylib" "$W/libprobeaudio.dylib" "$app/Contents/Frameworks/"
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
    make_app "$W/publish-v$v" "$ADHOC_ID" "$v" "$W/apps/adhoc-v$v/HindsoundProbe.app"
    sign_app "$W/apps/adhoc-v$v/HindsoundProbe.app" - > "$W/sign-adhoc-v$v.log" 2>&1
    probe "sign.adhoc_v$v" "rc=$? $(oneline < "$W/sign-adhoc-v$v.log")"
    make_app "$W/publish-v$v" "$CERT_ID" "$v" "$W/apps/cert-v$v/HindsoundProbe.app"
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

# --- ui -----------------------------------------------------------------------------------------------------------------

# Runs an .app and waits for it, with a watchdog. mode: open (through Launch Services: the app is its own responsible
# process for TCC) or direct (a child of this shell: the runner is the responsible process).
run_app() { # mode app outfile args...
  local mode="$1" app="$2" out="$3"
  shift 3
  rm -f "$out" "$out.err"
  if [ "$mode" = open ]; then
    open -n -W --stdout "$out" --stderr "$out.err" "$app" --args "$@" &
  elif [ "$mode" = cli ]; then
    shift
    "$W/probe_audio_cli" "$@" > "$out" 2> "$out.err" &
  else
    "$app/Contents/MacOS/HindsoundProbe" "$@" > "$out" 2> "$out.err" &
  fi
  local pid=$!
  ( sleep 150; kill "$pid" 2> /dev/null ) &
  local watchdog=$!
  wait "$pid"
  kill "$watchdog" 2> /dev/null
  pkill -f "HindsoundProbe.app/Contents/MacOS/HindsoundProbe" 2> /dev/null
  grep '^PROBE ' "$out" 2> /dev/null | tee -a "$W/probe.log"
  grep -v '^PROBE ' "$out" 2> /dev/null
  [ -s "$out.err" ] && sed 's/^/stderr: /' "$out.err" | tail -n 30
  return 0
}

install_app() { # source destination
  rm -rf "$2"
  mkdir -p "$(dirname "$2")"
  ditto "$1" "$2"
}

ui() {
  local app="$INSTALL_DIR/HindsoundProbe-ui.app" out="$W/run-ui.out" i
  install_app "$W/apps/adhoc-v1/HindsoundProbe.app" "$app"
  rm -f "$out"
  # Started in the background so that the screenshot is taken while the window is up.
  ( run_app open "$app" "$out" ui ui 12 > "$W/run-ui.console" 2>&1 ) &
  local runner=$!
  for i in $(seq 1 60); do
    grep -q 'PROBE ui.window_ready' "$out" 2> /dev/null && break
    sleep 0.5
  done
  sleep 2
  screencapture -x "$W/window.png" > "$W/screencapture.log" 2>&1
  probe ui.screencapture "rc=$? $(oneline < "$W/screencapture.log") $(sips -g pixelWidth -g pixelHeight "$W/window.png" 2>&1 | tail -n 2 | oneline)"
  wait "$runner"
  cat "$W/run-ui.console"
  python3 -m venv "$W/venv" > /dev/null 2>&1 && "$W/venv/bin/pip" install --quiet pillow > /dev/null 2>&1
  "$W/venv/bin/python" - "$W/window.png" <<'EOF' | tee -a "$W/probe.log"
import collections, sys
from PIL import Image
image = Image.open(sys.argv[1]).convert('RGB')
width, height = image.size
pixels = list(image.getdata())
target = (0xD4, 0xF5, 0x3C)
exact = sum(1 for p in pixels if p == target)
near = sum(1 for p in pixels if all(abs(p[i] - target[i]) <= 24 for i in range(3)))
top = ' '.join('#%02X%02X%02X=%d' % (c[0], c[1], c[2], n) for c, n in collections.Counter(pixels).most_common(4))
print(f'PROBE ui.screenshot size={width}x{height} lime_exact={exact} lime_near={near} top: {top}')
EOF
}

# --- audio --------------------------------------------------------------------------------------------------------------

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
    probe "$1.tcc_rows_$which" "$(sudo sqlite3 "$(tcc_db "$which")" "select service || ':' || client || ':auth=' || auth_value || ':reason=' || auth_reason || ':csreq=' || ifnull(length(csreq), 0) from access where client like 'com.hindsound.probe%' or client like '%HindsoundProbe%';" 2>&1 | oneline)"
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

audio_run() { # tag app mode
  local tag="$1" app="$2" mode="$3" tone silent
  afplay "$W/tone.wav" &
  tone=$!
  afplay "$W/silence.wav" &
  silent=$!
  sleep 1
  echo "=== $tag ($mode, $app)"
  run_app "$mode" "$app" "$W/run-$tag.out" audio "$tag" "$tone" "$silent" 2
  kill "$tone" "$silent" 2> /dev/null
  wait "$tone" "$silent" 2> /dev/null
  tcc_rows "$tag"
}

audio() {
  make_sounds
  # The same audio code from a plain C program, without .NET: if this one runs and the .app does not, .NET is the cause.
  audio_run native_cli "$W/probe_audio_cli" cli
  local kind id app
  for kind in adhoc cert; do
    if [ "$kind" = adhoc ]; then id="$ADHOC_ID"; else id="$CERT_ID"; fi
    app="$INSTALL_DIR/HindsoundProbe-$kind.app"
    install_app "$W/apps/$kind-v1/HindsoundProbe.app" "$app"
    audio_run "${kind}_v1_noauth" "$app" open
    [ "$kind" = adhoc ] && audio_run "${kind}_v1_direct" "$app" direct
    grant "${kind}_v1" "$app" "$id"
    audio_run "${kind}_v1_auth" "$app" open
    # Build 2 in place of build 1, same path and bundle id, and no new permission.
    install_app "$W/apps/$kind-v2/HindsoundProbe.app" "$app"
    probe "${kind}_v2.installed" "$(dr_of "$app")"
    audio_run "${kind}_v2_noregrant" "$app" open
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
  terrain | blackhole | build | sign | ui | audio | summary | cleanup) "$phase" ;;
  *) echo "Unknown phase: $phase" >&2; exit 2 ;;
esac
exit 0
