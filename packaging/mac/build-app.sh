#!/usr/bin/env bash
# Assembles Hindsound.app for Apple silicon and checks it:
#   1. publishes src/Hindsound.Mac for osx-arm64 with the options of the Linux app (self-contained, single file, trimmed);
#   2. lays out the bundle: Contents/MacOS/hindsound (and any other Mach-O the publish left: code only, or the signature
#      breaks), Contents/Frameworks/ the four dylibs of tools/mac/build-natives.sh, Contents/Resources/fonts/ the four
#      fonts of tools/linux/fetch-fonts.sh, Contents/Info.plist from Info.plist.in with the version;
#   3. signs it ad hoc, each Mach-O inside first, then the bundle (the certificate comes with the release, step M4);
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
for file in "$publish"/*; do
  if file -b "$file" | grep -q '^Mach-O'; then
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
sed -e "s/@VERSION@/$version/g" "$here/Info.plist.in" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"

# Inside out: every Mach-O but the main program, then the bundle, which seals the rest.
while IFS= read -r file; do
  codesign --force --timestamp=none --sign - "$file"
done < <(find "$app/Contents/Frameworks" "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/hindsound")
codesign --force --timestamp=none --sign - --identifier io.github.quelquun667.hindsound "$app"
codesign --verify --deep --strict --verbose=2 "$app"
codesign -dvv "$app" 2>&1 | grep -E '^(Identifier|Format|Signature|CodeDirectory)' || true

failed=0
while IFS= read -r file; do
  if file -b "$file" | grep -q '^Mach-O'; then
    links="$(otool -L "$file" | tail -n +2 | awk '{print $1}')"
    echo "$(basename "$file"): $(echo $links)"
    if printf '%s\n' "$links" | grep -Ev '^(/usr/lib/|/System/Library/|@rpath/|@executable_path/|@loader_path/)' | grep -q .; then
      echo "$(basename "$file") links outside the system and the bundle." >&2
      failed=1
    fi
  fi
done < <(find "$app/Contents" -type f)
[ "$failed" -eq 0 ] || exit 1
rm -rf "$publish"
echo "Built: $app ($(du -sh "$app" | cut -f1))"
