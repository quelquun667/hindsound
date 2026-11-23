# Releasing Hindsound

A tag `vX.Y.Z` on `main` is all it takes: GitHub Actions builds every file of the release, for Windows, Linux and macOS,
and publishes the Release.

Last checked: 2026-11-08, with the releases of 0.0.9 and 0.0.10 on GitHub.

**What has run on GitHub: `release.yml`, for 0.0.9 (run 42417937500) and 0.0.10 (run 42418331539), on 2026-11-07, both green
from the tag to the publication. An installed 0.0.9 then updated itself to 0.0.10 from the Release, on 2026-11-08.**

A tag waits until the last run of `lame.yml` is green:

```powershell
gh run list --workflow lame.yml --limit 1
# If no run started, or to run it again on main:
gh workflow run lame.yml --ref main
```

## Settings of the repository

Once: secret scanning, push protection and immutable releases. The rest needs nothing, but for the certificate of the Mac
app below, which is optional.

### The certificate of the Mac app

Not made yet: `packaging/mac/signing-certificate.sha1` does not exist, and the job `sign-mac` of `release.yml` sends
`Hindsound.app` signed ad hoc, with a warning in its log (`Mac app signed ad hoc`): the release still goes out. An app
signed ad hoc is a new app for macOS at each version, which asks again for the permissions (the sound of the system, the
microphone) after each update. Signed by the same certificate, version after version, it keeps them: the probe of the CI
showed it (version 2 signed by the certificate of version 1 read its permission and captured the sound; signed ad hoc, it
was stuck on a prompt). The certificate is made here, not by Apple: it does not remove `Open Anyway`, which macOS asks for
each version downloaded from the Release, certificate or not; only a paid Apple account and a notarization would.

Once `packaging/mac/signing-certificate.sha1` is on `main`, every release must be signed by that certificate: a missing
secret, or a `.p12` of another certificate, stops the job `sign-mac`, and nothing is published.

The commands below were written on 2026-11-21 and have not been run yet. In Git Bash, from the root of the repo (OpenSSL 3,
as Git for Windows has it; the folder must not exist):

```sh
# 1. The certificate, once.
bash packaging/mac/make-signing-certificate.sh ~/hindsound-signing

# 2. The environment mac-signing, which only the tags v* reach: a workflow pushed on a branch cannot read its secrets, as
#    it could read a secret of the repository.
gh api --method PUT repos/quelquun667/hindsound/environments/mac-signing --input - << 'JSON'
{"deployment_branch_policy": {"protected_branches": false, "custom_branch_policies": true}}
JSON
gh api --method POST repos/quelquun667/hindsound/environments/mac-signing/deployment-branch-policies -f name='v*' -f type=tag
gh api repos/quelquun667/hindsound/environments/mac-signing/deployment-branch-policies --jq '.branch_policies[] | .name + " " + .type'
# Expected: v* tag

# 3. The two secrets, in that environment only (never without --env: they would be secrets of the repository).
gh secret set MACOS_SIGNING_P12 --env mac-signing --repo quelquun667/hindsound < ~/hindsound-signing/hindsound-signing.p12.base64
gh secret set MACOS_SIGNING_P12_PASSWORD --env mac-signing --repo quelquun667/hindsound < ~/hindsound-signing/password.txt
gh secret list --env mac-signing --repo quelquun667/hindsound
# Expected: MACOS_SIGNING_P12 and MACOS_SIGNING_P12_PASSWORD

# 4. The SHA-1 of the certificate, public, committed on a branch and merged by a pull request like any change.
git fetch origin
git switch -c mac-certificate origin/main
openssl x509 -in ~/hindsound-signing/hindsound-signing.pem -noout -fingerprint -sha1 | sed 's/^.*=//; s/://g' | tr 'A-F' 'a-f' > packaging/mac/signing-certificate.sha1
cat packaging/mac/signing-certificate.sha1
# Expected: 40 hex digits, the SHA-1 make-signing-certificate.sh printed
git add packaging/mac/signing-certificate.sha1
git commit -m "Sign the Mac app with the certificate of the release"
git push -u origin mac-certificate
gh pr create --fill
```

