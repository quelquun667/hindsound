#!/usr/bin/env bash
# Makes the Debian package of Hindsound from the same inputs as build-packages.sh:
#   hindsound_<version>_amd64.deb   for apt install on Ubuntu 22.04 and later and Debian 12 and later (x64)
# and adds its line to the SHA256SUMS of the out folder.
#
# Usage: build-deb.sh <publish folder> <libmp3lame.so.0> <libSDL3.so.0> <fonts folder> <version> <out folder>
#   the first five as for build-packages.sh; a pre-release such as 1.2.3-rc.1 becomes the Debian version 1.2.3~rc.1,
#   which apt sorts before 1.2.3.
#
# What it holds:
#   /usr/lib/hindsound/          hindsound, libmp3lame.so.0, libSDL3.so.0 and fonts/, side by side as in the tar.gz:
#                                the app looks for them next to the real program
#   /usr/bin/hindsound           a link to ../lib/hindsound/hindsound
#   /usr/share/applications/     the desktop entry, and the hicolor icons in /usr/share/icons: the files of the other
#                                packages. The triggers of desktop-file-utils and hicolor-icon-theme update their caches:
#                                the package has no maintainer script.
#   /usr/share/doc/hindsound/    copyright (DEP-5), changelog.Debian.gz, and the licence texts of the tar.gz
#
# The same bytes for the same commit and the same inputs: SOURCE_DATE_EPOCH (or the date of the last commit) for every
# date, files owned by root, fixed modes, and dpkg-deb, which writes the files in order.
set -euo pipefail

readonly APP_ID='io.github.quelquun667.Hindsound'
readonly ICON_SIZES='48 128 256'
readonly FONTS='Selawik-Regular.ttf Selawik-Semibold.ttf BarlowSemiCondensed-SemiBold.ttf CascadiaMono-Regular.ttf'
readonly MAINTAINER="Quelqu'un <87375238+quelquun667@users.noreply.github.com>"
# What the program needs from the system, checked by check-deb.sh against what its files link, and by try-deb.sh in
# clean containers:
#   libc6 (>= 2.34)         the C library of the program and of both libraries (glibc 2.34: Ubuntu 22.04, Debian 12)
#   libgcc-s1, libstdc++6   linked by the .NET runtime inside the program
#   libssl3 | libssl3t64    loaded by .NET for the HTTPS of the update check (libssl3t64 on Ubuntu 24.04)
#   libpulse0               the sound: capture, monitor and playback through PulseAudio or PipeWire
#   libfreetype6, libharfbuzz0b, libfontconfig1   the text of the window
readonly DEPENDS='libc6 (>= 2.34), libgcc-s1, libstdc++6, libssl3 | libssl3t64, libpulse0, libfreetype6, libharfbuzz0b, libfontconfig1'
# The clipboard of a saved clip: wl-copy on Wayland, xclip or xsel on X11.
readonly RECOMMENDS='wl-clipboard | xclip | xsel'

fail() {
    echo "build-deb: $*" >&2
    exit 1
}

[ $# -eq 6 ] || fail "usage: build-deb.sh <publish folder> <libmp3lame.so.0> <libSDL3.so.0> <fonts folder> <version> <out folder>"

publish=$(realpath -e -- "$1") || fail "no publish folder at $1"
lame=$(realpath -e -- "$2") || fail "no libmp3lame.so.0 at $2"
sdl=$(realpath -e -- "$3") || fail "no libSDL3.so.0 at $3"
fonts=$(realpath -e -- "$4") || fail "no fonts folder at $4"
version=$5
mkdir -p -- "$6"
out=$(realpath -e -- "$6")

[[ $version =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-([0-9A-Za-z.-]+))?$ ]] || fail "not a version: $version"
# A Debian version without a revision may not hold a hyphen: the pre-release follows a tilde, its hyphens become dots.
debian_version=${BASH_REMATCH[1]}
if [ -n "${BASH_REMATCH[3]}" ]; then
    debian_version+="~${BASH_REMATCH[3]//-/.}"
fi
if command -v dpkg > /dev/null; then
    dpkg --validate-version -- "$debian_version" || fail "$debian_version is not a Debian version"
fi

[ -f "$publish/hindsound" ] || fail "$publish has no program named hindsound"
[ -f "$lame" ] || fail "$lame is not a file"
[ -f "$sdl" ] || fail "$sdl is not a file"
for font in $FONTS; do
    [ -f "$fonts/$font" ] || fail "$fonts has no $font"
done

here=$(dirname -- "$(realpath -e -- "$0")")
repo=$(realpath -e -- "$here/../..")
name="hindsound_${debian_version}_amd64.deb"
stamp=${SOURCE_DATE_EPOCH:-$(git -C "$repo" log -1 --format=%ct 2> /dev/null || date +%s)}
[[ $stamp =~ ^[0-9]+$ ]] || fail "not a date in seconds: $stamp"
export SOURCE_DATE_EPOCH=$stamp

work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
umask 022
root="$work/root"
lib="$root/usr/lib/hindsound"
share="$root/usr/share"
doc="$share/doc/hindsound"

