#!/usr/bin/env bash
# Runs the Linux app twice, as the CI does after its tests, and keeps what it drew:
#   1. --dump-settings OUT_DIR: every page of the window drawn into a PNG file, then the app leaves. Fails if no PNG came.
#   2. --settings-probe: the window opened on SDL's offscreen driver, sent keys, measured, closed. Fails on a code other
#      than 0.
# Both runs are made safe the same way: settings of their own in WORK_DIR, in which the microphone is disabled, and
# --no-mic, --no-clipboard, --no-hotkeys and --offscreen on the command line. Nothing is shown, no sound is played, the
# user's settings and menu are not touched. The log of each run is copied into OUT_DIR next to the PNG files.
#
# Usage: dump-window.sh OUT_DIR WORK_DIR PROGRAM [ARGUMENT...]
#   PROGRAM [ARGUMENT...]  how to start the app, for example src/Hindsound.Linux/bin/Release/net10.0/hindsound
# The fonts are those of HINDSOUND_FONT_DIR when it is set, else those next to the program.

set -uo pipefail

if [ "$#" -lt 3 ]; then
  echo "Usage: $0 OUT_DIR WORK_DIR PROGRAM [ARGUMENT...]" >&2
  exit 2
fi

mkdir -p "$1" "$2"
out="$(cd "$1" && pwd)"
work="$(cd "$2" && pwd)"
shift 2

safe=(--no-mic --no-clipboard --no-hotkeys --offscreen)
failed=0

# One run: its own settings folder, so its own log; the log is shown and kept whatever the outcome.
run() {
  local name="$1"
  shift
  local folder="$work/$name"
  mkdir -p "$folder"
  printf '%s\n' '{"audio":{"micDeviceId":"disabled"}}' > "$folder/settings.json"
  local code=0
  SDL_VIDEO_DRIVER=offscreen timeout 300 "$@" --settings "$folder/settings.json" || code=$?
  if [ -f "$folder/settings.log" ]; then
    sed "s/^/  $name log: /" "$folder/settings.log"
    cp "$folder/settings.log" "$out/$name.log"
  fi
  echo "$name: exit code $code"
  return "$code"
}

run dump-settings "$@" "${safe[@]}" --dump-settings "$out" || failed=1
count="$(find "$out" -maxdepth 1 -name '*.png' | wc -l)"
echo "dump-settings: $count PNG files in $out"
if [ "$count" -eq 0 ]; then
  echo "--dump-settings wrote no PNG." >&2
  failed=1
fi

run settings-probe "$@" "${safe[@]}" --settings-probe || failed=1

exit "$failed"
