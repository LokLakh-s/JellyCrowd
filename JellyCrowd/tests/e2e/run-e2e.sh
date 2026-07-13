#!/usr/bin/env bash
# End-to-end check: boot a REAL Jellyfin with a freshly-built Jelly Crowd and assert the things unit
# tests structurally cannot — that the plugin actually loads, its services resolve, its routes answer,
# its scheduled tasks register, its authorization holds, and its web shell is injected into the client.
#
#   ./run-e2e.sh                 # build, boot, assert, tear down
#   KEEP=1 ./run-e2e.sh          # leave the container up for poking around
#
# Requires: docker, a .NET SDK able to target net9.0, node. Nothing is assumed about the host network:
# the container joins a throwaway network which this runner attaches itself to, and the plugin is pushed
# in with `docker cp` (a bind mount would resolve against the DOCKER HOST's filesystem, not ours).
set -uo pipefail

JF_IMAGE="${JF_IMAGE:-jellyfin/jellyfin:10.11}"
NET=jc-e2e-net
NAME=jc-e2e-jellyfin
PORT="${PORT:-18096}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PLUGIN_DIR="$ROOT/Jellyfin.Plugin.JellyCrowd/bin/Release/net9.0"

# We may run on the host (a self-hosted CI runner) or inside a container (a dev shell). On the host we
# reach Jellyfin through a published port; in a container we join a throwaway network and address it by
# name — its published port lives on the HOST's loopback, not ours.
SELF=""
if docker inspect "$(hostname)" >/dev/null 2>&1; then
  SELF="$(hostname)"
fi

pass=0; fail=0
ok()   { printf '  \033[32m✓\033[0m %s\n' "$1"; pass=$((pass+1)); }
ko()   { printf '  \033[31m✗\033[0m %s\n' "$1"; fail=$((fail+1)); }
step() { printf '\n\033[1m%s\033[0m\n' "$1"; }

cleanup() {
  [ "${KEEP:-0}" = "1" ] && { echo "KEEP=1 → leaving $NAME up"; return; }
  docker rm -f "$NAME" >/dev/null 2>&1
  [ -n "$SELF" ] && docker network disconnect "$NET" "$SELF" >/dev/null 2>&1
  docker network rm "$NET" >/dev/null 2>&1
}
trap cleanup EXIT

step "Build the plugin"
( cd "$ROOT" && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build -c Release \
    Jellyfin.Plugin.JellyCrowd/Jellyfin.Plugin.JellyCrowd.csproj >/dev/null ) \
  && ok "built" || { ko "build failed"; exit 1; }

step "Boot a real Jellyfin ($JF_IMAGE) with the plugin"
KEEP=0 cleanup 2>/dev/null
docker network create "$NET" >/dev/null 2>&1
if [ -n "$SELF" ]; then
  docker network connect "$NET" "$SELF" >/dev/null 2>&1
  docker create --name "$NAME" --network "$NET" "$JF_IMAGE" >/dev/null || { ko "docker create"; exit 1; }
  BASE="http://$NAME:8096"
else
  docker create --name "$NAME" --network "$NET" -p "127.0.0.1:$PORT:8096" "$JF_IMAGE" >/dev/null || { ko "docker create"; exit 1; }
  BASE="http://127.0.0.1:$PORT"
fi

# Push the plugin in. Jellyfin loads any folder under /config/plugins.
docker cp "$PLUGIN_DIR" "$NAME:/tmp/jc" >/dev/null
docker start "$NAME" >/dev/null
docker exec "$NAME" sh -lc 'mkdir -p "/config/plugins/Jelly Crowd_e2e" && cp /tmp/jc/*.dll "/config/plugins/Jelly Crowd_e2e/"' >/dev/null
docker restart "$NAME" >/dev/null

# Wait on the container's OWN health check, not on an HTTP probe from here: the restart above hands back
# while the previous process is still serving 8096, so a probe would report "up" against a server that is
# about to die — and every assertion would then hit a connection-refused.
printf '  waiting for Jellyfin to become healthy'
for i in $(seq 1 90); do
  state=$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$NAME" 2>/dev/null)
  [ "$state" = "healthy" ] && break
  # No health check in the image: fall back to two CONSECUTIVE successful probes.
  if [ "$state" = "none" ] && curl -sf "$BASE/System/Info/Public" >/dev/null 2>&1; then
    sleep 2
    curl -sf "$BASE/System/Info/Public" >/dev/null 2>&1 && break
  fi
  printf '.'; sleep 2
done
echo
curl -sf "$BASE/System/Info/Public" >/dev/null 2>&1 && ok "Jellyfin is up" || { ko "Jellyfin never came up"; docker logs --tail 40 "$NAME"; exit 1; }

step "Complete the setup wizard + assert the plugin integrated"
BASE="$BASE" node "$ROOT/tests/e2e/assert.js"
rc=$?

step "No unhandled plugin exception in the server log"
if docker exec "$NAME" sh -lc 'grep -lE "Error|Exception" /config/log/*.log 2>/dev/null | head -1' | grep -q .; then
  hits=$(docker exec "$NAME" sh -lc 'grep -hE "JellyCrowd|Jelly Crowd" /config/log/*.log 2>/dev/null | grep -icE "\[ERR\]|Unhandled|Exception" || true')
  [ "${hits:-0}" -eq 0 ] && ok "no JellyCrowd error in the log" || {
    ko "$hits JellyCrowd error line(s) in the log"
    docker exec "$NAME" sh -lc 'grep -hE "JellyCrowd|Jelly Crowd" /config/log/*.log | grep -E "\[ERR\]|Exception" | head -5'
    rc=1
  }
else
  ok "no JellyCrowd error in the log"
fi

exit $rc
