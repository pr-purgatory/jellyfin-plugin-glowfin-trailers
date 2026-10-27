#!/usr/bin/env bash
# Builds the plugin at a version and zips it for a release: artifacts/glowfin-trailers_<version>.zip.
# Needs the .NET 10 SDK; scripts/build-in-docker.sh runs this without one.
set -euo pipefail
version="${1:?usage: scripts/package.sh <version, e.g. 1.0.0.0>}"
cd "$(dirname "$0")/.."
rm -rf artifacts
dotnet publish Jellyfin.Plugin.GlowfinTrailers -c Release -o artifacts/publish --nologo -v q \
  -p:AssemblyVersion="$version" -p:FileVersion="$version"
python3 - "$version" <<'PY'
import sys, zipfile
with zipfile.ZipFile(f"artifacts/glowfin-trailers_{sys.argv[1]}.zip", "w", zipfile.ZIP_DEFLATED) as z:
    z.write("artifacts/publish/Jellyfin.Plugin.GlowfinTrailers.dll", "Jellyfin.Plugin.GlowfinTrailers.dll")
PY
rm -rf Jellyfin.Plugin.GlowfinTrailers/bin Jellyfin.Plugin.GlowfinTrailers/obj
echo "artifacts/glowfin-trailers_$version.zip"
