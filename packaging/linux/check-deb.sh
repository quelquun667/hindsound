#!/usr/bin/env bash
# Checks the package made by build-deb.sh on the machine that built it: its fields, the exact list of its files with
# their owners and modes, that it carries the program, the libraries and the fonts of the tar.gz, what its ELF files link
# against its Depends, and lintian, which must find no error and no warning that packaging/linux/deb/lintian-overrides
# does not explain. try-deb.sh then installs it in clean systems.
#
# Usage: check-deb.sh <out folder> <version>
set -euo pipefail

readonly APP_ID='io.github.quelquun667.Hindsound'
readonly ICON_SIZES='48 128 256'
readonly FONTS='Selawik-Regular.ttf Selawik-Semibold.ttf BarlowSemiCondensed-SemiBold.ttf CascadiaMono-Regular.ttf'

fail() {
    echo "check-deb: $*" >&2
    exit 1
}

[ $# -eq 2 ] || fail "usage: check-deb.sh <out folder> <version>"
out=$(realpath -e -- "$1") || fail "no folder at $1"
version=$2
[[ $version =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-([0-9A-Za-z.-]+))?$ ]] || fail "not a version: $version"
debian_version=${BASH_REMATCH[1]}
if [ -n "${BASH_REMATCH[3]}" ]; then
    debian_version+="~${BASH_REMATCH[3]//-/.}"
fi
deb="$out/hindsound_${debian_version}_amd64.deb"
[ -f "$deb" ] || fail "missing: $deb"
repo=$(realpath -e -- "$(dirname -- "$(realpath -e -- "$0")")/../..")
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT

field() {
    local name=$1 expected=$2 actual
    actual=$(dpkg-deb --field "$deb" "$name")
    [ "$actual" = "$expected" ] || fail "the field $name is '$actual', expected '$expected'"
}
field Package hindsound
field Version "$debian_version"
field Architecture amd64
field Maintainer "Quelqu'un <87375238+quelquun667@users.noreply.github.com>"
field Depends 'libc6 (>= 2.34), libgcc-s1, libstdc++6, libssl3 | libssl3t64, libpulse0, libfreetype6, libharfbuzz0b, libfontconfig1'
field Recommends 'wl-clipboard | xclip | xsel'
field Section sound
field Priority optional
[ -z "$(dpkg-deb --field "$deb" Pre-Depends Suggests Conflicts Breaks Provides Replaces Essential)" ] \
    || fail "the package has a field it should not have"

# No maintainer script: the triggers of desktop-file-utils and hicolor-icon-theme update the caches.
dpkg-deb --ctrl-tarfile "$deb" | tar --list | sed -e 's|^\./||' -e '/^$/d' | LC_ALL=C sort > "$work/control"
printf '%s\n' control md5sums > "$work/control-expected"
diff -u "$work/control-expected" "$work/control" || fail "the control part holds other files than control and md5sums"

