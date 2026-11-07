# Changelog

All notable changes to Hindsound are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- The first launch card says that Hindsound checks GitHub once a day. In Off, Check now still asks GitHub, on a click. While a version is ready (in Ask me, also when one is available), the tray menu offers "Update to X...".
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
