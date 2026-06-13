# Roadmap — Jelly Crowd

Plugin Jellyfin (Overseerr-like) : catalogue TMDB + requêtes en file admin + quotas disque par utilisateur.
Cible : **Jellyfin 10.11.x / .NET 9**. Implémentation milestone par milestone.

Légende : ☐ à faire · ☑ fait · ◐ en cours

---

## 📍 État actuel (point de reprise) — au 2026-06-13

- **Version publiée** : releases auto sur GitHub `LokLakh-s/jellyfin-plugins` (dernière `v0.5.28`). Branche `main`, CI **verte**.
- **Fait (code, M0→M6)** : catalogue TMDB enrichi (filtres double-sliders genres/années/notes, tri, survol, fiche complète avec affiche + liens TMDB/IMDb, clic dispo → fiche Jellyfin, **scroll infini + rangées de catégories/plateformes**) ; requêtes en file admin **par saison**, **annulables tant que Pending**, avec **date souhaitée** optionnelle (`DesiredAt`, socle pour l'auto-download) ; quotas disque par user (overrides, barre d'usage, refus 403/bouton grisé) ; **limite de requêtes par période** ; disponibilité **temps réel** (`IRequestReconciler` sur `ItemAdded`/`ItemRemoved` + tâche planifiée de secours) ; **« Mes médias » + suppression disque** après rétention, **multi-user aware** (tâche) ; **notifications Discord (embeds) + e-mail SMTP (MailKit, 465/587)** avec boutons de test ; **réglage de langue admin** (auto/en/fr) ; **logo** ; **page admin à onglets** (Demandes/Quotas/Réglages/Notifs) ; **manifest de dépôt** (MAJ auto).
- **UI auto-hébergée** : Jelly Crowd injecte son shell (`header.js`) via **File Transformation** (seule dépendance plugin) et **héberge ses propres pages** dans un overlay à onglets — **Plugin Pages retiré**.
- **En cours / prochaine action** : **M7 — Téléchargement automatique des requêtes** (intégration Servarr / scripts custom).
- **Bloqué côté agent (à faire par l'utilisateur)** : vérifs live — shell/onglets + thème, suppression (destructif, rétention courte), notifications (Discord/e-mail), séries par saison.

### Ce qui tourne déjà (vérifié en CI)
- Pipeline complet : **CI** (`build.yml` : restore → build Release → `dotnet test` → tests JS `node --test` → package `.zip`) + **Release** (`release.yml` : versionning auto par mot-clé de commit `[major]`/`[minor]`/patch → tag + GitHub Release).
- Backend TMDB : `TmdbClient`/`ITmdbClient`, `TmdbResponseParser`, `CatalogController` (`/JellyCrowd/Catalog/Trending|Search|Details`), DI via `PluginServiceRegistrator`.
- Frontend : `Web/catalog.html|js|css` + `Web/strings/{en,fr}.json`, servis par `WebController` (`/JellyCrowd/Web/...`), logique pure testée dans `Web/catalog.lib.js` (+ `tests/js/`).
- Injection web : `WebInjectionService` (réflexion) enregistre le callback File Transformation sur `index.html` ; `header.js` héberge le shell + les pages (overlay à onglets).

### Faits à se rappeler en reprenant (IMPORTANT)
- **Pas de SDK .NET sur la machine de dev** → on ne build/teste PAS en local. On valide en **poussant sur GitHub et en lisant Actions** (gh CLI absent → API REST ; le token est dans l'URL du remote, ne pas l'afficher).
- **Toujours `git pull --ff-only` après un push** : la Release pousse un commit `chore(release): vX.Y.Z [skip ci]`.
- **Analyseurs très stricts** (`TreatWarningsAsErrors`, `AllEnabledByDefault`, StyleCop, Nullable) → écrire défensivement du premier coup (chaque itération = un aller-retour CI).
- **Dépendances plugin** : **File Transformation uniquement** (intégré **par réflexion**, sans NuGet). **Plugin Pages a été retiré** (on héberge nos pages nous-mêmes). **Ne PAS internaliser File Transformation** : il patche `Startup.Configure` de Jellyfin via HarmonyLib avec du code spécifique par version → fragile + risque de conflit. Garder son API stable `RegisterTransformation`.
- ⚠️ Un **token GitHub** (`ghp_…`) est exposé dans la config git du remote — à révoquer si besoin.
- Règles projet (anglais, indentation 2, i18n suit la langue Jellyfin, tests obligatoires par fonctionnalité) : voir `CLAUDE.md`.
- M1 n'a pas eu son bump `[minor]` (le commit `[minor]` avait échoué au build, le correctif est passé en patch). Pour marquer M1 → faire un commit `[minor]` (donnerait `v0.2.0`).

---

## M0 — Scaffolding & build  ☑

Objectif : un plugin vide qui **compile et se charge** dans Jellyfin 10.11, avec une page de config admin.

- ☑ Structure du dépôt + fichiers racine (`CLAUDE.md`, `ROADMAP.md`, `README.md`, `LICENSE`, `.gitignore`).
- ☑ Projet .NET : `.sln`, `.csproj` (net9.0), `Directory.Build.props`, `.editorconfig`, `jellyfin.ruleset`.
- ☑ `build.yaml` (manifest plugin : guid, `targetAbi 10.11.0.0`, framework `net9.0`, artefact dll).
- ☑ `Plugin.cs` (`BasePlugin<PluginConfiguration>`, `IHasWebPages`) + `PluginConfiguration.cs` + `configPage.html`.
- ☑ CI GitHub : build `Release` + tests + package `.zip` + workflow Release (versionning auto).
- ☑ **Vérif** : build Release OK en CI ; release `v0.1.x` produite. *(Chargement réel dans le Dashboard : à confirmer avec la vérif M1 live.)*

## M1 — Catalogue TMDB  ◐ (code fait, reste la vérif live)

Objectif : parcourir et chercher le catalogue TMDB depuis une page user.

- ☑ `TmdbClient`/`ITmdbClient` (HttpClient, clé API depuis la config) + `TmdbResponseParser` (testé).
- ☑ `CatalogController` : `Trending`, `Search`, `Details/{type}/{id}` (auth, langue, 400/404/503) + tests.
- ☑ Champ clé API TMDB dans la page de config admin.
- ☑ Assets page user `catalog` (HTML/JS/CSS) + i18n en/fr, servis par `WebController` (testé).
- ☑ **Enregistrement Plugin Pages** (`PluginPageRegistrationService` par réflexion, tolérant à l'absence) + logique JS pure testée (`node:test`).
- ☐ **Vérif (instance live — à faire par l'utilisateur)** :
  1. Installer **File Transformation** (dépôt `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`). *(Plugin Pages n'est plus requis.)*
  2. Installer Jelly Crowd (`.zip` de la release v0.1.4), renseigner la **clé TMDB** dans la config du plugin.
  3. Vérifier que la page « Jelly Crowd » apparaît et liste films/séries (browse + recherche).
  4. Si les onglets/pages n'apparaissent pas : vérifier que **File Transformation** est installé, lire le log `WebInjectionService`, et ajuster les sélecteurs DOM dans `Web/header.js` si besoin.

## M2 — Requêtes (file d'attente admin)  ◐ (code fait, reste la vérif live)

Objectif : créer des requêtes et les gérer côté admin.

- ☑ `IRequestStore` + `JsonRequestStore` — **store JSON** (fichier atomique dans le data path du plugin), choisi plutôt que SQLite pour éviter une dépendance native (volume de requêtes faible). Champs : id, userId, tmdbId, type, titre, poster, statut, dates, décideur.
- ☑ `RequestsController` : `Create`/`Mine` (user, `DefaultAuthorization`), `All`/`Approve`/`Deny` (admin, `RequiresElevation`). Doublon → 409, invalide → 400, introuvable → 404. + `ICurrentUserAccessor` (sur `IAuthorizationContext`). Tests nominal + erreurs.
- ☑ Bouton « Demander » sur les cartes du catalogue + page user « Mes requêtes » (`requests.html`/`requests.js`), logique pure (`statusLabelKey`) testée dans `tests/js`.
- ☑ File d'approbation dans la page de config admin (liste des `Pending` + Approuver/Refuser), i18n.
- ☐ **Vérif (instance live)** : un user crée une requête depuis le catalogue, l'admin la voit dans la config et l'approuve/refuse, le statut se met à jour dans « Mes requêtes ».

## M3 — Disponibilité bibliothèque  ◐ (code fait, reste la vérif live)

Objectif : savoir ce qui existe déjà et résoudre automatiquement les requêtes satisfaites.

- ☑ `ILibraryMatcher`/`LibraryMatcher` : recherche d'un item par `HasAnyProviderId[Tmdb]` via `ILibraryManager.GetItemList` (movie→`Movie`, tv→`Series`). Tests (Moq).
- ☑ Flag `Available` renseigné sur les résultats du catalogue (`CatalogController` enrichit Trending/Search/Details). Le badge + le modal l'affichent déjà.
- ☑ `ReconcileTask` (`IScheduledTask`, intervalle 6 h, auto-découverte) : passe les requêtes `Approved` en `Available` quand le média est en biblio. Tests.
- ☐ **Vérif (instance live)** : un titre déjà présent apparaît « Disponible » dans le catalogue ; après ajout d'un média demandé en biblio, la tâche planifiée « Jelly Crowd: reconcile requests » le bascule en `Available`.

## M4 — Quotas disque par utilisateur  ◐ (code fait, reste la vérif live)

Objectif : limiter l'occupation disque par user et bloquer au-delà.

- ☑ `LibraryMatcher.GetSizeBytes` : taille fichier d'un film, ou somme des tailles d'épisodes d'une série.
- ☑ `QuotaService` : usage = Σ tailles des requêtes `Available` du user ; `CanRequestAsync` = usage réel + estimations des requêtes en cours + estimation de la nouvelle ≤ quota ; override user sinon défaut ; 0 = illimité. Tests.
- ☑ Config : `QuotaOverrides` (par user) + défaut + estimations ; édités via la page admin (liste des users + Gio).
- ☑ `QuotaController` : `GET /JellyCrowd/Quota/Me` (usage du user courant).
- ☑ Enforcement à la création (`RequestsController.Create` → **403** si dépassement). Test.
- ☑ Affichage usage/quota côté user (barre sur « Mes requêtes ») + feedback « Quota dépassé » sur le bouton Demander. Helpers `formatBytes`/`quotaPercent` testés.
- ☐ **Vérif (instance live)** : régler un quota bas pour un user, vérifier la barre d'usage, et qu'une requête au-delà du quota est refusée (403 → « Quota dépassé »).

## M5 — Finition & distribution  ☑

- ☑ Accès direct : page de config dans la nav du dashboard admin (`EnableInMainMenu`), à onglets (Demandes / Quotas / Réglages / Notifications).
- ☑ **Manifest de dépôt plugin** (`manifest.json`, peuplé à chaque release) + **logo** + doc d'install (MAJ auto).
- ☑ i18n FR/EN.
- ☑ **Notifications** (créée / approuvée / disponible) → Discord + e-mail (SMTP).
- ☑ **Limite de requêtes par période**, **séries par saison**, **« Mes médias » + suppression disque** (rétention), **réconciliation temps réel** (ItemAdded), **liens/quota dans le bandeau** (File Transformation).
- ☐ **Vérif (instance live)** : install via dépôt, suppression (rétention courte), header, saisons.

## M6 — Catalogue avancé  ☑

Objectif : un catalogue « Netflix-like » plus riche.

- ☑ **Scroll infini** : pagination TMDB (`page`) côté `Discover`/`Trending` + chargement au scroll (IntersectionObserver) côté catalogue.
- ☑ **Rangées de catégories** intercalées : carrousels « Top plateformes », « Best Sci-Fi », etc. (TMDB `with_watch_providers` + `watch_region`) ; clic sur une plateforme → filtre par plateforme.
- ☑ Endpoints/contrats : `with_watch_providers`, `watch_region`, `GET /JellyCrowd/Catalog/Providers` ; UI en grille continue + rangées.
- ☐ **Vérif (instance live)** : scroll infini fluide, rangées peuplées, filtre par plateforme.

## M7 — Téléchargement automatique des requêtes (Servarr / scripts custom)  ☐ ← PROCHAINE ÉTAPE

Objectif : déclencher **automatiquement le téléchargement** d'une requête une fois **approuvée** (et à partir de sa **date souhaitée** `DesiredAt`), via un backend configurable — **Radarr/Sonarr** ou un **script/webhook custom** — puis laisser la réconciliation existante basculer la requête en `Available` quand le média arrive en biblio.

Socle déjà en place : champ **`DesiredAt`** sur les requêtes (date souhaitée, défaut « maintenant »), réconciliation temps réel sur `ItemAdded`, notifications.

- ☐ **Abstraction** `IDownloadClient` (+ `Models/DownloadTarget`) avec un orchestrateur qui prend une requête approuvée et la dispatche au backend configuré. `NoopDownloadClient` par défaut (comportement actuel : file admin manuelle).
- ☐ **Config admin** (nouvel onglet « Téléchargement ») : sélecteur de backend (Aucun / Servarr / Script / Webhook) + réglages :
  - Servarr : URL + clé API **Radarr** (films) et **Sonarr** (séries), **dossier racine** + **profil de qualité** (listés via leur API), option *monitor/search now*.
  - Script : chemin d'un exécutable + gabarit d'arguments ; la requête est passée en **JSON sur stdin** et/ou variables d'environnement.
  - Webhook : URL POST + en-têtes optionnels ; corps = la requête (JSON).
- ☐ **Implémentations** : `RadarrSonarrDownloadClient` (lookup par `tmdbId` film ; série par `tmdbId`/`tvdbId`, **par saison** via `DesiredAt`/`Season`), `CustomScriptDownloadClient`, `WebhookDownloadClient`. Builders de payload **purs et testés**.
- ☐ **Déclenchement** : à l'approbation **si** `DesiredAt <= now`, sinon une **tâche planifiée** (`DownloadDispatchTask`) ramasse les requêtes `Approved` dont `DesiredAt` est échu et non encore dispatchées. Suivi via un champ `DispatchedAt` (idempotent, pas de double envoi).
- ☐ **États & notifs** : nouvel événement de notification « en téléchargement » (dispatch) ; la bascule `Available` reste pilotée par la réconciliation quand Servarr/le script a importé le fichier.
- ☐ **Erreurs** : backend injoignable / lookup introuvable / script en échec → log + statut requête inchangé (reste `Approved`, re-tentée au prochain passage de la tâche) ; surfaçage dans la file admin.
- ☐ **Tests** : builders de payload (Radarr/Sonarr/JSON script/webhook), mapping TMDB→cible, logique d'éligibilité (`DesiredAt`/`DispatchedAt`), contrôleur/tâche avec fakes. Côté JS : réglages d'onglet si UI testable.
- ☐ **Vérif (instance live)** : configurer Radarr/Sonarr (ou un script), approuver une requête (date du jour) → l'item est ajouté côté Servarr/script ; à l'import en biblio, la requête passe `Available`. Tester aussi une **date future** (dispatch différé par la tâche).

---

## Hors périmètre / idées futures

- Mapping d'identifiants avancé TMDB↔TVDB côté Sonarr si les lookups natifs ne suffisent pas.
- Recommandations personnalisées, watchlists.
- Intégration d'autres downloaders (qBittorrent direct, etc.) via le même `IDownloadClient`.
