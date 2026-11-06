#!/usr/bin/env bash
# Builds a release zip of Kitten Main Menu: dist/KSAMainMenu-v<version>-ksa<game version>.zip, holding the KSAMainMenu
# mod folder. Builds into its own folders, so the installed copy in out/ is never touched (safe while KSA runs).
#   tools/make_release.sh 0.1.0 [/path/to/KSA]
set -euo pipefail
version="${1:?usage: make_release.sh <version> [KSA dir]}"
ksa="$(readlink -f "${2:-$HOME/Games/KSA/current}")"
root="$(cd "$(dirname "$0")/.." && pwd)"
game="$(basename "$ksa" | sed -E 's/.*_v//')"          # ksa_linux_v2026.10.25.5707 -> 2026.10.25.5707
stage="$root/dist/stage"
rm -rf "$stage"
mkdir -p "$stage/KSAMainMenu"
"${DOTNET:-$HOME/.dotnet/dotnet}" build "$root/KSAMainMenu" -c Release -nologo -v q \
    -p:KsaDir="$ksa" -p:OutputPath="$root/dist/build/" -p:ModOutDir="$stage/KSAMainMenu/"
cp "$root/LICENSE" "$root/README.md" "$root/CHANGELOG.md" "$stage/KSAMainMenu/"
zip_name="KSAMainMenu-v$version-ksa$game.zip"
rm -f "$root/dist/$zip_name"
(cd "$stage" && zip -qr "$root/dist/$zip_name" KSAMainMenu)
echo "dist/$zip_name"
unzip -l "$root/dist/$zip_name"
sha256sum "$root/dist/$zip_name"
stat -c '%s bytes' "$root/dist/$zip_name"
