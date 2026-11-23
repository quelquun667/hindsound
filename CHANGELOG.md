# Changelog

All notable changes to Hindsound are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- A hotkey `Save and edit`, not set at first, in Settings > Hotkeys under `Save clip`. It saves a clip as `Save clip` does, with the same length, clipboard and notification, then opens the editor on it, and the window comes to the front (in a fullscreen game, that takes you out of the game). If the editor is already open with a change or a save under way, it is not replaced: the window only comes to the front, the new clip is saved as usual, and the log says why the editor did not open. Where Hindsound has no window, the hotkey is not shown.

## [0.12.0] - 2026-11-23

Hotkeys that follow the keyboard layout on Mac, and a lost sound shown on Linux. On Windows, Hindsound works as in 0.11.0, but gives up sooner on a server that does not answer a check for a new version.

### Added

- On Linux, when the sound of the recorded application is lost, the screen says `Sound lost` with the name of the application, and the log says it too.
- On Mac, a hotkey follows the keyboard layout, as on Windows: `Ctrl+Option+A` is the key that types an A, on an AZERTY keyboard as on a QWERTY one. When the layout changes while Hindsound runs, the hotkeys move to the right keys. A hotkey whose key another app holds waits, and the `Hotkeys` page shows it as in use.
- On Mac, the log says once which permission macOS has not decided yet, and where to give it.
- The README shows a short film of Hindsound at the top.

### Changed

- On Mac, Hindsound says `Mac` where it said `PC`. Windows and Linux keep `PC`.
- On Windows, the check for a new version gives up on a server that does not answer after 2 seconds, instead of 4 to 6.
- The name of an application in the name of a clip is cut by the same rule as in the list of programs.
- On macOS, the CI now runs Hindsound and all its proofs on macOS 15 as well as on macOS 26, and also proves that the capture follows a change of the sound output while it records, and that a click on the notification of a clip opens the Library in front. It has still never run on a real Mac, nor on macOS 14.

### Fixed

- On Linux with PipeWire, an application whose output disappears while it plays is recorded again. Before, it might not be recorded at all.
- On Linux, one folder reached by two paths through a symbolic link, such as `/home` and `/var/home` on Fedora Silverblue, is no longer taken for two folders. An AppImage on its own no longer writes its entry in the list of applications again at each launch.

## [0.11.0] - 2026-11-22

A first Mac version. On Windows and Linux, Hindsound works as in 0.10.1.

### Added

- A first Mac version, for a Mac with Apple silicon on macOS 14.4 or later: `Hindsound-X.Y.Z-macos-arm64.zip` holds `Hindsound.app`. It has the same window as on Windows and Linux, and keeps the last minutes of the sound of the Mac in memory (Hindsound itself left out), or of one application and its child processes, with the microphone if your settings name one. It saves a clip on its hotkey, on `hindsound --save` or from the menu of its icon in the menu bar, says so with its own notification, as on Windows, and puts the clip on the clipboard. macOS asks for the permission to record the sound of the system, and for the microphone; without them, Hindsound records silence, and its log says which one is missing. While the releases are signed ad hoc, macOS takes each update for another app and asks again.
- On macOS, Hindsound is not signed by an Apple developer account nor notarized, so its first launch is blocked: try to open it once, then click `Open Anyway` in System Settings > Privacy & Security (see the README). It does not update itself: a notification says when a new version is out, with the link to its release. No confirmation sound plays when a clip is saved. There is no build for Intel Macs.
- On macOS, Hindsound has only been tried on the macOS machines of GitHub's CI, with a test tone played through a virtual sound device and the permissions written by the CI: never on a real Mac, with a real game, call, hotkey or prompt, and only on macOS 26.

## [0.10.1] - 2026-11-21

Small fixes on Linux. On Windows, Hindsound works as in 0.10.0.

### Fixed

- On Linux, a sound stream of the recorded application that the sound server ends now and then, such as a call that lasts for hours, is recorded again each time, as long as it had been recorded for half a minute or more before it ended. Before, after the fifth time it was no longer recorded, while the band still said `Capturing`.
- On Linux, the name of an application that holds a dot, such as `org.kde.foo` or `python3.12`, is shown whole in the `Record` list, the band and the wave. Before, what came after its last dot was cut, as for the `.exe` of a Windows program.
- On Linux, when `$XDG_DATA_DIRS` lists `~/.local/share`, an AppImage keeps its entry in the list of applications instead of removing it and writing it again at each launch.

