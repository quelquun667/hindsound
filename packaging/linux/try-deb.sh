#!/usr/bin/env bash
# Installs the package made by build-deb.sh in a clean system, as root, the way a person would: apt-get install of the
# file, with only what its Depends bring (no Recommends). Then, started as /usr/bin/hindsound:
#   --prove-mp3     writes one second of a sine as an MP3 and reads it back (see check-packages.sh), with the
#                   libmp3lame.so.0 of /usr/lib/hindsound, the folder of the real program, not of the link;
#   --dump-settings draws the 80 pages of the window off screen, with the libSDL3.so.0 and the fonts of /usr/lib/hindsound,
#                   and the FreeType, HarfBuzz and fontconfig that Depends brought.
# Then apt-get remove, and nothing of the package may stay in /usr.
#
# Usage: try-deb.sh <folder holding the .deb> <version>
set -euo pipefail

readonly PAGES=80

fail() {
    echo "try-deb: $*" >&2
    exit 1
}

[ $# -eq 2 ] || fail "usage: try-deb.sh <folder> <version>"
[ "$(id -u)" -eq 0 ] || fail "run as root, in a container"
folder=$(realpath -e -- "$1") || fail "no folder at $1"
version=$2
[[ $version =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-([0-9A-Za-z.-]+))?$ ]] || fail "not a version: $version"
debian_version=${BASH_REMATCH[1]}
if [ -n "${BASH_REMATCH[3]}" ]; then
    debian_version+="~${BASH_REMATCH[3]//-/.}"
fi
deb="$folder/hindsound_${debian_version}_amd64.deb"
[ -f "$deb" ] || fail "missing: $deb"

. /etc/os-release
echo "try-deb: $PRETTY_NAME"
export DEBIAN_FRONTEND=noninteractive
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT

apt-get update -q
# apt reads the file from the folder: it must be a path, and readable by the _apt user.
cp -- "$deb" "$work/"
chmod 755 -- "$work"
chmod 644 -- "$work/$(basename -- "$deb")"
apt-get install -y -q --no-install-recommends -- "$work/$(basename -- "$deb")"
[ "$(dpkg-query --show --showformat='${Status} ${Version}' hindsound)" = "install ok installed $debian_version" ] \
    || fail "hindsound $debian_version is not installed"
dpkg-query --listfiles hindsound > "$work/files"

# The command a person types: on the PATH, a link to the real program.
[ "$(command -v hindsound)" = /usr/bin/hindsound ] || fail "hindsound is not /usr/bin/hindsound on the PATH"
[ "$(readlink -f /usr/bin/hindsound)" = /usr/lib/hindsound/hindsound ] || fail "/usr/bin/hindsound does not lead to /usr/lib/hindsound/hindsound"

# The libraries loaded are those of /usr/lib/hindsound, found next to the real program though it was started by the link.
# The one named must have been loaded; the other, if it was.
loaded_from_package() {
    local label=$1 trace=$2 required=$3 library
    for library in libmp3lame.so.0 libSDL3.so.0; do
        local paths
        paths=$(grep -h -E "calling init: .*/${library//./\\.}\$" "$trace"/trace.* | sed 's/.*calling init: //' | sort -u || true)
        if [ "$library" = "$required" ] || [ -n "$paths" ]; then
            [ "$paths" = "/usr/lib/hindsound/$library" ] || fail "$label: $library was loaded from '${paths:-nowhere}', not from /usr/lib/hindsound"
            echo "try-deb: $label loaded /usr/lib/hindsound/$library"
        fi
    done
}

