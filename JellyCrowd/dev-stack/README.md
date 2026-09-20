# Jelly Crowd — local test stack (Jellyfin 12.1 + 10.11)

A throwaway local environment that runs **both** supported Jellyfin majors side by side, so the same plugin
build can be checked against each before a release.

## What's in it

| Service | Image | Port | Role |
|---|---|---|---|
| Jellyfin | `jellyfin/jellyfin:12.1` | 8096 | Current stable — the one to check new work against |
| Jellyfin | `jellyfin/jellyfin:10.11` | 8097 | Backward-compat check (production still runs this) |
| Radarr | `lscr.io/linuxserver/radarr` | 7878 | Movie requests dispatch here |
| Sonarr | `lscr.io/linuxserver/sonarr` | 8989 | Show requests dispatch here |
| Prowlarr | `lscr.io/linuxserver/prowlarr` | 9696 | Indexers for Radarr/Sonarr (optional) |

Everything is bind-mounted under this folder (`jellyfin/`, `jellyfin-1011/`, `radarr/`, … — all
git-ignored). Delete those folders to reset to a clean slate.

Both Jellyfin services run as `user: "1000:1000"`. The deploy script writes the plugin folder from the
host, and the official image would otherwise create `/config` as root and lock it out.

## Prerequisites

- **Docker** running (Docker Desktop with WSL integration enabled, or Docker Engine in WSL/Linux).
- For the **Windows** build script only: the .NET 9 **and** .NET 10 SDKs + PowerShell 7 (`pwsh`) on the
  host — the plugin is net9.0 and the Jellyfin 12 companion is net10.0.
  The **Linux/WSL/macOS** script builds inside a `dotnet/sdk:10.0` container (which targets net9.0 too),
  so **no host dotnet is needed**.

## Steps

1. **Build & deploy the plugin** into both instances. Re-run after every code change — it restarts the
   containers.

   - **Linux / WSL / macOS** (Docker only, no host dotnet):
     ```bash
     bash ./deploy-plugin.sh
     ```
   - **Windows** (needs the .NET 9 + .NET 10 SDKs and `pwsh` on PATH):
     ```pwsh
     pwsh ./deploy-plugin.ps1
     ```

2. **Start the stack:**
   ```bash
   docker compose up -d
   ```

3. Open **http://localhost:8096** (12.1) and **http://localhost:8097** (10.11) and finish each setup
   wizard.

4. **Configure Jelly Crowd** (Dashboard → Plugins → Jelly Crowd):
   - **TMDB API key** (v3, free) — required for the catalog. Without it the catalog routes answer `503`,
     which is the expected unconfigured state, not a failure.
   - **Radarr**: URL `http://radarr:7878`, API key from Radarr → Settings → General.
   - **Sonarr**: URL `http://sonarr:8989`, API key from Sonarr → Settings → General.
   - (Use the **service names** as hostnames — all containers share the compose network.)

5. Watch for load/runtime errors:
   ```bash
   docker logs -f jellycrowd-jellyfin        # 12.1
   docker logs -f jellycrowd-jellyfin-1011   # 10.11
   ```

## Compatibility — what this stack is guarding

One net9 build of the plugin itself, compiled against the 10.11 references, runs on **both** majors, and
the published manifest carries a single `targetAbi: 10.11.0.0` entry (a server accepts any ABI at or below
its own version, so both are offered the same package). What makes that work is a packaging rule that is
easy to undo by accident:

> The Skip Outro companion ships as **`lib/*.dll.bin`** — both halves, never with a `.dll` extension,
> anywhere in the plugin folder.

Jellyfin loads **every `*.dll`** it finds under a plugin folder, and each half of the companion throws a
`TypeLoadException` on the other's major — at which point the host disables the **whole plugin**:
`Malfunctioned`, every route `500`, no web shell. Two defences that look sufficient are not, both measured
against 12.1:

- `meta.json`'s `assemblies` allowlist is **rewritten to `[]`** ("scan everything") by an install from a
  repository manifest;
- the scan is **recursive**, so a plain subfolder gets walked too.

Only the extension takes the file out of that glob. `deploy-plugin.sh` therefore also deploys with
`"assemblies": []`, so the dev stack is never friendlier than a real install (set `JC_DEV_ALLOWLIST=1` to
keep the allowlist if you need to compare).

⚠️ `Malfunctioned` is **sticky** — recorded in `meta.json` and in the server database. Once an instance has
disabled the plugin, fixing the package is not enough: reinstall, or wipe the instance folder.

**Skip Outro works on both majors.** The companion is built twice from the same source —
`Jellyfin.Plugin.JellyCrowd.Segments` (net9, 10.11 SDK) and `Jellyfin.Plugin.JellyCrowd.Segments12`
(net10, 12.x SDK) — and the plugin loads whichever one the running host can, keeping the first that yields
the provider. The reason two are needed is small and sharp: 10.11's `IMediaSegmentProvider` had no
`CleanupExtractedData`, so the compiler emitted ours as a non-virtual method, and Jellyfin 12 — which added
it to the interface — cannot fill an interface slot with a non-virtual method.

Which half is live is reported by the **Media segments** check in Dashboard → Jelly Crowd → Diagnostics
(`GET /JellyCrowd/Diagnostics`). The loader swallows every mismatch on purpose, so that check is the only
place a packaging slip that silently disables Skip Outro shows up.

## Automated checks

```bash
cd ../tests/e2e
JF_IMAGE=jellyfin/jellyfin:12.1  PORT=18096 ./run-e2e.sh   # boots a clean server, asserts, tears down
JF_IMAGE=jellyfin/jellyfin:10.11 PORT=18097 ./run-e2e.sh
```

`run-e2e.sh` reproduces the *installed* layout (companion in `lib/` as `.bin`, plus a `meta.json` with
`"assemblies": []`), because a plugin folder without `meta.json` is a mode no real install uses — and
Jellyfin walks that one differently.

## Reset / teardown

```bash
docker compose down            # stop containers (keeps data)
docker compose down -v         # stop + remove anonymous volumes
# To fully reset: stop, then delete the jellyfin/ jellyfin-1011/ radarr/ sonarr/ prowlarr/ media/ folders.
```

## Recapturing the guide's screenshots

The guide shipped with the plugin must show a neutral server, never a real library. Bring the stack up,
deploy the plugin, sign in once, then:

```bash
npm --prefix ../tests/e2e ci     # once, for playwright
JC_BASE_URL=http://127.0.0.1:8096 JC_USER=Testor JC_PASSWORD='Test101!' node capture-guide-shots.mjs
```

It writes the shots into `Jellyfin.Plugin.JellyCrowd/Web/img/` and prints the `images` block to paste into
`Web/guide-content.json` (each step then declares its `figs`). An instance that wants its own screenshots
does not touch any of this — it drops them in its data folder, see `CONFIGURATION.md`.
