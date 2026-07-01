# Configuration de Jelly Crowd

Guide de configuration de chaque réglage du plugin. Tout se passe dans **Dashboard → Plugins →
Jelly Crowd**, une page à onglets : **Demandes**, **Quotas utilisateurs**, **Réglages**,
**Notifications**, **Téléchargement**.

## Prérequis

- **Aucune dépendance de plugin.** Jelly Crowd injecte lui-même ses liens (**Catalog / Calendar /
  My requests / Dashboard**) et la barre de quota dans l'en-tête de Jellyfin via son propre middleware
  (aucune installation tierce à prévoir).
- Une **clé API TMDB** (v3, gratuite) pour alimenter le catalogue.

## Onglet Réglages

| Réglage | Rôle |
|---|---|
| **Langue** | `Auto` suit la langue de chaque utilisateur (navigateur/Jellyfin) ; `English`/`Français` force la langue des pages et notifications. |
| **Clé API TMDB** | Clé v3 TMDB. Obligatoire pour le catalogue (sinon erreur « TMDB non configuré »). |
| **Quota par défaut (Gio)** | Quota disque appliqué à tout utilisateur sans override. `0` = illimité. |
| **Exiger l'approbation admin** | Si coché, les requêtes restent en attente jusqu'à validation. Sinon elles passent directement en `Approuvée`. |
| **Taille estimée film / épisode (Gio)** | Sert au pré-calcul de quota *avant* que la taille réelle soit connue (à la création de requête). |
| **Max de requêtes par période** | Limite de requêtes par utilisateur et par période. `0` = illimité. |
| **Période** | Fenêtre glissante de la limite : Jour / Semaine / Mois. |
| **Rétention de suppression (heures)** | Délai entre la demande de suppression d'un média par l'utilisateur et sa suppression disque effective par la tâche planifiée. |

## Onglet Quotas utilisateurs

Liste tous les utilisateurs Jellyfin avec un champ **quota (Gio)** par utilisateur. Laisser vide pour
utiliser le quota par défaut ; `0` = illimité. L'usage d'un utilisateur = somme des tailles de ses
requêtes satisfaites (`Disponible`). Au-delà du quota, ses nouvelles requêtes sont refusées (403).

## Onglet Notifications

