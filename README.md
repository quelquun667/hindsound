<!-- DRAFT: to finish once the app is stable. Every "TO CONFIRM" comment below is something not yet proven on a real install. -->

<p align="center">
  <img src="assets/logo.png" alt="Hindsound" width="160">
</p>

<h1 align="center">Hindsound</h1>

<p align="center"><em>Hit record after it happens.</em></p>

<!-- TO ADD when the CI workflow exists: the build badge. -->

**Status:** early development, nothing to download yet.

## What it does

Hindsound runs in the background and keeps the last few minutes of your PC audio and microphone in memory, and a hotkey saves that past to a file you can paste into Discord.

- **It keeps the past.** The last 5:00 by default, in RAM. No audio is written to disk until you press the hotkey.
- **One hotkey saves it.** The last 30 seconds by default, as an MP3 with the volume evened out.
- **It's ready to paste.** The file goes to the clipboard, so `Ctrl+V` puts it in a Discord message.

<!-- IMAGE TO ADD: screenshot of the notification, "Clip saved (0:30)" and "Copied. Paste [Ctrl][V]". -->

## How it works

1. Start Hindsound. It sits in the notification area of the taskbar. At every launch, a notification says `Capture on` and how much it keeps.
2. Something happens that you want to keep. Press `Ctrl+Shift+F10`.
3. A notification says `Clip saved (0:30)` and `Copied. Paste [Ctrl][V]`.
4. In Discord, click the message box, press `Ctrl+V`, and send.

<!-- TO CONFIRM: pasting the MP3 into the real Discord app, after the copy to the clipboard. -->

Good to know:

- The file stays in `Music\Hindsound`, with a name like `Hindsound_Discord_2026-11-06_21-14-03.mp3`.
- If Hindsound started 12 seconds ago, you get those 12 seconds: `Clip saved (0:12 of 0:30)`.
- The notification does not take the focus. In exclusive fullscreen, you hear a short sound instead, and the notification shows when you are back on the desktop.
- If the folder can't be written, the clip goes to `%LOCALAPPDATA%\Hindsound\Rescued`, and the notification says so. If the MP3 encoder fails, the clip is saved as WAV, and the notification says that too.
- A left click on the icon opens the settings. A right click opens a menu: save the last 15s, 30s, 1 min or 5 min, `Copy last clip`, `Pause capture`, `Open clips folder`, `Settings`, `Quit`.
- The first launch walks you through three steps: `Pick your hotkey`, `Check your mic`, `Try it`.

<!-- TO CONFIRM: the notification and the focus on real games (DX9 and DX11 in exclusive fullscreen, optimized fullscreen, borderless). -->

## Install

There is nothing to download yet. Once a version is out:

<!-- TO CONFIRM when the installer and the first release exist: everything in this section. The steps come from the installer tests, not from a published Setup. -->