## [0.10.0] - 2026-11-21

Record one application on Linux, as on Windows. On Windows, Hindsound works as in 0.9.0.

### Added

- On Linux, `Record` can follow one application and its child processes instead of the whole PC, as on Windows, through PulseAudio or PipeWire. The list shows the applications while they play sound, under the name an application gives its sound stream when it holds the name of its process, else under the name of the process (`Discord`, not `WEBRTC VoiceEngine`).
- On Linux with PulseAudio (Ubuntu 22.04), the first 2 seconds after you choose the application, after Hindsound starts, or after the application or the sound server starts again, may be missing from the memory. PipeWire does not have this gap.
- On Linux, the recording of one application has been tried in the CI only, with test players, not yet with real applications on a desktop; an application in a Flatpak or Snap sandbox has not been tried.

## [0.9.0] - 2026-11-20

A `.deb` package for Ubuntu and Debian, and small fixes on Linux. On Windows, Hindsound works as in 0.8.0.

### Added

- On Linux, a `.deb` package for Ubuntu 22.04 and later and Debian 12 and later: `sudo apt install ./hindsound_X.Y.Z_amd64.deb` installs Hindsound in `/usr/lib/hindsound`, with `/usr/bin/hindsound`, its entry in the list of applications and its icons, and `sudo apt remove hindsound` takes it away. Like the AppImage, it does not update itself: a notification says when a new version is out, with the link to its release.
- On Linux, once the `.deb` is installed, the next launch of Hindsound, from the list of applications or elsewhere, removes the entry that an AppImage or the tar.gz wrote in `~/.local/share/applications`, with the icons it copied that are the same as the package's: under the same name, that entry went before the one of the package, and the list kept starting the old copy. The launches after it start the `.deb`. An entry you changed or hid stays, and an AppImage started after the `.deb` is installed writes none. A tar.gz copied into `~/.local` by the steps of its `README.txt` is not removed: delete it before you install the `.deb`, as `~/.local/bin` comes before `/usr/bin` on the PATH.

### Fixed

- On Linux, the session skips the entry of `Start with the session` once its program is gone, after `sudo apt remove hindsound` or an AppImage deleted, instead of trying to start it at each login. This holds for an entry turned on from this version on.
- On Linux, the `Hotkeys` page no longer shows and copies `hindsound --save` when the path of the program has a character it cannot write in a command: nothing puts an AppImage on the PATH, so that command found no program. The page now says in words what to bind, with no `Copy` button, as the notice does.

## [0.8.0] - 2026-11-19

The window on Linux, with the Library, its tools and listening to a clip. On Windows, Hindsound works as in 0.7.0.

### Added

- On Linux, the window: `Now`, `Library` and the settings, drawn by the same pages as on Windows. The AppImage and the tar.gz ship SDL 3.2 and the fonts of the window (Selawik, Barlow Semi Condensed and Cascadia Mono); FreeType, HarfBuzz and fontconfig come from the system, and a character the fonts lack, a Japanese clip name for example, comes from a font of the system. Without one of these, Hindsound runs without the window, as before, and its log says why.
- On Linux, listen to a clip in the Library and in the editor. While a clip plays, the capture of the PC's sound records silence, so the clip does not end up in the next one; the microphone keeps recording.
- On Linux, the tools of the Library: `Delete` and an edit saved in place of its original send the file to the Trash of the desktop, never deleted for good, and `Edit...` and `Send to soundboard...` read MP3 and WAV clips back.
- On Linux, the `Hotkeys` page shows what the desktop allows: keys typed in a field on X11, the keys the desktop chose through its shortcut portal, or the command to bind to a key, with a `Copy` button. The settings say `Start with the session`, and leave out what the notification of the desktop does not have: its look, its position and the confirmation sound.

## [0.7.0] - 2026-11-18

Tools for the clips you saved, in the Library: rename, delete, edit, and send to the Discord soundboard.

### Added

