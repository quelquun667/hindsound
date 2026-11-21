#!/usr/bin/env bash
# Signs the Mac app of a zip again, with a certificate, and makes the zip that goes out with the release:
#   - the SHA-1 of the certificate of the .p12 (read by openssl) must be CERTIFICATE_SHA1, the one committed in
#     packaging/mac/signing-certificate.sha1: a .p12 of another certificate is refused before anything is signed;
#   - the zip signed ad hoc by the job macos of release-files.yml is unzipped by ditto;
#   - sign-app.sh signs Hindsound.app with the .p12 of HINDSOUND_SIGNING_P12 (its password in
#     HINDSOUND_SIGNING_P12_PASSWORD), in a keychain of its own that it deletes;
#   - ditto makes the zip again, as the job macos does, without the extended attributes and resource forks.
# Nothing here starts the program of the app: it runs with the .p12 and its password at hand. check-zip.sh checks the new
# zip afterwards, in a step that has neither (the step "Check the signed Mac app" of release.yml).
# release.yml runs it with the secret MACOS_SIGNING_P12; release-files.yml runs it for every pull request with a throwaway
# certificate, so the path of the release is the one tried.
#
# Usage: resign-zip.sh ZIP OUT_ZIP WORK_DIR CERTIFICATE_SHA1
#   ZIP               Hindsound-<version>-macos-arm64.zip, signed ad hoc
#   OUT_ZIP           the zip signed by the certificate, with the same name, in another folder; it must not exist
#   WORK_DIR          must not exist; removed at the end, unless a step fails
#   CERTIFICATE_SHA1  the SHA-1 the certificate of the .p12 must have, 40 hex digits
# Last checked: 21/11/2026, on the runner macos-26 (job macos of release-files.yml).

set -euo pipefail

[ "$#" -eq 4 ] || { echo "Usage: $0 ZIP OUT_ZIP WORK_DIR CERTIFICATE_SHA1" >&2; exit 2; }
zip="$1"
out="$2"
work="$3"
expected="$(tr 'A-F' 'a-f' <<< "$4")"
fail() { echo "resign-zip: $*" >&2; exit 1; }
here="$(cd "$(dirname "$0")" && pwd)"

[ -f "$zip" ] || fail "$zip does not exist"
[ "$(basename "$out")" = "$(basename "$zip")" ] || fail "$out must have the name of $zip"
[ ! -e "$out" ] || fail "$out exists"
[ ! -e "$work" ] || fail "$work exists"
[[ "$expected" =~ ^[0-9a-f]{40}$ ]] || fail "the expected SHA-1 '$4' is not 40 hex digits"
[ -n "${HINDSOUND_SIGNING_P12:-}" ] && [ -f "$HINDSOUND_SIGNING_P12" ] || fail "HINDSOUND_SIGNING_P12 names no .p12"
[ -n "${HINDSOUND_SIGNING_P12_PASSWORD:-}" ] || fail "HINDSOUND_SIGNING_P12_PASSWORD is empty"

certificate="$(openssl pkcs12 -in "$HINDSOUND_SIGNING_P12" -nokeys -clcerts -passin env:HINDSOUND_SIGNING_P12_PASSWORD \
  | openssl x509 -noout -fingerprint -sha1 | sed 's/^.*=//; s/://g' | tr 'A-F' 'a-f')"
[[ "$certificate" =~ ^[0-9a-f]{40}$ ]] || fail "the SHA-1 of the certificate of the .p12 could not be read"
[ "$certificate" = "$expected" ] || fail "the .p12 holds the certificate $certificate, which is not the certificate $expected"
echo "resign-zip: the certificate of the .p12 has the SHA-1 $certificate, the one expected."

mkdir -p "$work"
ditto -x -k "$zip" "$work/unzipped"
[ "$(ls -A "$work/unzipped")" = Hindsound.app ] || fail "$zip does not unzip to Hindsound.app alone"
bash "$here/sign-app.sh" "$work/unzipped/Hindsound.app"

mkdir -p "$(dirname "$out")"
ditto -c -k --norsrc --noextattr --noqtn --keepParent "$work/unzipped/Hindsound.app" "$out"
echo "resign-zip: $(basename "$out"), $(stat -f %z "$out") bytes, signed by the certificate $certificate."
rm -rf "$work"
