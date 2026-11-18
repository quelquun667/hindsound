#!/usr/bin/env bash
# Checks the packages made by build-packages.sh, as a user would get them: the files each one holds, and the program of
# each one started for real with --prove-mp3, which writes one second of a sine as an MP3 through the path of a real clip
# and reads it back with the decoder of the preview and the editor (code 5 if it does not come back as that second of sine),
# and leaves. That needs no sound server, no session bus and no FUSE. The run must load the libmp3lame.so.0 of the
# package, not one the machine may already have: the dynamic loader says which file it loaded (LD_DEBUG=libs).
# Then the program of each one draws the pages of its window into PNG files (--dump-settings), with the fonts of the
# package and the FreeType, HarfBuzz and fontconfig of the system. A libSDL3.so.0 that either run loads must be the
# package's.
#
# Usage: check-packages.sh <out folder> <version>
set -euo pipefail

readonly APP_ID='io.github.quelquun667.Hindsound'
readonly ICON_SIZES='48 128 256'
readonly FONTS='Selawik-Regular.ttf Selawik-Semibold.ttf BarlowSemiCondensed-SemiBold.ttf CascadiaMono-Regular.ttf'

fail() {
    echo "check-packages: $*" >&2
    exit 1
}

[ $# -eq 2 ] || fail "usage: check-packages.sh <out folder> <version>"
out=$(realpath -e -- "$1") || fail "no folder at $1"
name="hindsound-$2-linux-x64"
appimage="$out/$name.AppImage"
archive="$out/$name.tar.gz"

tools=$(realpath -e -- "$(dirname -- "$(realpath -e -- "$0")")/../../tools/linux")
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT

has() {
    [ -e "$1" ] || fail "missing: $1"
}

# A program starts and writes an MP3 with the encoder found at the path given (an extended regular expression).
prove_mp3() {
    local label=$1 encoder=$2
    shift 2
    local run="$work/run-$label"
    mkdir -p -- "$run/proof" "$run/ld"
    local code=0
    # Its own settings path, so that its log lands here and nothing reaches ~/.config/Hindsound.
    LD_DEBUG=libs LD_DEBUG_OUTPUT="$run/ld/trace" "$@" --settings "$run/settings.json" --prove-mp3 "$run/proof" || code=$?
    if [ -f "$run/settings.log" ]; then
        sed "s/^/  $label log: /" -- "$run/settings.log"
    fi
    [ "$code" -eq 0 ] || fail "$label: --prove-mp3 ended with code $code (4: the clip came out as WAV, 5: it does not read back)"

    local mp3
    mp3=$(find "$run/proof" -maxdepth 1 -name '*.mp3' -print -quit)
    [ -n "$mp3" ] || fail "$label: no MP3 in $run/proof"
    # An MP3 starts with an ID3 tag or with the 11 set bits of a frame header.
    local head
    head=$(od -An -tx1 -N3 -- "$mp3" | tr -d ' \n')
    [[ $head == 494433 || $head =~ ^ff[e-f] ]] || fail "$label: $mp3 starts with $head, not with an MP3 header"

    grep -h -E "calling init: ($encoder)\$" "$run/ld"/trace.* > /dev/null \
        || fail "$label: libmp3lame.so.0 was not loaded from the package (wanted $encoder)"
    echo "check-packages: $label wrote $(basename -- "$mp3"), $(stat -c %s -- "$mp3") bytes"
    sdl_from_package "$label" "$encoder" "$run/ld"
}

# Every libSDL3.so.0 the run loaded is the package's: the one in the folder of the encoder, the folder of the program.
sdl_from_package() {
    local label=$1 encoder=$2 trace=$3
    local sdl="${encoder%'libmp3lame\.so\.0'}libSDL3\.so\.0"
    local loaded path
    loaded=$(grep -h -E 'calling init: .*/libSDL3\.so\.0$' "$trace"/trace.* | sed 's/.*calling init: //' | sort -u || true)
    while IFS= read -r path; do
        [ -z "$path" ] || [[ $path =~ ^($sdl)$ ]] || fail "$label: libSDL3.so.0 was loaded from outside the package: $path"
    done <<< "$loaded"
}

# The program draws every page of its window into PNG files and leaves: off screen, with the microphone, the clipboard and
# the shortcuts off, and settings of its own in which the microphone is disabled too. No font folder is given: the app
# takes the fonts next to it. It loads SDL too, without opening it, to say in its log whether a window could open.
dump_window() {
    local label=$1 encoder=$2
    shift 2
    local run="$work/dump-$label"
    mkdir -p -- "$run/png" "$run/ld"
    printf '%s\n' '{"audio":{"micDeviceId":"disabled"}}' > "$run/settings.json"
    local code=0
    env -u HINDSOUND_FONT_DIR LD_DEBUG=libs LD_DEBUG_OUTPUT="$run/ld/trace" SDL_VIDEO_DRIVER=offscreen timeout 300 "$@" \
        --settings "$run/settings.json" --no-mic --no-clipboard --no-hotkeys --offscreen --dump-settings "$run/png" || code=$?
    if [ -f "$run/settings.log" ]; then
        sed "s/^/  $label log: /" -- "$run/settings.log"
    fi
    [ "$code" -eq 0 ] || fail "$label: --dump-settings ended with code $code"
    local count
    count=$(find "$run/png" -name '*.png' | wc -l)
    [ "$count" -gt 0 ] || fail "$label: --dump-settings wrote no PNG"
    # The log says where SDL and the fonts came from: with no font folder given, a package must have used its own copies.
    grep -q -F 'window: SDL next to the program, fonts next to the program' -- "$run/settings.log" \
        || fail "$label: the window did not load the libSDL3.so.0 and the fonts of the package"
    sdl_from_package "$label" "$encoder" "$run/ld"
    echo "check-packages: $label drew $count PNG files"
}

# The files each package must hold, under its prefix.
check_layout() {
    local label=$1 bin=$2 share=$3 doc=$4
    [ -x "$bin/hindsound" ] || fail "$label: $bin/hindsound is missing or not executable"
    has "$bin/libmp3lame.so.0"
    readelf -d -- "$bin/libmp3lame.so.0" | grep -q 'Library soname: \[libmp3lame\.so\.0\]' \
        || fail "$label: libmp3lame.so.0 does not have the soname libmp3lame.so.0"
    has "$bin/libSDL3.so.0"
    readelf -d -- "$bin/libSDL3.so.0" | grep -q 'Library soname: \[libSDL3\.so\.0\]' \
        || fail "$label: libSDL3.so.0 does not have the soname libSDL3.so.0"
    # The fonts of the window, next to the program: the four TrueType files, and nothing else in their folder.
    local font
    for font in $FONTS; do
        has "$bin/fonts/$font"
    done
    bash "$tools/fetch-fonts.sh" --check "$bin/fonts" > /dev/null || fail "$label: the fonts are not TrueType files"
    [ "$(find "$bin/fonts" -mindepth 1 | wc -l)" -eq 4 ] || fail "$label: $bin/fonts holds more than the four fonts"
    has "$share/applications/$APP_ID.desktop"
    local size
    for size in $ICON_SIZES; do
        has "$share/icons/hicolor/${size}x${size}/apps/$APP_ID.png"
    done
    has "$doc/LICENSE"
    has "$doc/THIRD-PARTY-NOTICES.md"
    has "$doc/licenses/LAME-COPYING.txt"
    has "$doc/licenses/linux/Tmds.DBus.Protocol-COPYING.txt"
    has "$doc/licenses/linux/NLayer-LICENSE.txt"
    # Every text only the Linux packages carry: those of the AppImage runtime and of the libraries built into it, and that
    # of Tmds.DBus.Protocol, which only the Linux app references. THIRD-PARTY-NOTICES.md links them.
    local licence
    for licence in AppImage-type2-runtime-LICENSE.txt musl-COPYRIGHT.txt libfuse-LGPL2.txt libfuse-LICENSE.txt \
        squashfuse-LICENSE.txt zstd-LICENSE.txt zlib-LICENSE.txt mimalloc-LICENSE.txt Tmds.DBus.Protocol-COPYING.txt \
        SDL3-LICENSE.txt SDL3-yuv2rgb-LICENSE.txt Selawik-LICENSE.txt Barlow-OFL.txt CascadiaCode-LICENSE.txt \
        NLayer-LICENSE.txt SDL3-x11-edid-LICENSE.txt SDL3-x11-xsettings-LICENSE.txt \
        SDL3-wayland-protocols-NOTICES.txt; do
        has "$doc/licenses/linux/$licence"
    done
    if command -v desktop-file-validate > /dev/null; then
        desktop-file-validate -- "$share/applications/$APP_ID.desktop" || fail "$label: the desktop entry is not valid"
    fi
}

# The AppImage: an ELF file with the type 2 magic (A I 2 at offset 8), executable, run as a user without FUSE would:
# extracted by its own runtime into TMPDIR, then AppRun.
has "$appimage"
[ -x "$appimage" ] || fail "$appimage is not executable"
[ "$(od -An -tx1 -N4 -- "$appimage" | tr -d ' \n')" = 7f454c46 ] || fail "$appimage is not an ELF file"
[ "$(od -An -tx1 -j8 -N3 -- "$appimage" | tr -d ' \n')" = 414902 ] || fail "$appimage is not a type 2 AppImage"

mkdir -p -- "$work/extract"
(cd -- "$work/extract" && "$appimage" --appimage-extract > /dev/null)
root="$work/extract/squashfs-root"
[ -x "$root/AppRun" ] || fail "AppImage: AppRun is missing or not executable"
has "$root/$APP_ID.desktop"
has "$root/$APP_ID.png"
has "$root/.DirIcon"
check_layout AppImage "$root/usr/bin" "$root/usr/share" "$root/usr/share/doc/hindsound"

mkdir -p -- "$work/tmp"
prove_mp3 AppImage "$work/tmp/appimage_extracted_[0-9a-f]+/usr/bin/libmp3lame\.so\.0" \
    env TMPDIR="$work/tmp" APPIMAGE_EXTRACT_AND_RUN=1 "$appimage"
dump_window AppImage "$work/tmp/appimage_extracted_[0-9a-f]+/usr/bin/libmp3lame\.so\.0" \
    env TMPDIR="$work/tmp" APPIMAGE_EXTRACT_AND_RUN=1 "$appimage"

# The archive: one folder named like it, nothing beside it.
has "$archive"
tops=$(tar --list --gzip --file "$archive" | cut -d/ -f1 | sort -u)
[ "$tops" = "$name" ] || fail "$archive must hold the single folder $name, it holds: $tops"
mkdir -p -- "$work/tar"
tar --extract --gzip --file "$archive" --directory "$work/tar"
folder="$work/tar/$name"
has "$folder/README.txt"
check_layout tar.gz "$folder" "$folder/share" "$folder"
prove_mp3 tar.gz "$folder/libmp3lame\.so\.0" "$folder/hindsound"
dump_window tar.gz "$folder/libmp3lame\.so\.0" "$folder/hindsound"

(cd -- "$out" && sha256sum --check --strict --quiet SHA256SUMS) || fail "SHA256SUMS does not match the packages"
echo "check-packages: both packages are fine"
