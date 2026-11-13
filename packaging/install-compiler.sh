#!/usr/bin/env bash
# Install only the digest-verified official compiler in a Wine prefix.
set -euo pipefail
command -v wine >/dev/null
command -v winepath >/dev/null
archive="$(mktemp -t thales-inno-XXXXXXXX.exe)"
trap 'rm -f "$archive"' EXIT
curl -fLsS --retry 3 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -o "$archive"
expected='8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'
if command -v sha256sum >/dev/null; then actual="$(sha256sum "$archive" | cut -d ' ' -f1)"; else actual="$(shasum -a 256 "$archive" | cut -d ' ' -f1)"; fi
[[ "$actual" == "$expected" ]] || { echo 'Inno Setup download checksum mismatch' >&2; exit 1; }
wine "$archive" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER '/DIR=C:\ThalesBuildTools\InnoSetup'
compiler="$(winepath -u 'C:\ThalesBuildTools\InnoSetup\ISCC.exe')"
test -f "$compiler"
echo "Set ISCC=$compiler"
