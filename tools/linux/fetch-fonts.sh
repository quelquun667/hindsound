#!/usr/bin/env bash
# Fetches the four fonts the Linux app draws its window with, each from a fixed upstream release checked before anything
# reads it, and puts them in OUT_DIR under the names the app looks for:
#   Selawik-Regular.ttf             the text of the pages (Selawik has the widths of Segoe UI, the font of the Windows app)
#   Selawik-Semibold.ttf            the medium weight
#   BarlowSemiCondensed-SemiBold.ttf  the titles (in place of Bahnschrift, which is not free to ship)
#   CascadiaMono-Regular.ttf        the fixed-width text, the same font as on Windows
# All four are under the SIL Open Font License 1.1: their texts are in licenses/linux of the repository.
#
# How each one is pinned:
#   - Selawik 1.01: the asset Selawik_Release.zip of the release 1.01 of microsoft/Selawik. GitHub gives no digest for an
#     asset that old: the SHA-256 is the one the Homebrew cask font-selawik publishes for that URL.
#   - Cascadia Mono 2407.24: the asset CascadiaCode-2407.24.zip of the release v2407.24 of microsoft/cascadia-code, its static
#     TTF. No digest from GitHub either: the SHA-256 is the one the Homebrew casks font-cascadia-mono and font-cascadia-code
#     publish for that URL.
#   - Barlow 1.422: the release 1.422 of jpt/barlow has no asset; the font is the file of the repository at the commit of
#     that tag, checked against the id git gives that file in that commit (its blob hash, which GitHub's API lists for the
#     tree of the commit) and its size.
#
# Usage:
#   fetch-fonts.sh WORK_DIR OUT_DIR   fetches, checks and writes the four fonts; WORK_DIR receives the downloads
#   fetch-fonts.sh --check OUT_DIR    checks four fonts already there (a cache gave them back)

set -euo pipefail

readonly SELAWIK_URL='https://github.com/microsoft/Selawik/releases/download/1.01/Selawik_Release.zip'
readonly SELAWIK_SHA256='3f62c51e05e3b5a1e6241cf92a371f0be2ea1183aa87b30718bbd40832a8d423'
readonly CASCADIA_URL='https://github.com/microsoft/cascadia-code/releases/download/v2407.24/CascadiaCode-2407.24.zip'
readonly CASCADIA_SHA256='e67a68ee3386db63f48b9054bd196ea752bc6a4ebb4df35adce6733da50c8474'
readonly BARLOW_COMMIT='697600fcece1685b0ba409eb82868546def9a84e'
readonly BARLOW_URL="https://raw.githubusercontent.com/jpt/barlow/$BARLOW_COMMIT/fonts/ttf/BarlowSemiCondensed-SemiBold.ttf"
readonly BARLOW_BLOB='bac233a9249fc0e33ce5140e52723fce5bf601f4'
readonly BARLOW_SIZE=168500

readonly FONTS='Selawik-Regular.ttf Selawik-Semibold.ttf BarlowSemiCondensed-SemiBold.ttf CascadiaMono-Regular.ttf'

usage() {
  echo "Usage: $0 WORK_DIR OUT_DIR | --check OUT_DIR" >&2
  exit 2
}

sha256_of() { sha256sum "$1" | cut -d' ' -f1; }

# A TrueType font starts with the version 1.0 of the sfnt table directory, or with "true".
check_fonts() {
  local dir="$1" font head
  for font in $FONTS; do
    if [ ! -s "$dir/$font" ]; then
      echo "$dir/$font is missing or empty." >&2
      exit 1
    fi
    head="$(od -An -tx1 -N4 -- "$dir/$font" | tr -d ' \n')"
    if [ "$head" != 00010000 ] && [ "$head" != 74727565 ]; then
      echo "$dir/$font is not a TrueType font: it starts with $head." >&2
      exit 1
    fi
    echo "$font: $(stat -c %s -- "$dir/$font") bytes, SHA-256 $(sha256_of "$dir/$font")"
  done
}

download() {
  local url="$1" dest="$2"
  echo "Downloading $url"
  curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 900 --output "$dest" "$url"
}

verify_sha256() {
  local file="$1" expected="$2" actual
  actual="$(sha256_of "$file")"
  if [ "$actual" != "$expected" ]; then
    echo "$file has the SHA-256 $actual, expected $expected. Nothing was extracted." >&2
    exit 1
  fi
  echo "Verified: $file, SHA-256 $actual"
}

# Writes the one member of the zip whose path is NAME or ends in /NAME to DEST; fails if there is none or more than one.
extract_one() {
  local zip="$1" name="$2" dest="$3" members
  members="$(unzip -Z1 "$zip" | grep -Ex "(.*/)?${name//./[.]}" || true)"
  if [ -z "$members" ] || [ "$(printf '%s\n' "$members" | wc -l)" -ne 1 ]; then
    echo "$zip must hold one $name, it holds: ${members:-none}" >&2
    exit 1
  fi
  unzip -p "$zip" "$members" > "$dest"
}

case "${1:-}" in
  --check)
    [ "$#" -eq 2 ] || usage
    check_fonts "$(cd "$2" && pwd)"
    exit 0
    ;;
esac
[ "$#" -eq 2 ] || usage

for tool in curl unzip git sha256sum od; do
  command -v "$tool" > /dev/null || { echo "$tool is not installed." >&2; exit 1; }
done

mkdir -p "$1" "$2"
work="$(cd "$1" && pwd)"
out="$(cd "$2" && pwd)"

download "$SELAWIK_URL" "$work/Selawik_Release.zip"
verify_sha256 "$work/Selawik_Release.zip" "$SELAWIK_SHA256"
download "$CASCADIA_URL" "$work/CascadiaCode-2407.24.zip"
verify_sha256 "$work/CascadiaCode-2407.24.zip" "$CASCADIA_SHA256"

download "$BARLOW_URL" "$work/BarlowSemiCondensed-SemiBold.ttf"
blob="$(git hash-object --no-filters -- "$work/BarlowSemiCondensed-SemiBold.ttf")"
size="$(stat -c %s -- "$work/BarlowSemiCondensed-SemiBold.ttf")"
if [ "$blob" != "$BARLOW_BLOB" ] || [ "$size" != "$BARLOW_SIZE" ]; then
  echo "The Barlow font has the git blob $blob and $size bytes, expected $BARLOW_BLOB and $BARLOW_SIZE." >&2
  exit 1
fi
echo "Verified: $work/BarlowSemiCondensed-SemiBold.ttf, git blob $blob of jpt/barlow at $BARLOW_COMMIT"

# The upstream file names: selawk.ttf is the regular weight, selawksb.ttf the semibold one.
extract_one "$work/Selawik_Release.zip" selawk.ttf "$out/Selawik-Regular.ttf"
extract_one "$work/Selawik_Release.zip" selawksb.ttf "$out/Selawik-Semibold.ttf"
extract_one "$work/CascadiaCode-2407.24.zip" ttf/static/CascadiaMono-Regular.ttf "$out/CascadiaMono-Regular.ttf"
cp -- "$work/BarlowSemiCondensed-SemiBold.ttf" "$out/BarlowSemiCondensed-SemiBold.ttf"
chmod 644 "$out"/*.ttf

# Nothing else in the folder: the packages copy it whole.
for file in "$out"/*; do
  case " $FONTS " in
    *" $(basename -- "$file") "*) ;;
    *) echo "Unexpected file in $out: $file" >&2; exit 1 ;;
  esac
done
check_fonts "$out"
echo "Fonts: $out"
