#!/usr/bin/env bash
# Builds libSDL3.so.0, the window library of the Linux app, from the upstream SDL 3.2.30 archive, and checks the result.
#
# Ubuntu 22.04 and 24.04 have no SDL3 package: the app ships this file next to the program, as it ships libmp3lame.so.0.
# The release builds it on ubuntu-22.04, the Ubuntu the Linux app is published on, so that it runs on every newer one; the
# CI builds it on the Ubuntu of its test job, for the tests. Both call this script, so both build the same thing.
#
# It stops at the first step that fails:
#   1. Downloads SDL3-3.2.30.tar.gz into WORK_DIR and verifies its SHA-256 (the "digest" GitHub gives for that asset of the
#      release release-3.2.30 of libsdl-org/SDL) before anything reads it.
#   2. Extracts it and builds the shared library alone, with CMake and Ninja, no file of the archive changed:
#      - video drivers: X11 and Wayland (with libdecor for the window's frame where the compositor draws none), each loaded
#        at run time, so a desktop without one of them still starts; and offscreen, for the tests and the CI;
#      - D-Bus, and through it the input methods IBus and Fcitx (accents and Asian scripts typed in the text fields);
#      - the dialogs (the folder picker goes through the desktop portal);
#      - no audio (the app plays and captures through the sound server itself), no GPU API, no camera, joystick, haptic,
#        sensor, HID or power code, no KMS/DRM driver. The renderer stays: on Wayland the window surface the app draws on
#        is a texture of SDL's renderer (SDL has no shared-memory framebuffer there), so SDL_GetWindowSurface needs it.
#   3. Checks from SDL's own build configuration that every feature above is in and every one left out is out.
#   4. Checks the library: x86-64, the name libSDL3.so.0, the functions the app calls, nothing linked beyond the C library
#      (SDL opens everything else itself at run time), version 3.2.30.
#
# Usage:
#   build-sdl3.sh WORK_DIR OUT_DIR   builds and checks; OUT_DIR receives libSDL3.so.0
#   build-sdl3.sh --check OUT_DIR    checks a libSDL3.so.0 already built (one a cache gave back)
#   build-sdl3.sh --packages         prints the Ubuntu packages the build needs, for apt-get install
# The packages are those README-linux.md, in the documentation folder of SDL 3.2.30, lists for Ubuntu, less those of the
# parts left out (audio, KMS/DRM, udev, pipewire, liburing), plus the CMake and Ninja the build runs with.

set -euo pipefail

readonly SDL_VERSION='3.2.30'
readonly SDL_URL="https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VERSION/SDL3-$SDL_VERSION.tar.gz"
readonly SDL_SHA256='4c3b09330d866dc52eb65b66259a6684ad387252ca8c57901b3a2b534eb42e3d'

readonly PACKAGES='build-essential pkg-config cmake ninja-build curl file binutils
libx11-dev libxext-dev libxrandr-dev libxcursor-dev libxfixes-dev libxi-dev libxss-dev libxkbcommon-dev
libgl1-mesa-dev libgles2-mesa-dev libegl1-mesa-dev
libwayland-dev libdecor-0-dev
libdbus-1-dev libibus-1.0-dev'

# Every option is here, so that the cache of the CI, keyed by the hash of this file, changes with them.
readonly CMAKE_OPTIONS=(
  -DCMAKE_BUILD_TYPE=Release
  -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST_LIBRARY=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF -DSDL_INSTALL_DOCS=OFF
  -DSDL_DEPS_SHARED=ON -DSDL_RPATH=OFF
  -DSDL_AUDIO=OFF -DSDL_GPU=OFF -DSDL_CAMERA=OFF -DSDL_JOYSTICK=OFF -DSDL_HAPTIC=OFF -DSDL_HIDAPI=OFF -DSDL_SENSOR=OFF
  -DSDL_POWER=OFF
  -DSDL_VIDEO=ON -DSDL_RENDER=ON -DSDL_DIALOG=ON
  -DSDL_X11=ON -DSDL_X11_SHARED=ON
  -DSDL_WAYLAND=ON -DSDL_WAYLAND_SHARED=ON -DSDL_WAYLAND_LIBDECOR=ON -DSDL_WAYLAND_LIBDECOR_SHARED=ON
  -DSDL_OFFSCREEN=ON -DSDL_DBUS=ON -DSDL_IBUS=ON
  -DSDL_KMSDRM=OFF -DSDL_LIBUDEV=OFF -DSDL_PIPEWIRE=OFF -DSDL_LIBURING=OFF
)

