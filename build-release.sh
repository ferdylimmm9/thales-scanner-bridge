#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Usage: $0 <semantic-version>" >&2
  exit 2
fi

version="$1"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
publish_dir="$root/publish"
publish_zip="$root/publish.zip"
payload_dir="$root/.installer-payload"
payload_zip="$root/Installer/payload.zip"
installer_out="$root/installer-out"
installer_exe="$root/ThalesBridgeSetup.exe"

cleanup() {
  rm -rf "$payload_dir" "$installer_out"
  rm -f "$payload_zip"
}
trap cleanup EXIT

rm -rf "$publish_dir"
rm -f "$publish_zip" "$installer_exe"

dotnet publish "$root/ThalesBridge" \
  -c Release -r win-x64 --self-contained true \
  -p:EnableWindowsTargeting=true -p:Version="$version" -o "$publish_dir"

(
  cd "$publish_dir"
  zip -q -r "$publish_zip" .
)

mkdir -p "$payload_dir/publish"
cp -R "$publish_dir/." "$payload_dir/publish/"
cp "$root/setup.ps1" "$root/uninstall.ps1" "$payload_dir/"
(
  cd "$payload_dir"
  zip -q -r "$payload_zip" .
)

dotnet publish "$root/Installer/ThalesBridgeInstaller.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:EnableWindowsTargeting=true -p:Version="$version" -o "$installer_out"

cp "$installer_out/ThalesBridgeSetup.exe" "$installer_exe"
echo "Built $installer_exe"
echo "Built $publish_zip"
