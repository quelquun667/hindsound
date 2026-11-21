#!/usr/bin/env bash
# Makes, once, the certificate that signs every version of the Mac app, so that macOS keeps the permissions given to the
# version before (the sound of the system, the microphone). It is made by its owner, not by Apple: Gatekeeper still
# asks for "Open Anyway" at the first launch, as for an ad hoc signature.
#
# Writes into OUT_DIR, which must not exist:
#   hindsound-signing.p12         the certificate (extension codeSigning, 10 years) and its RSA key, in the encryption
#                                 macOS reads (3DES and a SHA-1 MAC: the defaults of OpenSSL 3 fail in security import)
#   hindsound-signing.p12.base64  the same, in base64 on one line: the secret MACOS_SIGNING_P12
#   password.txt                  its password, random: the secret MACOS_SIGNING_P12_PASSWORD
#   hindsound-signing.pem         the certificate alone, without the key, to read its SHA-1 later
# tools/release/README.md says what to do with them. The CI runs this script too (job macos of release-files.yml), with a
# throwaway OUT_DIR, and signs the zip again with what it makes through resign-zip.sh, as the release does.
#
# Usage: make-signing-certificate.sh OUT_DIR
# OPENSSL may name another openssl program. Works with OpenSSL 3 (Git Bash on Windows, Homebrew) and LibreSSL (macOS).
# Last checked: 21/11/2026, with OpenSSL 3.2.4 (Git for Windows) and on the runner macos-26.

set -euo pipefail

[ "$#" -eq 1 ] || { echo "Usage: $0 OUT_DIR" >&2; exit 2; }
out="$1"
[ ! -e "$out" ] || { echo "$out exists: the certificate is made in a new folder." >&2; exit 1; }
openssl="${OPENSSL:-openssl}"
"$openssl" version

mkdir -p "$out"
chmod 700 "$out"
umask 077
# A file of settings rather than -subj, whose "/CN=" Git Bash would turn into a path of Windows.
cat > "$out/certificate.cnf" << 'CONF'
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = Hindsound code signing
[ext]
basicConstraints = critical,CA:false
keyUsage = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
subjectKeyIdentifier = hash
CONF
"$openssl" req -x509 -newkey rsa:2048 -nodes -days 3650 -config "$out/certificate.cnf" \
  -keyout "$out/key.pem" -out "$out/hindsound-signing.pem" 2> /dev/null
HINDSOUND_P12_PASSWORD="$("$openssl" rand -hex 24)"
export HINDSOUND_P12_PASSWORD
"$openssl" pkcs12 -export -inkey "$out/key.pem" -in "$out/hindsound-signing.pem" -name 'Hindsound code signing' \
  -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1 -passout env:HINDSOUND_P12_PASSWORD \
  -out "$out/hindsound-signing.p12"
rm -f "$out/key.pem" "$out/certificate.cnf"
printf '%s' "$HINDSOUND_P12_PASSWORD" > "$out/password.txt"
"$openssl" base64 -A -in "$out/hindsound-signing.p12" -out "$out/hindsound-signing.p12.base64"

# Read back: the .p12 opens with the password and holds the certificate.
"$openssl" pkcs12 -in "$out/hindsound-signing.p12" -nokeys -passin env:HINDSOUND_P12_PASSWORD 2> /dev/null \
  | "$openssl" x509 -noout -subject -enddate -fingerprint -sha1
echo "make-signing-certificate: written in $out"
