Hindsound for Windows, portable
===============================

Hindsound runs in the background and keeps the last minutes of the sound of your PC, and of your microphone if you
set one, in memory. Save that past to an MP3 or WAV file with a hotkey, from its window or from the menu of its icon.
Home page: https://github.com/quelquun667/hindsound


Run it
------

Unzip this folder where you want to keep it, then start Hindsound.exe. Nothing is installed.

Keep libmp3lame.64.dll and portable.txt next to Hindsound.exe: the first is the MP3 encoder, the second tells Hindsound
that this copy is portable.

Windows may stop it with a screen titled "Windows protected your PC": Hindsound is not signed. If the zip comes from the
Releases page of the home page, click More info, then Run anyway.


New versions
------------

This copy never updates itself. When a new version is out, a notification says so, and a click on it opens the page of
the release. To update, download the new zip there, quit Hindsound, and replace the files of this folder with those of
the new zip. Your settings stay.


Remove it
---------

1. If Start with Windows is on, turn it off first, in Settings > General: otherwise Windows keeps trying to start a
   program that is gone at each sign-in.
2. Quit Hindsound from the menu of its icon.
3. Delete this folder.

Hindsound leaves these, and you can delete them too: your settings and collections in %AppData%\Hindsound, the log and
the rescued clips in %LocalAppData%\Hindsound, and your clips in Music\Hindsound or the folder you chose.
