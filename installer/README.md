# The Windows installer

`Hindsound.iss` builds `Hindsound-X.Y.Z-Setup.exe`: a per-user Inno Setup installer, no administrator rights, with a Start
menu shortcut and no folder page. The app goes to `%LocalAppData%\Programs\Hindsound`.

Last checked: 06/11/2026, on Windows 11, with Inno Setup 7.1.0 and the .NET SDK 10.0.403.

## Build it

From the root of the repo, in PowerShell. Nothing is installed on the machine.

```powershell
# 1. ISCC, from the Tools.InnoSetup package, version and hash pinned
$tools = "$env:TEMP\hindsound-inno"
New-Item -ItemType Directory -Force $tools | Out-Null
Invoke-WebRequest https://api.nuget.org/v3-flatcontainer/tools.innosetup/7.1.0/tools.innosetup.7.1.0.nupkg -OutFile "$tools\inno.zip" -UseBasicParsing
if ((Get-FileHash "$tools\inno.zip" -Algorithm SHA256).Hash -ne 'AAD15C662593C6F5656457A5C001F4591BABD72820CACF63F9706D4AF5D93B75') { throw 'Unexpected Inno Setup package' }
Expand-Archive "$tools\inno.zip" "$tools\inno" -Force

# 2. The app: Hindsound.exe and libmp3lame.64.dll next to it
$version = '0.1.0'
$publish = "$env:TEMP\hindsound-publish"
dotnet publish Hindsound/Hindsound.csproj -p:PublishProfile=win-x64 "-p:PublishDir=$publish\" "-p:Version=$version" -m:2

# 3. The Setup
& "$tools\inno\tools\ISCC.exe" /Q "/DMyAppVersion=$version" "/DPublishDir=$publish" "/O$env:TEMP\hindsound-setup" installer\Hindsound.iss
```

Measured: exe 17.66 MB, DLL 1.03 MB, Setup 8.23 MB, 7 s for step 3.

`LICENSE` and `THIRD-PARTY-NOTICES.md` are taken from the root of the repo, each only if it exists.

`Hindsound/Assets/icon.ico` holds 16, 20, 24, 32, 48, 64 and 128 px as 32-bit BMP, and 256 px as PNG, from the final logo.

## How the app uses it

```
Hindsound-X.Y.Z-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /WAITEXIT=60 [/RESTARTAPP] [/LOG=<file>]
```

The app starts this, then leaves.

| Switch | What it does |
| --- | --- |
| `/WAITEXIT=N` | Waits up to N seconds for the mutex `Local\Hindsound.SingleInstance` to go. Without it, a silent Setup started while the app runs gives up in 0.6 s, exit code 1. |
| `/RESTARTAPP` | Starts the app at the end. If the installation failed, starts the version in place with `--update-failed` on its command line. Without it (the app was left with Quit), nothing starts, even after a failure. |

| Setup exit code | Seen when |
| --- | --- |
| 0 | Installed. |
| 1 | The app was still running at the end of the wait. Nothing changed, the app keeps running. |
| 5 | A file was locked. After 4 retries, Setup aborts and rolls back. |

**The exe is the last file written, and that order matters.** A rollback removes new files but does not bring back the ones
it replaced: with the DLL locked and the exe written first, the exe stayed new while the Uninstall key stayed old. With the
exe last, every failure tried leaves the old exe. The DLL may already be the new one then, so a release must keep the old
exe working with the new DLL.

Setup cannot help if it never runs its code: blocked by an antivirus, or killed. Then nothing starts the app again before
the next Windows session, through the Run value if the user has it.

## Uninstalling

- Refused while the app runs: exit code 1, nothing changed, the app keeps running.
- Removes the files, the shortcut, the Uninstall key, the `Hindsound` value of the Run key, and from
  `%LocalAppData%\Hindsound` the log and the `updates` folder.
- Keeps the clips and `Rescued`.
- Asks whether to delete `%AppData%\Hindsound`, no by default. A silent uninstallation asks nothing and keeps it.

## Trials

**Never run the uninstaller of a normal build on a working PC.** It deletes the real log and the real Run value.

`/DTrialRoot=<folder>` builds a Setup for trials and is never passed for a release:

