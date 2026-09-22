<div align="center">

# 🎬 Jelly Crowd

**One plugin to rule them all** — plugin Jellyfin natif : catalogue de découverte, requêtes,
quotas, branding et statistiques, sans aucune dépendance tierce.

![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg?style=for-the-badge)
![Jellyfin 12](https://img.shields.io/badge/Jellyfin-12-00A4DC?style=for-the-badge&logo=jellyfin)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)

</div>

---

> Le README complet (présentation, fonctionnalités, installation) est à la **racine du dépôt** :
> [`../README.md`](../README.md). La **documentation publique** (en anglais, admin + utilisateurs) vit
> dans le dépôt de distribution **[`LokLakh-s/JellyCrowd`](https://github.com/LokLakh-s/JellyCrowd)**.

## Docs de ce dossier

- [`CLAUDE.md`](./CLAUDE.md) — architecture, conventions, commandes de build/test.
- [`ROADMAP.md`](./ROADMAP.md) — état d'avancement vers la 1.0.
- [`CONFIGURATION.md`](./CONFIGURATION.md) — référence des réglages (admin).
- [`GETTING_STARTED.md`](./GETTING_STARTED.md) — prise en main (utilisateurs).

## Build local

```powershell
dotnet build -c Release
```

Copier le `.dll` produit dans `<jellyfin-data>/plugins/JellyCrowd/`, puis redémarrer Jellyfin.
La distribution publique passe par le dépôt `LokLakh-s/JellyCrowd` (voir le README racine).

## Licence

**GNU Affero General Public License v3.0** — voir [`LICENSE`](../LICENSE). Copyright © 2026 LokLakh-s.
