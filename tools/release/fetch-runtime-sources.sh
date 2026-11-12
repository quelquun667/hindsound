#!/usr/bin/env bash
# Puts the source of the AppImage runtime, of the libfuse it is statically linked with, and of the other libraries linked
# into it, next to the packages.
#
# libfuse is under the LGPL-2.1, and the runtime at the head of the AppImage is a program linked with it. Section 6 (d) of
# that licence is met by offering, from the place the AppImage is offered, the library's source and what the user needs to
# change it and link the runtime again: every release, and the artifact hindsound-linux-x64 of the CI, carry these three
# files next to it (see "libfuse and the LGPL-2.1" in THIRD-PARTY-NOTICES.md):
#   fuse-3.15.0.tar.xz                                the libfuse release archive the runtime was built from, unchanged;
#   type2-runtime-20251108-source.tar.gz              the runtime's repository at the commit of its tag 20251108: the
#                                                     runtime, its build scripts and patches/libfuse/mount.c.diff, the
#                                                     change made to libfuse;
#   type2-runtime-20251108-libraries-source.tar.gz    the source of every other library its Makefile links: squashfuse,
#                                                     as install-dependencies.sh downloads it, and the Alpine 3.21
#                                                     packages of Zstandard, zlib, mimalloc and musl, each as its upstream
#                                                     archive and its folder of alpinelinux/aports (APKBUILD and patches).
#
# Runs on Linux, with curl, git, tar, gzip, sha256sum and sha512sum. It stops at the first check that fails, and copies
# nothing into OUT_DIR before every check has passed:
#   1. Downloads the libfuse archive and verifies its SHA-256 before anything else.
#   2. Fetches the one commit of the runtime into a new repository, and verifies that git read that exact commit.
#   3. Writes the archive of that commit with git archive, and verifies it holds the licence, the runtime's source and the
#      patch to libfuse under its prefix.
#   4. Downloads each file of LIBRARY_FILES and verifies its SHA-256; verifies that each APKBUILD has the version and the
#      release of ALPINE_PACKAGES, that its upstream archive has the SHA-512 its sha512sums give, and that those
#      sha512sums name exactly the files of its folder; then writes the archive, the same bytes for the same files (names
#      sorted, the date of the runtime's commit, owner 0, mode 0644, gzip -n), and verifies what it holds, file by file.
#
# Usage: fetch-runtime-sources.sh WORK_DIR OUT_DIR
#   WORK_DIR  an empty or new folder: receives the downloads and the repository.
#   OUT_DIR   receives the three files.
#
# The pins below are the only copy in the build: check-release-files.ps1 reads FUSE_SHA256 and LIBRARY_FILES from here, and
# the tests compare all of them with THIRD-PARTY-NOTICES.md and with the runtime packaging/linux/build-packages.sh downloads.

set -euo pipefail

readonly FUSE_URL='https://github.com/libfuse/libfuse/releases/download/fuse-3.15.0/fuse-3.15.0.tar.xz'
readonly FUSE_SHA256='70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0'
readonly RUNTIME_REPOSITORY='https://github.com/AppImage/type2-runtime.git'
readonly RUNTIME_TAG='20251108'
readonly RUNTIME_COMMIT='dd6cebedcbddde9c82f89b011e8e1d40b6e43868'

# squashfuse: the address and the SHA-256 that scripts/common/install-dependencies.sh gives at the runtime's commit.
readonly SQUASHFUSE_URL='https://github.com/vasi/squashfuse/archive/0.5.2.tar.gz'
readonly SQUASHFUSE_NAME='squashfuse-0.5.2.tar.gz'
readonly SQUASHFUSE_SHA256='db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3'

