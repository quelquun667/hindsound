#!/usr/bin/env bash
# Checks the zip of the Mac app as a user gets it, unzipped in a new folder:
#   - its name, Hindsound-<version>-macos-arm64.zip, and its entries: Hindsound.app and nothing else, no AppleDouble file
#     (._*) nor __MACOSX;
#   - the bundle: its Info.plist (version, identifier, minimum macOS, icon, and the two texts macOS shows when it asks for
#     the permissions: without them it refuses the capture), the program, the four dylibs, the fonts, the icon, the
#     licences (NLayer's among them: the program carries it);
#   - its signature: codesign --verify --deep --strict, and what signed it (sign-app.sh printed the same): ad hoc (the
#     designated requirement is a cdhash), or, with CERTIFICATE_SHA1, the certificate of that SHA-1 (the requirement
#     names it);
#   - what each Mach-O links: the system and the bundle only, nothing of Homebrew (/opt/homebrew, /usr/local);
#   - the app started for real: --prove-mp3 (one second of a sine as an MP3 through the path of a clip, read back) with
#     the LAME of the bundle, then --dump-settings (the 81 pages of the window, off screen) with the SDL, FreeType and
#     HarfBuzz of the bundle and its fonts, the microphone, the clipboard and the shortcuts off.
#
# Usage: check-zip.sh ZIP VERSION WORK_DIR [CERTIFICATE_SHA1]
#   WORK_DIR          must not exist; the zip is unzipped there, and the runs keep their logs and pages there
#   CERTIFICATE_SHA1  the SHA-1 of the certificate that must have signed the app, 40 hex digits; without it, the app must
#                     be signed ad hoc
# Last checked: 21/11/2026, on the runner macos-26 (job macos of release-files.yml).

set -euo pipefail

[ "$#" -eq 3 ] || [ "$#" -eq 4 ] || { echo "Usage: $0 ZIP VERSION WORK_DIR [CERTIFICATE_SHA1]" >&2; exit 2; }
zip="$1"
version="$2"
work="$3"
certificate="$(tr 'A-F' 'a-f' <<< "${4:-}")"
readonly PAGES=81
fail() { echo "check-zip: $*" >&2; exit 1; }

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "the version '$version' is not x.y.z"
[ -z "$certificate" ] || [[ "$certificate" =~ ^[0-9a-f]{40}$ ]] || fail "the SHA-1 '$4' is not 40 hex digits"
[ "$(basename "$zip")" = "Hindsound-$version-macos-arm64.zip" ] || fail "$(basename "$zip") is not Hindsound-$version-macos-arm64.zip"
[ ! -e "$work" ] || fail "$work exists"
mkdir -p "$work"
work="$(cd "$work" && pwd)"

zipinfo -1 "$zip" > "$work/entries"
outside="$(grep -v '^Hindsound\.app/' "$work/entries" || true)"
[ -z "$outside" ] || fail "the zip holds more than Hindsound.app: $outside"
apple_double="$(grep -E '(^|/)\._|__MACOSX' "$work/entries" || true)"
[ -z "$apple_double" ] || fail "the zip holds AppleDouble files: $apple_double"

ditto -x -k "$zip" "$work/unzipped"
[ "$(ls -A "$work/unzipped")" = Hindsound.app ] || fail "the zip does not unzip to Hindsound.app alone"
app="$work/unzipped/Hindsound.app"
contents="$app/Contents"
plutil -lint "$contents/Info.plist"
[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$contents/Info.plist")" = "$version" ] || fail "the Info.plist does not give $version"
[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$contents/Info.plist")" = io.github.quelquun667.hindsound ] || fail "the Info.plist does not give the identifier of Hindsound"
plist() { /usr/libexec/PlistBuddy -c "Print :$1" "$contents/Info.plist" 2> /dev/null || true; }
[ "$(plist LSMinimumSystemVersion)" = 14.4 ] || fail "the Info.plist does not give macOS 14.4 as the minimum"
[ "$(plist CFBundleIconFile)" = Hindsound ] || fail "the Info.plist does not name the icon Hindsound"
for key in NSMicrophoneUsageDescription NSAudioCaptureUsageDescription; do
  [[ "$(plist "$key")" == Hindsound\ keeps\ * ]] || fail "the Info.plist has no $key"
done
[ -x "$contents/MacOS/hindsound" ] || fail "the program is missing or not executable"
for file in Frameworks/libSDL3.0.dylib Frameworks/libfreetype.6.dylib Frameworks/libharfbuzz.0.dylib Frameworks/libmp3lame.0.dylib \
            Resources/fonts/Selawik-Regular.ttf Resources/fonts/Selawik-Semibold.ttf Resources/fonts/BarlowSemiCondensed-SemiBold.ttf \
            Resources/fonts/CascadiaMono-Regular.ttf Resources/LICENSE Resources/THIRD-PARTY-NOTICES.md \
            Resources/licenses/LAME-COPYING.txt Resources/licenses/dotnet-runtime-LICENSE.txt Resources/licenses/linux/SDL3-LICENSE.txt \
            Resources/licenses/mac/FreeType-FTL.txt Resources/licenses/mac/HarfBuzz-COPYING.txt \
            Resources/licenses/linux/Selawik-LICENSE.txt Resources/licenses/linux/NLayer-LICENSE.txt Resources/Hindsound.icns \
            _CodeSignature/CodeResources; do
  [ -f "$contents/$file" ] || fail "the bundle has no Contents/$file"
