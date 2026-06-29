#!/usr/bin/env bash
# Build the File Transformation plugin (v12 branch → net10.0) and deploy it into the local test
# Jellyfin's plugins folder. Needed because the published FT repo has no Jellyfin-12-compatible build
# yet, so it doesn't show up in the catalog on 12.0. Only Docker is required (builds in a .NET 10 SDK
# container — no host dotnet needed).
#
# Usage:  ./deploy-file-transformation.sh [path-to-file-transformation-source]
#   default source: ../../../jellyfin-plugin-file-transformation-12  (i.e. LokLakh-s/<that folder>)
set -euo pipefail

stack="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ft="${1:-$stack/../../../jellyfin-plugin-file-transformation-12}"
ft="$(cd "$ft" && pwd)"
csproj="Jellyfin.Plugin.FileTransformation/Jellyfin.Plugin.FileTransformation.csproj"
bin="$ft/src/Jellyfin.Plugin.FileTransformation/bin/Release/net10.0"
target="$stack/jellyfin/config/plugins/FileTransformation"

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: 'docker' not found. On WSL, enable Docker Desktop → Settings → Resources → WSL integration." >&2
  exit 1
fi
if [[ ! -f "$ft/src/$csproj" ]]; then
  echo "ERROR: FT source not found at: $ft/src/$csproj" >&2
  echo "Pass the path to the extracted v12 branch as the first argument." >&2
  exit 1
fi

echo "==> Building File Transformation (Release, net10.0) in a .NET 10 SDK container..."
# JellyfinVersion defaults to 12.0.0 in the csproj → TargetFramework net10.0 + Jellyfin 12.0.0-rc1 pkgs.
# If the 10.0 SDK image is not found, try sdk:10.0-preview instead.
docker run --rm \
  -v "$ft":/src -w /src/src \
  -v jellycrowd_nuget:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet build "$csproj" -c Release

echo "==> Deploying to $target"
rm -rf "$target"
mkdir -p "$target"
# Ship only the plugin assembly + its bundled third-party dep (Newtonsoft.Json). The Jellyfin host
# assemblies must NOT be shipped in the plugin folder.
cp "$bin/Jellyfin.Plugin.FileTransformation.dll" "$target/"
if [[ -f "$bin/Newtonsoft.Json.dll" ]]; then
  cp "$bin/Newtonsoft.Json.dll" "$target/"
else
  echo "   WARN: Newtonsoft.Json.dll not found in build output — FT may fail to load without it." >&2
fi

cat > "$target/meta.json" <<'EOF'
{
  "guid": "5e87cc92-571a-4d8d-8d98-d2d4147f9f90",
  "name": "File Transformation",
  "version": "3.0.0.0",
  "targetAbi": "12.0.0.0",
  "framework": "net10.0",
  "owner": "IAmParadox27",
  "category": "General",
  "overview": "Intercept and transform jellyfin-web content (dependency of Jelly Crowd).",
  "assemblies": ["Jellyfin.Plugin.FileTransformation.dll"]
}
EOF

echo "==> Restarting Jellyfin container (if running)..."
docker restart jellycrowd-jellyfin >/dev/null 2>&1 \
  || echo "   (container not running yet — start it with: docker compose up -d)"

echo "Done. File Transformation deployed. Check: Dashboard → Plugins (should list 'File Transformation')."
echo "Logs: docker logs -f jellycrowd-jellyfin"