Optional, a reviewer for each release: GitHub then waits for an approval before `sign-mac` reads the secrets. The same `PUT`
again, with the reviewer (it replaces the settings of step 2, which it repeats):

```sh
id="$(gh api users/quelquun667 --jq .id)"
gh api --method PUT repos/quelquun667/hindsound/environments/mac-signing --input - << JSON
{"deployment_branch_policy": {"protected_branches": false, "custom_branch_policies": true}, "reviewers": [{"type": "User", "id": $id}]}
JSON
```

Then put `hindsound-signing.p12` and the text of `password.txt` in a password manager (an entry with the file attached),
and delete the folder: `rm -rf ~/hindsound-signing`. Do not keep the `.p12` and its password side by side in a folder:
`chmod` has no effect in Git Bash, and anything that reads the home folder would take both. GitHub never gives a secret
back, and the certificate cannot be made again the same. A new certificate works too, but the users then give the
permissions once more: run the four steps again, the same environment, with a new folder. Never commit the `.p12`, the
base64 or the password: the key signs the app of every user.

Until the pull request of step 4 is merged, a release ignores the secrets and goes out signed ad hoc, with the warning. The
script prints the SHA-1 of the certificate; the log of a signed release gives the same twice, in
`resign-zip: the certificate of the .p12 has the SHA-1 <sha1>, the one expected.` and in
`designated requirement: identifier "io.github.quelquun667.hindsound" and certificate leaf = H"<sha1>"`.

What the release does with it: `release-files.yml`, which every pull request also runs, reads no secret and signs the
app ad hoc. The job `sign-mac` of `release.yml`, which no pull request runs, declares the environment `mac-signing`, and
the two secrets reach only its step `Sign the Mac app with the certificate`. That step decodes the `.p12` into a folder of
its own, removed at the end of the step whatever happens, and `packaging/mac/resign-zip.sh` compares the SHA-1 of the
certificate of the `.p12` to `packaging/mac/signing-certificate.sha1`, unzips the zip of the job `macos`, has
`packaging/mac/sign-app.sh` put the `.p12` in a keychain made for the run, sign each Mach-O then the bundle with its SHA-1,
check the signature and the designated requirement, and delete the keychain whatever happens, then makes the zip again
with `ditto`. Nothing of the app runs in that step. The next step, `Check the signed Mac app`, has no secret: there,
`check-zip.sh` starts the app (`--prove-mp3`, `--dump-settings`) and requires the designated requirement to name the SHA-1
of `packaging/mac/signing-certificate.sha1`. The job `assemble-signed` then gathers the files again with that zip, so its
manifest and `SHA256SUMS` follow it, and the job `release` sends those. Every pull request, in the job `macos`, also makes
a throwaway certificate with `make-signing-certificate.sh`, checks that `resign-zip.sh` refuses it for another SHA-1, signs
the zip with it through the same base64, and checks that zip with `check-zip.sh` once the `.p12` is deleted: the path of the
release is tried before the secret exists.

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

Never `git push --all` or `--mirror`: the other local branches were not scanned for secrets.

## What happens

`release.yml` runs four jobs: `check`, then `files` (the workflow `release-files.yml`), then `release`, which publishes.
A failure stops everything after it, and no Release comes out; the job `report-failure` then says so at the end of the log.

One release runs at a time: a tag pushed during a run waits for it, and no run is cancelled to make room. GitHub keeps one
waiting run only: a third tag pushed meanwhile cancels the waiting one, whose version then stays below the highest tag and is
never published.

