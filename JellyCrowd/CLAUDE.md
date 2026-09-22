# CLAUDE.md — Jelly Crowd

Configuration agent pour ce dépôt. Lis ce fichier avant toute intervention.

## Overview

**Jelly Crowd** est un **plugin natif Jellyfin** (assembly .NET) qui apporte, intégré directement dans
Jellyfin, l'équivalent d'Overseerr :

1. **Catalogue de découverte TMDB** — parcourir/chercher films & séries, y compris ce qui n'est pas encore
   dans la bibliothèque, avec un marqueur « déjà disponible ».
2. **Requêtes utilisateur** — un user demande un média ; la requête passe par un **mode d'approbation**
   (manuel : file admin ; ou auto-approbation, avec mise en attente si quota dépassé). Le **fulfillment**
   se fait via des **backends de téléchargement** configurables : **Webhook**, **Radarr/Sonarr**, ou
   **script local** — avec auto-planification des sorties futures et **statut de DL en direct** (queue
   Radarr/Sonarr) + états *Missing/Unreleased*. (Granularité épisode ; watchlists ; calendrier ; recos.)
3. **Quotas disque par utilisateur** — chaque user a un quota (en octets) configurable ; ses requêtes
   satisfaites consomment son quota. Au-delà, nouvelles requêtes bloquées.

Ce n'est **pas** `jelly-quotas` (app externe React/Node à côté de Jellyfin) — c'est un plugin **dans** Jellyfin.

## Stack & versions

- **Cible annoncée : Jellyfin 12.** C'est la version pour laquelle le plugin est présenté et vers
  laquelle vont les nouveautés. **La rétrocompatibilité 10.11 est conservée** : même artefact, mêmes
  releases, et les deux majeures restent testées en E2E.
