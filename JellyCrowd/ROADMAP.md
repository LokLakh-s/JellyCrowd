# Roadmap — Jelly Crowd

Plugin Jellyfin (Overseerr-like) : catalogue TMDB + requêtes en file admin + quotas disque par utilisateur.
Cible : **Jellyfin 10.11.x / .NET 9**. Implémentation milestone par milestone.

Légende : ☐ à faire · ☑ fait · ◐ en cours

---

## 📍 État actuel (point de reprise) — au 2026-06-13

- **Dépôt** : le plugin vit dans le monorepo **`LokLakh-s/JellyCrowd`**, sous **`JellyCrowd/`**
  (migré depuis l'ancien `Klakh/jelly-crowd`). **Manifeste de dépôt à la racine** du repo (1 entrée/plugin,
  3 versions max). URL dépôt : `https://raw.githubusercontent.com/LokLakh-s/JellyCrowd/main/manifest.json`.
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

## M9 — Watchlists  ◐ (code fait, reste la vérif live)

Objectif : permettre à un utilisateur de **suivre des titres** (liste d'envies) sans forcément les demander.

- ☑ Modèle `WatchlistEntry` + `IWatchlistStore`/`JsonWatchlistStore` (par user) ; `WatchlistController`
  (`GET` mine, `POST` add, `POST Remove`) ; DI ; tests (store + contrôleur).
- ☑ Bouton **★** sur les cartes du catalogue et dans la fiche (catalogue + calendrier).
- ☑ **« Ma liste »** : bascule dans le catalogue (section) qui affiche les titres suivis, d'où l'on
  ouvre la fiche pour demander.
- ☐ (Option, plus tard) notif quand un titre suivi devient disponible / sort.
- ☐ **Vérif (instance live)** : ★ ajoute/retire ; « Ma liste » affiche les titres suivis.

## M10 — Recommandations personnalisées  ◐ (code fait, reste la vérif live)

Objectif : suggérer des titres à partir de l'historique de visionnage Jellyfin et/ou des requêtes.

- ☑ Source : TMDB `recommendations` à partir des **requêtes + watchlist** de l'utilisateur (graines),
  agrégation par fréquence (`RecommendationAggregator`, pur + testé), exclusion de ce qui est déjà
  demandé/suivi, croisement biblio. `CatalogController.Recommendations`.
- ☑ Rangée **« Pour vous »** en tête du catalogue (auto-masquée sans graines/résultats) ; logique pure testée.
- ☐ (Plus tard) inclure aussi l'historique de visionnage Jellyfin comme graines.
- ☐ **Vérif (instance live)** : après quelques requêtes/★, la rangée « Pour vous » se peuple.

## M11 — Backend « script local »  ◐ (code fait, reste la vérif live)

Réévalué : les **clients de téléchargement directs** (qBittorrent, Transmission, Deluge, SABnzbd, NZBGet)
attendent un **magnet/torrent ou un NZB** que Jelly Crowd **ne peut pas produire** (pas d'indexeur) — c'est
le rôle de **Radarr/Sonarr** (M7), déjà couvert. Les implémenter directement sortirait du cadre. À la place :

- ☑ Backend **« script local »** (`ScriptDownloadClient`) : exécute un exécutable/script configuré pour
  chaque requête approuvée, requête en **JSON sur stdin** + variables `JELLYCROWD_*`. L'admin y branche sa
  propre logique (qBittorrent, SABnzbd…). Abstraction `IProcessRunner` (testable) + `ProcessRunner`.
  Option dans l'onglet « Téléchargement » + bouton Test. Tests (runner simulé + IsConfigured).
- ☐ **Vérif (instance live)** : configurer un script, approuver une requête → le script reçoit le JSON.
- **Hors périmètre** : clients de téléchargement directs sans indexeur (utiliser Servarr ou le script).

## M12 — Canaux de notification additionnels  ◐ (code fait, reste la vérif live)

Objectif : élargir au-delà de Discord/SMTP, via une abstraction de notification.

- ☑ Abstraction `ITextNotifier` (titre+corps) + canaux : **Telegram**, **ntfy**, **Gotify**,
  **Pushover**, **Slack**, **webhook générique** ; branchés dans `NotificationService` (événements +
  bouton Test via `NotificationsController.Test/{channel}`). Tests (handler stub par canal + IsConfigured).
- ☑ Onglet Notifications : champs par canal + bouton Test par canal.
- ☐ **Vérif (instance live)** : configurer un canal, bouton Test, puis recevoir une notif d'événement.

## M13 — Gestion admin des demandes  ◐ (code fait, reste la vérif live)

Objectif : donner à l'admin la main sur les demandes (au-delà d'approuver/refuser).

- ☑ **Supprimer** une demande (n'importe laquelle) ; **éditer** statut + saison/épisode + date souhaitée.
- ☑ **Créer une demande pour un autre utilisateur** : sélecteur admin **« Demander au nom de »**
  directement sur la fiche du catalogue/calendrier (s'applique aux boutons film/saison/épisode) —
  bypass quota/limite, statut Approuvé (déclenche le dispatch).
- ☑ Endpoints admin (`RequiresElevation`) : `Requests/{id}/Delete`, `Requests/{id}/Edit`,
  `Requests/ForUser` ; store `AdminUpdateAsync` ; UI : suppr/statut/date dans l'onglet Demandes +
  sélecteur « au nom de » dans le modal (admin uniquement) ; tests.
- ☐ **Vérif (instance live)** : supprimer/éditer une demande ; créer une demande pour un autre user.

## M14 — Granularité épisode  ◐ (code fait, reste la vérif live)

Objectif : demander un **épisode** seul, garder le bouton **saison entière** (pas série entière), et un
**calendrier par épisode**.

- ☑ **Partie A — Requêtes épisode** : champ `Episode` (demande + dédup par épisode) ; endpoint
  `Catalog/Episodes/{tmdbId}/{season}` (TMDB, dates de diffusion) ; fiche série = liste d'épisodes
  (bouton par épisode) + bouton « Demander la saison » qui, si la saison a des épisodes non sortis,
  crée **une demande par épisode planifiée à sa date de diffusion** (sinon une demande de saison).
  Affichage `S2E3` dans Mes demandes + file admin. Tests (parser/dédup/contrôleur).
- ☐ **Partie B — Calendrier par épisode** : la grille mensuelle liste les **épisodes** des **séries
  suivies** (demandées ou en biblio) + sorties de films. Récupération par série (pas d'endpoint global
  TMDB) ; bornage aux séries suivies de l'utilisateur. Logique pure testée + vérif live.
- ⚠️ Limite connue : la réconciliation/disponibilité reste **au niveau série** (un épisode passe
  `Available` quand la série est en biblio) ; matching par épisode = amélioration future.

---

## Feuille de route détaillée — vers la 1.0.0, puis 2.0.0 / 3.0.0

> Issu des notes manuelles de Victor + brainstorm, mis en forme en étapes de dev.
> Tags de version = **estimations** : chaque milestone = un bump **mineur** (`[minor]`) ;
> la **`v1.0.0`** sera coupée (`[major]`) une fois l'ensemble **M15→M29** livré.
> Milestones ordonnés par **priorité** (valeur + déblocage).
>
> **État (2026-06-21)** : **M15→M26 + M29 livrés**, ainsi que **M27.B** (expiration des médias par âge).
> Un **gros lot de patches de finition** a suivi (voir « Patches de finition pré-1.0 » plus bas) :
> refonte du popup catalogue, **responsive/mobile** (overlay, listes, tableaux admin), **calendrier
> multi-vues** (Mois/Semaine/Jour), **dispatch idempotent** (fin du flapping « Blocked/400 »),
> **annulation qui stoppe le grab RDT** (file Radarr), **logs** (recherche + entrées admin/user),
> **Prowlarr** configurable, **accès plugin par utilisateur**, **packaging MailKit**, **persistance
> config** (genres/quotas), opt-in notifs e-mail par catégorie, avis & notes (M29).
> **Reste pour la 1.0** : **M27.A** (quotas adaptatifs), **M28** (réactivité/exactitude quota temps réel),
> le **reliquat de patches** (liste dédiée plus bas + Notes de Victor), et la **stabilisation v1.0**
> (responsive/a11y final, doc utilisateur, tests e2e/non-régression, polish).
> Sous-points reportés : calendrier épisodes (M24.B), canaux stable/nightly (M26.2).

### M15 — Téléchargement : correctifs & échecs  ☑ *(livré)*

Objectif : fiabiliser la chaîne de fulfillment (bug bloquant en tête).

- ☑ **Bug bloquant** (saison aux champs vides chez Prowlarr) : `ServarrPayload.BuildSeriesAdd` **monitore explicitement la saison demandée** + `addOptions.searchForMissingEpisodes = true`, et **évite volontairement** `monitor: none` (qui démonitorait la saison voulue → recherche vide). L'échec terrain initial était une **config Sonarr** (corrigée).
- ☑ État **« Bloqué »** distinct (badge rouge sur une requête approuvée portant une `DispatchError`, raison au survol) + action **« relancer la recherche »** (user) : `MoviesSearch`/`SeasonSearch`/`SeriesSearch` côté Radarr/Sonarr (ou ré-envoi pour webhook/script), erreur effacée au succès.
- ⤳ *Optionnel (hors périmètre 1.0, non bloquant)* : **poller la progression depuis rdt-client** plutôt que Radarr/Sonarr. Gain marginal (la file Radarr/Sonarr expose déjà la progression), matching flou par nom, et nécessite l'URL + l'auth du rdt-client interne. À faire seulement sur demande.

### M16 — Permissions, rôles & règles d'auto-approbation  ☑ *(livré)*

Objectif : faire de JellyCrowd un vrai outil **multi-utilisateur**.

- ☑ **Activer/désactiver les requêtes** par utilisateur ou par rôle ; catalogue réservable à certains.
- ☑ **Plafonds par utilisateur/rôle** (quota disque, nombre de requêtes/période) en surcharge du global.
- ☑ **Règles d'auto-approbation** : **taille < X Go** + **utilisateurs de confiance** + **critère genre** (liste de genres TMDB qui restreint l'auto-approbation par taille ; les utilisateurs de confiance la contournent).

### M17 — Notifications utilisateur & centre de notifications  ☑ *(livré)*

Objectif : prévenir **le demandeur** (aujourd'hui tout part vers les canaux admin uniquement).

- ☑ **Cloche** dans le bandeau, **juste à gauche du quota**, avec **pastille rouge + compteur** (style Facebook).
- ☑ Menu déroulant = **journal de notifications** de l'utilisateur, **effaçable** : *clear all* + **bouton « × » par item** (UI).
  - ☑ **Borné** : plafond par utilisateur (50) + TTL (30 j).
- ☑ Notifier le demandeur sur **Approved / Denied / Available / Échec** (`NotificationEvent.Failed` émis au **premier** échec de dispatch, in-app + canaux perso).
- ☑ **Préférences de notif par utilisateur** (son propre canal : e-mail perso, topic ntfy, etc.) en plus de l'in-app.

### M18 — Diagnostic & santé  ☑ *(livré)*

Objectif : tuer 90 % du support « mauvaise config » d'un plugin self-hosted.

- ☑ Onglet **Diagnostic** : **clé TMDB**, **File Transformation**, **joignabilité du backend** (Radarr/Sonarr/webhook/script), **accès en écriture** au dossier, et **check indexer** (≥ 1 indexer activé côté Radarr/Sonarr).
- ☑ Surfacer chaque échec avec un message clair + piste de résolution.
- ☑ **Empreinte disque du plugin** : taille de chaque store + total + **estimation de croissance** (~/utilisateur · ~/mois d'après les requêtes des 30 derniers jours).

### M19 — Robustesse données & sécurité  ☑ *(livré)*

Objectif : le « non-fonctionnel » qui sépare une 0.x d'une 1.0.

- ☑ **Verrouillage concurrent** des stores JSON (SemaphoreSlim) + **versionnage de schéma + migrations** (`VersionedJsonFile` : enveloppe `{ schemaVersion, items }`, lecture rétro-compatible du format legacy, hook de migration).
- ◑ **Export / sauvegarde** des **requêtes** fait ; **config exclue volontairement** du bundle (pas de secrets).
- ☑ **Cache TMDB & affiches** (respecter les quotas d'API, réduire la latence).
- ☑ **États vides & erreurs gracieuses** partout (backend down, réseau, 0 résultat).
- ☑ **Passe sécurité** : auth/élévation sur chaque endpoint + anti-XSS (`textContent`) + pas de fuite de clé + **rate-limit HTTP** (limiteur glissant par utilisateur sur les écritures, configurable, admins exemptés) en plus du cap requêtes/période.

### M20 — Finition du shell & navigation  ☑ *(livré)*

Objectif : que les écrans du plugin se fondent dans l'UI native de Jellyfin.

- ☑ Logo JellyCrowd **plus grand** dans le titre de chaque onglet.
- ☑ Liens d'onglets (Catalog / Calendar / My requests / My media) plus **ressemblants aux liens natifs** du bandeau Jellyfin, et **centrés**.
- ☑ Remplacer la **croix** de fermeture par une **flèche de retour** cohérente avec Jellyfin, **du même côté**.
- ☑ Afficher le **badge utilisateur** dans le bandeau des écrans du plugin (là où se trouve la croix aujourd'hui).

### M21 — « Mes médias » enrichi & liens cliquables  ☑ *(livré)*

- ☑ Afficher la **taille** de chaque média dans *My media* + y ajouter la **barre de quota**.
- ☑ Afficher le **temps restant avant suppression** sur les médias en *Deletion requested*.
- ☑ Rendre **cliquable** chaque média de *My media* et chaque requête *Available* → ouvre le média dans Jellyfin.

### M22 — Cycle de vie des requêtes  ☑ *(livré)*

- ☑ Tant qu'une requête est **Approved**, l'utilisateur peut la **supprimer** — en s'assurant qu'elle n'est plus traitée par Prowlarr/Sonarr/Radarr + RDT (annulation propagée en amont).
- ☑ Afficher sur chaque requête la **date/heure de la demande** et la **date/heure de mise à disposition** dans Jellyfin.
- ☑ Quand une requête est **Approved + Downloaded** (téléchargée, en attente de scan Jellyfin), refléter un statut « disponible dans < 2 min » : l'état live `completed` (fichier présent côté Radarr/Sonarr, requête encore Approved) s'affiche « Téléchargé · dispo dans <2 min ».

### M23 — Propriété partagée des médias  ☑ *(livré)*

Objectif : un même média peut « appartenir » à plusieurs utilisateurs, avec quota et suppression cohérents.

- ☑ Un utilisateur peut **s'ajouter un média déjà disponible** (déjà demandé par un autre) à ses médias, avec **avertissement** qu'il compte dans son quota.
- ☑ Modèle de **propriété multi-utilisateur** d'un même média.
- ☑ Sur demande de suppression par un propriétaire : retirer **son** appartenance en fin de délai + **décrémenter son quota** ; le média n'est **réellement supprimé** que s'il n'appartient **plus à personne** à la fin du délai.
- ☑ Pouvoir **annuler une demande de suppression** tant qu'on est à **plus d'une minute** de l'échéance ; le quota n'est pas décrémenté tant que le média « appartient » encore.

### M24 — Catalogue & Calendrier  ☑ *(livré)*

- ☑ Bouton **« Voir plus → »** sur chaque sous-section de *Browse* (For you, Netflix, Apple TV, …) pour n'afficher que les médias de cette sous-section.
- ☑ **« Demander toute la saga »** via les **collections TMDB** (toute la franchise en un clic).
- ☑ Le **calendrier** affiche aussi les **épisodes de séries** : épisodes (saison en cours) des séries **demandées + suivies (watchlist)** qui diffusent dans le mois affiché.

### M25 — Communauté : commentaires & signalements  ◑ *(livré en partie)*

- ☑ **Commentaires** sur chaque film/série du catalogue, dans le **popup, sous le Synopsis**.
- ☑ **Avis internes (notes + texte) visibles sur la fiche native Jellyfin** (film/série) : panneau « Reviews » injecté via `header.js` (déclenché sur navigation, défensif, gated par CommentsEnabled) — moyenne + saisie 1–10 + liste anonymisée. *(M25.2 — à vérifier en live)*
- ☑ **Signalements** : un utilisateur signale un souci sur un média (mauvaise VF, sous-titres manquants…) → **file admin** dédiée.
- ☑ Modération admin des commentaires/signalements (masquer/supprimer).

### M26 — Observabilité & canaux de version  ◑ *(livré : `v0.30.0`, M26.2 reporté)*

- ☑ **Logging global** : journal d'activité interne (`IActivityLog` / `JsonActivityLog`) — événements de requêtes (cycle de vie) + dispatch téléchargement (succès/échec).
- ☑ **Rotation & rétention des logs** : borne par cap récent (2000 entrées) + fenêtre de rétention (30 j) avec purge à chaque écriture — jamais de log non borné.
- ☑ Onglet **Logs** dans le panel admin avec **recherche par terme** + **filtres** (catégorie / niveau).
- ☐ Logique de canaux **« stable » / « nightly »** (idéalement automatique côté CI/release). *(reporté — M26.2)*

### M27 — Quotas adaptatifs & expiration des médias  ◑ *(A: à faire · B: livré)*

> Rien de tel n'existe aujourd'hui (vérifié) : le quota est **fixe** (`DefaultUserQuotaBytes` +
> surcharges `QuotaOverrides`), aucune notion d'activité/temps de visionnage, aucune expiration par âge.

#### A. Quotas adaptatifs (hystérésis) — **option globale désactivable**

- ☐ **Interrupteur global** on/off. Désactivé = comportement actuel (quota fixe).
- ☐ **Trois paliers configurables** : quota de **base** (déf. 50 Go), **plafond actif** (déf. 100 Go), **plancher inactif** (déf. 20 Go).
- ☐ **Hystérésis** : montée **rapide** vers le plafond selon l'activité, descente **lente et conditionnelle**.
- ☐ **Inactivité = pas de sanction immédiate** mais état de **« sursis »** (probation). Le quota cible théorique baisse, mais **n'est appliqué qu'à la reconnexion**.
- ☐ **Workflow au retour** : à la 1ʳᵉ connexion après longue inactivité → détection du dépassement théorique → **compte à rebours** (déf. 14 j) en **gelant** le quota courant pendant le sursis.
- ☐ **Notification** au retour : « Ravi de te revoir ! En raison d'une longue période d'inactivité, ton quota va être ajusté. Reprends ton activité pour le conserver. »
- ☐ **Validation du retour = 2 critères CUMULATIFS** sur la fenêtre de sursis :
  - **Volume** : minutes cumulées min (déf. **3 h** de visionnage total).
  - **Régularité** : activité répartie sur **≥ N jours distincts** (déf. **3 jours** sur 14). *(Regarder un média 10 min ne suffit pas.)*
- ☐ **Récompense historique** : si échec au test après 14 j, le quota cible redescend au quota de **base** (50 Go), **pas** au plancher (20 Go).
- ☐ **Exemple** (utilisateur à 98 Go après 2 mois) : J1 → sursis + gel à 98 Go + message ; pendant 14 j : succès → redevient actif, quota adaptatif repart ; échec → J15 plafond = 50 Go → **en dépassement (98/50)**, ne peut plus rien **ajouter** (médias existants conservés jusqu'à action/expiration).
- ⚠️ **Dépendance** : nécessite une **brique de suivi d'activité / temps de visionnage** (minutes + jours distincts par user) — **ABSENTE** aujourd'hui. Pistes : API sessions/lecture Jellyfin (`UserData`/`LastPlayedDate`), base du plugin **Playback Reporting** (présent sur le serveur), ou suivi minimal maison. À cadrer (lien avec le pan stats 2.0 / M30).
- ☐ **Borné (budget stockage)** : ne stocker que des **agrégats** par user (minutes/jour, dernier vu, palier courant, état de sursis + échéance), **jamais** les ticks bruts.
- ☐ **Visibilité** : palier courant + état « sursis » + compte à rebours dans la **barre de quota** et **Mes médias** ; surfacer côté **admin** (table des quotas) + **logs** (M26).

#### B. Expiration automatique des médias par âge  ☑ *(livré)*

- ☑ **Délai d'expiration configurable** (global `MediaExpiryDays`, **`0` = infini**). *(Surcharge par user/rôle : non fait — option future.)*
- ☑ Le **décompte démarre quand le média devient `Available`** (ou au dernier *claim*/renouvellement).
- ☑ À expiration → l'appartenance **lapse** (libère le quota) via `DeletionTask.ExpireOwnershipsAsync` ; le fichier reste géré par le pipeline de suppression on-demand + propriété partagée (M23).
- ☑ **Avertissement** : *My library* affiche « Expire dans … » + bouton **« Conserver (renouveler) »** (reset du compteur). À l'expiration effective, **notification** « média expiré » au propriétaire (catégorie quota/expiration).
- ☑ **Tâche planifiée** idempotente (`DeletionTask`) balayant les `Available` dont l'âge dépasse le délai (bornée).

### M28 — Réactivité de l'UI & exactitude temps réel du quota  ☐ *(périmètre 1.0)*

> Investigation (2026-06-20) : l'enforcement est **déjà** raisonnablement sûr — `CanRequestAsync`
> compte les requêtes **en vol** (Pending/Approved) via des **estimations** (`EstimateBytes`), donc
> l'exploit « enchaîner des requêtes pour dépasser » est en grande partie **déjà bloqué au niveau
> décision**. La lenteur perçue est surtout un **problème d'affichage** : la barre de quota
> (`header.js → buildQuota`) ne fetch `JellyCrowd/Quota/Me` **qu'une fois à la construction**,
> sans refresh après action ni polling. La taille **réelle** d'un titre n'est connue qu'après le
> **scan Jellyfin** (`item.Size`) + reconcile (debounce 20 s, fallback 15 min) — borné par la
> cadence de scan de Jellyfin, **pas** par un cache du plugin (il n'y en a aucun).

- ☐ **Rafraîchir la barre de quota** immédiatement après chaque **création / annulation / claim** de requête, et au **changement de vue** (catalog / requests / mymedia).
- ☐ **Léger polling** de la barre tant qu'une requête est **en vol** (réutiliser le polling de statut DL déjà à 3 s).
- ☐ **Enforcement** : toujours utiliser `max(estimation, taille partielle connue)` ; rendre les **estimations conservatrices + configurables** ; *(option)* déclencher un **scan ciblé** de la bibliothèque après import pour réduire la latence de la taille réelle.
- ☐ **Anti-exploit** : recompute atomique du *committed* à chaque création (déjà via mutex du store) + cap requêtes/période (M16) comme garde-fou ; documenter le modèle (estimation en vol → taille réelle à l'import).
- ☐ **Objectif transversal** : **UI optimiste** + invalidation ciblée pour que tout changement (requête, quota, statut) se reflète **sans force-refresh**.

### M29 — Avis & notes (style IMDb) — évolution des commentaires  ☑ *(livré)*

> Transforme le **système de commentaires existant** (M25 : `MediaComment` / `JsonMediaCommentStore` /
> `CommentsController` / `buildCommentsSection`) en **système d'avis noté**, interne. Principe directeur :
> **ne pas se transformer en réseau social** — pas de fil social, pas de pseudos exposés.

- ☐ **Note par avis** : chaque utilisateur attribue une **note** (échelle à fixer, ex. 1–10 ou 1–5) en plus du texte (le texte devient **optionnel**).
- ☐ **Moyenne interne** affichée sur chaque média : **moyenne des notes utilisateurs du serveur** + **nombre de votes** (popup catalogue, et page native via M25.2 le moment venu).
- ☐ **Anonymat** : les utilisateurs **non-admin ne voient PAS** le pseudo de l'auteur d'un avis ; seul l'**admin** voit qui a posté quoi (modération). Les avis s'affichent de façon **anonyme** côté public.
- ☐ **Un seul avis par user et par média** (modifiable), pour une moyenne honnête.
- ☐ **Migration** des commentaires existants (texte sans note) — note vide / exclus de la moyenne.
- ☐ **Modération admin** conservée (masquer / supprimer), + l'avis masqué **sort de la moyenne**.
- ☐ **Borné** : réutiliser le plafond existant par titre (M25) ; la moyenne est un **agrégat** recalculé (ou mis en cache borné).
- ☐ **Anti-réseau-social** : pas de réponses/threads, pas de likes, pas de profils publics — juste note + avis anonyme + moyenne.

### Patches de finition pré-1.0 (lot juin 2026)

> Issu des tests live (ex-`TESTS.TODO.md`) + Notes de Victor. Correctifs & polish sur l'existant
> (pas de nouveau milestone), à clôturer avant la stabilisation v1.0.

#### A. Livrés *(à revérifier en live)*

- ☑ **Popup catalogue** : refonte (cast, ~980 px, avis vertical, survol des étoiles, **Report** sur la ligne du titre, zone Request **sous l'affiche**, **séries** = sélecteur de saisons pleine largeur) ; **média dispo** ouvre le popup (bouton **Détails** partout) avec **Open in Jellyfin** + **Add to my library** ; **« Demander la saga »** = aperçu + confirmation ; menus déroulants lisibles (fond sombre).
- ☑ **Dispatch & requêtes** : **dispatch idempotent** (fin du flapping « Blocked / 400 already exists ») ; **annulation** retire le grab de la **file Radarr** (stoppe RDT) puis supprime ; un média **Available** efface l'erreur de dispatch ; **retry-search** admin opt-in (off par défaut) ; **taille de DL** affichée pendant le téléchargement ; polling 3 s → 2 s.
- ☑ **Notifications** : **opt-in e-mail par catégorie** (dispo released/unreleased, décisions, quota/expiration) ; **emoji de statut** par notif ; **packaging MailKit/MimeKit** (les e-mails repartent).
- ☑ **Logs** : recherche (**Entrée** + live débounce) ; nouvelles entrées **admin** (config sauvegardée) et **user** (prefs, suppression, claim).
- ☑ **Diagnostic** : **Radarr/Sonarr** comptés séparément + **Prowlarr** configurable (URL/clé, API v1) ; 1ʳᵉ colonne élargie.
- ☑ **Admin/config** : **persistance** `AutoApproveGenres` + `QuotaOverrides` (collections rendues settables) ; **accès plugin par utilisateur** (exemption de config mode) ; config mode **rend le bandeau natif** aux non-admins ; **annonce** mise à jour en direct (+ ✓).
- ☑ **Mobile / responsive** : overlay **verrouille le scroll de fond** ; listes *Mes demandes*/*Ma bibliothèque* **wrappent** ; **tableaux admin** scrollables ; navbar « My library » sur une ligne ; panneau notifs pleine largeur ; annonce sur sa propre ligne ; genres complets ; **calendrier Mois/Semaine/Jour** + barre de nav qui wrappe.

#### B. À faire *(reliquat pré-1.0)*

**Bugs (re-test live KO) :**

- ☑ **Boutons admin noirs** — re-corrigé : règles **préfixées `#JellyCrowdConfigPage`** (spécificité ID) → Approve vert, Deny/Delete rouge, **boutons Test bleu Jellyfin**. *(Notes 1, 2 ; à revérifier)*
- ☑ **Tableau User quotas, 1ʳᵉ ligne** — re-corrigé : **toutes** les lignes affichent un cercle d'initiales (plus aucune photo) → uniformes. *(à revérifier ; sinon capture)*
- ☑ **Popup mobile portrait** — re-corrigé : modal clampé (`width/max-width:100%`, `overflow-x:hidden`, padding réduit) + `content` en `min-width:0`. *(à revérifier en portrait)*
- ☑ **Taille de DL** — corrigé : affichée en **Go décimaux** (/1000) pour coller à RDT (c'était la même taille en unités différentes).

**UI / UX (Notes de Victor) :**

- ☑ **Note 2** — boutons *Test* en **bleu Jellyfin** (couvert par le correctif boutons admin).
- ☑ **Note 3** — écran **My requests** : clic sur titre/jaquette ouvre le **popup** du média (modal du catalogue partagé via `window.jellyCrowdOpenDetail`). *(côté écran admin config : non applicable, pas d'infra modal)*
- ☑ **Note 4** — requêtes **Unreleased/planifiées** : affichent **Sortie : …** + **Prochaine tentative : …** sous le titre.
- ☑ **Notes 5/8** — popup : **release date** (méta), **réalisateur** + **titre original** (ligne crédits), liens **TMDB/IMDb entre synopsis et cast**. *(cast déjà fait)*
- ☐ **Note 6** — **acteurs + réalisateur cliquables** → catalogue filtré (nécessite un filtre « par personne » dans le catalogue).
- ☑ **Note 7** — **curseur pointer** sur le logo/home + **état hover** (brightness + underline) sur le bloc *My library*.
- ☑ **Note 13** — l'écran **admin Requests** affiche le badge **Downloading** (+ %) via `Requests/All/DownloadStatus`.
- ☑ **Note 12** — option admin **« Show review author names to everyone »** (`ShowReviewAuthors`, off par défaut) ; sinon les avis restent anonymes pour les non-admins.
- ☑ **Calendrier** — le bouton **Today** ouvre un **sélecteur de date** (input date natif) pour sauter à n'importe quelle date.
- ☐ **M25.2** (fiche native) — remplacer le **slider** par les **étoiles** du popup, **textarea multiligne**, repositionner le panneau (sous l'affiche / 2ᵉ colonne au niveau du synopsis).
- ☑ **M16 (genres)** — champ texte remplacé par un **menu déroulant des genres TMDB** (movie+tv fusionnés) + **pastilles supprimables** ; persiste dans `AutoApproveGenres`.
- ☐ **Note 14** — **disclaimer** près du titre « Mes demandes » : l'affichage de la progression/disponibilité n'est pas temps réel (techno Jellyfin), le média peut être dispo dans Jellyfin avant que la requête soit marquée *Available*.

**Réactivité / temps réel → relève de M28 :**

- ☐ **Note 9** — Approved → Available **trop lent** (« Downloaded · dispo dans <2 min » persiste trop longtemps) : reconcile plus agressif après import.
- ☐ **Note 10** — la **pastille rouge** de notif n'apparaît pas toujours immédiatement au passage *Available*.

**Tests → relève de la stabilisation v1.0 :**

- ☐ **Note 11** — suite de **non-régression** (workflows, boutons, liens, affichages) — voir « Stratégie de tests e2e ».

### 🏁 v1.0.0 — Stabilisation

Objectif : passer le cap qualité avant de coller un « 1.0 ».

- ☐ **Responsive / mobile + accessibilité** de l'overlay (Jellyfin très utilisé sur mobile/TV : tailles tactiles, nav clavier, ARIA).
- ☐ **Doc utilisateur** (*Getting started* avec captures), en plus de `CONFIGURATION.md` (admin).
- ☐ **Tests e2e & non-régression** (voir stratégie ci-dessous).
- ☐ Passe de **polish** finale, puis **release `v1.0.0`** (commit `[major]`).

#### Principe transversal — budget de stockage & rétention

> Toute fonctionnalité qui **produit de la donnée** (logs, notifs, commentaires, stats) doit livrer
> avec : un **plafond** (taille/nombre), une **rétention** (âge max) et un **nettoyage** automatique.
> Aucun store non borné. L'empreinte est exposée dans le **Diagnostic** (M18).
>
> Ordres de grandeur (référence, ~50 utilisateurs actifs) :
>
> - `requests.json` / `watchlist.json` : ~0,5 Ko/entrée → **quelques Mo/an**, négligeable.
> - **Notifications** : bornées (plafond + TTL) → quelques Mo, stable.
> - **Logs** : **principal risque** (10 à 100+ Mo/mois si verbeux) → rotation + taille/âge max obligatoires.
> - **Stats (2.0)** : stocker des **agrégats** (par user / bibliothèque / jour), **jamais** les ticks bruts → ~10 Mo/an borné.

#### Stratégie de tests e2e & non-régression

> En plus des **tests unitaires** existants (.NET logique pure + contrôleurs avec fakes ; JS pur via `node:test`) :
>
> - ☐ **Tests de contrat / parsers sur fixtures réelles** : figer des réponses TMDB/Radarr/Sonarr (JSON) et vérifier les parsers contre elles (garde-fou anti-dérive d'API).
> - ☐ **Tests d'intégration HTTP** des contrôleurs avec un **serveur mock** (WireMock.Net) pour TMDB/Radarr/Sonarr → valide le flux requête→dispatch→statut sans Jellyfin live.
> - ☐ **Tests DOM (jsdom)** pour la logique de rendu critique (`header.js`, badges de statut, application des statuts de DL).
> - ☐ **Snapshot/golden** des payloads générés (embed Discord, ajout Servarr) pour détecter toute régression de format.
> - ☐ *(Optionnel, nightly)* **smoke e2e Playwright** sur un `docker-compose` (Jellyfin + plugin + *arr mockés*).
> - ☐ **Checklist de régression manuelle** documentée pour les vérifs live impossibles à automatiser (chaîne *arr réelle, RDT).

---

## Au-delà de la 1.0.0 — pan « JellyStats-like »  *(prévu : `v2.0.0`)*

### M30 — Statistiques utilisateur  ☐

- ☐ Recensement par utilisateur : nb de requêtes, médias vus **en entier**, films / épisodes / séries regardés, **watchtime** (7 / 30 / 90 jours + total), watchtime **par bibliothèque** accessible, etc.

### M31 — Dashboard utilisateur  ☐

- ☐ Dashboard avec ses stats et son **classement** (pour les métriques pertinentes).
- ☐ Onglet **graphes** : évolution dans le temps (échelle réglable, plusieurs courbes — regroupées ou séparées selon la pertinence).

### M32 — Popularité interne & vue admin enrichie  ☐

- ☐ **Popularité interne** : « le plus demandé / le plus regardé chez vous » mis en avant dans le catalogue.
- ☐ **Informations admin** sur chaque média de Jellyfin.

> Le passage à **`v2.0.0`** (commit `[major]`) marque l'ajout du pan statistiques.

### M33 — Système de ticketting  ☐

- ☐ **Ouverture de tickets** : les utilisateurs peuvent ouvrir des tickets pour signaler des bugs/problèmes avec des médias/sous titres/mauvaise langue audio, etc.)
- ☐ **Onglet admin dédié** : l'admin a une interface adaptée pour gérer cela.
- ☐ **Notifications et logs** : ce système génère des notifications pour les concernés (configurables par l'admin), ainsi que des logs.

---

## Encore plus loin  *(prévu : `v3.0.0`)*

### M34 — Bot bidirectionnel  ☐

- ☐ **Approuver/refuser depuis Discord/Telegram** (notifications interactives à double sens).
