Hindsound for Linux
===================

Hindsound runs in the background and keeps the last minutes of the sound of your PC, and of your microphone if you
set one, in memory. Save that past to an MP3 or WAV file with a hotkey, with "hindsound --save", or from the menu of
its icon.

This first Linux version has no window: no settings window, no listening, no recording of a single app.
Home page: https://github.com/quelquun667/hindsound


Install
-------

From this folder:

    mkdir -p ~/.local/bin ~/.local/share
    cp hindsound libmp3lame.so.0 ~/.local/bin/
    cp -r share/. ~/.local/share/

libmp3lame.so.0 stays next to hindsound: it is the MP3 encoder. ~/.local/bin is on the PATH of Ubuntu once you log in
again. Then start Hindsound from the list of applications, or run "hindsound".

You can also run ./hindsound from this folder without installing anything.


Save a clip from a key
----------------------

On X11, and on Wayland where the desktop offers global shortcuts (GNOME 48 and later, KDE), Hindsound uses the hotkey
of its settings.

Elsewhere, Ubuntu 24.04 on Wayland included: open Settings > Keyboard > Keyboard Shortcuts > Custom Shortcuts, add one, and set its
command to the full path of the program followed by --save, for example:

    /home/you/.local/bin/hindsound --save


The icon
--------

The icon goes next to the clock, through StatusNotifierItem. On GNOME it needs the AppIndicator extension, which
Ubuntu has already.


Settings
--------

~/.config/Hindsound/settings.json, the same file as on Windows. Edit it by hand while Hindsound is not running.


Uninstall
---------

    rm ~/.local/bin/hindsound ~/.local/bin/libmp3lame.so.0
    rm ~/.local/share/applications/io.github.quelquun667.Hindsound.desktop
    rm ~/.local/share/icons/hicolor/*/apps/io.github.quelquun667.Hindsound.png
    rm -f ~/.config/autostart/io.github.quelquun667.Hindsound.desktop

The last line removes what "Start with the session" adds. Your clips stay, and so do ~/.config/Hindsound (the settings)
and ~/.local/share/Hindsound (the log, rescued clips and the update state).


Licenses
--------

Hindsound is under the MIT license (LICENSE). LAME, the MP3 encoder, is under the LGPL; the licenses of everything
shipped with it are in THIRD-PARTY-NOTICES.md and the licenses folder.
