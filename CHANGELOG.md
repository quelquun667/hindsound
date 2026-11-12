# Changelog

All notable changes to Hindsound are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- Every release also carries `fuse-3.15.0.tar.xz`, `type2-runtime-20251108-source.tar.gz` and `type2-runtime-20251108-libraries-source.tar.gz`: the source of libfuse, of the runtime at the head of the AppImage, which holds libfuse, and of the other libraries linked into that runtime, offered under the LGPL-2.1. Hindsound does not need them to run.
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
