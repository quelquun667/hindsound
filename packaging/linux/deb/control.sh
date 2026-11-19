# The control fields of the package hindsound, written once: build-deb.sh writes the control file from them, check-deb.sh
# demands that the package holds exactly these fields with these values, and try-deb.sh reads Depends from them.
# Sourced, not run.

readonly MAINTAINER="Quelqu'un <87375238+quelquun667@users.noreply.github.com>"
# What the program needs from the system, checked by check-deb.sh against what its files link, and by try-deb.sh in
# clean containers, against the libraries the code loads by name (see LOADED_LIBRARIES in try-deb.sh):
#   libc6 (>= 2.34)         the C library of the program and of both libraries (glibc 2.34: Ubuntu 22.04, Debian 12)
#   libgcc-s1, libstdc++6   linked by the .NET runtime inside the program
#   libssl3 | libssl3t64    loaded by .NET for the HTTPS of the update check (libssl3t64 on Ubuntu 24.04)
#   libpulse0               the sound: capture, monitor and playback through PulseAudio or PipeWire
#   libfreetype6, libharfbuzz0b, libfontconfig1   the text of the window
readonly DEPENDS='libc6 (>= 2.34), libgcc-s1, libstdc++6, libssl3 | libssl3t64, libpulse0, libfreetype6, libharfbuzz0b, libfontconfig1'
# The clipboard of a saved clip (wl-copy on Wayland, xclip or xsel on X11), xdg-open for "show in folder", and the
# certificates of the HTTPS of the update check.
readonly RECOMMENDS='wl-clipboard | xclip | xsel, xdg-utils, ca-certificates'
readonly HOMEPAGE='https://github.com/quelquun667/hindsound'

# The size in KiB as dpkg-gencontrol counts it: each file rounded up to a KiB, one for each folder and link.
# Argument: the folder that holds usr/ (and DEBIAN/, which is not counted).
installed_size() {
    find "$1" -mindepth 1 -path "$1/DEBIAN" -prune -o -printf '%y %s\n' \
        | awk '{ if ($1 == "f") total += int(($2 + 1023) / 1024); else total += 1 } END { print total }'
}

# The control file, as dpkg-deb --field writes it. Arguments: the version, the installed size.
control_text() {
    cat << CONTROL
Package: hindsound
Version: $1
Architecture: amd64
Maintainer: $MAINTAINER
Installed-Size: $2
Depends: $DEPENDS
Recommends: $RECOMMENDS
Section: sound
Priority: optional
Homepage: $HOMEPAGE
Description: keeps the last minutes of sound and saves them on a key
 Hindsound runs in the background and keeps the last minutes of the sound of
 the PC, and of a microphone if one is set, in memory. A hotkey, the command
 "hindsound --save" or the menu of its icon saves that past to an MP3 or WAV
 file.
CONTROL
}