1. Download the Setup from the [Releases page](https://github.com/quelquun667/hindsound/releases/latest) of this repository. It is the only program published.
2. Run it. You will probably see the SmartScreen screen explained below.
3. Follow the Setup. It installs for your Windows user only, without administrator rights, and .NET comes with it.

You need Windows 10 version 2004 or later, 64-bit.

<!-- TO CONFIRM: only Windows 11 has been tried so far. Windows 10 2004 is what the capture code asks for. -->

### "Windows protected your PC"

Windows will probably stop the Setup with a screen titled **Windows protected your PC**. Here is why, and what to do.

Hindsound's versions are not signed: no certificate tells Windows who published the file. So Windows warns you. This screen does not mean a virus was found, and it does not prove the file is safe either.

1. Make sure the Setup comes from this repository's Releases page.
2. Click **More info**.
3. Click **Run anyway**.

This screen comes back for each new version.

<!-- TO CONFIRM with the first two releases: whether this screen also shows when the app installs an update by itself. -->

## Uninstall

Quit Hindsound first: the uninstaller refuses while it runs. Then go to Windows Settings > Apps, find Hindsound, and uninstall it.

<!-- TO CONFIRM when the installer exists: this whole section. -->

| | What happens |
|---|---|
| Program files, shortcut, the Start with Windows entry, log, downloaded updates | Removed |
| Your clips (`Music\Hindsound`, or the folder you chose) | Kept |
| Rescued clips (`%LOCALAPPDATA%\Hindsound\Rescued`) | Kept |
| Settings (`%AppData%\Hindsound\settings.json`) | The uninstaller asks. The default answer keeps them. |

## Default hotkeys

| Action | Default |
|---|---|
| Save clip (the length set in Durations) | `Ctrl+Shift+F10` |
| Save last 15s | None |
| Save last 1 min | None |
| Save last 5 min | None |
| Pause or resume | None |
| Copy last clip | None |

You can change them all in Settings > Hotkeys. If another app already holds a combination, Hindsound tells you.

## Settings

A left click on the icon opens them. They are saved at once, in `%AppData%\Hindsound\settings.json`.

<!-- IMAGE TO ADD: screenshot of the settings window. -->

| Page | Setting | Choices (default in bold) |
|---|---|---|
| Durations | Clip length | 15s, **30s**, 1 min, 2 min, 5 min, Custom (up to 30:00) |
| Durations | Keep in memory | From 1:00, **5:00** by default. The page shows the RAM it takes. |
| Audio | Record | **Whole PC**, or one application |
| Audio | Output | **Default output**, or one device |
| Audio | Microphone | **Default microphone**, one device, or Off |
| Audio | Tracks | **Mix** (one file), or Separate (PC sound and microphone apart) |
| Audio | PC sound gain, Mic gain | Sliders in dB, **0 dB** |
| Export | Format | WAV, **MP3** |
| Export | MP3 quality | 128, **192**, 320 kbps |
| Export | Volume | Off, Peak, **Loudness** (-16 LUFS) |
| Export | Folder, File name | **`Music\Hindsound`**, **`Hindsound_{app}_{date}_{time}`** |
| Export | Copy clip to clipboard | **On** |
| General | Accent color | **Lime**, Mint, Cyan, Violet, Pink, White, Custom |
| General | Start with Windows | **Off**. The first launch offers it. |
| General | Save notifications | **On** |
| General | Notification position | **Right edge**, Left edge |
| General | In fullscreen games | **Auto**, Sound only |
| General | Confirmation sound | **When no overlay can show**, Always, Never. It can be heard in your next clip if you capture a specific output. |
| General | Updates | **Install automatically**, Ask me, Off. See [Updates](#updates). |

### Record one application

`Record` can follow one application instead of the whole PC: only Discord, or only your game. The list shows the applications that have an audio session.

- Hindsound remembers the application by the name of its exe.
- If the application closes, the buffer gets silence, the window says `Not running: Discord`, and the capture attaches again when the application comes back.
- `Output` is greyed out in this mode: the capture does not depend on an output.

## Updates

<!-- TO CONFIRM when the update checker is in the app: this whole section, and the Updates row in the table above. -->

| Mode | What it does |
|---|---|
| `Install automatically` (default) | Checks once a day and downloads a new version. It installs when you quit or at the next launch, never while the app runs. |
| `Ask me` | You decide when to update. |
| `Off` | No network at all. |

**What GitHub sees.** Once a day, Hindsound asks GitHub for one small file that describes the latest version. GitHub sees your IP address and the time. The app sends no identifier and no version number. `Off` cuts all of it. The first launch tells you about this check.

- Hindsound does not check, download or announce during a fullscreen game, a presentation, or while a clip is being written. It tries again 30 minutes later.
- An update restarts Hindsound, and the audio in memory is lost. `Update now` warns you first, and offers `Save it, then update`.
- The size and SHA-256 of each download are checked. That catches a damaged file. It does not prove who published it, since the versions are not signed.

<!-- TO CONFIRM: what the user sees after a failed update. It has not been tried yet. -->

## Memory

Hindsound keeps audio in RAM, so its memory follows the length you keep. The audio takes 192 KB/s for the PC sound and 96 KB/s for the microphone. For 5:00, that is 58 MB without the microphone and 86 MB with it. Settings > Durations shows the estimate as you move the slider.

The whole app, measured on one PC with 5:00 kept:

| Case | Memory |
|---|---|
| Mic off, private memory from the start of the capture | 62.5 MB |
| Mic off, working set after 3 s | 32 MB |
| Mic off, working set after 123 s | 53.8 MB |
| Mic off, working set with the buffer full | About 85 MB (extrapolated, not measured) |
| Mic on, private memory | 94 MB |

Saving takes more, only while the clip is written, with both sources on: 17 MB for 30 seconds, 173 MB for 5:00.

<!-- TO CONFIRM: measure again on the stable app, with the update checker and the settings window. -->

## Build from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/quelquun667/hindsound.git
cd hindsound
dotnet test Hindsound.slnx -m:2 --filter "Category!=Device"
dotnet publish src/Hindsound/Hindsound.csproj -p:PublishProfile=win-x64 -m:2
```

- The code is in `src/Hindsound`, and the tests in `tests/Hindsound.Tests`.
- The filter leaves out the tests that need real audio devices.
- The result lands in `src/Hindsound/bin/publish/win-x64/`.
- A build from source never updates itself.

<!-- TO CONFIRM after the move to src/ and tests/: these commands and paths, and that Hindsound.slnx stays at the root. To add when the installer exists: how to build the Setup. -->

## Platforms

Windows only for now. Mac and Linux versions are planned, after the Windows version. There is no date yet.

## Recording other people

Recording a call or a voice chat without telling the other people can be illegal where you live. Tell them first.

The `Capture on` notification at each launch reminds you, not the people in the call. Telling the group is up to you.

### Your game

Hindsound does not touch your game. It injects nothing, sets no hook, and opens no handle to the game. The `{app}` part of a file name comes only from the title of the active window.

This is not a promise about any anti-cheat: no anti-cheat publisher guarantees it.

## License

[MIT](LICENSE). Hindsound uses NAudio, NAudio.Lame and System.Numerics.Tensors (MIT), the .NET runtime, and LAME (LGPL) for MP3. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

<!-- TO CONFIRM before the first release: THIRD-PARTY-NOTICES.md exists and matches this list. -->