- Each clip in the Library has a menu: right-click its row, or use the `...` button under the list. It offers `Rename`, `Edit...`, `Send to soundboard...`, `Copy`, `Show in folder` and `Delete`.
- Rename a clip from the Library (`F2`). A clip saved as two files keeps both files together under the new name, and its star and collections follow it. Hindsound refuses a name Windows can't use and says why before you confirm: forbidden characters, a dot or space at the end, a name Windows keeps for itself (`CON`, `NUL`, `COM1`...), a name that is too long, a name another clip already has, or, for a clip in one file, a name that ends with `_system` or `_mic`. A clip never takes the star or collections of a clip that had its name before going to the Recycle Bin.
- Delete a clip from the Library (`Del`). Hindsound asks first, then moves the clip to the Recycle Bin and never deletes it for good. If the Recycle Bin can't take it, the clip stays where it is and Hindsound says so.
- Edit a clip from the Library: drag two handles on its wave to choose the part to keep, listen to it, and turn its volume up or down (each track on its own for a clip saved as two files), with the peaks held under your ceiling. The arrow keys move the chosen handle by a tenth of a second, and `Shift` with an arrow by a second. Save the edit as a copy next to the original, or in place of it: the original then goes to the Recycle Bin, never deleted for good, and the edit keeps its star and its collections. A save you started always goes to its end, even if you leave the page, and the Library tells you how it went.
- Send a clip to the Discord soundboard: the editor keeps at most 5.2 seconds, saves a short mp3 copy, opens Discord if it is installed and shows the file in its folder, so you can upload it in the soundboard settings of your server.
- The clip editor reads your mp3 and wav clips at their exact length, without the short silence an mp3 encoder adds at the start and the end, and shows the shape of each track as a wave, even for long clips.
- A `Record my voice` switch on the `Now` page, next to the `Mic` lane. Turn it off and the microphone is no longer opened at all, and the `Mic level` bar and `Mic gain` slider of the `Audio` page are off; your chosen microphone is remembered for when you turn it back on.
- A `Window size` setting in `General` (100%, 120% or 140%). If your screen can't hold the size you chose, your choice stays selected, the window takes the largest size that fits, and a grey line under the setting says so.

### Changed

- The settings window is now 20% larger by default, and it picks its size again when you move it to a screen with another scaling.
- When you record one program alone, `{app}` in a clip's file name is now that program's name, as shown in the `Record` list, whichever window is in front when you save.

### Fixed

- In the window, the mouse wheel moves only what is under the mouse: over the list of sections on the left, the band at the top or the bar at the bottom, it no longer scrolls the page or the list of clips.
- On a screen too small for the window even at its smallest size, the window now opens at the top left of the usable area, so its title bar is always within reach.

## [0.6.0] - 2026-11-12

The first Linux version, and a portable copy for Windows. The copy installed by the Setup works as in 0.5.0, apart from the fix below.

### Added

- Hindsound for Linux, a first version for x64 PCs, with no window yet. It comes twice in a release: `hindsound-X.Y.Z-linux-x64.AppImage`, one file to run, and `hindsound-X.Y.Z-linux-x64.tar.gz`, a folder to unpack where the AppImage does not run. The README says how to set up each. It is built for 64-bit Ubuntu 22.04 and later and has not run on a real desktop yet: see `Not tried yet` in the README.
- On Linux, Hindsound keeps the last minutes of the sound of the PC, and of the microphone if your settings name one, through PulseAudio or PipeWire. It follows a change of the default output or microphone.
- On Linux, a clip is saved with its hotkey, with `hindsound --save`, or from the menu of its icon. The hotkey goes through the global shortcuts of the desktop (GNOME 48 and later, KDE), which may ask you first, or straight to X11. Where there is neither, a notification says once which command to bind to a key in the settings of the desktop: the full path of the file you launched, followed by `--save`.
- On Linux, a saved clip is announced by a notification without sound, with a `Show in folder` button; a click on it shows the clip selected in its folder. The clip is copied as a file when `wl-copy`, `xclip` or `xsel` is installed.
- On Linux, the icon next to the clock has a menu: `Save clip`, `Pause capture` or `Resume capture`, `Open clips folder`, `Start with the session` and `Quit`. On GNOME the icon needs the AppIndicator extension, which Ubuntu has already; without it, clips are still saved with the hotkey or `hindsound --save`.
- On its first launch from the AppImage or the unpacked folder, Hindsound adds itself, with its icon, to the list of applications. It never covers an entry installed by a package of the system.
- On Linux, the settings are in `~/.config/Hindsound/settings.json`, the same file as on Windows: edit it while Hindsound is not running. Clips go to `~/Music/Hindsound` unless the settings name another folder.
- On Linux, when a new version is out, a notification says so, and a click on it opens the page of the release. Nothing downloads or installs by itself: replace the file.
- Not on Linux yet: the window (settings, `Now`, `Library`), listening to a clip, recording one application, and processors other than x64.
- A portable copy for Windows, `Hindsound-X.Y.Z-win-x64-portable.zip`: unzip it and start `Hindsound.exe`, nothing is installed. `portable.txt`, next to `Hindsound.exe`, is what makes the copy portable. Its settings are in `%AppData%\Hindsound`, as for the Setup.
- The portable copy never updates itself. When a new version is out, a notification says so, and a click on it opens the page of the release. In `General`, `When a version is out` offers `Ask me` and `Off`, and `Download` opens the same page. To update, replace the files of its folder with those of the new zip.
- Every release carries `SHA256SUMS`, the SHA-256 of each of its files. On Linux, `sha256sum --check --ignore-missing SHA256SUMS` checks those you downloaded.
- Every release also carries `fuse-3.15.0.tar.xz`, `type2-runtime-20251108-source.tar.gz` and `type2-runtime-20251108-libraries-source.tar.gz`: the source of libfuse, of the runtime at the head of the AppImage, which holds libfuse, and of the other libraries linked into that runtime, offered for the LGPL-2.1 of libfuse. Hindsound does not need them to run.
- The release notes name each file of the release and whom it is for.

