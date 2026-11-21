#!/usr/bin/env bash
# Builds the four native libraries of the macOS app for Apple silicon (arm64, macOS 14.4 or later) from pinned sources, and
# checks them:
#   libSDL3.0.dylib      SDL 3.2.30, the archive of tools/linux/build-sdl3.sh (same SHA-256): the Cocoa video driver and
#                        the offscreen one (the CI draws the pages on it), the renderer and the dialogs; no audio, no GPU,
#                        no camera, joystick, haptic, HID, sensor or power code.
#   libfreetype.6.dylib  FreeType 2.13.2, the version of Ubuntu 24.04 (the text of the CI's Linux pages), without zlib,
#                        bzip2, libpng, HarfBuzz or Brotli: nothing in cascade.
#   libharfbuzz.0.dylib  HarfBuzz 8.3.0, the version of Ubuntu 24.04, with its own OpenType code only (the text calls
#                        hb_ot_font_set_funcs): no FreeType, GLib, ICU, Graphite or CoreText.
#   libmp3lame.0.dylib   LAME 3.100, the archive of tools/release/build-lame-linux.sh (same SHA-256), no file changed. Its
#                        own list of exports names lame_init_old, which 3.100 does not define, and the macOS linker refuses
#                        it: the list given to the link is tools/release/lame-exports.txt instead, the functions the app
#                        and NAudio.Lame call.
# Each archive is verified before anything reads it. Each library is named @rpath/<file> (the app finds them in
# Contents/Frameworks), is arm64, needs nothing beyond the system (no Homebrew path, no other library of this list), and
# exports what the app calls.
#
# Usage:
#   build-natives.sh WORK_DIR OUT_DIR   builds and checks; OUT_DIR receives the four dylibs
#   build-natives.sh --check OUT_DIR    checks dylibs already built (ones a cache gave back)
# Needs the Xcode command line tools, CMake and curl (all on the GitHub macOS runners). The CMake of the runner (4.x)
# refuses the cmake_minimum_required of FreeType 2.13.2 (below 3.5): CMAKE_POLICY_VERSION_MINIMUM=3.5 lets it configure.
# Last checked: 21/11/2026, on the runner macos-26 (the CI's job mac).

set -euo pipefail

readonly DEPLOYMENT_TARGET='14.4'

readonly SDL_VERSION='3.2.30'
readonly SDL_URL="https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VERSION/SDL3-$SDL_VERSION.tar.gz"
readonly SDL_SHA256='4c3b09330d866dc52eb65b66259a6684ad387252ca8c57901b3a2b534eb42e3d'

readonly FREETYPE_VERSION='2.13.2'
readonly FREETYPE_URL="https://download.savannah.gnu.org/releases/freetype/freetype-$FREETYPE_VERSION.tar.xz"
readonly FREETYPE_SHA256='12991c4e55c506dd7f9b765933e62fd2be2e06d421505d7950a132e4f1bb484d'

readonly HARFBUZZ_VERSION='8.3.0'
readonly HARFBUZZ_URL="https://github.com/harfbuzz/harfbuzz/releases/download/$HARFBUZZ_VERSION/harfbuzz-$HARFBUZZ_VERSION.tar.xz"
readonly HARFBUZZ_SHA256='109501eaeb8bde3eadb25fab4164e993fbace29c3d775bcaa1c1e58e2f15f847'

readonly LAME_URL='https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz'
readonly LAME_SHA256='ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e'

