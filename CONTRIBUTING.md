# Contributing to Jelly Crowd

Thanks for taking an interest. Jelly Crowd is a native Jellyfin plugin — a .NET assembly that runs
*inside* the media server rather than beside it — and that shapes most of what follows.

By contributing, you agree that your work is licensed under the
[GNU Affero General Public License v3.0](LICENSE), like the rest of the project.

## Before you write code

Open an issue first for anything beyond a bug fix or a typo. Jelly Crowd deliberately ships one
integrated feature set rather than a plugin platform, so the useful conversation is usually about
whether a feature belongs in it at all — and that conversation is cheaper before the code exists.

Bug reports are always welcome with no preamble. The more of *your Jellyfin version, your Jelly Crowd
version, what you did and what you expected* a report carries, the faster it gets fixed.

## Getting set up

You need:

- **.NET SDK 10** — it also compiles the `net9.0` projects, so one SDK covers the whole solution.
  A `mcr.microsoft.com/dotnet/sdk:10.0` container works if you would rather not install it.
- **Node 22**, for the JavaScript test suite.
- **Docker**, if you want to run the end-to-end checks.
- **A Jellyfin 10.11+ server** to try your work against. `JellyCrowd/dev-stack/` has a
  `docker-compose.yml` that boots one (two, in fact — 10.11 and 12 — since a single artifact serves
  both) plus a `deploy-plugin.sh` that builds and installs into them.

```bash
cd JellyCrowd
dotnet build -c Release
dotnet test -c Release --no-build
(cd tests/js && npm ci)          # once
node --test tests/js/*.test.js
```

> If only the .NET 8 and 10 runtimes are installed and not 9, the `net9.0` test host will not start.
> Prefix the test command with `DOTNET_ROLL_FORWARD=Major`.

To try a build by hand, copy `JellyCrowd/Jellyfin.Plugin.JellyCrowd/bin/Release/net9.0/` — **including
its `lib/` subfolder** — into `<jellyfin-data>/plugins/JellyCrowd_<version>/` and restart Jellyfin.

## How the project is laid out

```
JellyCrowd/
  Jellyfin.Plugin.JellyCrowd/            the plugin itself (net9.0)
    Api/            ASP.NET controllers, routed under /JellyCrowd/
    Services/       all the logic — quotas, TMDB, fulfilment, notifications, stats
    Models/         DTOs, and nothing else
    Configuration/  the plugin config object and the admin Dashboard page
    Web/            the user-facing UI, embedded into the assembly as resources
  Jellyfin.Plugin.JellyCrowd.Segments/   media-segment companion, built against the 10.11 SDK (net9.0)
  Jellyfin.Plugin.JellyCrowd.Segments12/ the same source, built against the 12 SDK (net10.0)
  Jellyfin.Plugin.JellyCrowd.Tests/      xUnit suite
  tests/js/                              node --test suite for the pure front-end logic
  tests/e2e/                             boots a real Jellyfin and asserts against it
```

### Three things that look wrong and are not

**The companion assemblies ship as `lib/*.dll.bin`.** Jellyfin recursively globs `*.dll` under a plugin
folder and loads everything it finds. The media-segment interface moved between Jellyfin 10.11 and 12, so
whichever companion does not match the running server throws on load — and the host then disables the
*whole* plugin, not just that type. Neither a subfolder nor `meta.json`'s `assemblies` allowlist prevents
the scan. Only the extension does. The plugin loads the right half by reflection, by path, at runtime.

**`Segments` and `Segments12` are one file compiled twice.** `Segments12` links the same
`JellyCrowdSegmentProvider.cs` rather than copying it. Do not fork the implementation.

**`targetAbi` stays at `10.11.0.0`.** A Jellyfin server accepts any `targetAbi` at or below its own, so
that single stamp offers the plugin to 10.11 and 12 alike. Raising it would silently drop the plugin out
of every 10.11 server's catalog.

## House style

- **Code and comments in English**, always — identifiers, XML docs, log messages, test names.
- **Two-space indentation** everywhere: C#, HTML, JS, YAML, csproj. `.editorconfig` enforces it.
- StyleCop via `jellyfin.ruleset`, and `TreatWarningsAsErrors=true` on the plugin project. A warning is a
  build failure; please do not suppress one without saying why in the suppression.
- XML documentation on public members.
- Controllers stay thin: they validate, delegate to a service, and shape a response. Logic lives in
  `Services/`.
- Comments explain *why*, not *what*. The codebase leans on this heavily — when something looks strange,
  a comment should already tell you what bit us. Please keep that up.
- Do not change the plugin GUID (`a1994160-4ea2-4d81-bd3c-ffe825700d98`).

## User-visible strings are translated

No user-facing string is hard-coded. They live in `Web/strings/en.json` and `Web/strings/fr.json`, and the
UI follows the active Jellyfin language, falling back to `en` for any missing key.

Any new string goes into `en.json` at minimum, in the same pull request. Adding a language means adding a
catalogue file — no code change.

## Tests are not optional

**A feature without tests is an incomplete pull request.** Concretely:

- Every endpoint gets its happy path *and* at least one failure — unauthorized, invalid input, quota
  exceeded, whatever applies.
- Every service gets unit tests for its logic.
- Front-end behaviour gets extracted into a `Web/*.lib.js` (UMD: browser global and CommonJS module) and
  tested under `node --test`. No untested logic hiding in HTML.

CI runs both suites, plus an end-to-end check that boots a real Jellyfin — 10.11 and 12 — and asserts that
the plugin loads, its services resolve out of the host's DI, its routes answer, its authorization holds
against an actual non-admin user and its web shell reaches the client. You can run that yourself:

```bash
cd JellyCrowd && ./tests/e2e/run-e2e.sh        # KEEP=1 leaves the container up
```

## Commits and pull requests

Keep commits focused and write messages that explain the reasoning, not the diff.

**Sign the CLA.** On your first pull request a bot asks you to sign Jelly Crowd's
[Contributor License Agreement](CLA.md) by replying to its comment. It takes one line, it is recorded
once, and you are never asked again.

You **keep the copyright** on everything you write — the agreement grants a licence, it does not take
ownership. What it adds is the right to sublicense, which is what lets the project relicense itself later
without having to find every past contributor. Code already released under the AGPL-3.0 stays under the
AGPL-3.0, permanently; the agreement concerns future releases. [`CLA.md`](CLA.md) opens with a plain-words
summary before the legal text.

One caveat worth knowing before you open a pull request: **a merge into `main` publishes a release.** The
version bump is driven by a keyword in the commit message — `[major]`, `[minor]`, `[revision]`, or a patch
bump by default, with `[skip release]` to publish nothing. Maintainers handle that on merge; you do not
need to put a keyword in your commits.

## Security

Please do not open a public issue for a vulnerability. [`SECURITY.md`](SECURITY.md) explains how to report
one privately.
