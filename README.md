<div align="center">

# 🎬 Jelly Crowd

**One plugin to rule them all** — catalogue de découverte, requêtes, quotas, branding et stats,
directement intégrés dans Jellyfin.

![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg?style=for-the-badge)
![Jellyfin 12](https://img.shields.io/badge/Jellyfin-12-00A4DC?style=for-the-badge&logo=jellyfin)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)

</div>

---

> **Dépôt source privé.** La distribution publique (manifest d'installation + archives de release) vit
> dans le dépôt public **[`LokLakh-s/JellyCrowd`](https://github.com/LokLakh-s/JellyCrowd)**, où
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
- ⭐ **Avis & tickets** — notes/critiques in-app avec modération, plus un canal de signalement : sur une
  fiche ou en général (depuis le menu avatar), avec notification immédiate des admins et rappels des
  tickets laissés ouverts.
- 🔔 **Notifications** — Discord, e-mail, Telegram, ntfy, Gotify, Pushover, Slack ou webhook.
- 🎨 **Branding** — thème complet de l'interface : logo, favicon, couleurs, police, fond, CSS custom, liens du menu.
- 📊 **Statistiques & dashboards** — analytics de lecture pour l'admin (top médias & utilisateurs, graphes
  d'activité, « en cours de lecture ») + un dashboard personnel pour chaque utilisateur.

> Pour l'état d'avancement, voir [`ROADMAP.md`](JellyCrowd/ROADMAP.md).

## 📦 Pré-requis

- **Jellyfin 12** — la cible du plugin. **10.11 reste supporté** et continue de recevoir chaque
  release, mais les nouveautés visent la 12 d'abord.
- Une **clé API TMDB** (gratuite) pour le catalogue.

## 🚀 Installation

L'installation publique passe par le dépôt public **[`LokLakh-s/JellyCrowd`](https://github.com/LokLakh-s/JellyCrowd)** :

1. **Dashboard → Plugins → Dépôts (Repositories) → +** et ajouter :
   ```text
   https://raw.githubusercontent.com/LokLakh-s/JellyCrowd/main/manifest.json
   ```
2. **Catalogue (Catalog)** → installer **Jelly Crowd** → redémarrer Jellyfin.
3. **Dashboard → Plugins → Jelly Crowd** → renseigner la clé TMDB.

Les mises à jour sont automatiques : à chaque release, la CI publie l'archive dans le dépôt public et met
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
jour du `manifest.json` dans le dépôt public `LokLakh-s/JellyCrowd` (via le secret `DIST_TOKEN`).

## 📄 Licence

**GNU Affero General Public License v3.0** — voir [`LICENSE`](./LICENSE).

Copyright © 2026 LokLakh-s.

Jelly Crowd est un logiciel libre : tu peux le redistribuer et le modifier selon les termes de l'AGPL-3.0,
telle que publiée par la Free Software Foundation, en version 3 ou ultérieure. Il est distribué dans
l'espoir d'être utile, mais **sans aucune garantie**.

L'AGPL est le choix cohérent ici : `Jellyfin.Controller` et `Jellyfin.Model`, contre lesquelles le plugin
est compilé et dans le processus desquelles il s'exécute, sont sous **GPL-3.0-only**. Son article 13 ajoute
que toute personne interagissant avec une version modifiée **à travers un réseau** doit se voir offrir
les sources correspondantes.
