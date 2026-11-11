#!/usr/bin/env bash
# Puts the source of the AppImage runtime, and of the libfuse it is statically linked with, next to the packages.
#
# libfuse is under the LGPL-2.1, and the runtime at the head of the AppImage is a program linked with it. Section 6 (d) of
# that licence is met by offering the source from the place the AppImage is offered: every release, and the artifact
# hindsound-linux-x64 of the CI, carry these two files next to it (see "libfuse and the LGPL-2.1" in THIRD-PARTY-NOTICES.md):
#   fuse-3.15.0.tar.xz                      the libfuse release archive the runtime was built from, unchanged;
#   type2-runtime-20251108-source.tar.gz    the runtime's repository at the commit of its tag 20251108: the runtime, its
#                                           build scripts and patches/libfuse/mount.c.diff, the change made to libfuse.
#
# Runs on Linux, with curl, git, tar and sha256sum. It stops at the first check that fails:
#   1. Downloads the libfuse archive and verifies its SHA-256 before anything else.
#   2. Fetches the one commit of the runtime into a new repository, and verifies that git read that exact commit.
#   3. Writes the archive of that commit with git archive, and verifies it holds the licence, the runtime's source and the
#      patch to libfuse under its prefix.
#
# Usage: fetch-runtime-sources.sh WORK_DIR OUT_DIR
#   WORK_DIR  an empty or new folder: receives the download and the repository.
#   OUT_DIR   receives the two files.
#
# The pins below are the only copy in the build: check-release-files.ps1 reads FUSE_SHA256 from here, and the tests compare
# all of them with THIRD-PARTY-NOTICES.md and with the runtime packaging/linux/build-packages.sh downloads.

set -euo pipefail

readonly FUSE_URL='https://github.com/libfuse/libfuse/releases/download/fuse-3.15.0/fuse-3.15.0.tar.xz'
readonly FUSE_SHA256='70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0'
readonly RUNTIME_REPOSITORY='https://github.com/AppImage/type2-runtime.git'
readonly RUNTIME_TAG='20251108'
readonly RUNTIME_COMMIT='dd6cebedcbddde9c82f89b011e8e1d40b6e43868'

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 WORK_DIR OUT_DIR" >&2
  exit 2
fi

mkdir -p "$1" "$2"
work="$(cd "$1" && pwd)"
out="$(cd "$2" && pwd)"

fuse_name="${FUSE_URL##*/}"
prefix="type2-runtime-$RUNTIME_TAG/"
runtime_name="type2-runtime-$RUNTIME_TAG-source.tar.gz"

for name in "$fuse_name" "$runtime_name"; do
  if [ -e "$out/$name" ]; then
    echo "$out/$name already exists: nothing is overwritten." >&2
    exit 1
  fi
done

# --- 1. libfuse, verified before it goes anywhere ---
fuse="$work/$fuse_name"
rm -f "$fuse"
echo "Downloading $FUSE_URL"
curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 300 --output "$fuse" "$FUSE_URL"
fuse_sha256="$(sha256sum "$fuse" | cut -d' ' -f1)"
if [ "$fuse_sha256" != "$FUSE_SHA256" ]; then
  echo "$fuse_name has the SHA-256 $fuse_sha256, expected $FUSE_SHA256. Nothing was copied." >&2
  exit 1
fi
echo "Verified: $fuse_name, SHA-256 $fuse_sha256"

# --- 2. The runtime, at its commit and no other ---
repository="$work/type2-runtime"
if [ -e "$repository" ]; then
  echo "$repository already exists: give an empty WORK_DIR." >&2
  exit 1
fi
git init --quiet "$repository"
git -C "$repository" fetch --quiet --depth 1 "$RUNTIME_REPOSITORY" "$RUNTIME_COMMIT"
fetched="$(git -C "$repository" rev-parse --verify 'FETCH_HEAD^{commit}')"
if [ "$fetched" != "$RUNTIME_COMMIT" ]; then
  echo "The fetch gave the commit $fetched, expected $RUNTIME_COMMIT. Nothing was written." >&2
  exit 1
fi
echo "Fetched: $RUNTIME_REPOSITORY at $fetched (tag $RUNTIME_TAG)"

# --- 3. Its archive, with what the notices point to ---
runtime="$work/$runtime_name"
rm -f "$runtime"
git -C "$repository" archive --format=tar.gz --prefix="$prefix" --output="$runtime" "$RUNTIME_COMMIT"
listing="$(tar -tzf "$runtime")"
for needed in LICENSE src/runtime/runtime.c patches/libfuse/mount.c.diff; do
  if ! printf '%s\n' "$listing" | grep -qxF -- "$prefix$needed"; then
    echo "$runtime_name has no $prefix$needed. Nothing was copied." >&2
    exit 1
  fi
done

cp -- "$fuse" "$out/$fuse_name"
cp -- "$runtime" "$out/$runtime_name"
echo "Added: $out/$fuse_name and $out/$runtime_name, SHA-256 $(sha256sum "$out/$runtime_name" | cut -d' ' -f1)"