| Job, step | What it does |
| --- | --- |
| check: Check the tag | `check-tag.ps1`: the form `vX.Y.Z`, the commit on `origin/main`, the version jump below. |
| check: Extract the release notes | `extract-changelog.ps1`: the section of this version in `CHANGELOG.md`. Missing or empty: failure. Then `write-release-notes.ps1` adds the list of the files and whom each one is for. |
| check: Test | All the tests but `Category=Device` and `Category=Linux` (those run on Ubuntu in the CI), then `test-release-scripts.ps1`. |
| files, windows: Publish the app | `dotnet publish` with the `win-x64` profile and this version. |
| files, windows: Build LAME | `build-lame.ps1`: downloads `lame-3.100.tar.gz`, checks its SHA-256 before extracting it, compiles with `nmake` and `Makefile.MSVC`, then `check-lame-dll.ps1`: x64, only Windows DLLs needed, every export of the DLL of the package, version 3.100. |
| files, windows: Replace the DLL in the published folder | The compiled DLL takes the place of the package's. |
| files, windows: Prove the encoding | `prove-encoding.ps1`: the compiled DLL in the tests' output folder, then 6 test cases with `--no-build` that write a real MP3 through NAudio.Lame and read it back. One decodes the whole file and compares its level with the sine that went in. |
| files, windows: Build the Setup | `get-iscc.ps1`, then ISCC on `installer/Hindsound.iss`. |
| files, windows: Check the Setup | `check-setup.ps1`: silent installation in a temporary folder of the runner; exe at the file version of the tag, DLL with the hash of the compiled one, `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses\*.txt` identical to the repo; the installed exe draws its notifications (`--dump-notifications`), exit code 0 and BMP files; the installed exe writes one second of sine as an MP3 through the path of a clip (`--prove-mp3`), exit code 0, one MP3 and no WAV; silent uninstallation. |
| files, windows: Write the update manifest | `write-manifest.ps1`: `hindsound-update-win-x64.json`, read by the installed app. |
| files, windows: Make the portable zip | `build-portable-zip.ps1`, from the folder the Setup was built from: the files the Setup installs, plus `packaging/windows/portable.txt` and `README.txt`, in one folder named like the zip. The zip is read back, entry by entry. |
| files, linux | On ubuntu-22.04: the Linux app published, `build-lame-linux.sh`, then `packaging/linux/build-packages.sh` and `check-packages.sh`, which start both packages once (`--prove-mp3`), then `packaging/linux/build-deb.sh`, twice to the same bytes, and `check-deb.sh` (fields, files, what the programs link, lintian). Then the verified `lame-3.100.tar.gz`, and `fetch-runtime-sources.sh` for the source of the AppImage runtime and of the libraries linked into it, go into the artifact next to them. |
| files, macos | On macos-26: `tools/mac/build-natives.sh` builds SDL3, FreeType, HarfBuzz and LAME from their pinned archives (no cache), `fetch-fonts.sh` the fonts, then `packaging/mac/build-app.sh` publishes the app for `osx-arm64` and makes `Hindsound.app` with its dylibs, fonts and licences, and `sign-app.sh` signs it ad hoc (`sign-mac` signs it again by the certificate above). `ditto -c -k --keepParent` makes `Hindsound-X.Y.Z-macos-arm64.zip`, and `packaging/mac/check-zip.sh` unzips it elsewhere: Hindsound.app alone, its signature (`codesign --verify --deep --strict`), nothing linked from Homebrew (`otool -L`), then the app started with `--prove-mp3` and `--dump-settings` (84 pages), each with the dylibs of the bundle. Last, the rehearsal of the certificate: `resign-zip.sh` refuses a throwaway `.p12` for another SHA-1, signs with it for its own, and `check-zip.sh` checks that zip once the `.p12` is deleted. |
| files, deb | In clean `ubuntu:22.04`, `ubuntu:24.04`, `debian:12` and `debian:13` containers, `packaging/linux/try-deb.sh`: `apt-get install` of the .deb with its Depends alone, `/usr/bin/hindsound --prove-mp3` and `--dump-settings` with the libraries and fonts of `/usr/lib/hindsound`, then `apt-get remove`, and nothing of the package left in `/usr`. The release job waits for it, as for every job of `release-files.yml`. |
| files, assemble | `assemble-release.ps1`: the files of the three jobs, the same LAME source from Windows and Linux, the source of the AppImage runtime from the Linux job, the zip from the macOS job, `write-announce-manifest.ps1` for `hindsound-update-win-x64-portable.json`, `hindsound-update-linux-x64.json` and `hindsound-update-macos-arm64.json` (the page of the release, nothing to download), and one `SHA256SUMS` for every file. Then `check-release-files.ps1`, and the artifact `release-files`. |
| sign-mac | On macos-26, in the environment `mac-signing`. With `packaging/mac/signing-certificate.sha1`: the secrets required, `resign-zip.sh` compares the SHA-1 of the `.p12` to that file, signs the zip of the job `macos` again and makes it again, the `.p12` deleted at the end of the step; then, in a step without secret, `check-zip.sh` starts the app and requires that certificate. Without the file: the zip signed ad hoc, a warning, and the same check. |
| assemble-signed | `assemble-release.ps1` and `check-release-files.ps1` again, with the zip of `sign-mac`: the files the job `release` sends. |
| release: Check the files of the release | `check-release-files.ps1` on the artifact, before anything goes up. |
| release: Create the draft release | A draft, with the notes. |
| release: Attach the files | The fifteen files, each named. Then they are downloaded back, and `check-release-files.ps1` wants exactly these fifteen, the .deb a Debian package, the Mac zip Hindsound.app alone with its program, dylibs and signature, the manifests right, `SHA256SUMS` right, the SHA-256 of the LAME source, the SHA-256 of `fuse-3.15.0.tar.xz` that `fetch-runtime-sources.sh` pins, in `type2-runtime-20251108-libraries-source.tar.gz` exactly the files of `LIBRARY_FILES` in that script, each with its SHA-256 there, and each file with the size and SHA-256 of the one sent. |
| release: Publish the release | `check-remote-tag.ps1`: the tag on GitHub must still point at the commit built. Then the draft goes public, marked as the latest release (`--latest`): the app reads that one, and sees the new version from then on. |