| Same as a release, except | With `TrialRoot` |
| --- | --- |
| Mutex | `Local\HindsoundTrial.SingleInstance` |
| Arguments of every start | `--settings <TrialRoot>\settings.json --offscreen --exit-after 90` |
| `%LocalAppData%\Hindsound` | `<TrialRoot>\local` |
| `%AppData%\Hindsound` | `<TrialRoot>\roaming` |
| Run key | `HKCU\Software\Hindsound-InstallerTrial\Run` |

To check that a release has none of it, save the preprocessed script: add `#expr SaveToFile("<file>")` at the end of a copy
of `Hindsound.iss` in this folder, and compile it with `ISCC /O-`. `--settings` must not appear in it.

The app of a trial comes from a scratch copy of `Hindsound/` where `App/Program.cs` differs in two lines: the same mutex
name, and `options = options.SettingsPath is null ? options with { SettingsPath = @"<TrialRoot>\settings.json" } : options;`
after `AppOptions.Parse`. So it can never start on the real settings, and no other Hindsound on the PC can disturb it.
Publish it twice, with `-p:Version=1.0.0` and `-p:Version=1.0.1`, and compile each with:

```powershell
& $iscc /Q "/DMyAppVersion=1.0.0" "/DPublishDir=$publish100" "/DTrialRoot=$trial" "/O$out" installer\Hindsound.iss
```

`<TrialRoot>\settings.json`, written before any start (the mic stays closed, nothing reaches the clipboard):

```json
{"hotkeys":{"saveDefault":"Ctrl+Alt+Shift+F24"},"audio":{"micDeviceId":"disabled"},"export":{"outputFolder":"<TrialRoot>\\clips","copyToClipboard":false},"general":{"startWithWindows":false,"confirmSound":"never"}}
```

Two seconds after each start, `<TrialRoot>\settings.log` must say `mic enabled False`. Every Setup below also gets
`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=<TrialRoot>\install /LOG=<file>`. To lock a file, another process keeps it
open: `[IO.File]::Open($path, 'Open', 'Read', 'Read')`. The running app leaves by itself: `--exit-after 8`.

Results of 06/11/2026:

| Case | Command | Result |
| --- | --- | --- |
| First installation | `1.0.0 Setup` | Exit 0 in 2 s. Exe, DLL, `LICENSE.txt`, uninstaller, Uninstall key, shortcut without arguments. No app started. |
| Update, app running | `1.0.1 Setup /WAITEXIT=60 /RESTARTAPP` | Exit 0 in 6.5 s. Waited 4.2 s. 1.0.1 runs, with `--settings` and `--offscreen`, mic off. |
| Exe locked | same | Exit 5 in 8 s. 1.0.0 runs again with `--update-failed`, mic off. Exe hash and Uninstall key still 1.0.0. |
| DLL locked | same | Same result. |
| The next attempt | same, lock gone | Exit 0. 1.0.1 runs. |
| Quit, install works | `1.0.1 Setup /WAITEXIT=60` | Exit 0. 1.0.1 on disk. Nothing started. |
| Quit, exe locked | same | Exit 5. 1.0.0 on disk. Nothing started. |
| App outlives the wait | `1.0.1 Setup /WAITEXIT=3 /RESTARTAPP` | Exit 1 in 3.8 s. Same process alive, no second one. |
| No wait | `1.0.1 Setup` | Exit 1 in 0.6 s. App untouched. |
| First installation fails | `1.0.1 Setup /RESTARTAPP`, a locked `Hindsound.exe` in the folder | Exit 5. No Uninstall key, no shortcut, DLL and licence removed. Nothing started. |
| Uninstall, app running | `unins000.exe` | Exit 1. Nothing changed. |
| Uninstall, app closed | `unins000.exe` | Exit 0, after a failed update too. Files, key and shortcut gone; log, `updates` and the Run value gone, another Run value kept; `Rescued` and the settings kept. |
| Delete the settings | `unins000.exe`, Setup built with `/DTrialDeleteSettings=1` | Settings folder gone, `Rescued` kept. |

Not tried: anything with a window, so the wizard, its launch box and the question about the settings; the uninstaller and
the Setup of a release build, which would touch the real log and Run value; the real mutex name, which `Program.cs` and
`Hindsound.iss` must keep equal.