readonly SDL_OPTIONS=(
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 "-DCMAKE_OSX_DEPLOYMENT_TARGET=$DEPLOYMENT_TARGET"
  -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST_LIBRARY=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF -DSDL_INSTALL_DOCS=OFF
  -DSDL_AUDIO=OFF -DSDL_GPU=OFF -DSDL_CAMERA=OFF -DSDL_JOYSTICK=OFF -DSDL_HAPTIC=OFF -DSDL_HIDAPI=OFF -DSDL_SENSOR=OFF
  -DSDL_POWER=OFF -DSDL_VIDEO=ON -DSDL_RENDER=ON -DSDL_DIALOG=ON -DSDL_OFFSCREEN=ON -DSDL_COCOA=ON
)
readonly FREETYPE_OPTIONS=(
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 "-DCMAKE_OSX_DEPLOYMENT_TARGET=$DEPLOYMENT_TARGET"
  -DBUILD_SHARED_LIBS=ON -DFT_DISABLE_ZLIB=ON -DFT_DISABLE_BZIP2=ON -DFT_DISABLE_PNG=ON -DFT_DISABLE_HARFBUZZ=ON
  -DFT_DISABLE_BROTLI=ON -DCMAKE_POLICY_VERSION_MINIMUM=3.5
)
readonly HARFBUZZ_OPTIONS=(
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 "-DCMAKE_OSX_DEPLOYMENT_TARGET=$DEPLOYMENT_TARGET"
  -DBUILD_SHARED_LIBS=ON -DHB_HAVE_FREETYPE=OFF -DHB_HAVE_GLIB=OFF -DHB_HAVE_GOBJECT=OFF -DHB_HAVE_ICU=OFF
  -DHB_HAVE_GRAPHITE2=OFF -DHB_HAVE_CORETEXT=OFF -DHB_BUILD_UTILS=OFF -DHB_BUILD_SUBSET=OFF -DHB_HAVE_INTROSPECTION=OFF
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5
)

readonly LIBRARIES='libSDL3.0.dylib libfreetype.6.dylib libharfbuzz.0.dylib libmp3lame.0.dylib'

# What each library must export: the calls of the app (SdlNative, TextNative, LameLibrary) and of NAudio.Lame's list.
exports_of() {
  case "$1" in
    libSDL3.0.dylib) echo 'SDL_Init SDL_Quit SDL_GetVersion SDL_GetError SDL_SetHint SDL_CreateWindow SDL_DestroyWindow
SDL_ShowWindow SDL_HideWindow SDL_RaiseWindow SDL_GetWindowSurface SDL_UpdateWindowSurface SDL_GetWindowDisplayScale
SDL_SetWindowSize SDL_GetWindowSizeInPixels SDL_PollEvent SDL_WaitEventTimeout SDL_PushEvent SDL_RegisterEvents
SDL_StartTextInput SDL_StopTextInput SDL_SetTextInputArea SDL_SetClipboardText SDL_GetClipboardText SDL_free
SDL_ShowOpenFolderDialog SDL_GetCurrentVideoDriver' ;;
    libfreetype.6.dylib) echo 'FT_Init_FreeType FT_Done_FreeType FT_New_Face FT_Done_Face FT_Set_Pixel_Sizes FT_Set_Transform
FT_Load_Glyph FT_Render_Glyph FT_Get_Char_Index FT_Get_Postscript_Name' ;;
    libharfbuzz.0.dylib) echo 'hb_version_string hb_blob_create_from_file hb_blob_get_length hb_blob_get_data hb_blob_destroy
hb_face_create hb_face_get_upem hb_face_reference_table hb_face_destroy hb_font_create hb_ot_font_set_funcs
hb_font_set_scale hb_font_destroy hb_buffer_create hb_buffer_clear_contents hb_buffer_add_utf16
hb_buffer_guess_segment_properties hb_shape hb_buffer_get_glyph_infos hb_buffer_get_glyph_positions hb_buffer_destroy' ;;
    libmp3lame.0.dylib) grep -Ev '^(#|$)' "$here/../release/lame-exports.txt" ;;
  esac
}

usage() {
  echo "Usage: $0 WORK_DIR OUT_DIR | --check OUT_DIR" >&2
  exit 2
}

here="$(cd "$(dirname "$0")" && pwd)"

