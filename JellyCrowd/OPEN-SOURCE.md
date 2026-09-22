# Ouverture de Jelly Crowd — état et reste à faire

Note de travail interne (comme `CLAUDE.md` et `ROADMAP.md`). Elle suit le passage de Jelly Crowd en
logiciel libre sous **AGPL-3.0**, décidé le 2026-09-22.

## Pourquoi l'AGPL et pas autre chose

`Jellyfin.Controller` et `Jellyfin.Model` sont sous **GPL-3.0-only**. Le plugin est compilé contre elles et
s'exécute dans le processus de Jellyfin : selon la lecture dominante, c'est une œuvre dérivée. La licence
propriétaire d'origine était donc juridiquement fragile — l'ouverture supprime ce risque.

Entre GPL-3.0 et AGPL-3.0, l'AGPL a été retenue pour une raison précise : un projet distribué **sous
GPL-3.0** (le cas de `n00bcodr/Jellyfin-Enhanced`, dont le périmètre recoupe le nôtre) ne peut pas absorber
du code AGPL et le relicencier en GPL-3.0. La portion reste AGPL et contamine sa distribution. C'est de la
friction, pas un mur, mais c'est le seul levier qui agit sur ce scénario.

Corollaire à ne pas perdre de vue : **rester seul titulaire des droits** est ce qui laisse la porte ouverte
à une relicence ultérieure. Le `CONTRIBUTING.md` demande aujourd'hui un simple **DCO** (`git commit -s`),
qui ne transfère rien — chaque contributeur garde son copyright. Passer à un **CLA** est la seule façon de
conserver la main, au prix d'une barrière à l'entrée. À trancher avant la première contribution externe.

## Fait

- `LICENSE` et `dist/LICENSE` : texte AGPL-3.0 verbatim (gnu.org), sans préambule, pour que le détecteur
  de licence de GitHub le reconnaisse.
- Badges et sections *Licence* des trois README ; mentions « non affilié à Jellyfin » et attribution TMDB
  (exigée par les CGU de l'API) dans le README public.
- **Offre de source AGPL article 13** dans le guide utilisateur, rendue par `Web/guide.js` et **pas** par
  `guide-content.json` : un administrateur peut remplacer ce document en entier (cf. `GuideController`), et
  l'offre doit survivre à un guide réécrit.
- CI durcie (voir ci-dessous).
- `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `.github/PULL_REQUEST_TEMPLATE.md`,
  `.github/CODEOWNERS`, `.github/dependabot.yml`.

### Ce que le durcissement CI a changé

- `build.yml` : une PR tourne désormais sur `ubuntu-latest`, plus sur le runner self-hosted. C'était le
  vrai bloquant — sur un dépôt public, n'importe qui ouvre une PR depuis un fork et obtient l'exécution de
  code arbitraire sur la machine perso, avec ses caches persistants et son socket Docker. Seuls `main` et
  un dispatch manuel (qui exigent tous deux un accès en écriture) restent sur le self-hosted.
- `build.yml` : `permissions: contents: read` explicite, cache NuGet sur les runners jetables uniquement,
  étapes propres au self-hosted conditionnées par `runner.environment`.
- `release.yml` : garde `github.repository_owner == 'LokLakh-s'` (un fork hérite du workflow), et
  `softprops/action-gh-release` épinglée à son commit plutôt qu'au tag mouvant `v3` — c'est l'étape qui
  manipule `DIST_TOKEN`.

## Réglages GitHub à appliquer — au moment de rendre le dépôt public

Aucun de ces points ne vit dans un fichier ; ils se règlent dans les *Settings* du dépôt.

| Réglage | Où | Valeur |
|---|---|---|
| Approbation des workflows de fork | Actions → General | **Require approval for all outside collaborators** (le défaut, « first-time contributors », ne suffit pas) |
| Permissions du `GITHUB_TOKEN` | Actions → General | Read-only par défaut ; décocher « Allow GitHub Actions to create and approve pull requests » |
| Signalement privé de vulnérabilité | Security | Activé (`SECURITY.md` y renvoie explicitement) |
| Secret scanning + push protection | Security | Activé (gratuit sur dépôt public) |
| Dependabot alerts + security updates | Security | Activé |
| Protection de `main` | Branches | Au minimum : CI verte obligatoire. La revue obligatoire casse le push direct sur `main` — à arbitrer |

⚠️ **Risque résiduel du runner self-hosted.** Il reste branché sur `main`. Toute modification de
`.github/workflows/` fusionnée dans `main` s'exécute sur la machine perso. `CODEOWNERS` couvre ce dossier,
mais il n'a d'effet que si la protection de branche exige la revue des fichiers possédés. Sinon, le seul
garde-fou est de relire soi-même tout diff touchant `.github/`.

## Étape différée : fusionner les deux dépôts

Aujourd'hui : source privée `LokLakh-s/JellyCrowd-dev`, distribution publique `LokLakh-s/JellyCrowd`
(force-pushée à un commit unique à chaque release). Ce découpage n'existait que pour garder les sources
fermées ; l'ouverture lui retire sa raison d'être.

**Sens de la fusion : `LokLakh-s/JellyCrowd` devient le dépôt unique.** Il détient trois choses qu'on ne
peut pas casser :

1. l'URL du manifeste `raw.githubusercontent.com/LokLakh-s/JellyCrowd/main/manifest.json`, configurée dans
   le Jellyfin de chaque utilisateur installé ;
2. les GitHub Releases vers lesquelles pointent les `sourceUrl` de **toutes** les versions du manifeste ;
3. ses étoiles.

### Deux pièges, dans cet ordre

1. **Neutraliser d'abord le force-push** (étape *Publish the distribution repo* de `release.yml`) : tant
   qu'il est là, la première release écrase l'historique qu'on vient de pousser.
2. **Supprimer l'étape *Reset public releases & tags*** : elle efface toutes les Releases sauf la dernière,
   ce qui invaliderait les `sourceUrl` des versions antérieures encore référencées par les manifestes
   installés.

### Ensuite

- Pousser les commits et tags de `JellyCrowd-dev` dans `JellyCrowd`, en gardant `manifest.json` à la racine.
- Remonter le contenu de `dist/` à la racine (README public, screenshots, logo, templates d'issues) et
  supprimer le dossier.
- Simplifier `release.yml` : plus de `DIST_TOKEN`, plus de clone ni de force-push du dépôt de distribution.
  Révoquer le PAT ensuite.
- `JellyCrowd/README.md` et le README racine : retirer la mention « dépôt source privé ».
- Envisager la soumission au **catalogue officiel des plugins Jellyfin**, que l'AGPL rend possible.