- **.NET 9** (`net9.0`), compilé contre les références **10.11.x** — **un seul artefact couvre Jellyfin
  10.11 ET 12.x** (vérifié : 12.1 charge l'assembly net9 et toutes les routes répondent). Le manifeste
  reste estampillé `targetAbi: 10.11.0.0` : un serveur accepte tout ABI **inférieur ou égal** au sien,
  donc cette seule entrée est proposée aux deux. **Ne pas monter l'ABI** — malgré l'annonce en 12, le
  passer à 12.x retirerait le plugin du catalogue des serveurs 10.11, c'est-à-dire romprait exactement
  la rétrocompatibilité qu'on garde.
- Références host : `Jellyfin.Controller`, `Jellyfin.Model` (`ExcludeAssets=runtime`, fournis par le host).
- UI user-facing : **aucun plugin tiers**. Jelly Crowd injecte son shell (`header.js`) dans `index.html`
  via son **propre middleware** (`WebInjectionStartupFilter` + `WebInjectionMiddleware`, au moment de la
  requête), puis **héberge lui-même** ses pages dans un overlay à onglets.
- Persistance : **fichiers JSON versionnés** (`VersionedJsonFile`, bornés en taille + rétention) dans le data folder du plugin. `Microsoft.Data.Sqlite` sert **uniquement** à lire la base de Playback Reporting lors de l'import.
- Catalogue : **API TMDB** (clé API requise, stockée en config plugin).
- Licence : **propriétaire** (tous droits réservés). Source privée ; distribution binaire via le dépôt public `jellycrowd-dist`.

## Layout

```
Jellyfin.Plugin.JellyCrowd/
  Plugin.cs                  # BasePlugin<PluginConfiguration>, IHasWebPages (page config admin, EnableInMainMenu)
  PluginServiceRegistrator.cs# DI : services + hosted services (injection header + stats + reconcile…)
  Configuration/
    PluginConfiguration.cs   # TMDB, quotas + overrides, estimations, rate limit, rétention, Discord/SMTP
    configPage.html          # page admin à onglets (Demandes/Quotas/Réglages/Notifications)
  Api/                       # contrôleurs ASP.NET ControllerBase (REST, /JellyCrowd/...)
    CatalogController.cs     # TMDB Trending/Search/Discover/Genres/Seasons/Details + flag Available + ItemId
    RequestsController.cs    # create/mine/all/approve/deny/RequestDeletion (403 quota, 409 dup, 429 rate)
    QuotaController.cs       # usage du user courant
    WebController.cs         # sert les assets embarqués Web/ (anonyme)
  Services/
    TmdbClient.cs / TmdbResponseParser.cs   # API TMDB + parsing pur (testé)
    LibraryMatcher.cs        # TMDB id -> item biblio (Exists/FindItemId/GetSizeBytes)
    JsonRequestStore.cs      # persistance JSON (pas SQLite) dans le data path
    QuotaService.cs          # usage (dédupliqué par titre) + enforcement
    CurrentUserAccessor.cs   # user courant via IAuthorizationContext
    NotificationService.cs / NotificationMessages.cs  # Discord + SMTP (+ builder pur testé)
    MediaDeleter.cs          # suppression disque via ILibraryManager.DeleteItem
    RequestReconciler.cs     # Approved -> Available quand le média arrive
    WebInjection*.cs         # WebInjectionMiddleware + WebInjectionStartupFilter : injectent header.js dans index.html (IStartupFilter, lecture seule)
    LibraryEventEntryPoint.cs # (hosted) ItemAdded -> reconcile temps réel (debounce)
  Tasks/
    ReconcileTask.cs         # IScheduledTask (15 min, backstop) -> RequestReconciler
    DeletionTask.cs          # IScheduledTask (1 h) -> supprime les médias échus
  Models/                    # DTOs (RequestRecord, CatalogItem, Genre, Season, QuotaInfo, *Dto, enums...)
  Web/                       # assets user embarqués : catalog/requests/mymedia (.html/.js), header.js,
                             # catalog.lib.js (logique pure testée), jellycrowd.css, logo.png, strings/{en,fr}.json

Jellyfin.Plugin.JellyCrowd.Segments/    # companion isolée (net9, SDK 10.11) : IMediaSegmentProvider
Jellyfin.Plugin.JellyCrowd.Segments12/  # MÊME source, compilée net10 contre le SDK 12.x
Jellyfin.Plugin.JellyCrowd.Tests/  # xUnit (+ Moq + node:test pour le JS) ; exécuté en CI
```

Persistance : **JSON** (`JsonRequestStore`, fichier dans le data path) — SQLite abandonné (dépendance native).
Réconciliation : **temps réel** sur `ILibraryManager.ItemAdded` + tâche planifiée de secours.

Racine : `CLAUDE.md`, `ROADMAP.md`, `README.md`, `LICENSE`, `build.yaml` (manifest plugin),
`Directory.Build.props`, `.editorconfig`, `jellyfin.ruleset`, `.sln`.

## Build / test

```powershell
dotnet build -c Release          # nécessite les SDK net9.0 ET net10.0 (cf. ci-dessous)
DOTNET_ROLL_FORWARD=Major dotnet test -c Release --no-build   # voir note ci-dessous
(cd tests/js && npm ci)          # une fois : la suite DOM a besoin de jsdom
node --test tests/js/*.test.js   # suite JS (logique pure des Web/*.lib.js)
```

> **Deux SDK** : le plugin, sa companion 10.11 et les tests sont en `net9.0` ; la companion Jellyfin 12 est
> en `net10.0` (`Jellyfin.Controller` 12.x ne publie que `lib/net10.0`). Le SDK 10 compile aussi bien le
> net9, donc un conteneur `mcr.microsoft.com/dotnet/sdk:10.0` suffit pour toute la solution — c'est ce
> qu'utilise `dev-stack/deploy-plugin.sh`.

> ⚠️ **Local** : si seuls les runtimes ASP.NET **net8/net10** sont installés (pas net9), le testhost net9
> ne démarre pas → préfixer par `DOTNET_ROLL_FORWARD=Major`. En CI (runner), `setup-dotnet` fournit le SDK
> net9 complet, donc `dotnet test` direct suffit.

Le `.dll` produit (`Jellyfin.Plugin.JellyCrowd/bin/Release/net9.0/`) se copie dans le data path Jellyfin :
`<jellyfin-data>/plugins/JellyCrowd_<version>/`. Redémarrer Jellyfin → le plugin apparaît dans
*Dashboard → Plugins* et expose sa page de config.

Pré-requis runtime côté Jellyfin pour les pages user : **aucun plugin tiers** — Jelly Crowd injecte
lui-même son interface dans le client web (`WebInjectionMiddleware` ajouté via un `IStartupFilter`).

## Conventions

- **Tout le code et les commentaires en anglais** (identifiants, commentaires, docs XML, messages de log,
  noms de tests). Le français est réservé à la doc projet (`CLAUDE.md`, `ROADMAP.md`, `README.md`) et aux
  chaînes traduites destinées aux utilisateurs (cf. i18n).
- **Indentation : 2 espaces**, pour tous les fichiers (C#, HTML, JS, YAML, XML/csproj). Imposé par `.editorconfig`
  (`indent_size = 2`).
- Namespace racine : `Jellyfin.Plugin.JellyCrowd`.
- Style imposé par `.editorconfig` + `jellyfin.ruleset` (StyleCop) ; `TreatWarningsAsErrors=true`.
- Licence **propriétaire** (pas d'en-tête GPL dans les fichiers) ; documentation XML sur les membres publics.
- DTOs dans `Models/` ; pas de logique métier dans les contrôleurs (déléguer aux `Services/`).
- GUID du plugin : `a1994160-4ea2-4d81-bd3c-ffe825700d98` (ne pas changer).

## Localisation (i18n) — suivre la langue de Jellyfin

**Le plugin doit afficher sa langue en fonction de la langue de Jellyfin**, pas une langue figée.

- Les chaînes destinées à l'utilisateur ne sont **jamais en dur** dans le code/HTML : elles vivent dans des
  catalogues de traduction par langue (`Web/strings/<lang>.json`, ex. `en.json`, `fr.json`).
- **Côté pages user (overlay hébergé)** : détecter la langue active de l'utilisateur Jellyfin (préférence utilisateur
  / locale du client web, fallback `navigator.language` puis `en`) et charger le catalogue correspondant ;
  fallback sur `en` pour toute clé manquante.
- **Côté serveur** (messages d'API/erreurs visibles par l'utilisateur) : prévoir aussi des chaînes localisables ;
  `en` par défaut.
- Langues de base : **en** (défaut/fallback) et **fr**. Ajouter une langue = déposer un nouveau fichier de
  catalogue, sans toucher au code.
- Toute nouvelle chaîne visible par l'utilisateur doit être ajoutée au moins à `en.json` (et idéalement `fr.json`)
  dans la même PR.

## Règle de tests (NON NÉGOCIABLE)

**Toute fonctionnalité ajoutée doit livrer ses tests dans la même PR, et la CI doit les exécuter.**
Concrètement, on n'ajoute rien sans couverture :

- **Chaque route/endpoint** (`Api/*Controller`) → test(s) couvrant le cas nominal + au moins un cas d'erreur
  (non autorisé, entrée invalide, quota dépassé…).
- **Chaque service** (`Services/*`) → tests unitaires de la logique (calcul d'usage, enforcement quota,
  matching biblio, parsing TMDB…).
- **Chaque bouton / interaction UI** → la logique métier déclenchée doit être testable et testée côté backend ;
  pour le comportement front (overlay hébergé), extraire le JS dans des fonctions pures testables et/ou ajouter un
  test e2e si pertinent. Pas de logique non testée cachée dans le HTML.
- Un PR qui ajoute du code sans test associé est considéré **incomplet**.
- La CI (`.github/workflows/build.yml`) lance `dotnet test` ; un test rouge **bloque** le merge.

Le projet de tests .NET vit dans `Jellyfin.Plugin.JellyCrowd.Tests/` (xUnit). Il référence les assemblies
Jellyfin **sans** `ExcludeAssets` pour disposer du runtime à l'exécution.

**Tests JS** : la logique front pure est isolée dans `Web/*.lib.js` (wrapper UMD : global navigateur +
module CommonJS) et testée via `node --test tests/js/*.test.js` (sans dépendance). La CI lance ces deux
suites (.NET + JS). Tout nouveau bouton/interaction expose sa logique dans un `*.lib.js` testé.

## Versionning automatique & CI/CD

- **CI** (`build.yml`) : sur push `main` et chaque PR → restore + build Release + `dotnet test` + package `.zip`.
- **Release** (`release.yml`) : sur push `main`, versionning sémantique auto piloté par un **mot-clé du message
  de commit** :
  - `[major]` ou `#major` → bump MAJEUR
  - `[minor]` ou `#minor` → bump MINEUR
  - sinon → bump PATCH
  - `[skip release]` → pas de release
  Le workflow estampille la version dans `Directory.Build.props` + `build.yaml`, commit `chore(release): vX.Y.Z [skip ci]`,
  pose le tag `vX.Y.Z`, et publie une **Release GitHub** avec le `.zip` du plugin + son `.md5`.
- Manifeste de dépôt installable **à la racine du monorepo** (`manifest.json`, 1 entrée/plugin par guid,
  3 versions max), mis à jour automatiquement par la Release.
- ⚠️ La Release pousse un commit sur `main` (`chore(release): … [skip ci]`) : **toujours `git fetch` +
  rebase avant de re-pousser**. Si une **protection de branche** est active, autoriser `github-actions[bot]`.

### Runner self-hosted (« JellyCrowd »)

Les workflows tournent sur un **runner self-hosted** pour économiser les minutes GitHub : `runs-on: [self-hosted]`
(le label `self-hosted` suffit ; le *nom* du runner n'est pas un label).

- **SDK non-root** : `setup-dotnet` installe par défaut dans `/usr/share/dotnet` → **Permission denied** sur
  un runner non-root. Fix en place : une étape `Configure .NET install dir` exporte
  `DOTNET_INSTALL_DIR=$RUNNER_TOOL_CACHE/dotnet` dans `$GITHUB_ENV` (inscriptible + **persistant**). Ne pas
  mettre ça en `env:` de job : le contexte `runner` y est interdit.
- **Outils hôte requis** : `zip` (build + release) et `jq` (release) doivent être installés sur l'hôte
  (`apt-get install -y zip jq`). `.NET`/Node sont fournis par `setup-dotnet`/`setup-node`.
- **Hygiène disque** : pas d'`actions/cache` (le `~/.nuget` et le tool-cache du runner persistent) ; étape de
  **cleanup `if: always()`** qui supprime `bin/obj/artifacts` et purge les caches NuGet http-cache + temp
  (une commande `dotnet nuget locals <emplacement> --clear` par emplacement — l'outil n'en accepte qu'un à la fois) ;
  rétention des artefacts CI = 7 j. `actions/checkout` nettoie déjà le workspace à chaque run.
- Si un job reste **`queued`** : le runner est probablement **offline** — le démarrer (`sudo ./svc.sh start`).

## Contraintes clés (à ne pas oublier)

- **Attribution des quotas** : Jellyfin ne sait pas « qui a demandé quoi ». C'est **Jelly Crowd** qui possède
  ce mapping (requête → item). Usage_user = Σ tailles fichiers des items liés à ses requêtes satisfaites.
  La taille réelle n'est connue qu'**après** satisfaction → enforcement à la création basé sur usage actuel +
  estimation configurable.
- **Compat** : ne pas casser 10.11 / net9. Les pages user sont **hébergées par Jelly Crowd** (overlay à
  onglets via `header.js`), injecté par **son propre middleware** (`WebInjectionStartupFilter` +
  `WebInjectionMiddleware` : sert `index.html` avec le `<script>` ajouté avant `</body>`, au moment de la requête).
- **Packaging (règle vitale pour Jellyfin 12)** : l'assembly companion Skip Outro est livrée en
  **`lib/Jellyfin.Plugin.JellyCrowd.Segments.dll.bin`** — jamais avec une extension `.dll`, nulle part dans le
  dossier du plugin. Jellyfin charge **tout `*.dll`** qu'il trouve sous ce dossier ; la companion cible
  `IMediaSegmentProvider` en SDK 10.11 et la 12 a déplacé ces types, donc son chargement lève une
  `TypeLoadException` et l'hôte **désactive le plugin ENTIER** (`Malfunctioned` : toutes les routes en 500,
  plus d'injection du shell). Deux protections qui semblent suffire ne le sont pas, c'est mesuré sur 12.1 :
  l'allowlist `assemblies` de `meta.json` est **réécrite en `[]`** (« scanne tout ») par une installation
  depuis le manifeste, et le scan est **récursif**, donc un simple sous-dossier est parcouru aussi. Seule
  l'extension sort le fichier du glob ; le plugin le charge par chemin via réflexion. Gardé par
  `Jellyfin.Plugin.JellyCrowd.Tests/Integration/CompanionAssemblyLayoutTests.cs`.
  ⚠️ Corollaire opérationnel : `Malfunctioned` est **persistant** (meta.json + base). Une fois le plugin
  cassé sur une instance, corriger le paquet ne suffit pas — il faut réinstaller.
- **Companion livrée en DEUX moitiés** : `Jellyfin.Plugin.JellyCrowd.Segments` (net9, SDK 10.11) et
  `Jellyfin.Plugin.JellyCrowd.Segments12` (net10, SDK 12.x). **Même fichier source**, lié par `<Compile
  Include>` — une seule implémentation, deux compilations. Le loader essaie la 12 puis la 10.11 et garde
  **la première qui se charge** : le runtime lui-même arbitre, donc une future majeure ne demandera pas de
  nouvelle branche ici. Pourquoi deux : en 10.11 l'interface n'avait pas `CleanupExtractedData`, donc le
  compilateur a émis la nôtre en **non-virtuelle** ; la 12 l'a ajoutée à l'interface et le runtime ne peut
  pas remplir un slot d'interface avec une méthode non virtuelle. Recompilée contre l'interface 12, elle
  sort en `virtual final`. `Jellyfin.Controller` 12.x ne publie que `lib/net10.0`, d'où le net10.
  Vérifié : Skip Outro produit bien son segment sur **10.11 et 12.1**.
- **Observabilité** : le probe avale toutes les erreurs par conception, donc la moitié réellement active est
  remontée par le diagnostic admin **« Media segments »** (`GET /JellyCrowd/Diagnostics`). Sans lui, un raté
  de packaging désactiverait Skip Outro sans que rien ne le signale.
- **API 12** : l'en-tête legacy `X-Emby-Authorization` est **refusé** (400) ; utiliser `Authorization` avec le
  même schéma `MediaBrowser`, accepté par 10.11 comme par 12. Les endpoints `/emby/` et `/mediabrowser/`
  n'existent plus.
- **Auth des contrôleurs (10.11)** : il n'existe PAS de policy nommée `DefaultAuthorization`. Pour un endpoint
  utilisateur authentifié → `[Authorize]` (policy par défaut). Pour un endpoint admin → `[Authorize(Policy = "RequiresElevation")]`.
  Assets statiques publics → `[AllowAnonymous]`.
- Implémentation **milestone par milestone** (voir `ROADMAP.md`) ; M0 = scaffold qui se charge dans Jellyfin.

## Décisions d'architecture validées

| Sujet | Choix |
|-------|-------|
| Fulfillment | Mode d'approbation (manuel/auto) + backends de DL : Webhook, Radarr/Sonarr, script local |
| Catalogue | TMDB (découverte) + croisement biblio Jellyfin |
| UI | Pages hébergées par Jelly Crowd (overlay à onglets via `header.js`), injectées par son propre middleware (`IStartupFilter`, au moment de la requête). Aucun plugin tiers. |
| Version | Annoncé pour Jellyfin 12, 10.11 toujours supporté. Un artefact (ABI 10.11.0.0) pour les deux ; companion média livrée en deux moitiés, net9 et net10 |