### Changed

- `THIRD-PARTY-NOTICES.md` lists the components of the Linux packages too, and says for each component whether it is in the Windows build, in the Linux packages, or in both. The licence texts used only on Linux are in `licenses/linux`: the Setup installs the same licence texts as 0.5.0, and the portable zip carries those same texts, not the ones in `licenses/linux`.

### Fixed

- On Windows as on Linux: when a folder of your account does not exist yet, such as a deleted Music folder, your clips, settings, log and the other files of the app no longer go to a folder relative to wherever Hindsound was started. Hindsound uses the full path, and creates the folder when it first writes there.

## [0.5.0] - 2026-11-10

### Added

- `Play` in `Library` plays the chosen clip on your default output, and turns into `Stop` while it plays. A double click on a line plays it too.
- While a clip plays, `Playback can end up in your next clip on this setup.` shows next to the menu when Hindsound records a chosen output, or on a Windows too old to leave its own sound out. On your default output, a clip played here stays out of your next clip.
- A clip that can't be played says `Can't play this file` in place of its name.

### Changed

- On `Now`, while the line of a change of source is on the wave, the title of the PC side names the app captured before it and the app captured now, such as `Discord, then Spotify`. It said `Whole PC` even when the past came from one app.
- A name cut to fit no longer ends on a comma before its dots.

## [0.4.0] - 2026-11-09

### Added

- Stars: each clip of `Library` has one. Click it, or press F on the chosen clip, to make it a favorite.
- Collections: under the list, tick the collections the chosen clip is in, such as a sound or a name. A clip can be in several. Those that don't fit on the line are behind `+N`.
- A menu above the list shows `All clips`, `Favorites` or one collection. `Rename` and `Delete` appear next to it when it shows a collection.
- `New collection...`, `Rename` and `Delete` open a small box. Esc closes it and nothing changes. Deleting a collection never deletes a clip.
- Stars and collections are kept in `library.json`, next to `settings.json`. It is written only when you change something, and your clips are never moved or renamed.
- A clip with a star or in a collection is always listed, even past the 2,000 most recent.
- If the collections file can't be read or saved, a line in amber says so, and your clips stay listed. A damaged file can be started over.

### Changed

- `Library` shows six clips at a time, under the menu.
- Uninstalling: the same answer that deletes your settings now deletes your collections too. A silent uninstall keeps both.

## [0.3.0] - 2026-11-09

### Added

- A new `Library` page lists your clips, newest first, with their length. It shows the clips saved before this version too.
- Choose a clip to see its length, size and name at the bottom, then `Copy` it or `Show in folder`. If the file is gone, it says so: `This clip was moved or deleted`.
- It shows the 2,000 most recent clips, and says so at the end of the list.
- A clip saved while the page is open shows up by itself, and a file renamed in the Explorer shows up under its new name when the window comes back to the front.
- `Library` in the menu of the icon, under `Open Hindsound`.

### Changed

- A click on a `Clip saved` notification opens `Library` on that clip, instead of the folder. The other notifications about a clip open `Library` too.
- Changing the output folder no longer hides your older clips: `Library` keeps listing up to 8 earlier folders, counted from this version on.
- Closing the window gives back the memory of the page as soon as it closes.

## [0.2.0] - 2026-11-09

### Added

- The window opens on a new `Now` page: a live wave of what the memory holds, one lane for the PC sound and one for the microphone. It fills from the right, and the part not held yet shows `Filling...` and dots.
- The part the save button would take is bright, with `Save: last 0:30` under the wave, or `Save: last 0:12 of 0:30` while the memory is short.
- `Record` on `Now` is the same setting as on the `Audio` page. Choosing an app greys the past, which is still the sound of the whole PC, draws a `Discord only from here` line, and says `Save: last 0:30 (0:25 from before Discord)`.
- `Open Hindsound` in the menu of the icon, in bold.

### Changed

- A left click on the icon and a second launch open the window on `Now`, or bring it up as it is if it is already open.
- `Settings` in the menu goes to the last page of the settings you looked at, `Hotkeys` at first.
- A click on a notification about the capture (`Capture on`, `Microphone lost` and the like) opens `Now`, `Save hotkey is taken` opens `Hotkeys`, and `Couldn't change Start with Windows` opens `General`.
- The first-launch card ends on `Now`.
- With no audio device, the band shows `No audio device` without its figures, so the words are never cut.
- An entry of the menu says `(only ...)` only when it is more than a second short, as the band does.
- The wave stops with the capture when paused, and its timer stops on other pages and while the window is minimized. Closed, the window costs nothing, as before.