# What SDL's generated SDL_build_config.h must define, and what it must define as left out.
readonly WANTED_DEFINES='SDL_VIDEO_DRIVER_X11 SDL_VIDEO_DRIVER_X11_DYNAMIC SDL_VIDEO_DRIVER_WAYLAND
SDL_VIDEO_DRIVER_WAYLAND_DYNAMIC HAVE_LIBDECOR_H SDL_VIDEO_DRIVER_WAYLAND_DYNAMIC_LIBDECOR SDL_VIDEO_DRIVER_OFFSCREEN
HAVE_DBUS_DBUS_H HAVE_IBUS_IBUS_H HAVE_FCITX SDL_USE_IME SDL_AUDIO_DISABLED SDL_GPU_DISABLED SDL_CAMERA_DISABLED
SDL_JOYSTICK_DISABLED SDL_HAPTIC_DISABLED SDL_HIDAPI_DISABLED SDL_SENSOR_DISABLED SDL_POWER_DISABLED'
readonly UNWANTED_DEFINES='SDL_RENDER_DISABLED SDL_VIDEO_DISABLED SDL_DIALOG_DISABLED SDL_VIDEO_DRIVER_KMSDRM'

# Functions of the window, text input, clipboard and dialog APIs the app's window is built on: a library without one of
# them is not the build it needs.
readonly EXPORTS='SDL_Init SDL_Quit SDL_GetVersion SDL_GetError SDL_SetHint SDL_CreateWindow SDL_DestroyWindow
SDL_ShowWindow SDL_HideWindow SDL_RaiseWindow SDL_GetWindowSurface SDL_UpdateWindowSurface SDL_GetWindowDisplayScale
SDL_SetWindowSize SDL_GetWindowSizeInPixels SDL_PollEvent SDL_WaitEventTimeout SDL_PushEvent SDL_RegisterEvents
SDL_StartTextInput SDL_StopTextInput SDL_SetTextInputArea SDL_SetClipboardText SDL_GetClipboardText SDL_free
SDL_ShowOpenFolderDialog SDL_GetCurrentVideoDriver'

usage() {
  echo "Usage: $0 WORK_DIR OUT_DIR | --check OUT_DIR | --packages" >&2
  exit 2
}

sha256_of() { sha256sum "$1" | cut -d' ' -f1; }

check_library() {
  local library="$1/libSDL3.so.0"
  if [ ! -f "$library" ]; then
    echo "$library is not there." >&2
    exit 1
  fi
  if ! file -L "$library" | grep -q "ELF 64-bit LSB shared object, x86-64"; then
    echo "The library is not an x86-64 shared object: $(file -L "$library")" >&2
    exit 1
  fi

  local soname
  soname="$(readelf -d "$library" | sed -n 's/.*(SONAME).*\[\(.*\)\].*/\1/p')"
  if [ "$soname" != "libSDL3.so.0" ]; then
    echo "The library has the name '$soname', expected libSDL3.so.0." >&2
    exit 1
  fi

  local exports name
  exports="$(nm -D --defined-only "$library" | awk '{print $3}' | sed 's/@.*//')"
  for name in $EXPORTS; do
    if ! printf '%s\n' "$exports" | grep -qx "$name"; then
      echo "The library does not export $name." >&2
      exit 1
    fi
  done

  # SDL opens X11, Wayland, libdecor, EGL and D-Bus itself, when they are there: linked, any one of them missing on a
  # desktop would stop the app from starting at all, where the app runs without a window instead.
  local needed unexpected
  needed="$(readelf -d "$library" | sed -n 's/.*(NEEDED).*\[\(.*\)\].*/\1/p')"
  echo "Needed by the library: $(echo "$needed" | tr '\n' ' ')"
  unexpected="$(printf '%s\n' "$needed" | grep -Ev '^(libc\.so\.6|libm\.so\.6|libpthread\.so\.0|libdl\.so\.2|librt\.so\.1|ld-linux-x86-64\.so\.2)?$' || true)"
  if [ -n "$unexpected" ]; then
    echo "The library needs more than the C library: $unexpected" >&2
    exit 1
  fi

  # The version is in the file as SDL's revision string, "SDL-3.2.30-..." or "SDL-release-3.2.30-...".
  if ! strings -a "$library" | grep -Eq "SDL-(release-)?${SDL_VERSION//./[.]}([^0-9]|$)"; then
    echo "The library does not carry the revision of SDL $SDL_VERSION." >&2
    exit 1
  fi
  echo "Checked: $library, $(stat -c %s "$library") bytes, SHA-256 $(sha256_of "$library")"
}

