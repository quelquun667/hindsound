#!/usr/bin/env bash
# Assembles Hindsound.app for Apple silicon and checks it:
#   1. publishes src/Hindsound.Mac for osx-arm64 with the options of the Linux app (self-contained, single file, trimmed);
#   2. lays out the bundle: Contents/MacOS/hindsound (and any other Mach-O the publish left: code only, or the signature
#      breaks), Contents/Frameworks/ the four dylibs of tools/mac/build-natives.sh, Contents/Resources/fonts/ the four
#      fonts of tools/linux/fetch-fonts.sh, Contents/Resources/ LICENSE, THIRD-PARTY-NOTICES.md and, in licenses/ laid out
#      as in the repository (the notices link to them), the licences of what the bundle carries, Contents/Info.plist from
#      Info.plist.in with the version;
#   3. signs it with sign-app.sh: ad hoc in the CI and in release-files.yml (resign-zip.sh signs the zip again with the
#      certificate in the job sign-mac of release.yml), by the certificate of HINDSOUND_SIGNING_P12 when it is set, ad hoc
#      otherwise;
#   4. checks: plutil -lint, codesign --verify --deep --strict, and otool -L of every Mach-O: no path of Homebrew
#      (/opt/homebrew, /usr/local) nor of the build machine, only the system and @rpath/@executable_path.
#
# Usage: build-app.sh NATIVE_DIR FONT_DIR OUT_DIR VERSION
#   NATIVE_DIR  the dylibs (build-natives.sh WORK_DIR NATIVE_DIR)
#   FONT_DIR    the fonts (fetch-fonts.sh WORK_DIR FONT_DIR)
#   OUT_DIR     receives Hindsound.app (an older one there is replaced)
#   VERSION     the version, such as 0.10.1
# Last checked: 21/11/2026, on the runner macos-26 (the CI's job mac).

set -euo pipefail

[ "$#" -eq 4 ] || { echo "Usage: $0 NATIVE_DIR FONT_DIR OUT_DIR VERSION" >&2; exit 2; }
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
natives="$(cd "$1" && pwd)"
fonts="$(cd "$2" && pwd)"
mkdir -p "$3"
out="$(cd "$3" && pwd)"
version="$4"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "The version '$version' is not x.y.z." >&2; exit 1; }

bash "$root/tools/mac/build-natives.sh" --check "$natives"
bash "$root/tools/linux/fetch-fonts.sh" --check "$fonts"

publish="$out/publish"
rm -rf "$publish"
dotnet publish "$root/src/Hindsound.Mac/Hindsound.Mac.csproj" -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=true "-p:Version=$version" "-p:PublishDir=$publish/"

app="$out/Hindsound.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Frameworks" "$app/Contents/Resources/fonts"
# No "file | grep -q" in this script: with pipefail, grep -q leaving early can fail the pipe, and the test with it.
for file in "$publish"/*; do
  if [[ "$(file -b "$file")" == Mach-O* ]]; then
    cp "$file" "$app/Contents/MacOS/"
  else
    echo "Left out of the bundle: $(basename "$file") (not code)"
  fi
done
[ -x "$app/Contents/MacOS/hindsound" ] || { echo "The publish has no hindsound." >&2; exit 1; }
for library in libSDL3.0.dylib libfreetype.6.dylib libharfbuzz.0.dylib libmp3lame.0.dylib; do
  cp "$natives/$library" "$app/Contents/Frameworks/"
done
for font in Selawik-Regular.ttf Selawik-Semibold.ttf BarlowSemiCondensed-SemiBold.ttf CascadiaMono-Regular.ttf; do
  cp "$fonts/$font" "$app/Contents/Resources/fonts/"
done
# The licences: of Hindsound, of the .NET runtime and of LAME (licenses/), of SDL and of the fonts (licenses/linux/, the
# same files as on Linux), of FreeType and HarfBuzz (licenses/mac/). THIRD-PARTY-NOTICES.md says where each comes from.
resources="$app/Contents/Resources"
mkdir -p "$resources/licenses/linux" "$resources/licenses/mac"
cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$resources/"
cp "$root"/licenses/*.txt "$resources/licenses/"
cp "$root"/licenses/mac/*.txt "$resources/licenses/mac/"
for notice in SDL3-LICENSE.txt SDL3-yuv2rgb-LICENSE.txt Selawik-LICENSE.txt CascadiaCode-LICENSE.txt Barlow-OFL.txt; do
  cp "$root/licenses/linux/$notice" "$resources/licenses/linux/"
done
sed -e "s/@VERSION@/$version/g" "$here/Info.plist.in" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"

bash "$here/sign-app.sh" "$app"

failed=0
while IFS= read -r file; do
  if [[ "$(file -b "$file")" == Mach-O* ]]; then
    links="$(otool -L "$file" | tail -n +2 | awk '{print $1}')"
    echo "$(basename "$file"): $(echo $links)"
    if [ -n "$(grep -Ev '^(/usr/lib/|/System/Library/|@rpath/|@executable_path/|@loader_path/)' <<< "$links" || true)" ]; then
      echo "$(basename "$file") links outside the system and the bundle." >&2
      failed=1
    fi
  fi
done < <(find "$app/Contents" -type f)
[ "$failed" -eq 0 ] || exit 1
rm -rf "$publish"
echo "Built: $app ($(du -sh "$app" | cut -f1))"
