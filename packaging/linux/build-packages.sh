#!/usr/bin/env bash
# Makes the two Linux packages of Hindsound from the published app:
#   hindsound-<version>-linux-x64.AppImage  one file to run, on the static type2 runtime (no libfuse2 needed)
#   hindsound-<version>-linux-x64.tar.gz    the same program in a folder, to unpack anywhere
# and SHA256SUMS for both.
#
# Usage: build-packages.sh <publish folder> <libmp3lame.so.0> <version> <out folder>
#   publish folder   the output of: dotnet publish src/Hindsound.Linux/Hindsound.Linux.csproj -c Release -r linux-x64
#                    --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true
#   libmp3lame.so.0  the MP3 encoder built by tools/release/build-lame-linux.sh
#   version          for the file names, for example 0.5.0
#
# appimagetool and the runtime come from fixed releases and are checked against the sha256 below before anything runs.
# To move to newer ones, take both values from the "digest" of the asset on its GitHub release.
set -euo pipefail

readonly APPIMAGETOOL_URL='https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage'
readonly APPIMAGETOOL_SHA256='ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0'
readonly RUNTIME_URL='https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64'
readonly RUNTIME_SHA256='2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d'

# The id of the portal, the desktop entry and the bus (DBusNames.AppId in the app).
readonly APP_ID='io.github.quelquun667.Hindsound'
readonly ICON_SIZES='48 128 256'

fail() {
    echo "build-packages: $*" >&2
    exit 1
}

[ $# -eq 4 ] || fail "usage: build-packages.sh <publish folder> <libmp3lame.so.0> <version> <out folder>"

publish=$(realpath -e -- "$1") || fail "no publish folder at $1"
lame=$(realpath -e -- "$2") || fail "no libmp3lame.so.0 at $2"
version=$3
mkdir -p -- "$4"
out=$(realpath -e -- "$4")

[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || fail "not a version: $version"
[ -f "$publish/hindsound" ] || fail "$publish has no program named hindsound"
[ -f "$lame" ] || fail "$lame is not a file"

here=$(dirname -- "$(realpath -e -- "$0")")
repo=$(realpath -e -- "$here/../..")
name="hindsound-$version-linux-x64"

# A single-file publish leaves the program alone, with at most its symbols. Anything else would be left out of the
# packages without a word, so it stops the build until someone decides where it goes.
while IFS= read -r -d '' file; do
    case "$(basename -- "$file")" in
        hindsound | hindsound.pdb | hindsound.dbg) ;;
        *) fail "unexpected file in the publish folder: $file" ;;
    esac
done < <(find "$publish" -mindepth 1 -maxdepth 1 -print0)

work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT

fetch() {
    local url=$1 sha=$2 dest=$3
    curl --fail --location --silent --show-error --retry 3 --output "$dest" -- "$url"
    echo "$sha  $dest" | sha256sum --check --strict --quiet - || fail "$url does not have the expected sha256"
}

# What both packages hold, under a prefix: bin holds the program and the encoder side by side.
lay_out() {
    local bin=$1 share=$2 doc=$3
    mkdir -p -- "$bin" "$share/applications" "$doc/licenses"
    install -m 755 -- "$publish/hindsound" "$bin/hindsound"
    install -m 644 -- "$lame" "$bin/libmp3lame.so.0"
    install -m 644 -- "$here/$APP_ID.desktop" "$share/applications/$APP_ID.desktop"
    local size
    for size in $ICON_SIZES; do
        local icon="icons/hicolor/${size}x${size}/apps/$APP_ID.png"
        mkdir -p -- "$share/$(dirname -- "$icon")"
        install -m 644 -- "$here/$icon" "$share/$icon"
    done
    install -m 644 -- "$repo/LICENSE" "$doc/LICENSE"
    install -m 644 -- "$repo/THIRD-PARTY-NOTICES.md" "$doc/THIRD-PARTY-NOTICES.md"
    install -m 644 -- "$repo"/licenses/* "$doc/licenses/"
}

# The AppImage: the AppDir has AppRun, the desktop entry and the icon at its root, as appimagetool asks.
appdir="$work/Hindsound.AppDir"
lay_out "$appdir/usr/bin" "$appdir/usr/share" "$appdir/usr/share/doc/hindsound"
install -m 755 -- "$here/AppRun" "$appdir/AppRun"
cp -- "$appdir/usr/share/applications/$APP_ID.desktop" "$appdir/$APP_ID.desktop"
cp -- "$appdir/usr/share/icons/hicolor/256x256/apps/$APP_ID.png" "$appdir/$APP_ID.png"
ln -s -- "$APP_ID.png" "$appdir/.DirIcon"

fetch "$APPIMAGETOOL_URL" "$APPIMAGETOOL_SHA256" "$work/appimagetool.AppImage"
fetch "$RUNTIME_URL" "$RUNTIME_SHA256" "$work/runtime-x86_64"
chmod +x -- "$work/appimagetool.AppImage"

# appimagetool is an AppImage itself: extracted instead of mounted, it needs no FUSE on the build machine.
mkdir -p -- "$work/tool"
(cd -- "$work/tool" && "$work/appimagetool.AppImage" --appimage-extract > /dev/null)
ARCH=x86_64 "$work/tool/squashfs-root/AppRun" --no-appstream --runtime-file "$work/runtime-x86_64" \
    "$appdir" "$out/$name.AppImage"
chmod 755 -- "$out/$name.AppImage"

# The archive: one folder named like the archive, its README at the top, and share/ ready to copy to ~/.local/share.
folder="$work/tar/$name"
lay_out "$folder" "$folder/share" "$folder"
install -m 644 -- "$here/README.txt" "$folder/README.txt"

# The same bytes for the same commit: fixed owner, order and dates, and no name or date in the gzip header.
stamp=${SOURCE_DATE_EPOCH:-$(git -C "$repo" log -1 --format=%ct 2> /dev/null || date +%s)}
tar --create --sort=name --owner=0 --group=0 --numeric-owner --mtime="@$stamp" --directory="$work/tar" -- "$name" \
    | gzip -9 -n > "$out/$name.tar.gz"

(cd -- "$out" && sha256sum -- "$name.AppImage" "$name.tar.gz" > SHA256SUMS)
echo "build-packages: $out/$name.AppImage, $out/$name.tar.gz"
