# Changelog

All notable changes to Hindsound are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Keeps the last minutes of PC audio and microphone in memory, stored as 16-bit samples to stay light.
- Hindsound's own sounds stay out of the capture.
- Record one chosen application instead of the whole PC. If it closes, its silence is kept, and the capture attaches again when it comes back.
- Global hotkey that saves the past to a file: mix or separate tracks, WAV or MP3, with peak or loudness normalization.
- The clip goes to the clipboard, ready to paste into Discord.
- A notification drawn by the app, which does not take the focus. In exclusive fullscreen, a sound instead, and the notification when back on the desktop.
- Tray icon and menu.
- A clip is never lost: if the output folder cannot be written, it goes to a rescue folder; if the MP3 encoder fails, it is saved as WAV.
- Settings stored in `%AppData%\Hindsound\settings.json`, with a choice of accent color.
- Repository setup: README, MIT license, changelog.

### Changed

- A left click or Enter on the tray icon opens the settings, and so does launching the app a second time.
- Quit waits for a clip being written, 5 minutes at most: the icon says so, and a second Quit leaves at once.

### Fixed

- A damaged or locked settings file starts the app with the mic off and says so, and the broken file is kept as a copy.
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
