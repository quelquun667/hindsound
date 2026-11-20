Hindsound for Linux
===================

Hindsound runs in the background and keeps the last minutes of the sound of your PC, and of your microphone if you
set one, in memory. Save that past to an MP3 or WAV file with a hotkey, with "hindsound --save", or from the menu of
its icon.

The window (Now, Library and the settings) is drawn with libSDL3.so.0 and the fonts folder of this package, and with
FreeType, HarfBuzz and fontconfig, which every desktop has. Without them Hindsound runs with its icon only, and its log
says why. "Record" can follow one application: it lists the applications while they play sound.
Home page: https://github.com/quelquun667/hindsound


Install
-------

From this folder:

    mkdir -p ~/.local/bin ~/.local/share
    cp -r hindsound libmp3lame.so.0 libSDL3.so.0 fonts ~/.local/bin/
    cp -r share/. ~/.local/share/

libmp3lame.so.0, libSDL3.so.0 and the fonts folder stay next to hindsound: the MP3 encoder, the library of the window
and the fonts it draws with. ~/.local/bin is on the PATH of Ubuntu once you log in
again. Then start Hindsound from the list of applications, or run "hindsound".

You can also run ./hindsound from this folder, without copying anything. It then adds itself to the list of
applications, with an entry and an icon in ~/.local/share that start it from this folder; Uninstall below removes them.


Save a clip from a key
----------------------

On X11, and on Wayland where the desktop offers global shortcuts (GNOME 48 and later, KDE), Hindsound uses the hotkey
of its settings.

Elsewhere, Ubuntu 24.04 on Wayland included: open Settings > Keyboard > View and Customize Shortcuts > Custom Shortcuts,
add one, and set its command to the full path of the program followed by --save, for example:

    /home/you/.local/bin/hindsound --save


The icon
--------

The icon goes next to the clock, through StatusNotifierItem. On GNOME it needs the AppIndicator extension, which
Ubuntu has already.


Settings
--------

~/.config/Hindsound/settings.json, the same file as on Windows. The settings window edits it; you can also edit it by
hand while Hindsound is not running.


Uninstall
---------

Remove this copy as below before you install the .deb of Hindsound: ~/.local/bin comes before /usr/bin on the PATH,
so "hindsound", and the list of applications with it, would keep starting this copy.

Turn "Start with the session" off in the menu of the icon first, or the last line below removes it for you. Then:

    rm ~/.local/bin/hindsound ~/.local/bin/libmp3lame.so.0 ~/.local/bin/libSDL3.so.0
    rm -r ~/.local/bin/fonts
    rm ~/.local/share/applications/io.github.quelquun667.Hindsound.desktop
    rm ~/.local/share/icons/hicolor/*/apps/io.github.quelquun667.Hindsound.png
    rm -f ~/.config/autostart/io.github.quelquun667.Hindsound.desktop

The last line removes what "Start with the session" adds: if you delete the program without it, your session tries to
start a file that is gone at each login.

What Hindsound leaves, and you can delete too:

    ~/Music/Hindsound                the clips (or the folder your settings name)
    ~/.config/Hindsound              the settings
    ~/.local/share/Hindsound         the log, the clips rescued after a failed save (Rescued) and the update state (updates)


Licenses
--------

Hindsound is under the MIT license (LICENSE). LAME, the MP3 encoder, is under the LGPL; SDL, the library of the window,
under the zlib license; the fonts under the SIL Open Font License 1.1. The licenses of everything shipped with it are in
THIRD-PARTY-NOTICES.md and the licenses folder.