case "${1:-}" in
  --packages)
    [ "$#" -eq 1 ] || usage
    echo $PACKAGES
    exit 0
    ;;
  --check)
    [ "$#" -eq 2 ] || usage
    check_library "$(cd "$2" && pwd)"
    exit 0
    ;;
esac
[ "$#" -eq 2 ] || usage

for tool in cmake ninja curl sha256sum readelf nm strings file; do
  command -v "$tool" > /dev/null || { echo "$tool is not installed: see $0 --packages." >&2; exit 1; }
done

mkdir -p "$1" "$2"
work="$(cd "$1" && pwd)"
out="$(cd "$2" && pwd)"

# --- 1. The archive, verified before anything reads it ---
archive="$work/SDL3-$SDL_VERSION.tar.gz"
rm -f "$archive"
echo "Downloading $SDL_URL"
curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 600 --output "$archive" "$SDL_URL"
actual_sha256="$(sha256_of "$archive")"
if [ "$actual_sha256" != "$SDL_SHA256" ]; then
  echo "$archive has the SHA-256 $actual_sha256, expected $SDL_SHA256. Nothing was extracted." >&2
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
source="$source_root/SDL3-$SDL_VERSION"
for needed in CMakeLists.txt LICENSE.txt include/SDL3/SDL.h; do
  if [ ! -f "$source/$needed" ]; then
    echo "The archive has no SDL3-$SDL_VERSION/$needed." >&2
    exit 1
  fi
done
( cd "$source" && find . -type f -print0 | sort -z | xargs -0 sha256sum ) > "$work/source-before.sha256"

build="$work/build"
cmake -S "$source" -B "$build" -G Ninja "${CMAKE_OPTIONS[@]}" > "$work/configure.log" 2>&1 \
  || { tail -n 80 "$work/configure.log" >&2; echo "CMake failed: see above." >&2; exit 1; }
# The end of SDL's configure output: its summary of the drivers and options it found.
tail -n 60 "$work/configure.log"
cmake --build "$build" --target SDL3-shared > "$work/build.log" 2>&1 \
  || { tail -n 80 "$work/build.log" >&2; echo "The build failed: see above." >&2; exit 1; }
tail -n 2 "$work/build.log"

( cd "$source" && find . -type f -print0 | sort -z | xargs -0 sha256sum ) > "$work/source-after.sha256"
if ! diff -q "$work/source-before.sha256" "$work/source-after.sha256" > /dev/null; then
  echo "The build changed a source file:" >&2
  diff "$work/source-before.sha256" "$work/source-after.sha256" >&2 || true
  exit 1
fi

# --- 3. The features, as SDL configured them ---
config="$build/include-config-release/build_config/SDL_build_config.h"
if [ ! -f "$config" ]; then
  echo "SDL's build configuration is not at $config." >&2
  exit 1
fi
for define in $WANTED_DEFINES; do
  if ! grep -Eq "^#define $define( |$)" "$config"; then
    echo "SDL was configured without $define: a development package is missing (see $0 --packages)." >&2
    exit 1
  fi
done
for define in $UNWANTED_DEFINES; do
  if grep -Eq "^#define $define( |$)" "$config"; then
    echo "SDL was configured with $define, which this build must not have." >&2
    exit 1
  fi
done
echo "Features: $(echo $WANTED_DEFINES)"

# --- 4. The library ---
built="$build/libSDL3.so.0"
if [ ! -e "$built" ]; then
  echo "The build succeeded, but $built is not there." >&2
  exit 1
fi
rm -f "$out/libSDL3.so.0"
cp -L "$built" "$out/libSDL3.so.0"
strip --strip-unneeded "$out/libSDL3.so.0"
chmod 755 "$out/libSDL3.so.0"
check_library "$out"
echo "Built: $out/libSDL3.so.0"