# The packages scripts/docker/Dockerfile installs from Alpine 3.21 and src/runtime/Makefile links. One line each: the
# folder in aports, the version and release, the last commit of the branch 3.21-stable that changed that folder before the
# runtime was built (2025-12-08), the upstream archive the APKBUILD names in source=, its SHA-512 copied from the
# sha512sums of that APKBUILD, and its address.
readonly APORTS_FILES='https://raw.githubusercontent.com/alpinelinux/aports'
readonly ALPINE_PACKAGES='main/musl 1.2.5-r9 efd4d5df7be0ea48d98512671f676fc5ceb14899 musl-1.2.5.tar.gz 7bb7f7833923cd69c7a1a9b8a5f1784bfd5289663eb6061dcd43d583e45987df8a68a1be05d75cc1c88a3f5b610653d1a70f4a9cff4d8f7fd41ae73ee058c17c https://musl.libc.org/releases/musl-1.2.5.tar.gz
main/zstd 1.5.6-r2 5c2ddf18f193dafdd25ae7391d96c7e30766eecd zstd-1.5.6.tar.gz ca12dffd86618ca008e1ecc79056c1129cb4e61668bf13a3cd5b2fa5c93bc9c92c80f64c1870c68b9c20009d9b3a834eac70db72242d5106125a1c53cccf8de8 https://github.com/facebook/zstd/archive/v1.5.6.tar.gz
main/zlib 1.3.1-r2 4c2722e7ecc1c5e3ba882429044a101497121223 zlib-1.3.1.tar.gz 580677aad97093829090d4b605ac81c50327e74a6c2de0b85dd2e8525553f3ddde17556ea46f8f007f89e435493c9a20bc997d1ef1c1c2c23274528e3c46b94f https://zlib.net/fossils/zlib-1.3.1.tar.gz
community/mimalloc2 2.1.7-r0 5a1a5dc3216a69e3cd66a57ee07cb5ef01b8be39 mimalloc-2.1.7.tar.gz 4e30976758015c76a146acc1bfc8501e2e5c61b81db77d253de0d58a8edef987669243f232210667b32ef8da3a33286642acb56ba526fd24c4ba925b44403730 https://github.com/microsoft/mimalloc/archive/v2.1.7/mimalloc-2.1.7.tar.gz
'

# Every file of the libraries archive but README.txt, by its path under the archive's prefix, with its SHA-256. A file
# under aports/ is downloaded from the commit of its package, at the same path.
readonly LIBRARY_FILES='0eed39319f139afde8515010ff59baf24de9e47ea316a315398e8027d198202d  mimalloc-2.1.7.tar.gz
a9a118bbe84d8764da0ea0d28b3ab3fae8477fc7e4085d90102b8596fc7c75e4  musl-1.2.5.tar.gz
db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3  squashfuse-0.5.2.tar.gz
9a93b2b7dfdac77ceba5a558a580e74667dd6fede4585b91eefb60f03b72df23  zlib-1.3.1.tar.gz
30f35f71c1203369dc979ecde0400ffea93c27391bfd2ac5a9715d2173d92ff7  zstd-1.5.6.tar.gz
bb2618381e98ef508b41697d9177b7d1fef6f61bf38543aa055b9e0002c3c54b  aports/community/mimalloc2/APKBUILD
bbf09f82b9a187399f33f6cc4136bd088f475d769f385aa1f08d0850473b04ae  aports/community/mimalloc2/cmake-add-insecure-suffix.patch
5c31b8e5c25bcc4fe51e913dce8e057bebaf9c8980b88f76bc19d05d67a906ba  aports/main/musl/APKBUILD
ab382da2bb5e1cc32d8904be28fb13f8ad3ce813967bd4bf7f5b1c6baee33e17  aports/main/musl/CVE-2025-26519.patch
299a7d75a09de3e2e11e7fb4acc3182e4a14e868093d2f30938fce9bfcff13da  aports/main/musl/__stack_chk_fail_local.c
d87d0cbb3690ae2c5d8cc218349fd8278b93855dd625deaf7ae50e320aad247c  aports/main/musl/getconf.c
b25f32d306d542246e9358ef65cd67c12b3e3a77656bc467853d3b0c76b5fbbd  aports/main/musl/getent.c
2a65e03b94217063d72fe998e5fb2e25cd01716078a3ba1b0ab574ab205111e6  aports/main/musl/handle-aux-at_base.patch
f79a2930a2e5bb0624321589edf8b889d1e9b603e01e6b7ae214616605b3fdd7  aports/main/musl/iconv.c
1321d542b40921a4c248bb2d15dbd03fcbbf008c8725eb2043d3cab62faeba48  aports/main/musl/isatty-glibc-align.patch
b4a2c06db38742e8c42c3c9838b285a7d8cdac6c091ff3df5ff9a15f1e41b9c7  aports/main/musl/ldconfig
7f5806124d4a89536e53a07bfe95fcd0a8f1b7a27fc0dfc89c78c724dea1c924  aports/main/musl/ldso-fix-non-functional-fix-to-early-dynamic-PAGE_SI.patch
28efacd507c01b89e1725ed33723f3f9fdfe6cb83eb78ffea1e2acb7fda8a52c  aports/main/musl/loongarch64-Add-lsx-and-lasx-vector-structure-defini.patch
1efb4604e0b28b84e10faf37d17bad448510e73f6a3db89c8be47d38b9e85138  aports/main/musl/loongarch64-add-TLSDESC-support.patch
8849080b49dff046b1f29db24e54ffe6262b5884078091994db77efdafca2505  aports/main/musl/loongarch64-add-bits-hwcap.h.patch
d73f85a90fe52930dd8b5d16cb7f3369a994918c0e41ae3c3dc9a25df4b45099  aports/main/musl/loongarch64-add-fpu-and-simd-context-to-signal.h.patch
47ba2fb65734f65fbbfc980d261cf86ab99e8c5bad9d0977a59620cde18ffd53  aports/main/zlib/APKBUILD
7344ccab938fb86eb3eb437f1caa11c99ceca58c475b31458d3ab04cce2e2388  aports/main/zstd/APKBUILD
'

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
libraries_prefix="type2-runtime-$RUNTIME_TAG-libraries-source/"
libraries_name="type2-runtime-$RUNTIME_TAG-libraries-source.tar.gz"

