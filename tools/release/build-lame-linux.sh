#!/usr/bin/env bash
# Builds libmp3lame.so.0 and the lame command from the upstream LAME 3.100 archive, and checks the result.
#
# Runs on Linux (the CI runs it on ubuntu-22.04, so that the library runs on every newer Ubuntu), with a C compiler, make
# and curl. It stops at the first step that fails:
#   1. Downloads lame-3.100.tar.gz into WORK_DIR, or takes ARCHIVE, and verifies its SHA-256 before anything else. It is the
#      archive build-lame.ps1 verifies, with the same SHA-256: Windows and Linux are built from one source.
#   2. Extracts it and builds out of the tree, with the configure of the archive and no file changed:
#      - the shared library, libmp3lame.so.0, without the command line program;
#      - the command line program, linked statically, so that it needs no library next to it. It is a tool of the tests
#        (lame --decode); it is not shipped.
#      No NASM (as on Windows: configure leaves it off unless asked) and the default optimisation of the archive.
#   3. Fails if the build changed a file of the archive: the notices promise an unchanged LAME.
#   4. Checks the library: x86-64, the name libmp3lame.so.0, the functions the app calls, nothing needed beyond libc and
#      libm, version 3.100; and that the command reports 3.100.
#
# Usage: build-lame-linux.sh WORK_DIR OUT_DIR [ARCHIVE]
#   WORK_DIR  an empty or new folder: receives the archive, the sources, the builds and the logs.
#   OUT_DIR   receives libmp3lame.so.0 and lame.
#   ARCHIVE   an archive already there, instead of downloading. Its SHA-256 is verified just the same.
# For tests of this script only (a fake archive): LAME_EXPECTED_SHA256. A workflow never sets it.

set -euo pipefail

if [ "$#" -lt 2 ] || [ "$#" -gt 3 ]; then
  echo "Usage: $0 WORK_DIR OUT_DIR [ARCHIVE]" >&2
  exit 2
fi

expected_sha256="${LAME_EXPECTED_SHA256:-ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e}"
urls=(
  "https://sourceforge.net/projects/lame/files/lame/3.100/lame-3.100.tar.gz/download"
  "https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz"
)

mkdir -p "$1" "$2"
work="$(cd "$1" && pwd)"
out="$(cd "$2" && pwd)"
archive_given="${3:-}"

sha256_of() { sha256sum "$1" | cut -d' ' -f1; }

# --- 1. The archive, verified before anything reads it ---
if [ -n "$archive_given" ]; then
  archive="$archive_given"
else
  archive="$work/lame-3.100.tar.gz"
  rm -f "$archive"
  # The second address is the same file on the download host: it is tried when the first one fails or gives another file.
  for url in "${urls[@]}"; do
    echo "Downloading $url"
    if curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 300 --output "$archive" "$url"; then
      if [ "$(sha256_of "$archive")" = "$expected_sha256" ]; then
        break
      fi
      echo "What came is not the archive: its SHA-256 differs."
    else
      echo "curl failed."
    fi
    rm -f "$archive"
  done
  if [ ! -f "$archive" ]; then
    echo "The LAME archive could not be downloaded from any address." >&2
    exit 1
  fi
fi

actual_sha256="$(sha256_of "$archive")"
if [ "$actual_sha256" != "$expected_sha256" ]; then
  echo "$archive has the SHA-256 $actual_sha256, expected $expected_sha256. Nothing was extracted." >&2
  exit 1
fi
echo "Archive verified: $archive, SHA-256 $actual_sha256"

# --- 2. Extraction and build ---
source_root="$work/src"
if [ -e "$source_root" ]; then
  echo "$source_root already exists: give an empty WORK_DIR." >&2
  exit 1
fi
mkdir -p "$source_root"
tar -xzf "$archive" -C "$source_root"
source="$source_root/lame-3.100"
for needed in configure include/lame.h libmp3lame/lame.c frontend/main.c; do
  if [ ! -f "$source/$needed" ]; then
    echo "The archive has no lame-3.100/$needed." >&2
    exit 1
  fi
