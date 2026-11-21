#!/usr/bin/env bash
# Signs Hindsound.app, each Mach-O inside first, then the bundle, which seals the rest, and checks the signature.
#
# With HINDSOUND_SIGNING_P12 set to a .p12 file (a certificate with the extension codeSigning and its private key, made
# once by make-signing-certificate.sh) and HINDSOUND_SIGNING_P12_PASSWORD to its password, the bundle is signed by that
# certificate: it goes into a keychain of its own, made for this run in a folder of its own and deleted when the script
# ends, whatever happens. Every version signed by the same certificate has the same designated requirement
# (identifier "io.github.quelquun667.hindsound" and certificate leaf = H"<SHA-1>"): macOS keeps the permissions given to
# the version before (the probe of step M0 showed it with tccd). Without HINDSOUND_SIGNING_P12, the bundle is signed ad
# hoc: macOS then takes each version for another app, and asks for the permissions again.
#
# Usage: sign-app.sh APP
# Last checked: 21/11/2026, on the runner macos-26.

set -euo pipefail

[ "$#" -eq 1 ] || { echo "Usage: $0 APP" >&2; exit 2; }
app="$1"
[ -d "$app/Contents/MacOS" ] || { echo "$app is not a bundle." >&2; exit 1; }
readonly ID=io.github.quelquun667.hindsound

identity=-
keychain_args=()
if [ -n "${HINDSOUND_SIGNING_P12:-}" ]; then
  [ -f "$HINDSOUND_SIGNING_P12" ] || { echo "HINDSOUND_SIGNING_P12 names no file." >&2; exit 1; }
  [ -n "${HINDSOUND_SIGNING_P12_PASSWORD:-}" ] || { echo "HINDSOUND_SIGNING_P12 is set without HINDSOUND_SIGNING_P12_PASSWORD." >&2; exit 1; }
  work="$(mktemp -d)"
  chmod 700 "$work"
  keychain="$work/signing.keychain-db"
  searched="$(security list-keychains -d user | tr -d '"' | xargs)"
  # The keychain and its key go away with the script, and the search list of the user is as it was.
  cleanup() {
    # shellcheck disable=SC2086
    security list-keychains -d user -s $searched 2> /dev/null || true
    security delete-keychain "$keychain" 2> /dev/null || true
    rm -rf "$work"
    echo "sign-app: the keychain of the certificate is deleted."
  }
  trap cleanup EXIT
  password="$(uuidgen)"
  security create-keychain -p "$password" "$keychain"
  security set-keychain-settings "$keychain"
  security unlock-keychain -p "$password" "$keychain"
  # shellcheck disable=SC2086
  security list-keychains -d user -s "$keychain" $searched
  # security import takes the password of a .p12 only as an argument (-P), or asks for it in a window: ps can read it
  # while the import runs, about a second. Accepted: on the runner no other user shares the machine, and the password
  # only opens the .p12, which release.yml deletes at the end of the step.
  security import "$HINDSOUND_SIGNING_P12" -k "$keychain" -P "$HINDSOUND_SIGNING_P12_PASSWORD" -T /usr/bin/codesign > /dev/null
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$keychain" > /dev/null
  # The certificate is made by its owner, not by Apple: find-identity calls it not trusted, but codesign signs with its
  # SHA-1, the one certificate of this keychain.
  hashes="$(security find-certificate -a -Z "$keychain" | sed -n 's/^SHA-1 hash: \([0-9A-F]\{40\}\)$/\1/p')"
  [ "$(printf '%s\n' "$hashes" | grep -c .)" -eq 1 ] || { echo "The .p12 must hold exactly one certificate." >&2; exit 1; }
  identity="$hashes"
  keychain_args=(--keychain "$keychain")
fi

while IFS= read -r file; do
  codesign --force --timestamp=none --sign "$identity" ${keychain_args[@]+"${keychain_args[@]}"} "$file"
done < <(find "$app/Contents/Frameworks" "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/hindsound")
codesign --force --timestamp=none --sign "$identity" ${keychain_args[@]+"${keychain_args[@]}"} --identifier "$ID" "$app"
codesign --verify --deep --strict --verbose=2 "$app"
codesign -dvv "$app" 2>&1 | grep -E '^(Identifier|Format|Signature|Authority|CodeDirectory)' || true

# An implicit requirement is written "# designated => ...": the same reading as the jobs of ci.yml.
requirement="$(codesign -d -r- "$app" 2>&1 | sed -n 's/^#* *designated => //p' | head -n 1)"
echo "sign-app: designated requirement: $requirement"
if [ "$identity" = - ]; then
  [[ "$requirement" == cdhash* ]] || { echo "An ad hoc signature should be required by its cdhash." >&2; exit 1; }
  echo "sign-app: signed ad hoc: macOS will ask again for the permissions after each update."
else
  [ "$requirement" = "identifier \"$ID\" and certificate leaf = H\"$(tr 'A-F' 'a-f' <<< "$identity")\"" ] \
    || { echo "The designated requirement does not name the certificate $identity." >&2; exit 1; }
  echo "sign-app: signed by the certificate with the SHA-1 $identity."
fi
