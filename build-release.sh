#!/usr/bin/env bash
set -euo pipefail
if [[ $# -ne 1 || ! "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Usage: $0 <semantic-version>" >&2
  exit 2
fi
version="$1"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# A verified Inno compiler under Wine is required on Linux/macOS. Windows uses build-release.ps1.
: "${ISCC:?Set ISCC to the verified ISCC.exe path; see packaging/install-compiler.sh}"
command -v wine >/dev/null
command -v winepath >/dev/null
[[ -f "$ISCC" ]] || { echo "ISCC.exe missing: $ISCC" >&2; exit 1; }
cleanup() { rm -rf "$root/manager-out"; }
trap cleanup EXIT
rm -rf "$root/publish" "$root/manager-out"
dotnet publish "$root/ThalesBridge" -c Release -r win-x64 --self-contained true \
  -p:EnableWindowsTargeting=true -p:Version="$version" -o "$root/publish"
dotnet publish "$root/Manager/ThalesBridgeManager.csproj" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false -p:Version="$version" -o "$root/manager-out"
cp -R "$root/manager-out/." "$root/publish/"
rm -f "$root/publish.zip"
(cd "$root/publish" && zip -q -r "$root/publish.zip" .)
file_version="${version%%[-+]*}.0"
wine "$ISCC" "/DReleaseVersion=$version" "/DFileVersion=$file_version" "$(winepath -w "$root/packaging/ThalesBridge.iss")"
test -s "$root/ThalesBridgeSetup.exe"
echo "Built $root/ThalesBridgeSetup.exe and publish.zip"
