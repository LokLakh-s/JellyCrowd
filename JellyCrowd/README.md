<div align="center">

# 🎬 Jelly Crowd

**Un catalogue de découverte et un système de requêtes — façon Overseerr — directement intégré dans Jellyfin, avec quotas disque par utilisateur.**

[![License: GPL-3.0](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](https://www.gnu.org/licenses/gpl-3.0)
![Jellyfin 10.11](https://img.shields.io/badge/Jellyfin-10.11.x-00A4DC?style=for-the-badge&logo=jellyfin)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)

</div>

---

**Jelly Crowd** est un **plugin Jellyfin natif**. Contrairement aux services externes type Overseerr/Jellyseerr
qui tournent à côté du serveur, Jelly Crowd vit **dans** Jellyfin et réutilise ses utilisateurs, son
authentification et son thème.

## ✨ Fonctionnalités

- 🍿 **Catalogue de découverte (TMDB)** — parcours/recherche de films & séries avec **filtres** (genres,
  années & notes en double-sliders, tri), **survol** des affiches, et **fiche détaillée** (genres, durée,
  synopsis, liens TMDB & IMDb). Les titres déjà présents sont marqués « disponible » et ouvrent la fiche Jellyfin.
- 📝 **Requêtes** — demande d'un média via une **file d'attente admin** (approbation/refus). Les **séries
  se demandent par saison**. *(Intégration Radarr/Sonarr possible ultérieurement.)*
- 💾 **Quotas disque par utilisateur** — quota par défaut + **overrides par utilisateur** ; l'usage reflète la
  taille réelle en bibliothèque ; au-delà, les requêtes sont bloquées (bouton grisé). **Limite de requêtes par
  période** (jour/semaine/mois) configurable.
- 🗑️ **Gestion des médias** — écran « Mes médias » où l'utilisateur demande la suppression ; un média marqué est
  **supprimé du disque** après une **rétention** configurable (tâche planifiée).
- 🔔 **Notifications** — événements de requête (créée / approuvée / disponible) vers **Discord** et/ou **e-mail (SMTP)**.
- 🎨 **Intégration UI** — onglets Catalogue / Mes requêtes + barre de quota injectés dans le bandeau, et pages
  utilisateur hébergées par Jelly Crowd lui-même (**aucun plugin tiers requis** : Jelly Crowd injecte lui-même
  son interface dans le client web), plus une page d'admin à onglets accessible directement depuis le dashboard.
- 🔄 **Mises à jour automatiques** via dépôt de plugin (voir Installation).

> Pour l'état d'avancement, voir [`ROADMAP.md`](./ROADMAP.md).

## 📦 Pré-requis

- **Jellyfin 10.11.x**
- **Aucun plugin tiers requis** — Jelly Crowd injecte lui-même son interface dans le client web (via son
  propre middleware, au moment de la requête). Plus besoin de *File Transformation* ni de *Plugin Pages*.
- Une **clé API TMDB** (gratuite) pour le catalogue.

## 🚀 Installation

### Via dépôt de plugin (recommandé)

1. **Dashboard → Plugins → Dépôts (Repositories) → +** et ajouter :
   ```
   https://raw.githubusercontent.com/LokLakh-s/JellyCrowd/main/manifest.json
   ```
2. **Catalogue (Catalog)** → installer **Jelly Crowd** → redémarrer Jellyfin.

Une entrée **Jelly Crowd** apparaît directement dans la barre latérale du dashboard admin
(onglets *Réglages / Quotas utilisateurs / Demandes*). Renseigner la clé TMDB et les quotas.

### 🔄 Mises à jour automatiques (sans télécharger de zip)

Une fois le dépôt ajouté, **plus jamais besoin de télécharger/décompresser un `.zip`** : à chaque
nouvelle version, le `manifest.json` du dépôt est mis à jour automatiquement par la CI (avec
l'URL de l'archive et son empreinte MD5). Jellyfin détecte la nouvelle version et l'installe.

- **Mise à jour en place** : *Dashboard → Plugins → Jelly Crowd* affiche « Mise à jour disponible » → 1 clic, puis redémarrage.
- **Tout automatique** : *Dashboard → Plugins → Repositories* / réglages des plugins → activer la
  vérification/installation auto des mises à jour ; Jellyfin applique alors les nouvelles versions au
  redémarrage, sans intervention.

> ⚠️ **Pré-requis indispensable** : le dépôt GitHub `LokLakh-s/JellyCrowd` doit être **public** — Jellyfin
> télécharge le `manifest.json` (URL *raw*) et l'archive de release **sans authentification**. Si le dépôt
> est privé, l'install/MAJ par dépôt échoue (il faudrait alors héberger le manifest ailleurs).

### Configuration

Une fois installé, ouvrir **Dashboard → Plugins → Jelly Crowd** (onglets Demandes / Quotas / Réglages /
Notifications / Téléchargement). Le détail de chaque réglage est documenté dans **[CONFIGURATION.md](CONFIGURATION.md)**.

### En développement (build local)

```powershell
dotnet build -c Release
```

Copier le `.dll` produit dans `<jellyfin-data>/plugins/JellyCrowd/`, puis redémarrer Jellyfin.

## 🛠️ Développement

Voir [`CLAUDE.md`](./CLAUDE.md) pour l'architecture, les conventions et les commandes.

## 📄 Licence

[GPL-3.0](./LICENSE) — aligné sur l'écosystème des plugins Jellyfin.