# The MP3, through the encoder of the package.
run="$work/prove"
mkdir -p -- "$run/proof" "$run/ld"
code=0
LD_DEBUG=libs LD_DEBUG_OUTPUT="$run/ld/trace" timeout 120 /usr/bin/hindsound --settings "$run/settings.json" --prove-mp3 "$run/proof" || code=$?
[ ! -f "$run/settings.log" ] || sed 's/^/  prove log: /' -- "$run/settings.log"
[ "$code" -eq 0 ] || fail "--prove-mp3 ended with code $code (4: the clip came out as WAV, 5: it does not read back)"
mp3=$(find "$run/proof" -maxdepth 1 -name '*.mp3' -print -quit)
[ -n "$mp3" ] || fail "no MP3 in $run/proof"
head=$(od -An -tx1 -N3 -- "$mp3" | tr -d ' \n')
[[ $head == 494433 || $head =~ ^ff[e-f] ]] || fail "$mp3 starts with $head, not with an MP3 header"
loaded_from_package prove "$run/ld" libmp3lame.so.0
echo "try-deb: --prove-mp3 wrote $(basename -- "$mp3"), $(stat -c %s -- "$mp3") bytes"

# The pages of the window, off screen, with the microphone, the clipboard and the shortcuts off.
run="$work/dump"
mkdir -p -- "$run/png" "$run/ld"
printf '%s\n' '{"audio":{"micDeviceId":"disabled"}}' > "$run/settings.json"
code=0
env -u HINDSOUND_FONT_DIR LD_DEBUG=libs LD_DEBUG_OUTPUT="$run/ld/trace" SDL_VIDEO_DRIVER=offscreen timeout 300 /usr/bin/hindsound \
    --settings "$run/settings.json" --no-mic --no-clipboard --no-hotkeys --offscreen --dump-settings "$run/png" || code=$?
[ ! -f "$run/settings.log" ] || sed 's/^/  dump log: /' -- "$run/settings.log"
[ "$code" -eq 0 ] || fail "--dump-settings ended with code $code"
count=$(find "$run/png" -name '*.png' | wc -l)
[ "$count" -eq "$PAGES" ] || fail "--dump-settings drew $count pages, expected $PAGES"
grep -q -F 'window: SDL next to the program, fonts next to the program' -- "$run/settings.log" \
    || fail "the window did not take the libSDL3.so.0 and the fonts of /usr/lib/hindsound"
loaded_from_package dump "$run/ld" libSDL3.so.0
echo "try-deb: --dump-settings drew $count pages"

# The copy of the package adds no entry of its own to the menu: the package has one.
[ -z "$(find / -xdev -path /proc -prune -o -name 'io.github.quelquun667.Hindsound.desktop' -print | grep -v -x -F /usr/share/applications/io.github.quelquun667.Hindsound.desktop)" ] \
    || fail "the app wrote a desktop entry of its own"

# Removed: nothing of the package stays, and nothing else under /usr changed.
find /usr | LC_ALL=C sort > "$work/usr-installed"
apt-get remove -y -q hindsound
status=$(dpkg-query --show --showformat='${Status}' hindsound 2> /dev/null || true)
case "$status" in
    *' installed') fail "hindsound is still installed after apt-get remove: $status" ;;
esac
find /usr | LC_ALL=C sort > "$work/usr-removed"
left=$(comm -13 "$work/usr-installed" "$work/usr-removed")
[ -z "$left" ] || fail "apt-get remove added to /usr: $left"
while IFS= read -r path; do
    case "$path" in
        /usr/lib/hindsound* | /usr/bin/hindsound | /usr/share/doc/hindsound* | /usr/share/lintian/overrides/hindsound | \
            /usr/share/applications/io.github.quelquun667.Hindsound.desktop | /usr/share/icons/hicolor/*/apps/io.github.quelquun667.Hindsound.png)
            [ ! -e "$path" ] && [ ! -L "$path" ] || fail "$path stays after apt-get remove"
            ;;
    esac
done < "$work/files"
stays=$(find /usr -iname '*hindsound*' -print)
[ -z "$stays" ] || fail "these stay in /usr after apt-get remove: $stays"
echo "try-deb: installed, run and removed on $PRETTY_NAME"