## [0.1.0] - 2026-11-09

The first announced version. What the app does is in the README, and the full list is under 0.0.9.

### Changed

- The notification says what is really kept when it shows: `Filling, up to 5:00` at the start, `2:10 of 5:00 kept` when capture resumes, and `Keeping the last 5:00` only once it is true. A pause says what can still be saved.
- The band of the window shows `0:12 / 5:00` while the memory fills.
- `Save last ...` is now `Save up to ...`, on the button, in the tray menu and on the Hotkeys page. An entry of the menu longer than what is kept says so: `Save up to 5 min (only 0:12)`.
- The button and the save entries of the menu are grey with less than a second kept.
- A clip that takes a while to write says the length it really has: `Saving clip (0:12)`, not the length asked for.
- The first-launch card and the update card follow what is kept and the length of a clip.
- The `Updates` row of General is now `When a version is out`, as the section already has that title.
- A run with its own settings file (`--settings`) no longer gets in the way of the copy in use: only two runs on the same file refuse each other.

## [0.0.10] - 2026-11-07

### Changed

- No change in the app: this version is the first update offered to 0.0.9, to check that an installed copy updates itself.

## [0.0.9] - 2026-11-07

### Added

- Keeps the last minutes of PC audio and microphone in memory, stored as 16-bit samples to stay light.
- Hindsound's own sounds stay out of the capture of the default output, from Windows 10 version 2004. On a chosen output or an older Windows they can be heard in a clip.
- Record one chosen application instead of the whole PC. If it closes, its silence is kept, and the capture attaches again when it comes back.
- Global hotkey that saves the past to a file: mix or separate tracks, WAV or MP3, with peak or loudness normalization.
- The clip goes to the clipboard, ready to paste into Discord.
- A notification drawn by the app, which does not take the focus. In exclusive fullscreen, a sound instead, and the notification when back on the desktop.
- Tray icon and menu.
- A clip has a way out when saving goes wrong: if the output folder cannot be written, it goes to a rescue folder; if the MP3 encoder fails, it is saved as WAV. If the rescue folder fails too, or a press comes while too much is already waiting to be written, the app says the clip was not saved.
- Settings stored in `%AppData%\Hindsound\settings.json`, with a choice of accent color.
- Repository setup: README, MIT license, changelog.
- Command-line options to look into a problem without touching what the user relies on: `--no-mic`, `--no-clipboard` and `--no-hotkeys` keep this run away from the microphone, the clipboard and the global shortcuts, whatever the settings say.
- Updates, for an installed copy: once a day, 2 minutes after launch at the earliest, Hindsound asks GitHub for the latest version. Never during a game in full screen, a presentation or a clip being saved: it tries again 30 minutes later. A copy that was not installed, or started with a command-line option for a test or a check (only the one Setup passes after a failed update is let through), never updates itself.
- Three update modes in Settings > General: Install automatically (the default), Ask me, and Off. Off asks GitHub nothing unless Check now is pressed. The section shows the version, when it last checked, and the buttons Check now, Update now, What's new and Skip this version.
- A downloaded update is checked against its size and SHA-256 before Setup starts. In Install automatically, it installs when you choose Quit in the menu, or at the next launch. Closing the window, ending the session or a crash never starts it while Hindsound runs.
- "Update ready" or "Update available": one notification per version, which waits for the desktop. While a version is ready (in Ask me, also when one is available), the tray menu offers "Update to X...".
- Update now opens a card first: updating restarts Hindsound and loses the audio in memory, so it offers Save it, then update, as well as Update and Cancel. Nothing is updated if that clip could not be saved.
- After an update, the "Capture on" notification says "Updated to X" instead. A failed one says "Update failed" and the version still running; Hindsound tries 3 times per version, then only Update now tries again.
- A damaged or unreadable settings file turns updates Off until you choose again.
- The first launch card says that Hindsound checks GitHub once a day.
- `--update-failed` (passed by Setup to the version it starts again after a failed update), and the test options `--selftest-update <folder>` and `--selftest-update-check`.