for name in "$fuse_name" "$runtime_name" "$libraries_name"; do
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
# A here-string, not a pipe: with pipefail, grep -q leaving early could fail the writer of a pipe and turn a match into a miss.
for needed in LICENSE src/runtime/runtime.c patches/libfuse/mount.c.diff; do
  if ! grep -qxF -- "$prefix$needed" <<< "$listing"; then
    echo "$runtime_name has no $prefix$needed. Nothing was copied." >&2
    exit 1
  fi
done

# --- 4. The other libraries linked into the runtime, each file verified, in one archive ---
tree="$work/libraries"
if [ -e "$tree" ]; then
  echo "$tree already exists: give an empty WORK_DIR." >&2
  exit 1
fi
staging="$tree/${libraries_prefix%/}"
mkdir -p "$staging"

# The address of a path of LIBRARY_FILES: squashfuse, an upstream archive of ALPINE_PACKAGES, or a file of an aports folder
# at the commit of its package.
address_of() {
  local folder version commit archive sha512 url
  if [ "$1" = "$SQUASHFUSE_NAME" ]; then
    echo "$SQUASHFUSE_URL"
    return 0
  fi
  while read -r folder version commit archive sha512 url; do
    if [ -n "$folder" ] && [ "$1" = "$archive" ]; then
      echo "$url"
      return 0
    fi
    if [ -n "$folder" ] && [ "${1%/*}" = "aports/$folder" ]; then
      echo "$APORTS_FILES/$commit/$folder/${1##*/}"
      return 0
    fi
  done <<< "$ALPINE_PACKAGES"
  echo "LIBRARY_FILES names $1, which is no file of squashfuse or of ALPINE_PACKAGES. Nothing was copied." >&2
  return 1
}

while read -r sha256 path; do
  [ -n "$path" ] || continue
  url="$(address_of "$path")"
  mkdir -p "$(dirname "$staging/$path")"
  echo "Downloading $url"
  curl --fail --silent --show-error --location --retry 3 --connect-timeout 30 --max-time 300 --output "$staging/$path" "$url"
  actual="$(sha256sum "$staging/$path" | cut -d' ' -f1)"
  if [ "$actual" != "$sha256" ]; then
    echo "$path has the SHA-256 $actual, expected $sha256. Nothing was copied." >&2
    exit 1
  fi
done <<< "$LIBRARY_FILES"
squashfuse_sha256="$(sha256sum "$staging/$SQUASHFUSE_NAME" | cut -d' ' -f1)"
if [ "$squashfuse_sha256" != "$SQUASHFUSE_SHA256" ]; then
  echo "$SQUASHFUSE_NAME has the SHA-256 $squashfuse_sha256, expected $SQUASHFUSE_SHA256. Nothing was copied." >&2
  exit 1
fi
echo "Verified: the SHA-256 of every file of LIBRARY_FILES"

while read -r folder version commit archive sha512 url; do
  [ -n "$folder" ] || continue
  if ! [[ "$commit" =~ ^[0-9a-f]{40}$ ]]; then
    echo "$folder is pinned to $commit, not to the full SHA of a commit. Nothing was copied." >&2
    exit 1
  fi
  apkbuild="$staging/aports/$folder/APKBUILD"
  if [ ! -f "$apkbuild" ] || ! grep -qxF "pkgver=${version%-r*}" "$apkbuild" || ! grep -qxF "pkgrel=${version##*-r}" "$apkbuild"; then
    echo "aports/$folder/APKBUILD is not that of the version $version. Nothing was copied." >&2
    exit 1
  fi
  actual="$(sha512sum "$staging/$archive" | cut -d' ' -f1)"
  if [ "$actual" != "$sha512" ]; then
    echo "$archive has the SHA-512 $actual, and the APKBUILD of $folder gives $sha512. Nothing was copied." >&2
    exit 1
  fi
  # The sha512sums of the APKBUILD name its upstream archive and every other source of the folder: the folder must hold
  # exactly those, so that no patch the package was built with is left out.
  listed="$(sed -n '/^sha512sums="/,/^"/p' "$apkbuild" | grep -E '^[0-9a-f]{128}  ' | LC_ALL=C sort)"
  held="$({
    echo "$sha512  $archive"
    for file in "$staging/aports/$folder"/*; do
      if [ "${file##*/}" != APKBUILD ]; then
        echo "$(sha512sum "$file" | cut -d' ' -f1)  ${file##*/}"
      fi
    done
  } | LC_ALL=C sort)"
  if [ "$listed" != "$held" ]; then
    echo "The sha512sums of aports/$folder/APKBUILD do not name exactly the files held for it. Nothing was copied." >&2
    exit 1
  fi
  echo "Verified: $folder $version, aports commit $commit"
