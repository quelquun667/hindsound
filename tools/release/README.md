# Releasing Hindsound

A tag `vX.Y.Z` on `main` is all it takes: GitHub Actions builds the Setup and the manifest, and publishes the Release.

Last checked: 2026-11-07, on Windows 11 for the scripts, by reading and with stand-ins.

**What has never run, as of 2026-11-07: no real release has been made, and neither `release.yml` nor `lame.yml` has run on
GitHub. The compilation of LAME and the installation test in `check-setup.ps1` have never run at all.** The first push of
`lame.yml` to `main` runs the compilation, without publishing anything: check that run before the first tag.

## Before the first release

These are settings of the repository, once: secret scanning, push protection and immutable releases. The rest needs nothing.

## Release a version

From the root of the repo, in PowerShell, with `0.1.0` as the example.

```powershell
git switch main
git pull --ff-only

# 1. In CHANGELOG.md, rename "## [Unreleased]" to "## [0.1.0] - 2026-11-06", the date of today.
#    Add a new empty "## [Unreleased]" above it.
git add CHANGELOG.md
git commit -m "Release 0.1.0"
git push origin main

# 2. The tag carries the same notes as the Release. --cleanup=whitespace keeps the "### Added" lines:
#    by default, git removes the lines that start with #.
$notes = Join-Path $env:TEMP 'hindsound-notes.md'
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\extract-changelog.ps1 -Version 0.1.0 -OutFile $notes
git tag -a v0.1.0 --cleanup=whitespace -F $notes
git push origin v0.1.0
Remove-Item $notes
```

Never `git push --all` or `--mirror`: the `worktree-*` branches were not scanned for secrets.

## What happens

`release.yml` runs these steps in order. A failure stops everything after it, and no Release comes out.

One release runs at a time: a tag pushed during a run waits for it, and no run is cancelled to make room. GitHub keeps one
waiting run only: a third tag pushed meanwhile cancels the waiting one, whose version then stays below the highest tag and is
never published.

| Step | What it does |
| --- | --- |
| Check the tag | `check-tag.ps1`: the form `vX.Y.Z`, the commit on `origin/main`, the version jump below. |
| Extract the release notes | `extract-changelog.ps1`: the section of this version in `CHANGELOG.md`. Missing or empty: failure. |
| Test | All the tests but `Category=Device`, then `test-release-scripts.ps1`. |
| Publish the app | `dotnet publish` with the `win-x64` profile and this version. |
| Build LAME | `build-lame.ps1`: downloads `lame-3.100.tar.gz`, checks its SHA-256 before extracting it, compiles with `nmake` and `Makefile.MSVC`, then `check-lame-dll.ps1`: x64, only Windows DLLs needed, every export of the DLL of the package, version 3.100. |
| Replace the DLL in the published folder | The compiled DLL takes the place of the package's. |
| Prove the encoding | `prove-encoding.ps1`: the compiled DLL in the tests' output folder, then 6 test cases with `--no-build` that write a real MP3 through NAudio.Lame and read it back. One decodes the whole file and compares its level with the sine that went in. |
| Build the Setup | `get-iscc.ps1`, then ISCC on `installer/Hindsound.iss`. |
| Check the Setup | `check-setup.ps1`: silent installation in a temporary folder of the runner; exe at the file version of the tag, DLL with the hash of the compiled one, `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses\*.txt` identical to the repo; the installed exe draws its notifications (`--dump-notifications`), exit code 0 and BMP files; the installed exe writes one second of sine as an MP3 through the path of a clip (`--prove-mp3`), exit code 0, one MP3 and no WAV; silent uninstallation. |
| Write the update manifest | `write-manifest.ps1`: `hindsound-update-win-x64.json`. |
| Create the draft release | A draft, with the notes. |
| Attach the files | The Setup, the manifest, `lame-3.100.tar.gz`. Then they are downloaded back, and `check-release-files.ps1` wants exactly these three, a manifest that matches the Setup and its address, and the SHA-256 of the LAME source. |
| Publish the release | `check-remote-tag.ps1`: the tag on GitHub must still point at the commit built. Then the draft goes public, and the app sees the new version from then on. |

`lame.yml` runs only the compilation, its checks and the proof of the encoding. It publishes nothing, on a manual start or on a
push to `main` that changes `tools/release/build-lame.ps1`, `check-lame-dll.ps1`, `lame-exports.txt`, `prove-encoding.ps1` or
`lame.yml` itself.

## The version jump

The app remembers the highest version it has read. A 9.0.0 by mistake would block everything lower, for good, for whoever
read it. So `check-tag.ps1` compares the tag with the `vX.Y.Z` tags already there, itself aside:

| Case | Result |
| --- | --- |
| No earlier tag, version 0.x | Passes. |
| No earlier tag, any other version | Needs the confirmation. |
| Lower than or equal to the highest | Always refused. |
| The next patch: 0.1.0 then 0.1.1 | Passes. |
| The next minor with patch 0: 0.1.3 then 0.2.0 | Passes. |
| Any other jump, a new major included | Needs the confirmation. |

The confirmation is the repository variable `RELEASE_CONFIRM_VERSION`, set to exactly this version, with no `v`:

```powershell
gh variable set RELEASE_CONFIRM_VERSION --body 1.0.0
git push origin v1.0.0
# Once the Release is out:
gh variable delete RELEASE_CONFIRM_VERSION
```

If the tag is already pushed and refused, set the variable, then rerun the run as below.

## Rerun a failed run

```powershell
gh run list --workflow release.yml --limit 5
gh run rerun <run id> --failed
```

Nothing is left to clean up before this. The tag no longer counts against itself, a draft left by the failed run is reused, and
its files are replaced. A release already published is refused: an immutable release cannot change, so go to the next version.

A rerun uses the commit of the tag. If the fix is in the code or in a workflow, the tag must move, which is possible only if the
release was never published:

```powershell
gh release delete v0.1.0 --yes    # only if a draft exists
git push origin :refs/tags/v0.1.0
git tag -d v0.1.0
# Commit the fix on main and push it, then make the tag again as above.
```

Pushing the tag again starts a new run, from the fix. Never rerun a run started before the tag moved: it built the old commit,
and its last step refuses to publish it, as `check-remote-tag.ps1` sees the tag elsewhere on GitHub.

## Test the scripts

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\test-release-scripts.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\mutate-release-guards.ps1 -From 1 -To 15
```

`test-release-scripts.ps1` compiles nothing, downloads nothing and installs nothing: `build-lame.ps1` gets a fake compiler, and
`check-setup.ps1` a fake folder. It runs in CI with `pwsh`. It needs the NAudio.Lame package, so run a restore first.
`mutate-release-guards.ps1` undoes each guard on a copy and fails if the tests do not notice. It takes about 30 s per entry.

**Never start `check-setup.ps1` without `-CheckFolder` on a PC in use, or a release Setup at all.** The script refuses anywhere but
a GitHub-hosted runner, because the uninstaller of a release build deletes the real log and the real Run value.

`lame-exports.txt` lists what NAudio.Lame 2.1.0 imports. After an update of that package, read it again from the `[DllImport]`
attributes of `NAudio.Lame.x64.LameDLLWrap.dll`, a resource inside `NAudio.Lame.dll`.