### Changed

- The tray icon and the title bar show the new logo, an H with a wave: drawn pixel by pixel at the tray sizes, in the accent color; two gray stems when paused.
- A left click or Enter on the tray icon opens the settings, and so does launching the app a second time.
- Quit waits for a clip being written, 5 minutes at most: the icon says so, and a second Quit leaves at once.

### Fixed

- A damaged settings file starts the app with the mic off and says so, and is kept as a copy when one can be made. A locked one starts the same way and is left as it is.
- A settings file locked at the start is read by itself once it can be, and watched until a real read has replaced the defaults, also if it first turns out broken or gone. Until then the icon says that the defaults are running with the mic off.
- The settings notices that a later read made false are taken back, and no longer come back after another notification.
- Quit stays until a clip with a problem can be read: 5 seconds on screen, or until it has been shown or given up on back from a game. Only a clip keeps the app: "Saving your clip" no longer holds it up or shows after the clip is done.
- The "Capture on" reminder stays due until it has really been shown, and says the length and the hotkey of that moment.
- A status that is no longer true leaves the line, and an important one replaced by a settings notice comes back after it.
- A settings file edited by hand is read when the settings window comes back, before the window can write over it.
- Says when the settings can't be saved, and puts the Start with Windows switch back when Windows refuses.
- Important messages wait for the desktop until they can be read, whatever the Notifications setting says: no audio device, mic lost, a clip with a problem, settings problems.
- Says when the mic is lost while the sound of the PC goes on, and when it comes back.
- Says when the save hotkey is taken instead of showing keys that do nothing, and keeps saying it when hotkeys can't start at all.
- Copy last clip says when the files of the clip are gone.
- A copy that failed plays no success sound, and is said even on a rescued or silent clip.
- Presses that come faster than clips can be written stop taking memory past 1.2 GB, and the app says it is still saving.
- File names are never cut inside a character.
- Closing by an installer or at the end of the Windows session really ends the app, and gives clips being written 15 seconds.
- The tray icon comes back when the shell was not ready for it, shows its tooltip, and is redrawn when the display scale changes.
- The notification stays off the taskbar and goes to the screen of the click after a tray menu.
- Opening a folder no longer holds up the hotkeys, and the settings window keeps the latest list of audio devices and apps.
- In the settings window, a held key repeats only what moves or types: it never saves, toggles or opens again.
- A touchpad scrolls the settings: small wheel steps add up.
- The first click on a half-hidden widget counts, and the scroll thumb is redrawn when its drag ends.
- A field scrolled away comes back at the first key, with that key typed in it.
- Text on the accent color stays readable whatever the color, and the accent is no longer used as text where it would not read.
- Alt+Space and Alt+letter typed for a hotkey no longer open the window menu or beep.
- A click on Custom, for the clip length or the color, puts the cursor in its field.
- Escape in File name or Color code gives back the value from before the typing, as each valid value is written as it is typed.
- A click on a notification about the settings opens the settings, not the clips folder.
- The first-launch card only says to paste a clip that was really copied, and otherwise says what the notification says.
- The list of audio devices and apps runs twice at the same time at most.
