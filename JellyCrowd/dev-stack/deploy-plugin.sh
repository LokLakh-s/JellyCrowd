#!/usr/bin/env bash
# Build Jelly Crowd (Release) and deploy it into the local test Jellyfin's plugins folder, then restart
# the container. Linux / WSL / macOS — only Docker is required: the build runs inside a .NET SDK
# container, so you do NOT need dotnet installed on the host. Run from anywhere.
#
#   ./deploy-plugin.sh
set -euo pipefail

stack="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$stack/.." && pwd)"                       # project dir (holds the .csproj + build.yaml)
target="$stack/jellyfin/config/plugins/JellyCrowd"
csproj="Jellyfin.Plugin.JellyCrowd/Jellyfin.Plugin.JellyCrowd.csproj"

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: 'docker' not found. On WSL, enable Docker Desktop → Settings → Resources → WSL integration." >&2
  exit 1
fi

echo "==> Building Jelly Crowd (Release) in a .NET SDK 9 container..."
# Named volume caches the NuGet packages so re-runs are fast.
docker run --rm \
  -v "$repo":/src -w /src \
  -v jellycrowd_nuget:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build "$csproj" -c Release

bin="$repo/Jellyfin.Plugin.JellyCrowd/bin/Release/net9.0"
echo "==> Deploying to $target"
rm -rf "$target"
mkdir -p "$target"
# Same DLL set the CI ships: the plugin + its bundled runtime deps (MailKit/MimeKit/BouncyCastle).
# Jellyfin host assemblies are ExcludeAssets=runtime, so they are not in bin and won't be copied.
cp "$bin"/*.dll "$target"/

# meta.json so Jellyfin registers the plugin. Name/guid/version are read from build.yaml; targetAbi is
# forced to 12.0.0.0 so the RC server attempts to load it (build.yaml stays on 10.11 until the real bump).
yaml="$repo/build.yaml"   # build.yaml lives in the JellyCrowd dir, not the project subdir
yval() { grep -E "^$1:" "$yaml" | head -1 | sed -E 's/^[^:]+:[[:space:]]*"?([^"]*)"?[[:space:]]*$/\1/' | tr -d '\r'; }
cat > "$target/meta.json" <<EOF
{
  "guid": "$(yval guid)",
  "name": "$(yval name)",
  "version": "$(yval version)",
  "targetAbi": "12.0.0.0",
  "framework": "net9.0",
  "owner": "LokLakh-s",
  "category": "General",
  "overview": "Local Jellyfin 12 RC test build",
  "assemblies": ["Jellyfin.Plugin.JellyCrowd.dll"]
}
EOF

echo "==> Restarting Jellyfin container (if running)..."
docker restart jellycrowd-jellyfin >/dev/null 2>&1 \
  || echo "   (container not running yet — start it with: docker compose up -d)"

echo "Done. Watch load/runtime errors with:  docker logs -f jellycrowd-jellyfin"
