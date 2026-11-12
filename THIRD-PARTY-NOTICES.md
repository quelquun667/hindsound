# Third-party notices

Hindsound itself is under the MIT licence: see [LICENSE](LICENSE).

Hindsound contains or ships the components below. The last column, "In", says whether a component is in the Windows build, in the Linux packages (the AppImage and the tar.gz), or in both; it comes from the files of each trimmed publish, listed on 2026-11-10, and from what build-packages.sh adds to it for the Linux packages: `libmp3lame.so.0` in both, and the runtime at the head of the AppImage, with the libraries built into it. The Windows build comes as the Setup and as the portable zip: a Windows component is in both, except Inno Setup, in the Setup only (the portable zip has no `unins000.exe`). Every file in [licenses/](licenses) is an unchanged copy of the upstream text; the table says where each one comes from. The texts of the AppImage runtime, of the libraries built into it and of Tmds.DBus.Protocol, which only the Linux app references, are in [licenses/linux/](licenses/linux), which only the Linux packages carry: the Setup and the portable zip take the files at the top of [licenses/](licenses) only. This document records what the project checked on 2026-11-06, 2026-11-07 and 2026-11-10. It is not legal advice.

| Component | Version | Licence | Copyright | Text | Copied from | In |
|---|---|---|---|---|---|---|
| LAME (`libmp3lame.64.dll`) | 3.100 | GNU Library General Public License, version 2 or any later version | See [LAME](#lame) | [LAME-COPYING.txt](licenses/LAME-COPYING.txt), [LAME-LICENSE.txt](licenses/LAME-LICENSE.txt), [LAME-README.txt](licenses/LAME-README.txt) | `COPYING`, `LICENSE` and `README` in `lame-3.100.tar.gz` | Windows (the Setup and the portable zip) |
| LAME (`libmp3lame.so.0`) | 3.100 | The same as `libmp3lame.64.dll` above | See [LAME](#lame) | The same three files | The same files of the same `lame-3.100.tar.gz`; see [On Linux](#on-linux) | Linux |
| NAudio.Core and NAudio.Wasapi | 3.3.0 | MIT | Copyright 2008-2026 Mark Heath | [NAudio-LICENSE.txt](licenses/NAudio-LICENSE.txt) | `LICENSE` in [naudio/NAudio](https://github.com/naudio/NAudio) at commit `4f8889e2bd34c3978a25652c6c172fa1502ccba3`, the commit both packages name. The packages themselves state "MIT" but hold no licence file | NAudio.Core: Windows (the Setup and the portable zip) and Linux. NAudio.Wasapi: Windows (the Setup and the portable zip) |
| NAudio.Lame | 2.1.0 | MIT | Copyright (c) 2013-2019 Corey Murtagh | [NAudio.Lame-LICENSE.txt](licenses/NAudio.Lame-LICENSE.txt) | `LICENSE.txt` in the NuGet package | Windows (the Setup and the portable zip): `NAudio.Lame.dll` and `LameDLLWrap.dll`. Linux: `NAudio.Lame.dll` only (Hindsound.Core references it; the Windows DLLs of the package are left out of the Linux publish) |
| Tmds.DBus.Protocol | 0.95.2 | MIT | Copyright 2006 Alp Toker, Copyright 2010 Other Contributors, Copyright 2016 Tom Deseyn | [Tmds.DBus.Protocol-COPYING.txt](licenses/linux/Tmds.DBus.Protocol-COPYING.txt) | `COPYING` in [tmds/Tmds.DBus](https://github.com/tmds/Tmds.DBus) at commit `5557e043025de2251128ab8048c9d3f0cf50495e`, the commit the package names. The package itself states "MIT" (`license` of its nuspec) but holds no licence file | Linux |
| System.Numerics.Tensors | 9.0.0 | MIT | Copyright (c) .NET Foundation and Contributors | [System.Numerics.Tensors-LICENSE.txt](licenses/System.Numerics.Tensors-LICENSE.txt), [System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt](licenses/System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt) | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in the NuGet package | Neither: a dependency of NAudio.Core whose DLL is not in the trimmed publish of either build (checked on 2026-11-10); its licence is kept in case a build keeps it |
| .NET runtime | 10 (texts from 10.0.14) | MIT, with the third-party notices listed in its own file | Copyright (c) .NET Foundation and Contributors | [dotnet-runtime-LICENSE.txt](licenses/dotnet-runtime-LICENSE.txt), [dotnet-runtime-THIRD-PARTY-NOTICES.txt](licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt) | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in the `Microsoft.NETCore.App.Runtime.win-x64` 10.0.14 package | Windows (the Setup and the portable zip) and Linux (self-contained in both) |
| Inno Setup (the Setup program, and the `unins000.exe` it installs) | 7.1.0 | Inno Setup License | Copyright (C) 1997-2026 Jordan Russell, portions Copyright (C) 2000-2026 Martijn Laan | [InnoSetup-license.txt](licenses/InnoSetup-license.txt) | `tools/license.txt` in the NuGet package Tools.InnoSetup 7.1.0, the one [get-iscc.ps1](tools/release/get-iscc.ps1) pins. The licence asks to keep its notices in the binaries, which the Setup does, and calls a mention appreciated, not required | Windows (the Setup only: the portable zip has no `unins000.exe`) |
| AppImage type2 runtime (`runtime-x86_64`) | 20251108 | MIT, with the libraries below built into it: see [The AppImage runtime](#the-appimage-runtime) | Copyright (c) 2004-23 probonopd (its `LICENSE`); Copyright (c) 2004-24 Simon Peter, portions Copyright (c) 2007 Alexander Larsson (the header of `src/runtime/runtime.c` at the tag); MD5 code from WjCryptLib_Md5, by Alexander Peslyak, modified by WaterJuice, public domain | [AppImage-type2-runtime-LICENSE.txt](licenses/linux/AppImage-type2-runtime-LICENSE.txt) | `LICENSE` of [AppImage/type2-runtime](https://github.com/AppImage/type2-runtime) at the tag `20251108` (commit `dd6cebedcbddde9c82f89b011e8e1d40b6e43868`): <https://github.com/AppImage/type2-runtime/blob/20251108/LICENSE>. The runtime itself is the asset `https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64` of the release 20251108, SHA-256 `2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d`, the file and hash that [build-packages.sh](packaging/linux/build-packages.sh) pins | Linux (AppImage only) |
| musl libc (in the AppImage runtime) | 1.2.5 (Alpine 3.21 package, release 1.2.5-r9) | MIT | Copyright © 2005-2020 Rich Felker, et al. | [musl-COPYRIGHT.txt](licenses/linux/musl-COPYRIGHT.txt) | `COPYRIGHT` of musl at the tag `v1.2.5`: <https://git.musl-libc.org/cgit/musl/plain/COPYRIGHT?h=v1.2.5>. The file also lists the authors and the few parts under other permissive terms | Linux (AppImage only) |
| libfuse (in the AppImage runtime) | 3.15.0, with the runtime's patch to `lib/mount.c` | GNU Lesser General Public License, version 2.1 (the files of `lib/` and `include/` the library is built from) | Copyright (C) 2001-2007 Miklos Szeredi, and the authors named in the files of `lib/` | [libfuse-LGPL2.txt](licenses/linux/libfuse-LGPL2.txt), [libfuse-LICENSE.txt](licenses/linux/libfuse-LICENSE.txt) | `LGPL2.txt` and `LICENSE` of [libfuse/libfuse](https://github.com/libfuse/libfuse) at the tag `fuse-3.15.0` (commit `6d08472ea47db895748d5ca7d3daf032c3fefcf8`). Its source: see [libfuse and the LGPL-2.1](#libfuse-and-the-lgpl-21) | Linux (AppImage only) |
| squashfuse (in the AppImage runtime) | 0.5.2 | BSD-2-Clause | Copyright (c) 2012 Dave Vasilevsky, Phillip Lougher | [squashfuse-LICENSE.txt](licenses/linux/squashfuse-LICENSE.txt) | `LICENSE` of [vasi/squashfuse](https://github.com/vasi/squashfuse) at the tag `0.5.2` (commit `775b4cc72ab47641637897f11ce0da15d5c1f115`) | Linux (AppImage only) |
| Zstandard (`libzstd`, in the AppImage runtime) | 1.5.6 (Alpine 3.21 package, release 1.5.6-r2) | BSD-3-Clause, the BSD side of its dual licence "BSD OR GPLv2" | Copyright (c) Meta Platforms, Inc. and affiliates | [zstd-LICENSE.txt](licenses/linux/zstd-LICENSE.txt) | `LICENSE` of [facebook/zstd](https://github.com/facebook/zstd) at the tag `v1.5.6` (commit `794ea1b0afca0f020f4e57b6732332231fb23c70`) | Linux (AppImage only) |
| zlib (in the AppImage runtime) | 1.3.1 (Alpine 3.21 package, release 1.3.1-r2) | zlib License | (C) 1995-2022 Jean-loup Gailly and Mark Adler, as this `LICENSE` reads; the inflate code in the runtime carries "Copyright 1995-2024 Mark Adler" | [zlib-LICENSE.txt](licenses/linux/zlib-LICENSE.txt) | `LICENSE` of [madler/zlib](https://github.com/madler/zlib), the author's repository, at the tag `v1.3.1` (commit `51b7f2abdade71cd9bb0e7a373ef2610ec6f9daf`) | Linux (AppImage only) |
| mimalloc (in the AppImage runtime) | 2.1.7 (Alpine 3.21 recipe `mimalloc2`, release 2.1.7-r0) | MIT | Copyright (c) 2018-2021 Microsoft Corporation, Daan Leijen | [mimalloc-LICENSE.txt](licenses/linux/mimalloc-LICENSE.txt) | `LICENSE` of [microsoft/mimalloc](https://github.com/microsoft/mimalloc) at the tag `v2.1.7` (commit `8c532c32c3c96e5ba1f2283e032f69ead8add00f`). The runtime's own `LICENSE` does not name it: see [What is built into it](#what-is-built-into-it) | Linux (AppImage only) |

## The AppImage runtime

The AppImage is a single file. Its first part is the type2 runtime of the AppImage project, the static program that mounts the rest of the file and starts the app; the rest is the folder of the app. The tar.gz does not hold this runtime.

- It comes from the release 20251108 of [AppImage/type2-runtime](https://github.com/AppImage/type2-runtime), whose tag points to the commit `dd6cebedcbddde9c82f89b011e8e1d40b6e43868`. The upstream workflow built it in the run <https://github.com/AppImage/type2-runtime/actions/runs/20037536435>, the build log the release names, and published it on 2025-12-08.
- [packaging/linux/build-packages.sh](packaging/linux/build-packages.sh) downloads `runtime-x86_64` from that release, checks its SHA-256, `2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d`, before using it, and hands it unchanged to `appimagetool` with `--runtime-file`.
- The file with that SHA-256 was downloaded and read on 2026-11-10. Its version string is `https://github.com/AppImage/type2-runtime/commit/dd6cebe`, the commit of the tag, and it holds the version strings `squashfuse 0.5.2`, `3.15.0` (libfuse), `1.5.6` (Zstandard) and `inflate 1.3.1` (zlib).
- The runtime is under the MIT licence of its repository: [licenses/linux/AppImage-type2-runtime-LICENSE.txt](licenses/linux/AppImage-type2-runtime-LICENSE.txt).
- The runtime's own code is one source file, `src/runtime/runtime.c` at the tag `20251108` (the other files of `src/runtime/` are its `Makefile`, `data_sections.ld` and `version`). That file carries its own notice, with copyright holders the `LICENSE` does not name: see below.

The copyright lines of the header of `src/runtime/runtime.c` at the tag `20251108` read:

```text
Copyright (c) 2004-24 Simon Peter
Portions Copyright (c) 2007 Alexander Larsson
Portions from WjCryptLib_Md5 originally written by Alexander Peslyak,
  modified by WaterJuice retaining Public Domain license
```

The header follows them with "All Rights Reserved." and then the same permission notice and disclaimer, word for word, as that `LICENSE`.

### What is built into it

The runtime is one static executable. At the tag, `src/runtime/Makefile` links it with `-static` and `-static-pie`, and with `-lsquashfuse -lsquashfuse_ll -lzstd -lz -lfuse3 -lmimalloc`, on the musl C library of Alpine Linux. Each library and where it comes from:

- **libfuse 3.15.0**: compiled from source by `scripts/common/install-dependencies.sh`, from `fuse-3.15.0.tar.xz` of the libfuse release `fuse-3.15.0`, after a SHA-256 check (`70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0`). The script applies `patches/libfuse/mount.c.diff` of the runtime's repository to `lib/mount.c`, then builds a static library with Meson.
- **squashfuse 0.5.2**: compiled from source by the same script, from `https://github.com/vasi/squashfuse/archive/0.5.2.tar.gz`, after a SHA-256 check (`db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3`), with `./configure LDFLAGS="-static"`.
- **Zstandard, zlib, mimalloc and musl**: Alpine 3.21 packages, installed by `scripts/docker/Dockerfile` (`FROM alpine:3.21`, with `zstd-static`, `zlib-static`, `mimalloc-dev` and `musl-dev`). Their versions are those of the recipes on the `3.21-stable` branch of [alpinelinux/aports](https://github.com/alpinelinux/aports) at the build, on 2025-12-08: `main/musl` 1.2.5-r9, `main/zstd` 1.5.6-r2, `main/zlib` 1.3.1-r2, `community/mimalloc2` 2.1.7-r0. The strings of the runtime confirm the Zstandard and zlib versions; the musl and mimalloc versions rest on the recipes alone. The last commit of `3.21-stable` that changed each recipe before that build, read on 2026-11-12, is `efd4d5df7be0ea48d98512671f676fc5ceb14899` for `main/musl` (2025-02-13), `5c2ddf18f193dafdd25ae7391d96c7e30766eecd` for `main/zstd` (2024-12-25), `4c2722e7ecc1c5e3ba882429044a101497121223` for `main/zlib` (2024-09-18) and `5a1a5dc3216a69e3cd66a57ee07cb5ef01b8be39` for `community/mimalloc2` (2024-07-23), and the `APKBUILD` at each of these commits has the version and release above. `mimalloc-dev` is the name the `-dev` package of `community/mimalloc2` provides (`provides="mimalloc-dev=$pkgver-r$pkgrel"` in its `APKBUILD`); the branch has no other `mimalloc` recipe.

What the runtime's `LICENSE` leaves out:

- **mimalloc** is linked (`-lmimalloc` in the Makefile, and the runtime holds mimalloc's own messages, such as `mimalloc: warning: `), but the `LICENSE` of the runtime does not name it. Its MIT text is recorded here like the others.
- **No other compression library**: squashfuse can also use xz, LZ4 and LZO, but the link line names only Zstandard and zlib, and a static link fails on any symbol left unresolved. The runtime holds no string of liblzma, LZ4 or LZO either.
- **The compiler's own support code**: the runtime was compiled by Alpine's clang 19.1.4, and its `.comment` section also names `GCC: (Alpine 14.2.0) 14.2.0`. Which start-up and support objects the link added was not identified; the parts that come from GCC's `libgcc` fall under the GCC Runtime Library Exception, which asks for no notice.

### libfuse and the LGPL-2.1

libfuse's `lib/` and `include/` are under the GNU Lesser General Public License, version 2.1 ([libfuse-LICENSE.txt](licenses/linux/libfuse-LICENSE.txt), [libfuse-LGPL2.txt](licenses/linux/libfuse-LGPL2.txt)). The runtime is a program linked statically with that library, after a change to `lib/mount.c`; the runtime itself is free software under the MIT licence.

The exact source of what is in the runtime:

- libfuse: <https://github.com/libfuse/libfuse/releases/download/fuse-3.15.0/fuse-3.15.0.tar.xz>, SHA-256 `70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0`, with the patch <https://github.com/AppImage/type2-runtime/blob/20251108/patches/libfuse/mount.c.diff>.
- The program linked with it, and how: <https://github.com/AppImage/type2-runtime/tree/20251108>, with `src/runtime/runtime.c`, `src/runtime/Makefile`, `scripts/common/install-dependencies.sh`, `scripts/docker/Dockerfile` and `scripts/build-runtime.sh`.
- squashfuse, linked into the same program: <https://github.com/vasi/squashfuse/archive/0.5.2.tar.gz>, SHA-256 `db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3`, the archive `install-dependencies.sh` downloads.
- Zstandard, zlib, mimalloc and musl, linked into the same program: for each Alpine package, the upstream archive its `APKBUILD` names, with the SHA-512 of its `sha512sums`, and its folder of [alpinelinux/aports](https://github.com/alpinelinux/aports) (the `APKBUILD` and every patch and file it names), at the commits above: <https://github.com/alpinelinux/aports/tree/efd4d5df7be0ea48d98512671f676fc5ceb14899/main/musl>, <https://github.com/alpinelinux/aports/tree/5c2ddf18f193dafdd25ae7391d96c7e30766eecd/main/zstd>, <https://github.com/alpinelinux/aports/tree/4c2722e7ecc1c5e3ba882429044a101497121223/main/zlib> and <https://github.com/alpinelinux/aports/tree/5a1a5dc3216a69e3cd66a57ee07cb5ef01b8be39/community/mimalloc2>.

What section 6 of the licence asks of whoever distributes such a program (read in [libfuse-LGPL2.txt](licenses/linux/libfuse-LGPL2.txt)):

- the terms of the program must permit modification for the user's own use and reverse engineering for debugging such modifications: the runtime's MIT licence does;
- each copy must give prominent notice that the library is used in it and is covered by the licence, with a copy of the licence: this document and [licenses/](licenses), which both Linux packages carry;
- and one of these: (a) the complete source of the library, with its changes, travels with the program, along with the program as object code or source, so that the user can change the library and relink; (c) a written offer, valid for at least three years, to give the same materials; (d) when the program is distributed by offering access to copy it from a designated place, the same materials can be copied from that same place; (e) the user has already received them. Point (b), a shared library, does not apply to a static program.

Every release that hands out the AppImage attaches, next to it, as it attaches LAME's source, three files:

- `fuse-3.15.0.tar.xz`, the libfuse archive above, unchanged;
- `type2-runtime-20251108-source.tar.gz`, the runtime's repository at the commit of the tag 20251108 (the runtime, its build scripts and `patches/libfuse/mount.c.diff`);
- `type2-runtime-20251108-libraries-source.tar.gz`, the source of every other library linked into the runtime: `squashfuse-0.5.2.tar.gz`, the upstream archives of musl, Zstandard, zlib and mimalloc, their folders of aports under `aports/`, and a `README.txt` that gives where each file comes from and its SHA-256.

With these three, the release meets point (d): it offers the source of libfuse with its change, and the source of everything else the runtime is linked from, with the scripts that build and link it. Without any one of them it would not, and the check of the files of a release refuses a release that lacks one. The compiler's own start-up and support code is not in them: section 6 does not ask for anything normally distributed with the major components of the operating system, such as the compiler, unless that component itself accompanies the executable. The artifact `hindsound-linux-x64` of the CI, which also hands out the AppImage, carries the same three files next to it.

## LAME

Hindsound uses **LAME** (LAME Ain't an MP3 Encoder) to write MP3 files. LAME is a separate library, and the library and its use are covered by the GNU Library General Public License, version 2, or any later version: see [licenses/LAME-COPYING.txt](licenses/LAME-COPYING.txt). The project's own note on using LAME in other programs is in [licenses/LAME-LICENSE.txt](licenses/LAME-LICENSE.txt).

LAME's project site is <https://lame.sourceforge.io/>. Its licence note gives the address www.mp3dev.org.

The copyright lines in LAME's README read:

```text
Copyrights (c) 1999-2011 by The LAME Project
Copyrights (c) 1999,2000,2001 by Mark Taylor
Copyrights (c) 1998 by Michael Cheng
Copyrights (c) 1995,1996,1997 by Michael Hipp: mpglib

As well as additional copyrights as documented in the source code.
```

### How Hindsound uses it

- `libmp3lame.64.dll` is a separate file in the folder of `Hindsound.exe`. It is loaded at run time, by that name.
- Hindsound does not change LAME.
- Hindsound's MIT licence permits modifying the program for your own use, and reverse engineering to debug such changes.

### Replacing the library

Quit Hindsound, then put your own 64-bit build of LAME, with the same interface and named `libmp3lame.64.dll`, in the folder of `Hindsound.exe`, in place of the one there. Hindsound uses it at its next start. Installing a Hindsound update puts the shipped file back, and so does replacing the files of a portable copy with those of a new zip.

### Source code

Every [GitHub Release](https://github.com/quelquun667/hindsound/releases) of Hindsound has the upstream LAME source attached, unchanged, as `lame-3.100.tar.gz`: the source of the Windows DLL and of the Linux `libmp3lame.so.0` alike.

- Upstream: <https://sourceforge.net/projects/lame/files/lame/3.100/lame-3.100.tar.gz/download>
- SHA-256: `ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e`
- MD5, as published by SourceForge: `83e260acbe4389b54fe08e0bdbf7cddb`

Hindsound's own source is this repository, with one tag per release.

### Where the shipped DLL comes from

**In the Setup and the portable zip of a GitHub Release**, the DLL is compiled by the release workflow, with [tools/release/build-lame.ps1](tools/release/build-lame.ps1), from the `lame-3.100.tar.gz` attached to that Release. The script verifies the SHA-256 above before extracting the archive. The portable zip takes the DLL from the folder the Setup is built from.

- No file of the archive is changed, neither source nor build file, and the script fails if the build changes one.
- It runs `nmake` on the archive's own `Makefile.MSVC`, target `libmp3lame.dll`, with three settings on the command line: `MSVCVER=Win64`, `MACHINE=/machine:X64`, and `ADDL_OBJ=kernel32.lib` in place of a library that current Visual Studio does not have.
- The compiler is Microsoft's, from the Visual Studio of the GitHub runner. The Microsoft C runtime is linked statically into the DLL (`/MT`, set by that makefile), so it needs no redistributable.
- This DLL has no version resource: the makefile does not add one. Its function `get_lame_short_version` returns `3.100`.

What has run: the workflow `lame.yml` built and checked this DLL on GitHub once, on 2026-11-07, for the commit `c6fb54d` (run 42318035034), on a `windows-latest` runner with Visual Studio 18 and MSVC 14.51.36231. Its log shows the archive downloaded from SourceForge with the SHA-256 above, `nmake` with the command line above, a 64-bit DLL that imports `KERNEL32.dll` only, 220 exports with the same names as the DLL of the package and the 190 that NAudio.Lame calls among them, `get_lame_short_version` returning `3.100`, and 6 tests that write an MP3 with this DLL through NAudio.Lame and read it back. `/MT` and the lack of a version resource come from reading `Makefile.MSVC`, not from a check: the imports show only that the DLL needs no C runtime DLL. Every release since 0.0.9 has also built it this way, in its release workflow, and its Setup holds that DLL; from 0.6.0 on, so does its portable zip.

**In a build from source** (`dotnet publish`), the DLL is the one below, from the NuGet package.

What is established about the DLL of the package:

- The file is taken unchanged from the NuGet package NAudio.Lame 2.1.0, folder `build`. SHA-256: `71dba147b6c16d8d0f012b8e657d52e12cb8796a659e1151200d0a6c8b04bce3`. The file in that project's repository is the same.
- It is byte for byte the `libmp3lame.dll` of the RareWares archive <https://www.rarewares.org/files/mp3/libmp3lame-3.100x64.zip> (compared on 2026-11-06; archive SHA-256: `99d934cf99c74e8e76d8a2fdbf83b778dfdc3c4476af6f81173fe5a7e278e313`). [RareWares](https://www.rarewares.org/mp3-lame-libraries.php) lists it as dated 2017-10-22, and its link timestamp is 2017-10-22 12:00:39 UTC.
- Its version resource says LAME 3.100.

What is not established about it:

- Neither NAudio.Lame nor RareWares says which exact source tree or build scripts produced this binary, and RareWares does not say whether it was patched. It describes the library as "the standard libmp3lame.dll" and its builds as "Intel compiles".
- A build path inside the DLL, `E:\lame-3.100-CVS\output\Release\libmp3lame.pdb`, points to a CVS checkout, and the build is nine days younger than the source archive (2017-10-13). So `lame-3.100.tar.gz` is the published source of LAME 3.100, but it is not proven to be the exact source of this binary.

### On Linux

The Linux build (the AppImage and the tar.gz the CI makes) ships LAME as `libmp3lame.so.0`, a separate shared library next to the `hindsound` program, under the same licence.

- It is compiled by [tools/release/build-lame-linux.sh](tools/release/build-lame-linux.sh) from the same `lame-3.100.tar.gz`, after the same SHA-256 check, with the archive's own `configure` and no file changed: the script fails if the build changes one.
- Hindsound looks for it first in the folder of the program, then asks the system for `libmp3lame.so.0`. To use your own build, put it in that folder under that name (in the tar.gz; an AppImage is read-only), or remove the shipped one so that the system's copy is used.
- Both packages carry this document and [licenses/](licenses).
- The CI artifact that holds the packages (`hindsound-linux-x64`) also holds that `lame-3.100.tar.gz`, unchanged, the archive the library of those packages was built from.
- What has run: the CI builds it, then [packaging/linux/check-packages.sh](packaging/linux/check-packages.sh) starts the program of each package with `--prove-mp3`, which writes an MP3 through the `libmp3lame.so.0` of that package, and the dynamic loader (`LD_DEBUG=libs`) shows that file was the one loaded. Both packages passed it on 2026-11-11, on an `ubuntu-22.04` runner of GitHub, in the CI run 42931513159. It has not run on a Linux desktop yet.

### The decoder inside the DLL

The DLL also contains LAME's MP3 decoder (`hip_*` and `lame_decode*`). Hindsound's code and NAudio.Lame's MP3 writer do not call it. LAME's own statements about its licence differ:

- In the LAME 3.100 archive, all the `mpglib` source files carry the Library GPL header, `mpglib/README` calls the decoder "A LGPLed mpeg audio decoding library", and the `ChangeLog` records "update licence information/header in decoder files" on 2010-03-22.
- The same archive's `README` still says "MPGLIB is released under the GPL", and the page <https://lame.sourceforge.io/license.txt> still says the decoding functions are under the GPL.

The project has not had this reviewed by a lawyer.