mkdir -p -- "$lib/fonts" "$root/usr/bin" "$share/applications" "$doc/licenses/linux" "$share/lintian/overrides" "$root/DEBIAN"
install -m 755 -- "$publish/hindsound" "$lib/hindsound"
install -m 644 -- "$lame" "$lib/libmp3lame.so.0"
install -m 644 -- "$sdl" "$lib/libSDL3.so.0"
for font in $FONTS; do
    install -m 644 -- "$fonts/$font" "$lib/fonts/$font"
done
# Relative, within /usr, as the Debian policy asks. The kernel gives the program its real path (/proc/self/exe): it
# finds the libraries and the fonts next to /usr/lib/hindsound/hindsound, not next to the link.
ln -s -- ../lib/hindsound/hindsound "$root/usr/bin/hindsound"

install -m 644 -- "$here/$APP_ID.desktop" "$share/applications/$APP_ID.desktop"
for size in $ICON_SIZES; do
    icon="icons/hicolor/${size}x${size}/apps/$APP_ID.png"
    mkdir -p -- "$share/$(dirname -- "$icon")"
    install -m 644 -- "$here/$icon" "$share/$icon"
done

# The licence texts of the tar.gz, at the same places, so that the links of THIRD-PARTY-NOTICES.md hold.
install -m 644 -- "$repo/LICENSE" "$doc/LICENSE"
install -m 644 -- "$repo/THIRD-PARTY-NOTICES.md" "$doc/THIRD-PARTY-NOTICES.md"
install -m 644 -- "$repo"/licenses/*.txt "$doc/licenses/"
install -m 644 -- "$repo"/licenses/linux/*.txt "$doc/licenses/linux/"
install -m 644 -- "$here/deb/lintian-overrides" "$share/lintian/overrides/hindsound"

# A text as the body of a DEP-5 licence paragraph: each line indented by one space, an empty line as " .".
licence_body() {
    sed -e 's/[[:space:]]*$//' -e 's/^$/./' -e 's/^/ /' -- "$1"
}
{
    cat -- "$here/deb/copyright"
    printf '\nLicense: Expat\n'
    licence_body "$repo/LICENSE"
    printf '\nLicense: Zlib\n'
    licence_body "$repo/licenses/linux/SDL3-LICENSE.txt"
    printf '\nLicense: OFL-1.1\n'
    licence_body "$repo/licenses/linux/Barlow-OFL.txt"
} | sed 's/\r$//' > "$doc/copyright"
chmod 644 -- "$doc/copyright"

# One entry: this package is made from the release of that version, whose notes are on its page.
{
    printf 'hindsound (%s) unstable; urgency=medium\n\n' "$debian_version"
    printf '  * Hindsound %s. What changed is in the notes of its release:\n' "$version"
    printf '    https://github.com/quelquun667/hindsound/releases/tag/v%s\n\n' "$version"
    printf ' -- %s  %s\n' "$MAINTAINER" "$(LC_ALL=C date -u -R -d "@$stamp")"
} | gzip -9 -n > "$doc/changelog.Debian.gz"
chmod 644 -- "$doc/changelog.Debian.gz"

# The size in KiB as dpkg-gencontrol counts it: each file rounded up to a KiB, one for each folder and link.
installed_size=$(find "$root" -mindepth 1 -path "$root/DEBIAN" -prune -o -printf '%y %s\n' \
    | awk '{ if ($1 == "f") total += int(($2 + 1023) / 1024); else total += 1 } END { print total }')

cat > "$root/DEBIAN/control" << EOF
Package: hindsound
Version: $debian_version
Architecture: amd64
Maintainer: $MAINTAINER
Installed-Size: $installed_size
Depends: $DEPENDS
Recommends: $RECOMMENDS
Section: sound
Priority: optional
Homepage: https://github.com/quelquun667/hindsound
Description: keeps the last minutes of sound and saves them on a key
 Hindsound runs in the background and keeps the last minutes of the sound of
 the PC, and of a microphone if one is set, in memory. A hotkey, the command
 "hindsound --save" or the menu of its icon saves that past to an MP3 or WAV
 file.
EOF
(cd -- "$root" && find usr -type f -print0 | LC_ALL=C sort -z | xargs -0 md5sum) > "$root/DEBIAN/md5sums"
chmod 644 -- "$root/DEBIAN/control" "$root/DEBIAN/md5sums"

# Fixed modes and dates: the folders 755, every date that of the commit.
find "$root" -type d -exec chmod 755 -- {} +
find "$root" -exec touch --no-dereference --date="@$stamp" -- {} +

rm -f -- "$out/$name"
dpkg-deb --root-owner-group -Zxz --build "$root" "$out/$name" > /dev/null

# The line of the package in the SHA256SUMS of the out folder, which build-packages.sh wrote for the other two.
sums="$out/SHA256SUMS"
if [ -f "$sums" ]; then
    grep -v -F "  $name" -- "$sums" > "$work/sums" || true
else
    : > "$work/sums"
fi
(cd -- "$out" && sha256sum -- "$name") >> "$work/sums"
cp -- "$work/sums" "$sums"
echo "build-deb: $out/$name"
