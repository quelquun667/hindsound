#!/usr/bin/env bash
# Checks the packages made by build-packages.sh, as a user would get them: the files each one holds, and the program of
# each one started for real with --prove-mp3, which writes one second of a sine as an MP3 through the path of a real clip
# and leaves. That needs no sound server, no session bus and no FUSE. The run must load the libmp3lame.so.0 of the
# package, not one the machine may already have: the dynamic loader says which file it loaded (LD_DEBUG=libs).
#
# Usage: check-packages.sh <out folder> <version>
set -euo pipefail

readonly APP_ID='io.github.quelquun667.Hindsound'
readonly ICON_SIZES='48 128 256'

fail() {
    echo "check-packages: $*" >&2
    exit 1
}

[ $# -eq 2 ] || fail "usage: check-packages.sh <out folder> <version>"
out=$(realpath -e -- "$1") || fail "no folder at $1"
name="hindsound-$2-linux-x64"
appimage="$out/$name.AppImage"
archive="$out/$name.tar.gz"

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
    [ "$code" -eq 0 ] || fail "$label: --prove-mp3 ended with code $code (4: the clip came out as WAV)"

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
}

# The files each package must hold, under its prefix.
check_layout() {
    local label=$1 bin=$2 share=$3 doc=$4
    [ -x "$bin/hindsound" ] || fail "$label: $bin/hindsound is missing or not executable"
    has "$bin/libmp3lame.so.0"
    readelf -d -- "$bin/libmp3lame.so.0" | grep -q 'Library soname: \[libmp3lame\.so\.0\]' \
        || fail "$label: libmp3lame.so.0 does not have the soname libmp3lame.so.0"
    has "$share/applications/$APP_ID.desktop"
    local size
    for size in $ICON_SIZES; do
        has "$share/icons/hicolor/${size}x${size}/apps/$APP_ID.png"
    done
    has "$doc/LICENSE"
    has "$doc/THIRD-PARTY-NOTICES.md"
    has "$doc/licenses/LAME-COPYING.txt"
    has "$doc/licenses/linux/Tmds.DBus.Protocol-COPYING.txt"
    # Every text only the Linux packages carry: those of the AppImage runtime and of the libraries built into it, and that
    # of Tmds.DBus.Protocol, which only the Linux app references. THIRD-PARTY-NOTICES.md links them.
    local licence
    for licence in AppImage-type2-runtime-LICENSE.txt musl-COPYRIGHT.txt libfuse-LGPL2.txt libfuse-LICENSE.txt \
        squashfuse-LICENSE.txt zstd-LICENSE.txt zlib-LICENSE.txt mimalloc-LICENSE.txt Tmds.DBus.Protocol-COPYING.txt; do
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

(cd -- "$out" && sha256sum --check --strict --quiet SHA256SUMS) || fail "SHA256SUMS does not match the packages"
echo "check-packages: both packages are fine"