check_one() {
  local library="$1/$2" name="$2" file_type id linked exports symbol
  [ -f "$library" ] || { echo "$library is not there." >&2; exit 1; }
  file_type="$(file -b "$library")"
  case "$file_type" in
    *"Mach-O 64-bit dynamically linked shared library arm64"*) ;;
    *) echo "$name is not an arm64 dylib: $file_type" >&2; exit 1 ;;
  esac
  id="$(otool -D "$library" | tail -n 1)"
  [ "$id" = "@rpath/$name" ] || { echo "$name is named '$id', expected @rpath/$name." >&2; exit 1; }
  # Only the system: /usr/lib and the frameworks of /System, never Homebrew (/opt/homebrew, /usr/local) nor another
  # library of this list.
  linked="$(otool -L "$library" | tail -n +2 | awk '{print $1}' | grep -v "^@rpath/$name\$" || true)"
  if printf '%s\n' "$linked" | grep -Ev '^(/usr/lib/|/System/Library/)' | grep -q .; then
    echo "$name links beyond the system:" >&2
    printf '%s\n' "$linked" | grep -Ev '^(/usr/lib/|/System/Library/)' >&2
    exit 1
  fi
  exports="$(nm -gU "$library" | awk '{print $3}' | sed 's/^_//')"
  for symbol in $(exports_of "$name"); do
    printf '%s\n' "$exports" | grep -qx "$symbol" || { echo "$name does not export $symbol." >&2; exit 1; }
  done
  echo "Checked: $name, $(stat -f %z "$library") bytes, SHA-256 $(shasum -a 256 "$library" | cut -d' ' -f1), links: $(echo $linked)"
}

check_all() {
  local name
  for name in $LIBRARIES; do
    check_one "$1" "$name"
  done
}

if [ "${1:-}" = --check ]; then
  [ "$#" -eq 2 ] || usage
  check_all "$(cd "$2" && pwd)"
  exit 0
fi
[ "$#" -eq 2 ] || usage
[ "$(uname -s)" = Darwin ] || { echo "This script builds for macOS, on macOS." >&2; exit 1; }

mkdir -p "$1" "$2"
work="$(cd "$1" && pwd)"
out="$(cd "$2" && pwd)"
jobs="$(sysctl -n hw.ncpu)"

# Downloads $2 into $work/$1 and stops unless its SHA-256 is $3.
fetch() {
  local file="$work/$1" actual
  rm -f "$file"
  echo "Downloading $2"
  curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 600 --output "$file" "$2"
  actual="$(shasum -a 256 "$file" | cut -d' ' -f1)"
  if [ "$actual" != "$3" ]; then
    echo "$file has the SHA-256 $actual, expected $3. Nothing was extracted." >&2
    exit 1
  fi
  echo "Verified: $1, SHA-256 $actual"
}

# Copies the built library into $out under its file name, with the install name @rpath/<file>, signed ad hoc again.
place() {
  local built="$1" name="$2"
  rm -f "$out/$name"
  cp -L "$built" "$out/$name"
  chmod 755 "$out/$name"
  install_name_tool -id "@rpath/$name" "$out/$name"
  strip -x "$out/$name"
  codesign --force --sign - "$out/$name"
}

# --- SDL 3 ---
fetch "SDL3-$SDL_VERSION.tar.gz" "$SDL_URL" "$SDL_SHA256"
mkdir -p "$work/sdl" && tar -xzf "$work/SDL3-$SDL_VERSION.tar.gz" -C "$work/sdl"
cmake -S "$work/sdl/SDL3-$SDL_VERSION" -B "$work/sdl/build" "${SDL_OPTIONS[@]}" > "$work/sdl-configure.log" 2>&1 \
  || { tail -n 60 "$work/sdl-configure.log" >&2; exit 1; }
