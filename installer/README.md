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
dotnet publish src/Hindsound/Hindsound.csproj -p:PublishProfile=win-x64 "-p:PublishDir=$publish\" "-p:Version=$version" -m:2

# 3. The Setup
& "$tools\inno\tools\ISCC.exe" /Q "/DMyAppVersion=$version" "/DPublishDir=$publish" "/O$env:TEMP\hindsound-setup" installer\Hindsound.iss
```

Measured: exe 17.66 MB, DLL 1.03 MB, Setup 8.23 MB, 7 s for step 3.

These commands make a Setup with the DLL of the NAudio.Lame package. A release does not: the workflow compiles the DLL from the
LAME source and puts it in the published folder before step 3. See [tools/release/README.md](../tools/release/README.md).

`LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses\*.txt` are taken from the root of the repo and installed next to the exe,
under the same names and with the `licenses` folder, because the notices link to them. If one is missing, ISCC fails.

`src/Hindsound/Assets/icon.ico` holds 16, 20, 24, 32, 48, 64 and 128 px as 32-bit BMP, and 256 px as PNG, from the final logo.

## How the app uses it

```
Hindsound-X.Y.Z-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /WAITEXIT=330 [/RESTARTAPP] [/LOG=<file>]
```

The app starts this, then leaves.

| Switch | What it does |
| --- | --- |
| `/WAITEXIT=N` | Waits up to N seconds for the mutex `Local\Hindsound.SingleInstance` to go. Without it, a silent Setup started while the app runs gives up in 0.6 s, exit code 1. |
| `/RESTARTAPP` | Starts the app at the end. If the installation failed, starts the version in place with `--update-failed` on its command line. Without it (the app was left with Quit), nothing starts, even after a failure. |

| Setup exit code | Seen when |
| --- | --- |
| 0 | Installed. |
| 1 | The app was still running at the end of the wait. Nothing changed and nothing starts, because the app is there. |
| 5 | A file was locked. After 4 retries, Setup aborts and rolls back. |

**N must outlast the slowest exit of the app.** That is 305 s: 5 min for a clip being written, then 5 s for the engine. The
app has already decided to leave when Setup waits, so if Setup gives up first (exit code 1), the app leaves a little later
and nothing is left to start it again: no app and no update until the next Windows session, through the Run value if the
user has it. Measured with `/WAITEXIT=60` and an app leaving after 70 s. Hence 330.

**The exe is the last file written, and that order matters.** A rollback removes new files but does not bring back the ones
it replaced: with the DLL locked and the exe written first, the exe stayed new while the Uninstall key stayed old. With the
exe last, every failure tried leaves the old exe. The DLL may already be the new one then, so a release must keep the old
exe working with the new DLL.

Setup cannot help if it never runs its code: blocked by an antivirus, or killed. Then nothing starts the app again before
the next Windows session, through the Run value if the user has it.

## Uninstalling

- Refused while the app runs: exit code 1, nothing changed, the app keeps running.
- Removes the files, the shortcut, the Uninstall key, the `Hindsound` value of the Run key, and from
  `%LocalAppData%\Hindsound` the log and the `updates` folder, then that folder too if nothing else is in it.
- Keeps the clips and `Rescued`.
- Asks whether to delete the settings, no by default. A silent uninstallation asks nothing and keeps them. The answer yes
  deletes `settings.json*` from `%AppData%\Hindsound`, by name, because the clips folder can be set to any folder, this one
  included. The folder goes only if nothing else is in it.

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

The app of a trial comes from a scratch copy of `src/` (`Hindsound/` references `Hindsound.Core/` next to it) where
`Hindsound/App/Program.cs` differs in two places. First,
`MutexNameFor` is made to return `@"Local\HindsoundTrial.SingleInstance"` whatever the options say: since 08/11/2026 any
run with `--settings`, which every start of a trial has, gets a hashed name of its own and never looks at `MutexName`, so
changing that constant has no effect. Second, `options = options.SettingsPath is null ? options with { SettingsPath = @"<TrialRoot>\settings.json" } : options;`
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

The real mutex name needs one more trial: exes from a scratch copy where `MutexNameFor` returns `MutexName`, the real
name, whatever the options say (the code as it is cannot hold that name under `--settings`, which `/DTrialRoot` always
passes), and a copy of `Hindsound.iss` in this folder with `Local\HindsoundTrial.SingleInstance` replaced by
`Local\Hindsound.SingleInstance`, still with `/DTrialRoot`. Wait until no other Hindsound holds the mutex:
`[Threading.Mutex]::OpenExisting('Local\Hindsound.SingleInstance')` must throw.

The two trials above were rewritten on 08/11/2026 to match the mutex code of that day and have not been run since. The
results below date from 06/11/2026, before `MutexNameFor` existed, when the trial copy only had to change the constant.

Results of 06/11/2026:

| Case | Command | Result |
| --- | --- | --- |
| First installation | `1.0.0 Setup` | Exit 0 in 2 s. Exe, DLL, `LICENSE.txt`, uninstaller, Uninstall key, shortcut without arguments. No app started. |
| Default folder, not elevated | `1.0.0 Setup` without `/DIR` | Exit 0. `%LocalAppData%\Programs\Hindsound`, no prompt. The manifest of the Setup says `asInvoker`. |
| Update, app running | `1.0.1 Setup /WAITEXIT=60 /RESTARTAPP` | Exit 0 in 6.5 to 7.5 s. Waited 4.2 to 5.4 s. 1.0.1 runs, with `--settings` and `--offscreen`, mic off. |
| Exe locked | same | Exit 5 in 8 to 9.4 s. 1.0.0 runs again with `--update-failed`, mic off. Exe hash and Uninstall key still 1.0.0, no stray file. One app process and no Setup over the next 20 s. |
| DLL locked | same | Same result. |
| The next attempt | same, lock gone | Exit 0. 1.0.1 runs. |
| Quit, install works | `1.0.1 Setup /WAITEXIT=60` | Exit 0. 1.0.1 on disk. Nothing started. |
| Quit, exe locked | same | Exit 5. 1.0.0 on disk. Nothing started. |
| App outlives the wait | `1.0.1 Setup /WAITEXIT=3 /RESTARTAPP` | Exit 1 in 3.8 s. Same process alive, no second one. |
| App leaves after the wait | `1.0.1 Setup /WAITEXIT=60 /RESTARTAPP`, app leaves after 70 s | Exit 1 in 62 s. The app leaves at 69 s. 15 s later no process runs, and 1.0.0 is still on disk. This is why N is 330. |
| Slowest exit | `1.0.1 Setup /WAITEXIT=330 /RESTARTAPP`, app leaves after 300 s | Exit 0 in 299 s. Waited 286 s. 1.0.1 runs, mic off. |
| Real mutex name | `1.0.1 Setup /WAITEXIT=330 /RESTARTAPP`, exes holding the real name | The running exe holds the mutex. Uninstall refused, exit 1. `/WAITEXIT=2`: exit 1, app alive. Then exit 0 in 9.4 s, waited 7.6 s, 1.0.1 runs, mic off. |
| No wait | `1.0.1 Setup` | Exit 1 in 0.6 s. App untouched. |
| First installation fails | `1.0.1 Setup /RESTARTAPP`, a locked `Hindsound.exe` in the folder | Exit 5. No Uninstall key, no shortcut, DLL and licence removed. Nothing started. |
| Uninstall, app running | `unins000.exe` | Exit 1. Nothing changed. |
| Uninstall, app closed | `unins000.exe` | Exit 0, after a failed update too. Files, key and shortcut gone; log, `updates` and the Run value gone, another Run value kept; `Rescued` and the settings kept. Without `Rescued`, `%LocalAppData%\Hindsound` goes too. |
| Delete the settings | `unins000.exe`, Setup built with `/DTrialDeleteSettings=1` | `settings.json`, its `.bak` and `.tmp` files gone, the folder too. `Rescued` kept. |
| Delete the settings, clips in that folder | same | The settings files gone. The clips and the folder kept. Deleting the whole folder lost them. |

These trials ran when the only licence file was `LICENSE.txt`. The present list of licence files was checked in the output
of ISCC, not by a new installation.

Not tried: anything with a window, so the wizard, its launch box and the question about the settings; the uninstaller and
the Setup of a release build, which would touch the real log and Run value; a real clip being written while Setup waits:
`--exit-after` stands in for it, and Setup only sees the mutex.
