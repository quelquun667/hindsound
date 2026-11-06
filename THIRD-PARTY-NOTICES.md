# Third-party notices

Hindsound itself is under the MIT licence: see [LICENSE](LICENSE).

The Windows build of Hindsound contains or ships the components below. Every file in [licenses/](licenses) is an unchanged copy of the upstream text; the table says where each one comes from. This document records what the project checked on 2026-11-06. It is not legal advice.

| Component | Version | Licence | Copyright | Text | Copied from |
|---|---|---|---|---|---|
| LAME (`libmp3lame.64.dll`) | 3.100 | GNU Library General Public License, version 2 or any later version | See [LAME](#lame) | [LAME-COPYING.txt](licenses/LAME-COPYING.txt), [LAME-LICENSE.txt](licenses/LAME-LICENSE.txt), [LAME-README.txt](licenses/LAME-README.txt) | `COPYING`, `LICENSE` and `README` in `lame-3.100.tar.gz` |
| NAudio.Core and NAudio.Wasapi | 3.3.0 | MIT | Copyright 2008-2026 Mark Heath | [NAudio-LICENSE.txt](licenses/NAudio-LICENSE.txt) | `LICENSE` in [naudio/NAudio](https://github.com/naudio/NAudio) at commit `4f8889e2bd34c3978a25652c6c172fa1502ccba3`, the commit both packages name. The packages themselves state "MIT" but hold no licence file |
| NAudio.Lame | 2.1.0 | MIT | Copyright (c) 2013-2019 Corey Murtagh | [NAudio.Lame-LICENSE.txt](licenses/NAudio.Lame-LICENSE.txt) | `LICENSE.txt` in the NuGet package |
| System.Numerics.Tensors | 9.0.0 | MIT | Copyright (c) .NET Foundation and Contributors | [System.Numerics.Tensors-LICENSE.txt](licenses/System.Numerics.Tensors-LICENSE.txt), [System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt](licenses/System.Numerics.Tensors-THIRD-PARTY-NOTICES.txt) | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in the NuGet package |
| .NET runtime | 10 (texts from 10.0.14) | MIT, with the third-party notices listed in its own file | Copyright (c) .NET Foundation and Contributors | [dotnet-runtime-LICENSE.txt](licenses/dotnet-runtime-LICENSE.txt), [dotnet-runtime-THIRD-PARTY-NOTICES.txt](licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt) | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` in the `Microsoft.NETCore.App.Runtime.win-x64` 10.0.14 package |

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

**In a build from source** (`dotnet publish`), the DLL is the one below, from the NuGet package.

What is established about the DLL of the package:

- The file is taken unchanged from the NuGet package NAudio.Lame 2.1.0, folder `build`. SHA-256: `71dba147b6c16d8d0f012b8e657d52e12cb8796a659e1151200d0a6c8b04bce3`. The file in that project's repository is the same.
- It is byte for byte the `libmp3lame.dll` of the RareWares archive <https://www.rarewares.org/files/mp3/libmp3lame-3.100x64.zip> (compared on 2026-11-06; archive SHA-256: `99d934cf99c74e8e76d8a2fdbf83b778dfdc3c4476af6f81173fe5a7e278e313`). [RareWares](https://www.rarewares.org/mp3-lame-libraries.php) lists it as dated 2017-10-22, and its link timestamp is 2017-10-22 12:00:39 UTC.
- Its version resource says LAME 3.100.

What is not established about it:

- Neither NAudio.Lame nor RareWares says which exact source tree or build scripts produced this binary, and RareWares does not say whether it was patched. It describes the library as "the standard libmp3lame.dll" and its builds as "Intel compiles".
- A build path inside the DLL, `E:\lame-3.100-CVS\output\Release\libmp3lame.pdb`, points to a CVS checkout, and the build is nine days younger than the source archive (2017-10-13). So `lame-3.100.tar.gz` is the published source of LAME 3.100, but it is not proven to be the exact source of this binary.

### The decoder inside the DLL

The DLL also contains LAME's MP3 decoder (`hip_*` and `lame_decode*`). Hindsound's code and NAudio.Lame's MP3 writer do not call it. LAME's own statements about its licence differ:

- In the LAME 3.100 archive, all the `mpglib` source files carry the Library GPL header, `mpglib/README` calls the decoder "A LGPLed mpeg audio decoding library", and the `ChangeLog` records "update licence information/header in decoder files" on 2010-03-22.
- The same archive's `README` still says "MPGLIB is released under the GPL", and the page <https://lame.sourceforge.io/license.txt> still says the decoding functions are under the GPL.

The project has not had this reviewed by a lawyer.