cmake --build "$work/sdl/build" --parallel "$jobs" > "$work/sdl-build.log" 2>&1 || { tail -n 60 "$work/sdl-build.log" >&2; exit 1; }
config="$(find "$work/sdl/build" -name SDL_build_config.h | head -n 1)"
for define in SDL_VIDEO_DRIVER_COCOA SDL_VIDEO_DRIVER_OFFSCREEN SDL_AUDIO_DISABLED SDL_GPU_DISABLED SDL_JOYSTICK_DISABLED; do
  grep -Eq "^#define $define( |$)" "$config" || { echo "SDL was configured without $define." >&2; exit 1; }
done
place "$work/sdl/build/libSDL3.0.dylib" libSDL3.0.dylib

# --- FreeType ---
fetch "freetype-$FREETYPE_VERSION.tar.xz" "$FREETYPE_URL" "$FREETYPE_SHA256"
mkdir -p "$work/freetype" && tar -xJf "$work/freetype-$FREETYPE_VERSION.tar.xz" -C "$work/freetype"
cmake -S "$work/freetype/freetype-$FREETYPE_VERSION" -B "$work/freetype/build" "${FREETYPE_OPTIONS[@]}" > "$work/freetype-configure.log" 2>&1 \
  || { tail -n 60 "$work/freetype-configure.log" >&2; exit 1; }
cmake --build "$work/freetype/build" --parallel "$jobs" > "$work/freetype-build.log" 2>&1 || { tail -n 60 "$work/freetype-build.log" >&2; exit 1; }
place "$(find "$work/freetype/build" -name 'libfreetype.6.*.dylib' -type f | head -n 1)" libfreetype.6.dylib

# --- HarfBuzz ---
fetch "harfbuzz-$HARFBUZZ_VERSION.tar.xz" "$HARFBUZZ_URL" "$HARFBUZZ_SHA256"
mkdir -p "$work/harfbuzz" && tar -xJf "$work/harfbuzz-$HARFBUZZ_VERSION.tar.xz" -C "$work/harfbuzz"
cmake -S "$work/harfbuzz/harfbuzz-$HARFBUZZ_VERSION" -B "$work/harfbuzz/build" "${HARFBUZZ_OPTIONS[@]}" > "$work/harfbuzz-configure.log" 2>&1 \
  || { tail -n 60 "$work/harfbuzz-configure.log" >&2; exit 1; }
cmake --build "$work/harfbuzz/build" --target harfbuzz --parallel "$jobs" > "$work/harfbuzz-build.log" 2>&1 \
  || { tail -n 60 "$work/harfbuzz-build.log" >&2; exit 1; }
place "$(find "$work/harfbuzz/build" -name 'libharfbuzz*.dylib' -type f | head -n 1)" libharfbuzz.0.dylib

# --- LAME ---
fetch lame-3.100.tar.gz "$LAME_URL" "$LAME_SHA256"
mkdir -p "$work/lame-src" "$work/lame-build" && tar -xzf "$work/lame-3.100.tar.gz" -C "$work/lame-src"
grep -Ev '^(#|$)' "$here/../release/lame-exports.txt" > "$work/lame-exports.sym"
(
  cd "$work/lame-build"
  CFLAGS="-O2 -arch arm64 -mmacosx-version-min=$DEPLOYMENT_TARGET" LDFLAGS="-arch arm64 -mmacosx-version-min=$DEPLOYMENT_TARGET" \
    "$work/lame-src/lame-3.100/configure" --enable-shared --disable-static --disable-frontend --disable-gtktest > "$work/lame-configure.log" 2>&1 \
    || { tail -n 60 "$work/lame-configure.log" >&2; exit 1; }
  make -j"$jobs" libmp3lame_la_LDFLAGS="-version-info 0:0:0 -no-undefined -export-symbols $work/lame-exports.sym" > "$work/lame-build.log" 2>&1 \
    || { tail -n 60 "$work/lame-build.log" >&2; exit 1; }
)
place "$work/lame-build/libmp3lame/.libs/libmp3lame.0.dylib" libmp3lame.0.dylib

check_all "$out"
echo "Built: $out"