The files of a release: `Hindsound-X.Y.Z-Setup.exe`, `Hindsound-X.Y.Z-win-x64-portable.zip`,
`hindsound-X.Y.Z-linux-x64.AppImage`, `hindsound-X.Y.Z-linux-x64.tar.gz`, `hindsound_X.Y.Z_amd64.deb`,
`Hindsound-X.Y.Z-macos-arm64.zip`, `hindsound-update-win-x64.json`, `hindsound-update-win-x64-portable.json`,
`hindsound-update-linux-x64.json`, `hindsound-update-macos-arm64.json`, `lame-3.100.tar.gz`, `fuse-3.15.0.tar.xz`,
`type2-runtime-20251108-source.tar.gz`, `type2-runtime-20251108-libraries-source.tar.gz` and `SHA256SUMS`.

`ci.yml` calls `release-files.yml` too, on every pull request and push to `main`, with the version of the latest release in
`CHANGELOG.md`: the same files are built and checked, and kept as the artifacts `hindsound-windows-x64`,
`hindsound-linux-x64`, `hindsound-macos-arm64` and `release-files` of the run. That workflow has no token that could write: only `release.yml`
talks to the Release.

`lame.yml` runs only the compilation, its checks and the proof of the encoding. It publishes nothing, on a manual start or on a
push to `main` that changes `tools/release/build-lame.ps1`, `check-lame-dll.ps1`, `lame-exports.txt`, `prove-encoding.ps1` or
`lame.yml` itself.

`build-lame-linux.sh WORK_DIR OUT_DIR` is the Linux counterpart of `build-lame.ps1`: the same archive and SHA-256, the
archive's own `configure`, and it leaves `libmp3lame.so.0` and a `lame` command (for the tests only) in `OUT_DIR`.
`core-on-linux` in `ci.yml` runs it for the MP3 tests of the Linux app, and the `linux` job of `release-files.yml` for the
library the AppImage and the tar.gz ship; that job puts the verified `lame-3.100.tar.gz` in its artifact next to them.

