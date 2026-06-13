# Roadmap — Jelly Crowd

Plugin Jellyfin (Overseerr-like) : catalogue TMDB + requêtes en file admin + quotas disque par utilisateur.
Cible : **Jellyfin 10.11.x / .NET 9**. Implémentation milestone par milestone.

Légende : ☐ à faire · ☑ fait · ◐ en cours

---

## 📍 État actuel (point de reprise) — au 2026-06-13

- **Dépôt** : le plugin vit dans le monorepo **`LokLakh-s/jellyfin-plugins`**, sous **`JellyCrowd/`**
  (migré depuis l'ancien `Klakh/jelly-crowd`). **Manifeste de dépôt à la racine** du repo (1 entrée/plugin,
  3 versions max). URL dépôt : `https://raw.githubusercontent.com/LokLakh-s/jellyfin-plugins/main/manifest.json`.
- **Version publiée** : releases auto, dernière **`v0.6.0`**. Branche `main`, CI **verte**.
- **Fait (code, M0→M6)** : catalogue TMDB enrichi (filtres double-sliders genres/années/notes, tri, survol, fiche complète avec affiche + liens TMDB/IMDb, clic dispo → fiche Jellyfin, **scroll infini + rangées de catégories/plateformes**) ; requêtes en file admin **par saison**, **annulables tant que Pending**, avec **date souhaitée** optionnelle (`DesiredAt`) ; quotas disque par user (overrides, barre d'usage dégradée vert→rouge, refus 403/bouton grisé) ; **limite de requêtes par période** ; disponibilité **temps réel** (`IRequestReconciler` sur `ItemAdded`/`ItemRemoved` + tâche planifiée de secours) ; **« Mes médias » + suppression disque** après rétention, **multi-user aware** (tâche) ; **notifications Discord (embeds) + e-mail SMTP (MailKit, 465/587)** avec boutons de test ; **réglage de langue admin** (auto/en/fr) ; **logo** ; **page admin à onglets** (Demandes/Quotas/Réglages/Notifs/**Téléchargement**) ; **manifest de dépôt** (MAJ auto).
- **Fait (code, M7)** : **téléchargement auto des requêtes** — onglet « Téléchargement » avec sélecteur de
  backend piloté par menu déroulant : **Webhook** (POST JSON + tooltip d'exemple), **Radarr/Sonarr**
  (URL + clé API, dropdowns dossier/profil via « Connect », résolution TMDB→TVDB, recherche déclenchée).
  Déclenchement à l'approbation + tâche de secours, idempotent (`DispatchedAt`). **Auto-planification**
  des sorties futures à leur date (`RequestScheduling`) + badge « Planifié pour le … ».
- **Fait (code, M8)** : onglet **« Calendar »** (sorties à venir, films+séries, groupées par date).
- **UI auto-hébergée** : Jelly Crowd injecte son shell (`header.js`) via **File Transformation** (seule dépendance plugin) et **héberge ses propres pages** dans un overlay à onglets — **Plugin Pages retiré**. Liens header (Catalog/Calendar/My requests) rendus comme les onglets natifs Jellyfin.
- **En cours / prochaine action** : essentiellement les **vérifs en conditions réelles** (voir ci-dessous).
  Côté code, pas de milestone ouvert ; pistes futures dans « Hors périmètre ».
- **Bloqué côté agent (à faire par l'utilisateur — vérifs live)** : shell/onglets + thème ; suppression
  (destructif, rétention courte) ; notifications (Discord/e-mail) ; séries par saison ; **chaîne *arr**
  (Radarr/Sonarr : Connect → ajout + recherche) ; **webhook** (avec un récepteur) ; **Calendar** (liste
  des sorties + requête auto-planifiée).

### Ce qui tourne déjà (vérifié en CI)
- Pipeline complet : **CI** (`build.yml` : restore → build Release → `dotnet test` → tests JS `node --test` → package `.zip`) + **Release** (`release.yml` : versionning auto par mot-clé de commit `[major]`/`[minor]`/patch → tag + GitHub Release).
- Backend TMDB : `TmdbClient`/`ITmdbClient`, `TmdbResponseParser`, `CatalogController` (`/JellyCrowd/Catalog/Trending|Search|Details`), DI via `PluginServiceRegistrator`.
- Frontend : `Web/catalog.html|js|css` + `Web/strings/{en,fr}.json`, servis par `WebController` (`/JellyCrowd/Web/...`), logique pure testée dans `Web/catalog.lib.js` (+ `tests/js/`).
- Injection web : `WebInjectionService` (réflexion) enregistre le callback File Transformation sur `index.html` ; `header.js` héberge le shell + les pages (overlay à onglets).

### Faits à se rappeler en reprenant (IMPORTANT)
- **SDK .NET disponible en local** (dotnet 10.x build le `net9.0`) → **builder/tester en local AVANT de pousser** :
  `cd JellyCrowd && dotnet build -c Release` puis `DOTNET_ROLL_FORWARD=Major dotnet test -c Release --no-build`
  (le roll-forward sert car seul le runtime ASP.NET 10 est installé, pas le 9) + `node --test tests/js/*.test.js`.
  `gh` CLI absent (suivi CI via l'API REST si besoin ; le token est dans l'URL du remote, ne pas l'afficher).
- **Toujours `git fetch` + rebase après un push** : la Release pousse un commit `chore(release): vX.Y.Z [skip ci]` sur `main`.
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

**Avancement** :

- ☑ **Phase 1 — socle + Webhook** : champ `DispatchedAt`, abstraction `IDownloadClient`, `DownloadDispatcher` (résolution du backend actif + éligibilité `DesiredAt`/`DispatchedAt`), `WebhookDownloadClient` (POST JSON + en-têtes), déclenchement à l'approbation + `DownloadDispatchTask` (15 min), onglet admin « Téléchargement » (sélecteur de backend piloté par menu déroulant + bouton Test), `DownloadController`. Tests purs (builder/éligibilité/headers) + dispatcher + contrôleur + store.
- ☑ **Auto-planification à la sortie** : `RequestScheduling.ResolveDesiredAt` cale `DesiredAt` sur la date de sortie quand le média n'est pas encore sorti ; indicateur « Planifié pour le … » sur « Mes demandes ».
- ◐ **Phase 2 — Radarr + Sonarr** : client `ServarrDownloadClient` (lookup + add + search, résolution TMDB→TVDB pour Sonarr) + menus déroulants dossiers/profils auto-remplis. **PROCHAINE ÉTAPE**.

## M8 — Calendrier des sorties  ◐ (code fait, reste la vérif live)

Objectif : un onglet **« Calendar »** (entre Catalog et My requests) affichant un **calendrier des sorties**
à venir, films et séries confondus, depuis TMDB.

- ☑ Backend : `CatalogController.Calendar` — sorties à venir (`movie/upcoming` + `tv/on_the_air`) **ou**,
  avec `from`/`to`, toutes les sorties d'une plage via `discover` par date (`GetReleasesAsync`, films+séries,
  2 pages/type) ; filtre/ordonne (`CalendarPlanner.OrderUpcoming`/`OrderByDate`, purs + testés), croise la biblio.
- ☑ UI : `calendar.html`/`calendar.js` — **grille mensuelle** (lundi→dimanche) avec navigation mois
  précédent/suivant + « Aujourd'hui », affiches/titres dans la case du jour de sortie, clic → fiche (modal)
  avec requête/saisons/quota. Entrée de nav dans `header.js` entre Catalog et My requests.
- ☑ i18n (en/fr) + logique pure testée (`groupByReleaseDate` + `buildMonthMatrix` JS ; `CalendarPlanner` C#).
- ☐ **Vérif (instance live)** : la grille mensuelle se peuple, navigation entre mois OK, clic → fiche →
  requête (auto-planifiée à la date de sortie via `RequestScheduling`).

## M9 — Watchlists  ☐

Objectif : permettre à un utilisateur de **suivre des titres** (liste d'envies) sans forcément les demander.

- ☐ Modèle + store (par user, tmdbId/type) ; endpoints add/remove/list ; bouton « ★ » sur cartes/fiche.
- ☐ Page/onglet « Ma liste » (ou section) ; option « demander » depuis la watchlist.
- ☐ (Option) notif quand un titre suivi devient disponible / sort. Tests (store/endpoints/JS).

## M10 — Recommandations personnalisées  ☐

Objectif : suggérer des titres à partir de l'historique de visionnage Jellyfin et/ou des requêtes.

- ☐ Source : TMDB `recommendations`/`similar` à partir des derniers visionnages (Jellyfin) ou des requêtes
  de l'utilisateur ; agrégation/dédup ; croisement biblio.
- ☐ Rangée « Pour vous » dans le catalogue (ou onglet) ; logique pure testée.

## M11 — Backends de téléchargement additionnels  ☐

Objectif : couvrir les outils les plus répandus via la même abstraction `IDownloadClient`.

- ☐ Clients directs : **qBittorrent**, **Transmission**, **Deluge** (torrents) ; **SABnzbd**, **NZBGet** (usenet) ;
  via leurs API (URL + identifiants). Sélection dans l'onglet « Téléchargement » + bouton Test + builders testés.
- ☐ (Option) script custom (stdin JSON) déjà esquissé dans M7.
- Note : reste dans le cadre — Jelly Crowd émet vers le client de téléchargement, ne gère pas l'indexation.

## M12 — Canaux de notification additionnels  ☐

Objectif : élargir au-delà de Discord/SMTP, via une abstraction de notification.

- ☐ Canaux : **Telegram**, **ntfy**, **Gotify**, **Pushover**, **Slack**, **webhook générique**.
- ☐ Onglet Notifications : activer/configurer chaque canal + bouton Test ; builders de payload purs et testés.

## M13 — Gestion admin des demandes  ☐

Objectif : donner à l'admin la main sur les demandes (au-delà d'approuver/refuser).

- ☐ **Supprimer** une demande (n'importe laquelle) ; **éditer** (statut/saison/date souhaitée).
- ☐ **Créer une demande pour un autre utilisateur** (sélecteur d'utilisateur dans la file admin) — bypass
  quota/limite optionnel.
- ☐ Endpoints admin (`RequiresElevation`) + UI dans l'onglet Demandes ; tests (nominal + autorisation).

---

## Hors périmètre / idées futures

- Mapping d'identifiants avancé TMDB↔TVDB côté Sonarr si les lookups natifs ne suffisent pas.
- Vue calendrier par semaine/agenda ; calendrier des **épisodes** (pas seulement premières de séries).
- Intégration directe d'un lecteur de téléchargement supplémentaire non couvert par M11.