# Every path, with its type, mode and owner.
{
    echo "drwxr-xr-x root/root ./"
    for dir in usr usr/bin usr/lib usr/lib/hindsound usr/lib/hindsound/fonts usr/share usr/share/applications \
        usr/share/doc usr/share/doc/hindsound usr/share/doc/hindsound/licenses usr/share/doc/hindsound/licenses/linux \
        usr/share/icons usr/share/icons/hicolor usr/share/lintian usr/share/lintian/overrides; do
        echo "drwxr-xr-x root/root ./$dir/"
    done
    for size in $ICON_SIZES; do
        echo "drwxr-xr-x root/root ./usr/share/icons/hicolor/${size}x${size}/"
        echo "drwxr-xr-x root/root ./usr/share/icons/hicolor/${size}x${size}/apps/"
        echo "-rw-r--r-- root/root ./usr/share/icons/hicolor/${size}x${size}/apps/$APP_ID.png"
    done
    echo "lrwxrwxrwx root/root ./usr/bin/hindsound -> ../lib/hindsound/hindsound"
    echo "-rwxr-xr-x root/root ./usr/lib/hindsound/hindsound"
    echo "-rw-r--r-- root/root ./usr/lib/hindsound/libmp3lame.so.0"
    echo "-rw-r--r-- root/root ./usr/lib/hindsound/libSDL3.so.0"
    for font in $FONTS; do
        echo "-rw-r--r-- root/root ./usr/lib/hindsound/fonts/$font"
    done
    echo "-rw-r--r-- root/root ./usr/share/applications/$APP_ID.desktop"
    for file in copyright changelog.Debian.gz LICENSE THIRD-PARTY-NOTICES.md; do
        echo "-rw-r--r-- root/root ./usr/share/doc/hindsound/$file"
    done
    for file in "$repo"/licenses/*.txt; do
        echo "-rw-r--r-- root/root ./usr/share/doc/hindsound/licenses/$(basename -- "$file")"
    done
    for file in "$repo"/licenses/linux/*.txt; do
        echo "-rw-r--r-- root/root ./usr/share/doc/hindsound/licenses/linux/$(basename -- "$file")"
    done
    echo "-rw-r--r-- root/root ./usr/share/lintian/overrides/hindsound"
} | LC_ALL=C sort > "$work/expected"
dpkg-deb --contents "$deb" > "$work/contents"
awk '{ $3 = ""; $4 = ""; $5 = ""; print }' "$work/contents" | sed 's/  */ /g' | LC_ALL=C sort > "$work/listed"
diff -u "$work/expected" "$work/listed" || fail "the files of the package are not the expected ones (-: expected, +: in the package)"

# One date for every file: that of the commit.
dates=$(awk '{ print $4, $5 }' "$work/contents" | sort -u)
[ "$(echo "$dates" | wc -l)" -eq 1 ] || fail "the files of the package have several dates: $dates"

# The same program, encoder, window library and fonts as the tar.gz, and the same desktop entry.
mkdir -p -- "$work/root" "$work/tar"
dpkg-deb --extract "$deb" "$work/root"
lib="$work/root/usr/lib/hindsound"
tar --extract --gzip --file "$out/hindsound-$version-linux-x64.tar.gz" --directory "$work/tar"
folder="$work/tar/hindsound-$version-linux-x64"
for file in hindsound libmp3lame.so.0 libSDL3.so.0; do
    cmp -- "$folder/$file" "$lib/$file" || fail "$file differs from the one of the tar.gz"
done
for font in $FONTS; do
    cmp -- "$folder/fonts/$font" "$lib/fonts/$font" || fail "fonts/$font differs from the one of the tar.gz"
done
cmp -- "$repo/packaging/linux/$APP_ID.desktop" "$work/root/usr/share/applications/$APP_ID.desktop" || fail "the desktop entry differs"
desktop-file-validate -- "$work/root/usr/share/applications/$APP_ID.desktop" || fail "the desktop entry is not valid"

# What the three ELF files link: each library must come from a package of Depends, on this system. The versions of the
# C library they ask for must be at most the 2.34 of Depends.
for elf in "$lib/hindsound" "$lib/libmp3lame.so.0" "$lib/libSDL3.so.0"; do
    while IFS= read -r needed; do
        case "$needed" in
            libc.so.6 | libm.so.6 | libdl.so.2 | libpthread.so.0 | librt.so.1 | ld-linux-x86-64.so.2) package=libc6 ;;
            libgcc_s.so.1) package=libgcc-s1 ;;
            libstdc++.so.6) package=libstdc++6 ;;
            *) fail "$(basename -- "$elf") links $needed, which no package of Depends provides" ;;
        esac
        owner=$(dpkg -S "*/$needed" 2> /dev/null | cut -d: -f1 | tr ',' '\n' | sed 's/^ *//' | grep -x -F -- "$package" | head -n 1 || true)
        [ "$owner" = "$package" ] || fail "$needed is not from $package on this system"
        echo "check-deb: $(basename -- "$elf") links $needed, from $package"
    done < <(readelf -d -- "$elf" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p')
done
glibc=$(for elf in "$lib/hindsound" "$lib/libmp3lame.so.0" "$lib/libSDL3.so.0"; do objdump -T -- "$elf"; done \
    | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -V | tail -n 1)
dpkg --compare-versions "$glibc" le 2.34 || fail "the files ask for GLIBC_$glibc, above the libc6 (>= 2.34) of Depends"
echo "check-deb: the newest symbol of the C library asked for is GLIBC_$glibc"

# The copyright file is machine-readable, and changelog.Debian.gz starts with this version.
grep -q -x -F 'Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/' -- "$work/root/usr/share/doc/hindsound/copyright" \
    || fail "the copyright file is not in the DEP-5 format"
zcat -- "$work/root/usr/share/doc/hindsound/changelog.Debian.gz" | head -n 1 | grep -q -x -F "hindsound ($debian_version) unstable; urgency=medium" \
    || fail "changelog.Debian.gz does not start with the entry of $debian_version"

# lintian: no error, and no warning that the overrides do not explain. The notes below the warnings are shown, not counted.
lintian --version
lintian --fail-on error,warning --display-info --pedantic --show-overrides -- "$deb" || fail "lintian found an error or a warning"

echo "check-deb: $(basename -- "$deb") is fine"
