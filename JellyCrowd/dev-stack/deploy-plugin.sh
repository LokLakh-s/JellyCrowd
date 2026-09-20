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

# SDK 10, not 9: the Jellyfin 12 companion targets net10.0 (Jellyfin.Controller 12.x ships lib/net10.0
# only), and the 10 SDK builds the net9.0 projects just as well — one container covers the whole solution.
echo "==> Building Jelly Crowd (Release) in a .NET SDK 10 container..."
docker run --rm \
  -v "$repo":/src -w /src \
  -v jellycrowd_nuget:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:10.0 \
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
  # Isolated Skip Outro companion, as lib/*.dll.bin — BOTH halves (10.11-built and 12-built); the plugin
  # loads whichever one the running host can. Jellyfin loads every "*.dll" under the plugin folder and each
  # half throws on the other's major, which would disable the whole plugin; the allowlist is rewritten to []
  # by a manifest install and the walk is recursive, so only the extension keeps them out.
  mkdir -p "$target/lib"
  cp "$bin"/lib/*.dll.bin "$target/lib/"
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
  # A real install from the repository manifest gets "assemblies": [] (scan everything) — so deploy the
  # dev stack that way too, or the stack would be friendlier than production and hide a disabled plugin.
  [ "${JC_DEV_ALLOWLIST:-0}" = "1" ] || python3 - "$target/meta.json" <<'PY'
import json, sys
p = sys.argv[1]
d = json.load(open(p))
d["assemblies"] = []
json.dump(d, open(p, "w"), indent=2)
PY
  docker restart "$container" >/dev/null 2>&1 \
    || echo "   ($container not running yet — start it with: docker compose up -d)"
}

# BOTH instances get targetAbi 10.11.0.0 — the same stamp the published manifest carries. A server accepts
# any targetAbi at or below its own version, so one artifact covers 10.11 and 12 alike, and stamping the dev
# 12 instance any other way would test a package we never ship.
deploy "$stack/jellyfin/config/plugins/JellyCrowd" "10.11.0.0" "jellycrowd-jellyfin"
deploy "$stack/jellyfin-1011/config/plugins/JellyCrowd" "10.11.0.0" "jellycrowd-jellyfin-1011"

echo "Done."
echo "  12.1  : http://localhost:8096   (logs: docker logs -f jellycrowd-jellyfin)"
echo "  10.11 : http://localhost:8097   (logs: docker logs -f jellycrowd-jellyfin-1011)"