`fetch-runtime-sources.sh WORK_DIR OUT_DIR` puts the source of the AppImage runtime in `OUT_DIR`, for the LGPL-2.1 of the
libfuse it is statically linked with (see "libfuse and the LGPL-2.1" in `THIRD-PARTY-NOTICES.md`): `fuse-3.15.0.tar.xz`,
downloaded and refused unless it has the pinned SHA-256, and `type2-runtime-20251108-source.tar.gz`, made by `git archive`
from the one commit of the tag `20251108`, fetched and refused unless `git rev-parse` reads that commit, and refused unless
it holds `LICENSE`, `src/runtime/runtime.c` and `patches/libfuse/mount.c.diff`. The third file,
`type2-runtime-20251108-libraries-source.tar.gz`, holds the source of the other libraries the runtime's `Makefile` links:
`squashfuse-0.5.2.tar.gz`, refused unless it has the SHA-256 of `install-dependencies.sh`; for each Alpine 3.21 package
(`main/musl`, `main/zstd`, `main/zlib`, `community/mimalloc2`), its upstream archive, refused unless it has the SHA-512
of its `APKBUILD`, and its folder of aports at a pinned commit, each file refused unless it has its pinned SHA-256, the
`APKBUILD` refused unless it has the pinned version and release and its `sha512sums` name exactly the files of the folder;
and a `README.txt` it writes. The archive is the same bytes for the same files: names sorted, the date of the runtime's
commit, owner 0, mode 0644, `gzip -n`. A run on Windows under Git Bash on 2026-11-12 wrote it twice with the same SHA-256.
Its pins are the only copy: `check-release-files.ps1` reads the SHA-256 of libfuse and `LIBRARY_FILES` from it. The
`linux` job of `release-files.yml` runs it into its
artifact, so every release and every run of the CI hands out these three files with the AppImage.

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

A tag pushed by mistake, such as `v9.0.0`, is refused, but it stays on GitHub, and every later version is refused against it
too. A higher tag on a branch other than `main` does the same. If it has no published release, delete it:

```powershell
git push origin :refs/tags/v9.0.0
git tag -d v9.0.0
```

## Rerun a failed run

```powershell
gh run list --workflow release.yml --limit 5
gh run rerun <run id> --failed
```

Nothing is left to clean up before this. The tag no longer counts against itself, a draft left by the failed run is reused, and
its files are replaced. A release already published is refused: an immutable release cannot change, so go to the next version.

A rerun builds the commit of the original event, even if the tag has moved since. If the fix is in the code or in a workflow, the tag must move, which is possible only if the
release was never published:

```powershell
# Deletes the release only if it is a draft, and prints whether it was one.
$isDraft = gh release view v0.1.0 --json isDraft --jq .isDraft
$isDraft
if ($isDraft -eq 'true') { gh release delete v0.1.0 --yes }
```

If this printed `false`, stop: the release is public, go to the next version. Otherwise:

```powershell
git push origin :refs/tags/v0.1.0
git tag -d v0.1.0
# Commit the fix on main and push it, then make the tag again as above.
```

Pushing the tag again starts a new run, from the fix. Never rerun a run started before the tag moved: it built the old commit,
and its last step refuses to publish it, as `check-remote-tag.ps1` sees the tag elsewhere on GitHub.

## Test the scripts

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\test-release-scripts.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\mutate-release-guards.ps1
```

`test-release-scripts.ps1` compiles nothing, downloads nothing and installs nothing: `build-lame.ps1` gets a fake compiler, and
`check-setup.ps1` a fake folder. It runs in CI with `pwsh`. It needs the NAudio.Lame package, so run a restore first.
`mutate-release-guards.ps1` undoes the guards listed in it, one at a time, on a copy, and fails if the tests do not notice.
It takes about 25 to 45 s per entry, depending on the load of the PC, so the whole list takes about 45 min: run it in parts of
14 entries (6 to 12 min each) with `-From` and `-To`.

Not covered by these tests, because they need a real run on a runner: in `check-setup.ps1`, the exit codes of the Setup, of
the installed exe and of the uninstaller, the BMP files, and the exe left after the uninstallation; in `prove-encoding.ps1`,
the exit code of `dotnet test` and the DLL replaced during the proof. After a change to one of these scripts, read them in the
log of the next release.

**Never start `check-setup.ps1` without `-CheckFolder` on a PC in use, or a release Setup at all.** The script refuses anywhere but
a GitHub-hosted runner, because the uninstaller of a release build deletes the real log and the real Run value.

`lame-exports.txt` lists what NAudio.Lame 2.1.0 imports. After an update of that package, read it again from the `[DllImport]`
attributes of `NAudio.Lame.x64.LameDLLWrap.dll`, a resource inside `NAudio.Lame.dll`.
