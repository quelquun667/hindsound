# Changelog

All notable changes to Hindsound are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Keeps the last minutes of PC audio and microphone in memory, stored as 16-bit samples to stay light.
- Hindsound's own sounds stay out of the capture.
- Global hotkey that saves the past to a file: mix or separate tracks, WAV or MP3, with peak or loudness normalization.
- The clip goes to the clipboard, ready to paste into Discord.
- A notification drawn by the app, which does not take the focus. In exclusive fullscreen, a sound instead, and the notification when back on the desktop.
- Tray icon and menu.
- A clip is never lost: if the output folder cannot be written, it goes to a rescue folder; if the MP3 encoder fails, it is saved as WAV.
- Settings stored in `%AppData%\Hindsound\settings.json`, with a choice of accent color.
- Repository setup: README, MIT license, changelog.
