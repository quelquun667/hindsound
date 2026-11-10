# Third-party notices

Hindsound itself is under the MIT licence: see [LICENSE](LICENSE).

Hindsound contains or ships the components below. The last column, "In", says whether a component is in the Windows build, in the Linux packages (the AppImage and the tar.gz), or in both; it comes from the files of each trimmed publish, listed on 2026-11-10, and from what build-packages.sh adds to it for the Linux packages: `libmp3lame.so.0` in both, and the runtime at the head of the AppImage. Every file in [licenses/](licenses) is an unchanged copy of the upstream text; the table says where each one comes from. This document records what the project checked on 2026-11-06, 2026-11-07 and 2026-11-10. It is not legal advice.

| Component | Version | Licence | Copyright | Text | Copied from | In |
|---|---|---|---|---|---|---|
| LAME (`libmp3lame.64.dll`) | 3.100 | GNU Library General Public License, version 2 or any later version | See [LAME](#lame) | [LAME-COPYING.txt](licenses/LAME-COPYING.txt), [LAME-LICENSE.txt](licenses/LAME-LICENSE.txt), [LAME-README.txt](licenses/LAME-README.txt) | `COPYING`, `LICENSE` and `README` in `lame-3.100.tar.gz` | Windows |
| LAME (`libmp3lame.so.0`) | 3.100 | The same as `libmp3lame.64.dll` above | See [LAME](#lame) | The same three files | The same files of the same `lame-3.100.tar.gz`; see [On Linux](#on-linux) | Linux |
| NAudio.Core and NAudio.Wasapi | 3.3.0 | MIT | Copyright 2008-2026 Mark Heath | [NAudio-LICENSE.txt](licenses/NAudio-LICENSE.txt) | `LICENSE` in [naudio/NAudio](https://github.com/naudio/NAudio) at commit `4f8889e2bd34c3978a25652c6c172fa1502ccba3`, the commit both packages name. The packages themselves state "MIT" but hold no licence file | NAudio.Core: Windows and Linux. NAudio.Wasapi: Windows |
| NAudio.Lame | 2.1.0 | MIT | Copyright (c) 2013-2019 Corey Murtagh | [NAudio.Lame-LICENSE.txt](licenses/NAudio.Lame-LICENSE.txt) | `LICENSE.txt` in the NuGet package | Windows: `NAudio.Lame.dll` and `LameDLLWrap.dll`. Linux: `NAudio.Lame.dll` only (Hindsound.Core references it; the Windows DLLs of the package are left out of the Linux publish) |
| Tmds.DBus.Protocol | 0.95.2 | MIT | Copyright 2006 Alp Toker, Copyright 2010 Other Contributors, Copyright 2016 Tom Deseyn | [Tmds.DBus.Protocol-COPYING.txt](licenses/Tmds.DBus.Protocol-COPYING.txt) | `COPYING` in [tmds/Tmds.DBus](https://github.com/tmds/Tmds.DBus) at commit `5557e043025de2251128ab8048c9d3f0cf50495e`, the commit the package names. The package itself states "MIT" (`license` of its nuspec) but holds no licence file | Linux |
| System.Numerics.Tensors | 9.0.0 | MIT | Copyright (c) .NET Foundation and Contributors | [System.Numerics.Tensors-LICENSE.txt](licenses/System.Numerics.Tensors-LICENSE.txt), [System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt](licenses/System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt) | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in the NuGet package | Neither: a dependency of NAudio.Core whose DLL is not in the trimmed publish of either build (checked on 2026-11-10); its licence is kept in case a build keeps it |
| .NET runtime | 10 (texts from 10.0.14) | MIT, with the third-party notices listed in its own file | Copyright (c) .NET Foundation and Contributors | [dotnet-runtime-LICENSE.txt](licenses/dotnet-runtime-LICENSE.txt), [dotnet-runtime-THIRD-PARTY-NOTICES.txt](licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt) | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in the `Microsoft.NETCore.App.Runtime.win-x64` 10.0.14 package | Windows and Linux (self-contained in both) |
| Inno Setup (the Setup program, and the `unins000.exe` it installs) | 7.1.0 | Inno Setup License | Copyright (C) 1997-2026 Jordan Russell, portions Copyright (C) 2000-2026 Martijn Laan | [InnoSetup-license.txt](licenses/InnoSetup-license.txt) | `tools/license.txt` in the NuGet package Tools.InnoSetup 7.1.0, the one [get-iscc.ps1](tools/release/get-iscc.ps1) pins. The licence asks to keep its notices in the binaries, which the Setup does, and calls a mention appreciated, not required | Windows (the Setup only) |
| AppImage type2 runtime (`runtime-x86_64`) | 20251108 | Not recorded yet: see [The AppImage runtime](#the-appimage-runtime) | Not recorded yet | None in [licenses/](licenses) yet | The asset `https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64` of the [AppImage/type2-runtime](https://github.com/AppImage/type2-runtime) release 20251108, SHA-256 `2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d`, the file and hash that [build-packages.sh](packaging/linux/build-packages.sh) pins | Linux (AppImage only) |

## The AppImage runtime

The AppImage is a single file. Its first part is the type2 runtime of the AppImage project, the static program that starts it; the rest is the folder of the app. The tar.gz does not hold this runtime.

- [packaging/linux/build-packages.sh](packaging/linux/build-packages.sh) downloads it from the release named in the table, checks the SHA-256 given there before using it, and hands it unchanged to `appimagetool` with `--runtime-file`.
- Its licence, and the licences of the libraries built into it, are not recorded here yet: the project has not checked them against the upstream sources, and [licenses/](licenses) holds no text for it. They are to be recorded before an AppImage is attached to a release.

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

Quit Hindsound, then put your own 64-bit build of LAME, with the same interface and named `libmp3lame.64.dll`, in the folder of `Hindsound.exe`, in place of the one there. Hindsound uses it at its next start. Installing a Hindsound update puts the shipped file back.

### Source code

Every [GitHub Release](https://github.com/quelquun667/hindsound/releases) of Hindsound has the upstream LAME source attached, unchanged, as `lame-3.100.tar.gz`.

- Upstream: <https://sourceforge.net/projects/lame/files/lame/3.100/lame-3.100.tar.gz/download>
- SHA-256: `ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e`
- MD5, as published by SourceForge: `83e260acbe4389b54fe08e0bdbf7cddb`

Hindsound's own source is this repository, with one tag per release.

### Where the shipped DLL comes from

**In the Setup of a GitHub Release**, the DLL is compiled by the release workflow, with [tools/release/build-lame.ps1](tools/release/build-lame.ps1), from the `lame-3.100.tar.gz` attached to that Release. The script verifies the SHA-256 above before extracting the archive.

- No file of the archive is changed, neither source nor build file, and the script fails if the build changes one.
- It runs `nmake` on the archive's own `Makefile.MSVC`, target `libmp3lame.dll`, with three settings on the command line: `MSVCVER=Win64`, `MACHINE=/machine:X64`, and `ADDL_OBJ=kernel32.lib` in place of a library that current Visual Studio does not have.
- The compiler is Microsoft's, from the Visual Studio of the GitHub runner. The Microsoft C runtime is linked statically into the DLL (`/MT`, set by that makefile), so it needs no redistributable.
- This DLL has no version resource: the makefile does not add one. Its function `get_lame_short_version` returns `3.100`.

What has run: the workflow `lame.yml` built and checked this DLL on GitHub once, on 2026-11-07, for the commit `c6fb54d` (run 42318035034), on a `windows-latest` runner with Visual Studio 18 and MSVC 14.51.36231. Its log shows the archive downloaded from SourceForge with the SHA-256 above, `nmake` with the command line above, a 64-bit DLL that imports `KERNEL32.dll` only, 220 exports with the same names as the DLL of the package and the 190 that NAudio.Lame calls among them, `get_lame_short_version` returning `3.100`, and 6 tests that write an MP3 with this DLL through NAudio.Lame and read it back. `/MT` and the lack of a version resource come from reading `Makefile.MSVC`, not from a check: the imports show only that the DLL needs no C runtime DLL. No release has been made yet: no Setup holds a compiled DLL so far.

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
- Nothing of it has run yet on a Linux machine: the CI is the first place it will.

### The decoder inside the DLL

The DLL also contains LAME's MP3 decoder (`hip_*` and `lame_decode*`). Hindsound's code and NAudio.Lame's MP3 writer do not call it. LAME's own statements about its licence differ:

- In the LAME 3.100 archive, all the `mpglib` source files carry the Library GPL header, `mpglib/README` calls the decoder "A LGPLed mpeg audio decoding library", and the `ChangeLog` records "update licence information/header in decoder files" on 2010-03-22.
- The same archive's `README` still says "MPGLIB is released under the GPL", and the page <https://lame.sourceforge.io/license.txt> still says the decoding functions are under the GPL.

The project has not had this reviewed by a lawyer.
