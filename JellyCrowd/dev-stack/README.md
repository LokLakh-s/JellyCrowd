# Jelly Crowd — local test stack (Jellyfin 12.0 RC2)

A throwaway local environment to test Jelly Crowd against **Jellyfin 12.0 RC2** and start the
compatibility work for the reworked 12.0 web UI.

## What's in it

| Service | Image | Port | Role |
|---|---|---|---|
| Jellyfin | `jellyfin/jellyfin:12.0-rc2` | 8096 | The server we test against |
| Radarr | `lscr.io/linuxserver/radarr` | 7878 | Movie requests dispatch here |
| Sonarr | `lscr.io/linuxserver/sonarr` | 8989 | Show requests dispatch here |
| Prowlarr | `lscr.io/linuxserver/prowlarr` | 9696 | Indexers for Radarr/Sonarr (optional) |

Everything is bind-mounted under this folder (`jellyfin/`, `radarr/`, … — all git-ignored). Delete those
folders to reset to a clean slate.

## Prerequisites

- **Docker** running (Docker Desktop with WSL integration enabled, or Docker Engine in WSL/Linux).
- For the **Windows** build script only: .NET 9 SDK + PowerShell 7 (`pwsh`) on the host.
  The **Linux/WSL/macOS** script builds inside a .NET SDK container, so **no host dotnet is needed**.

## Steps

1. **Build & deploy the plugin** into the Jellyfin plugins folder (copies the DLLs + a `meta.json` to
   `./jellyfin/config/plugins/JellyCrowd/`). Re-run after every code change — it restarts the container.

   - **Linux / WSL / macOS** (Docker only, no host dotnet):
     ```bash
     bash ./deploy-plugin.sh
     ```
   - **Windows** (needs .NET 9 SDK + `pwsh` on PATH):
     ```pwsh
     pwsh ./deploy-plugin.ps1
     ```

   > On WSL you ran into `pwsh: command not found` / `dotnet: not recognized` — that's expected: dotnet
   > lives on your Windows host, not inside WSL. Use `deploy-plugin.sh`, which builds in a container.

2. **Start the stack:**
   ```pwsh
   docker compose up -d
   ```

3. Open **http://localhost:8096**, finish the Jellyfin setup wizard (create an admin user).

4. **Install File Transformation** (the only hard dependency — without it the backend runs but the
   header UI does not appear). Dashboard → Plugins → Repositories → add:
   ```
   https://www.iamparadox.dev/jellyfin/plugins/manifest.json
   ```
   then Catalog → install **File Transformation**, and restart Jellyfin.
   ⚠️ See the compatibility note below — File Transformation must itself support Jellyfin 12.

5. **Configure Jelly Crowd** (Dashboard → Plugins → Jelly Crowd):
   - **TMDB API key** (v3, free) — required for the catalog.
   - **Radarr**: URL `http://radarr:7878`, API key from Radarr → Settings → General.
   - **Sonarr**: URL `http://sonarr:8989`, API key from Sonarr → Settings → General.
   - (Use the **service names** as hostnames — all containers share the compose network.)

6. Watch for load/runtime errors:
   ```pwsh
   docker logs -f jellycrowd-jellyfin
   ```

## Compatibility — what to expect (this is the point of the exercise)

The plugin currently targets **Jellyfin 10.11** (`build.yaml` `targetAbi: 10.11.0.0`, references
`Jellyfin.Controller`/`Jellyfin.Model` **10.11.10**). On 12.0 RC2, watch for, in order:

1. **File Transformation on 12.0** — it patches Jellyfin's `Startup` via Harmony, which is
   version-specific. If it has no 12.0-compatible release yet, the header injection won't run at all
   (and our UI won't appear) regardless of whether Jelly Crowd itself loads. Check its repo/releases first.
2. **Plugin load** — `deploy-plugin.ps1` writes `targetAbi: 12.0.0.0` in the dev `meta.json` so Jellyfin
   attempts to load the assembly. If it throws on load, it's a host-API break (10.11.10 → 12.0).
3. **Runtime API breaks** — controllers, auth policies, `ILibraryManager`/`ISessionManager` usage, etc.,
   may have changed. The logs will show the exact type/method.
4. **Reworked web UI** — `header.js` anchors its UI to the 10.11 web DOM (`.skinHeader`, `.headerLeft`,
   `.headerRight`, `.headerTabs`). 12.0 reworked the interface, so these selectors will need updating —
   the main front-end compat task.

None of these are fixed here; this stack just makes them reproducible so we can work through them.

## Reset / teardown

```pwsh
docker compose down            # stop containers (keeps data)
docker compose down -v         # stop + remove anonymous volumes
# To fully reset: stop, then delete the jellyfin/ radarr/ sonarr/ prowlarr/ media/ folders.
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
