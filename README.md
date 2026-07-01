<div align="center">

# 🎬 Jelly Crowd

**One plugin to rule them all** — catalogue de découverte, requêtes, quotas, branding et stats,
directement intégrés dans Jellyfin.

![License: Proprietary](https://img.shields.io/badge/License-Proprietary-red.svg?style=for-the-badge)
![Jellyfin 10.11](https://img.shields.io/badge/Jellyfin-10.11%2B-00A4DC?style=for-the-badge&logo=jellyfin)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)

</div>

---

> **Dépôt source privé.** La distribution publique (manifest d'installation + archives de release) vit
> dans le dépôt public **[`LokLakh-s/jellycrowd-dist`](https://github.com/LokLakh-s/jellycrowd-dist)**, où
> se trouve aussi la **documentation publique** (en anglais, admin + utilisateurs).

**Jelly Crowd** est un **plugin Jellyfin natif**. Contrairement aux services externes type Overseerr/Jellyseerr
qui tournent à côté du serveur, Jelly Crowd vit **dans** Jellyfin et réutilise ses utilisateurs, son
authentification et son thème. **Aucun plugin tiers requis** : il héberge ses propres pages web via un
middleware ASP.NET intégré.

## ✨ Fonctionnalités

- 🍿 **Catalogue de découverte (TMDB)** — parcours/recherche de films & séries avec filtres (genres, années
  & notes en double-sliders, tri), survol des affiches, fiche détaillée (casting, saisons, liens TMDB & IMDb)
  et **calendrier des sorties**.
- 📝 **Requêtes** — demande d'un média via une file d'attente admin (approbation / auto-approbation). Séries
  par saison ou par épisode. **Fulfilment automatique** via Radarr/Sonarr, webhook ou script.
- 💾 **Quotas disque par utilisateur** — quota par défaut + overrides ; **quota adaptatif** optionnel qui
  récompense les utilisateurs actifs ; expiration des médias pour libérer de l'espace ; limites de requêtes.
- ⭐ **Avis & signalements** — notes/critiques in-app avec modération, plus un canal de signalement.
- 🔔 **Notifications** — Discord, e-mail, Telegram, ntfy, Gotify, Pushover, Slack ou webhook.
- 🎨 **Branding** — thème complet de l'interface : logo, favicon, couleurs, police, fond, CSS custom, liens du menu.
- 📊 **Statistiques & dashboards** — analytics de lecture pour l'admin (top médias & utilisateurs, graphes
  d'activité, « en cours de lecture ») + un dashboard personnel pour chaque utilisateur.

> Pour l'état d'avancement, voir [`ROADMAP.md`](JellyCrowd/ROADMAP.md).

## 📦 Pré-requis

- **Jellyfin 10.11+**
- Une **clé API TMDB** (gratuite) pour le catalogue.

## 🚀 Installation

L'installation publique passe par le dépôt **`jellycrowd-dist`** :

1. **Dashboard → Plugins → Dépôts (Repositories) → +** et ajouter :
   ```text
   https://raw.githubusercontent.com/LokLakh-s/jellycrowd-dist/main/manifest.json
   ```
2. **Catalogue (Catalog)** → installer **Jelly Crowd** → redémarrer Jellyfin.
3. **Dashboard → Plugins → Jelly Crowd** → renseigner la clé TMDB.

Les mises à jour sont automatiques : à chaque release, la CI publie l'archive dans `jellycrowd-dist` et met
à jour son `manifest.json` ; Jellyfin propose la nouvelle version (ou l'installe seul si l'auto-update est actif).

## 🛠️ Développement

```powershell
dotnet build -c Release
```

Copier le `.dll` produit dans `<jellyfin-data>/plugins/JellyCrowd/`, puis redémarrer Jellyfin.
Architecture, conventions et commandes : voir [`CLAUDE.md`](JellyCrowd/CLAUDE.md).

### Release & distribution

Un push sur `main` déclenche le workflow [`release.yml`](.github/workflows/release.yml) (runner self-hosted) :
versioning 4-parties piloté par mot-clé de commit, build + tests, puis publication de l'archive et mise à
jour du `manifest.json` dans le dépôt public `jellycrowd-dist` (via le secret `DIST_TOKEN`).

## 📄 Licence

Logiciel propriétaire — voir [`LICENSE`](./LICENSE). Tous droits réservés.
