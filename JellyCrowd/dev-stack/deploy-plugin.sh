#!/usr/bin/env bash
# Build Jelly Crowd (Release) once and deploy the SAME build to both test instances — Jellyfin 12 RC and
# Jellyfin 10.11 (backward-compat check) — then restart them. Linux / WSL / macOS; only Docker is
# required (the build runs in a .NET SDK container, so no host dotnet needed). Run from anywhere.
#
#   ./deploy-plugin.sh
set -euo pipefail

stack="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$stack/.." && pwd)"                       # project dir (holds the .csproj + build.yaml)
csproj="Jellyfin.Plugin.JellyCrowd/Jellyfin.Plugin.JellyCrowd.csproj"
bin="$repo/Jellyfin.Plugin.JellyCrowd/bin/Release/net9.0"
yaml="$repo/build.yaml"   # build.yaml lives in the JellyCrowd dir, not the project subdir

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: 'docker' not found. On WSL, enable Docker Desktop → Settings → Resources → WSL integration." >&2
  exit 1
fi

echo "==> Building Jelly Crowd (Release) in a .NET SDK 9 container..."
docker run --rm \
  -v "$repo":/src -w /src \
  -v jellycrowd_nuget:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build -c Release

yval() { grep -E "^$1:" "$yaml" | head -1 | sed -E 's/^[^:]+:[[:space:]]*"?([^"]*)"?[[:space:]]*$/\1/' | tr -d '\r'; }
GUID="$(yval guid)"; NAME="$(yval name)"; VERSION="$(yval version)"

# deploy <plugins-dir> <targetAbi> <container>
deploy() {
  local target="$1" abi="$2" container="$3"
  echo "==> Deploying to $target (targetAbi $abi)"
  rm -rf "$target"
  mkdir -p "$target"
  # Same DLL set the CI ships: the plugin + bundled runtime deps (MailKit/MimeKit/BouncyCastle). Jellyfin
  # host assemblies are ExcludeAssets=runtime, so they are not in bin and won't be copied.
  cp "$bin"/*.dll "$target"/
  # Isolated Skip Outro companion assembly — bundled in the folder but NOT listed in meta.json assemblies,
  # so Jellyfin never scans it (safe on a newer Jellyfin); the plugin loads it by path when compatible.
  cp "$repo/Jellyfin.Plugin.JellyCrowd.Segments/bin/Release/net9.0/Jellyfin.Plugin.JellyCrowd.Segments.dll" "$target"/
  cat > "$target/meta.json" <<EOF
{
  "guid": "$GUID",
  "name": "$NAME",
  "version": "$VERSION",
  "targetAbi": "$abi",
  "framework": "net9.0",
  "owner": "LokLakh-s",
  "category": "General",
  "overview": "Local test build",
  "assemblies": ["Jellyfin.Plugin.JellyCrowd.dll"]
}
EOF
  docker restart "$container" >/dev/null 2>&1 \
    || echo "   ($container not running yet — start it with: docker compose up -d)"
}

# Jellyfin 12 RC: needs an ABI it will accept (>= would be rejected; 12.0.0.0 matches the host).
deploy "$stack/jellyfin/config/plugins/JellyCrowd" "12.0.0.0" "jellycrowd-jellyfin"
# Jellyfin 10.11 backward-compat check: a 10.11 server rejects a higher targetAbi, so stamp 10.11.0.0.
deploy "$stack/jellyfin-1011/config/plugins/JellyCrowd" "10.11.0.0" "jellycrowd-jellyfin-1011"

echo "Done."
echo "  12 RC : http://localhost:8096   (logs: docker logs -f jellycrowd-jellyfin)"
echo "  10.11 : http://localhost:8097   (logs: docker logs -f jellycrowd-jellyfin-1011)"