done <<< "$ALPINE_PACKAGES"

{
  echo "type2-runtime-$RUNTIME_TAG-libraries-source"
  echo
  echo "The source of the libraries the AppImage runtime of the tag $RUNTIME_TAG of AppImage/type2-runtime"
  echo "(commit $RUNTIME_COMMIT) is statically linked with, other than libfuse. The source of"
  echo "libfuse and of the runtime itself is in $fuse_name and $runtime_name, next to this file."
  echo
  echo "How the runtime links them: src/runtime/Makefile links it with -static and -static-pie and with"
  echo "-lsquashfuse -lsquashfuse_ll -lzstd -lz -lfuse3 -lmimalloc, on the musl C library. scripts/docker/Dockerfile"
  echo "installs Zstandard, zlib, mimalloc and musl as Alpine 3.21 packages, and scripts/common/install-dependencies.sh"
  echo "builds libfuse and squashfuse from source. Both scripts are in $runtime_name."
  echo
  echo "squashfuse 0.5.2: $SQUASHFUSE_NAME, the archive install-dependencies.sh downloads, unchanged, from"
  echo "  $SQUASHFUSE_URL"
  echo
  echo "Each Alpine 3.21 package: its upstream archive, unchanged, and under aports/ its folder of alpinelinux/aports (the"
  echo "APKBUILD and every patch and file it names), at the last commit of the branch 3.21-stable that changed that folder"
  echo "before the runtime was built:"
  while read -r folder version commit archive sha512 url; do
    [ -n "$folder" ] || continue
    echo "  $folder $version, commit $commit: $archive, from"
    echo "    $url"
  done <<< "$ALPINE_PACKAGES"
  echo
  echo "The SHA-256 of every other file of this archive:"
  printf '%s' "$LIBRARY_FILES"
} > "$staging/README.txt"

libraries="$work/$libraries_name"
rm -f "$libraries"
# The same files give the same bytes: names sorted, file entries only, the date of the runtime's commit, owner 0, mode 0644,
# and gzip without a name or a date of its own.
date="$(git -C "$repository" show -s --format=%ct "$RUNTIME_COMMIT")"
(cd "$tree" && find "${libraries_prefix%/}" -type f | LC_ALL=C sort) > "$work/libraries-files"
tar --create --format=ustar --no-recursion --owner=0 --group=0 --numeric-owner --mode=a=r,u+w --mtime="@$date" \
  --directory="$tree" --files-from="$work/libraries-files" --file=- | gzip -9 -n > "$libraries"

expected="$({
  echo "${libraries_prefix}README.txt"
  while read -r sha256 path; do
    if [ -n "$path" ]; then echo "$libraries_prefix$path"; fi
  done <<< "$LIBRARY_FILES"
} | LC_ALL=C sort)"
if [ "$(tar -tzf "$libraries")" != "$expected" ]; then
  echo "$libraries_name does not hold exactly README.txt and the files of LIBRARY_FILES, in order. Nothing was copied." >&2
  exit 1
fi
while read -r sha256 path; do
  [ -n "$path" ] || continue
  actual="$(tar -xzOf "$libraries" "$libraries_prefix$path" | sha256sum | cut -d' ' -f1)"
  if [ "$actual" != "$sha256" ]; then
    echo "$libraries_name holds $path with the SHA-256 $actual, expected $sha256. Nothing was copied." >&2
    exit 1
  fi
done <<< "$LIBRARY_FILES"

cp -- "$fuse" "$out/$fuse_name"
cp -- "$runtime" "$out/$runtime_name"
cp -- "$libraries" "$out/$libraries_name"
echo "Added: $out/$fuse_name, $out/$runtime_name and $out/$libraries_name, SHA-256 $(sha256sum "$out/$libraries_name" | cut -d' ' -f1)"