done

codesign --verify --deep --strict --verbose=2 "$app"
codesign -dvv "$app" 2>&1 | grep -E '^(Identifier|Signature|Authority)' | sed 's/^/check-zip: signature: /' || true
# An implicit requirement is written "# designated => ...": the same reading as sign-app.sh and the jobs of ci.yml.
requirement="$(codesign -d -r- "$app" 2>&1 | sed -n 's/^#* *designated => //p' | head -n 1)"
echo "check-zip: designated requirement: $requirement"
if [ -z "$certificate" ]; then
  [[ "$requirement" == cdhash* ]] || fail "the app is not signed ad hoc: its designated requirement is '$requirement'"
  echo "check-zip: signed ad hoc."
else
  [ "$requirement" = "identifier \"io.github.quelquun667.hindsound\" and certificate leaf = H\"$certificate\"" ] \
    || fail "the designated requirement '$requirement' does not name the certificate $certificate"
  echo "check-zip: signed by the certificate with the SHA-1 $certificate."
fi

failed=0
while IFS= read -r file; do
  if [[ "$(file -b "$file")" == Mach-O* ]]; then
    links="$(otool -L "$file" | tail -n +2 | awk '{print $1}')"
    if [ -n "$(grep -Ev '^(/usr/lib/|/System/Library/|@rpath/|@executable_path/|@loader_path/)' <<< "$links" || true)" ]; then
      echo "check-zip: $(basename "$file") links outside the system and the bundle: $(echo $links)" >&2
      failed=1
    fi
  fi
done < <(find "$contents" -type f)
[ "$failed" -eq 0 ] || exit 1

program="$contents/MacOS/hindsound"
settings='{"audio":{"micDeviceId":"disabled"}}'

# Each library of the bundle that a run needs must be loaded from the bundle, and no other copy of it: dyld prints the
# path of every image it loads (DYLD_PRINT_LIBRARIES, on stderr).
from_bundle() { # dyld_output library...
  local output="$1" library others
  shift
  for library in "$@"; do
    grep -q -F "$contents/Frameworks/$library" "$output" || fail "$library was not loaded from the bundle"
    others="$(grep -F "/$library" "$output" | grep -v -F "$contents/Frameworks/$library" || true)"
    [ -z "$others" ] || fail "another $library was loaded: $others"
  done
}

run="$work/mp3"
mkdir -p "$run"
printf '%s\n' "$settings" > "$run/settings.json"
code=0
env -u HINDSOUND_FONT_DIR DYLD_PRINT_LIBRARIES=1 perl -e 'alarm shift; exec @ARGV' 120 "$program" --settings "$run/settings.json" \
  --no-mic --no-clipboard --no-hotkeys --offscreen --prove-mp3 "$run/proof" 2> "$run/dyld" || code=$?
[ ! -f "$run/settings.log" ] || sed 's/^/  mp3 log: /' "$run/settings.log"
[ "$code" -eq 0 ] || { grep -v '^dyld\[' "$run/dyld" >&2 || true; fail "--prove-mp3 ended with code $code (4: the clip came out as WAV, 5: it does not read back)"; }
from_bundle "$run/dyld" libmp3lame.0.dylib

# The pages, off screen, with the SDL, FreeType and HarfBuzz of the bundle and its fonts.
run="$work/dump"
mkdir -p "$run/png"
printf '%s\n' "$settings" > "$run/settings.json"
code=0
env -u HINDSOUND_FONT_DIR SDL_VIDEO_DRIVER=offscreen DYLD_PRINT_LIBRARIES=1 perl -e 'alarm shift; exec @ARGV' 300 "$program" \
  --settings "$run/settings.json" --no-mic --no-clipboard --no-hotkeys --offscreen --dump-settings "$run/png" 2> "$run/dyld" || code=$?
[ ! -f "$run/settings.log" ] || sed 's/^/  dump log: /' "$run/settings.log"
[ "$code" -eq 0 ] || { grep -v '^dyld\[' "$run/dyld" >&2 || true; fail "--dump-settings ended with code $code"; }
count=$(find "$run/png" -name '*.png' | wc -l | tr -d ' ')
[ "$count" -eq "$PAGES" ] || fail "--dump-settings drew $count pages, expected $PAGES"
from_bundle "$run/dyld" libSDL3.0.dylib libfreetype.6.dylib libharfbuzz.0.dylib

echo "check-zip: $(basename "$zip"), $(stat -f %z "$zip") bytes ($(du -h "$zip" | cut -f1)); --prove-mp3 passed, --dump-settings drew $count pages"