done
( cd "$source" && find . -type f -print0 | sort -z | xargs -0 sha256sum ) > "$work/source-before.sha256"

jobs="$(nproc 2>/dev/null || echo 2)"

echo "--- the shared library ---"
mkdir -p "$work/build-shared"
(
  cd "$work/build-shared"
  "$source/configure" --enable-shared --disable-static --disable-frontend --disable-gtktest
  make -j"$jobs"
) > "$work/build-shared.log" 2>&1 || { tail -n 60 "$work/build-shared.log" >&2; echo "The build of the library failed: see above." >&2; exit 1; }
tail -n 3 "$work/build-shared.log"

echo "--- the command line program ---"
mkdir -p "$work/build-static"
(
  cd "$work/build-static"
  "$source/configure" --disable-shared --enable-static --enable-frontend --disable-gtktest
  make -j"$jobs"
) > "$work/build-static.log" 2>&1 || { tail -n 60 "$work/build-static.log" >&2; echo "The build of the command failed: see above." >&2; exit 1; }
tail -n 3 "$work/build-static.log"

# --- 3. The sources are as they came ---
( cd "$source" && find . -type f -print0 | sort -z | xargs -0 sha256sum ) > "$work/source-after.sha256"
if ! diff -q "$work/source-before.sha256" "$work/source-after.sha256" > /dev/null; then
  echo "The build changed a source file, which the notices rule out:" >&2
  diff "$work/source-before.sha256" "$work/source-after.sha256" >&2 || true
  exit 1
fi

# --- 4. The result ---
built_library="$work/build-shared/libmp3lame/.libs/libmp3lame.so.0.0.0"
built_command="$work/build-static/frontend/lame"
for built in "$built_library" "$built_command"; do
  if [ ! -f "$built" ]; then
    echo "The build succeeded, but $built is not there." >&2
    exit 1
  fi
done

cp "$built_library" "$out/libmp3lame.so.0"
cp "$built_command" "$out/lame"
strip --strip-unneeded "$out/libmp3lame.so.0" "$out/lame"
chmod 755 "$out/libmp3lame.so.0" "$out/lame"

library="$out/libmp3lame.so.0"

if ! file -L "$library" | grep -q "ELF 64-bit LSB shared object, x86-64"; then
  echo "The library is not an x86-64 shared object: $(file -L "$library")" >&2
  exit 1
fi

soname="$(readelf -d "$library" | sed -n 's/.*(SONAME).*\[\(.*\)\].*/\1/p')"
if [ "$soname" != "libmp3lame.so.0" ]; then
  echo "The library has the name '$soname', expected libmp3lame.so.0." >&2
  exit 1
fi

# What the app calls (src/Hindsound.Linux/Export/LameLibrary.cs).
exports="$(nm -D --defined-only "$library" | awk '{print $3}')"
for name in lame_init lame_close lame_set_in_samplerate lame_set_num_channels lame_set_brate lame_init_params \
  lame_encode_buffer lame_encode_buffer_interleaved lame_encode_flush lame_get_lametag_frame get_lame_version; do
  if ! printf '%s\n' "$exports" | grep -qx "$name"; then
    echo "The library does not export $name." >&2
    exit 1
  fi
done

# Nothing beyond the C library and the maths library: the app ships the file next to itself and nothing else.
needed="$(readelf -d "$library" | sed -n 's/.*(NEEDED).*\[\(.*\)\].*/\1/p')"
echo "Needed by the library: $(echo "$needed" | tr '\n' ' ')"
unexpected="$(printf '%s\n' "$needed" | grep -Ev '^(libc\.so\.6|libm\.so\.6|ld-linux-x86-64\.so\.2)?$' || true)"
if [ -n "$unexpected" ]; then
  echo "The library needs more than libc and libm: $unexpected" >&2
  exit 1
fi

# The library and the command both say 3.100.
command_version="$("$out/lame" --version | head -n 1)"
echo "$command_version"
case "$command_version" in
  *"3.100"*) ;;
  *) echo "The command does not report version 3.100." >&2; exit 1 ;;
esac

echo "Built: $library and $out/lame"