Chaque canal est optionnel (activé dès qu'il est configuré). Événements notifiés : requête **créée**,
**approuvée**, **disponible**.

- **Discord** : *Webhook URL* du salon. Laisser vide pour désactiver.
  - Bloc dépliable **« Discord — events, colors & content »** pour personnaliser :
    - **Events déclencheurs** : cocher quels événements notifient sur Discord (*Created / Approved / Denied / Available*).
    - **Couleur d'embed par event** (sélecteur de couleur).
    - **Contenu de l'embed** : afficher ou non l'affiche, le synopsis, et les champs *Requested by / Status / Season*, et lier (ou non) le titre vers TMDB.
    - **Mention / ping** optionnelle postée au-dessus de l'embed (ex. `<@&ROLE_ID>` pour notifier un rôle, ou `@everyone`).
- **E-mail (SMTP)** : *hôte*, *port* (587 STARTTLS ou 465 SSL), *SSL/TLS*, *identifiants*, *adresse
  d'expéditeur*, *destinataire*. *Accepter un certificat invalide* : uniquement pour un serveur
  auto-hébergé de confiance.
- **Autres canaux** (chacun optionnel, activé dès qu'il est rempli) :
  - **Telegram** : *bot token* (@BotFather) + *chat id*.
  - **ntfy** : *URL serveur* (défaut `https://ntfy.sh`) + *topic* + *token* optionnel.
  - **Gotify** : *URL serveur* + *application token*.
  - **Pushover** : *API token* + *user/group key*.
  - **Slack** : *incoming webhook URL*.
  - **Webhook** : URL recevant un POST JSON `{title, body}`.
- Bouton **Test** par canal : envoie une notification de test (enregistrez d'abord vos réglages).

## Onglet Téléchargement

Définit comment les requêtes **approuvées** sont satisfaites. **Jelly Crowd se contente d'émettre la
requête** : il ne cherche pas et ne télécharge pas les fichiers — c'est le backend qui s'en charge.

Le **menu déroulant « Download backend »** pilote les réglages affichés :

- **Aucun (file admin manuelle)** : comportement par défaut, rien n'est émis automatiquement.
- **Webhook** : à l'approbation (et une fois la date souhaitée atteinte), Jelly Crowd envoie un
  **POST JSON** de la requête à l'URL configurée.
  - **Webhook URL** : l'URL cible. Le « ? » au survol montre un exemple de requête. Charge utile :

    ```json
    {
      "RequestId": "8f1c…",
      "UserId": "a2b9…",
      "UserName": "alice",
      "TmdbId": 603,
      "MediaType": "movie",
      "Title": "The Matrix",
      "Year": 1999,
      "ReleaseDate": "1999-03-30",
      "Season": null,
      "PosterPath": "/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg",
      "RequestedAt": "2026-06-13T12:00:00Z",
      "DesiredAt": "2026-06-13T12:00:00Z",
      "TmdbUrl": "https://www.themoviedb.org/movie/603"
    }
    ```

    Le payload contient assez d'identité (`TmdbId`, `MediaType`, `Title`, `Year`, `Season`) pour qu'un
    relais le mappe trivialement vers Radarr/Sonarr ou toute automatisation (n8n, script exposé en HTTP…).
  - **Webhook headers** : en-têtes HTTP optionnels, un par ligne au format `Nom: Valeur` (ex. un en-tête
    d'autorisation).
  - **Test backend** : envoie un POST d'exemple à l'URL (enregistrez d'abord).
- **Script local** : Jelly Crowd exécute un **script/exécutable** que tu configures (chemin + arguments
  optionnels) pour chaque requête approuvée, en lui passant la requête en **JSON sur stdin** + des
  variables d'environnement `JELLYCROWD_*` (`TMDBID`, `MEDIATYPE`, `TITLE`, `YEAR`, `SEASON`, `EPISODE`,
  `USER`). À toi d'y brancher ta logique (qBittorrent, SABnzbd, etc.). Bouton **Test backend** = lance le
  script avec un échantillon (titre « Jelly Crowd test »). Sur Windows, pointe vers l'interpréteur
  (ex. chemin `python.exe`, arguments = ton script).
- **Radarr / Sonarr (Servarr)** : connexion directe par **URL + clé API**. Jelly Crowd ajoute le film
  (Radarr) / la série (Sonarr) et **déclenche la recherche** ; Radarr/Sonarr cherchent et téléchargent.
  - Pour chaque service : saisir **URL** (ex. `http://localhost:7878` Radarr, `http://localhost:8989`
    Sonarr) et **clé API** (dans *Settings → General* de Radarr/Sonarr), puis cliquer **Connect** :
    Jelly Crowd liste les **dossiers racine** et **profils de qualité** (et **profils de langue** pour
    Sonarr v3) à choisir dans les menus déroulants. **Enregistrer**.
  - Les **films** passent par Radarr, les **séries** par Sonarr. Comme Sonarr fonctionne en TVDB,
    l'identifiant TVDB est résolu automatiquement depuis TMDB. Pour une demande de **saison précise**,
    seule cette saison est surveillée ; sinon toute la série.
  - Bouton **Test backend** : vérifie la connexion aux instances configurées (`system/status`).
  - **Statut de téléchargement en direct** : quand le backend est **Servarr**, la page *Mes demandes*
    interroge la file d'attente de Radarr/Sonarr (`/queue`) toutes les 5 s et affiche sur chaque demande
    approuvée son état réel — *En file d'attente / Téléchargement %/ETA / Import en cours / Téléchargé /
    Problème*. Les films sont rapprochés par identifiant TMDB, les épisodes par TVDB + saison/épisode.
    Pour une demande approuvée **pas encore en téléchargement**, l'état est déduit de l'item lui-même :
    **Pas encore sorti** (non disponible / non diffusé) ou **Manquant** (sorti/diffusé mais sans fichier).
    Ces états-là sont mis en cache ~60 s côté serveur (ils changent lentement). La liste se met aussi à
    jour seule quand une demande passe à *Disponible*, sans recharger la page.

Idempotence : une requête n'est dispatchée qu'une seule fois (horodatage `DispatchedAt`). Une tâche
planifiée **« Jelly Crowd: dispatch downloads »** (toutes les 15 min) rattrape les requêtes dont la date
souhaitée est échue et les éventuels échecs.

### Planification automatique des sorties

Si un film/une série demandé **n'est pas encore sorti**, la requête est automatiquement **planifiée à sa
date de sortie** (TMDB) : le dispatch vers le backend n'a lieu qu'à partir de cette date. La page
**« Mes demandes »** l'indique par un badge **« Planifié pour le … »**. (Pour les séries déjà en cours,
Sonarr gère ensuite l'arrivée des nouveaux épisodes.)

## Onglet Demandes (file admin)

Liste toutes les requêtes (triées : en attente, approuvées, disponibles, refusées). Par ligne :
**Approuver / Refuser** (pour les demandes en attente), **forcer le statut**, **replanifier** (date
souhaitée) et **Supprimer**. L'approbation déclenche la notification et, si un backend de téléchargement
est configuré et la requête échue, le dispatch.

**Demander au nom d'un utilisateur** : ouvrir un titre dans le **Catalog** (ou le **Calendar**) ; en tant
qu'admin, un sélecteur **« Demander au nom de »** apparaît dans la fiche. Choisir l'utilisateur, puis
utiliser les boutons habituels (film / saison / épisode) : la demande est créée pour cet utilisateur
(approuvée, sans quota/limite).

## Pages utilisateur (bandeau Jellyfin)

- **Catalog** : catalogue TMDB (films/séries), filtres (genres, années, note, tri, **langue d'origine**,
  **pays de production**), recherche, fiche détaillée ; bouton **Requête** ouvrant la fiche d'où l'on
  envoie la demande. **★** sur chaque carte/fiche pour suivre un titre ; la bascule **« Ma liste »**
  affiche les titres suivis (watchlist). Une rangée **« Pour vous »** suggère des titres d'après tes
  requêtes et ta watchlist (masquée tant qu'il n'y a pas assez d'historique).
- **Calendar** : **grille mensuelle** des sorties (films et séries confondus, via TMDB) avec navigation
  mois précédent/suivant + « Aujourd'hui » ; les affiches/titres apparaissent dans la case de leur jour de
  sortie, clic → fiche → requête (auto-planifiée à la date de sortie). Les **sorties films** couvrent
  plusieurs régions (ta région + FR, ES, IT, GB, US) ; les **épisodes** sont ceux de tes séries suivies.
- **My requests** : requêtes de l'utilisateur et leur statut, annulation tant qu'en attente, demande de
  suppression d'un média disponible, badge de planification.
- **Barre de quota** (entre la recherche et l'avatar) : usage/quota, dégradé vert→jaune→rouge ; clic →
  « Mes médias ».
