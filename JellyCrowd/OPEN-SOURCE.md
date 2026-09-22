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

- `LICENSE` : texte AGPL-3.0 verbatim (gnu.org), sans préambule, pour que le détecteur de licence de
  GitHub le reconnaisse.
- Badges et sections *Licence* des trois README ; mentions « non affilié à Jellyfin » et attribution TMDB
  (exigée par les CGU de l'API) dans le README public.
- **Offre de source AGPL article 13** dans le guide utilisateur, rendue par `Web/guide.js` et **pas** par
  `guide-content.json` : un administrateur peut remplacer ce document en entier (cf. `GuideController`), et
  l'offre doit survivre à un guide réécrit.
- CI durcie (voir ci-dessous).
- `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `.github/PULL_REQUEST_TEMPLATE.md`,
  `.github/CODEOWNERS`, `.github/dependabot.yml`.

### Ce que le durcissement CI a changé

- **Plus aucun runner self-hosted**, ni en CI ni en release. C'était le vrai bloquant : sur un dépôt
  public, n'importe qui ouvre une PR depuis un fork et obtient l'exécution de code arbitraire sur la
  machine perso, avec ses caches persistants et son socket Docker. Les minutes GitHub étant gratuites et
  illimitées sur un dépôt public, le seul argument du self-hosted tombait de toute façon. Effet de bord
  utile : plus aucun workflow fusionné dans `main` ne peut atteindre une machine personnelle.
- `build.yml` : `permissions: contents: read` explicite, cache NuGet.
- `release.yml` : garde `github.repository_owner == 'LokLakh-s'` (un fork hérite du workflow), et
  `softprops/action-gh-release` épinglée à son commit plutôt qu'au tag mouvant `v3`.
- Les deux dépôts sont fusionnés (voir plus bas) : `DIST_TOKEN` n'est plus utilisé par rien.

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

## Fusion des deux dépôts — faite

`LokLakh-s/JellyCrowd` est désormais le dépôt unique : source, distribution et documentation. Il a été
choisi comme cible parce qu'il détenait trois choses qu'on ne pouvait pas casser — l'URL du manifeste
`raw.githubusercontent.com/LokLakh-s/JellyCrowd/main/manifest.json` configurée dans le Jellyfin de chaque
utilisateur installé, les GitHub Releases vers lesquelles pointent les `sourceUrl` de toutes les versions
du manifeste, et ses étoiles.

Ce qui a été fait :

- `release.yml` réécrit : plus de clone du dépôt de distribution, plus de force-push à un commit unique,
  et surtout **plus d'étape « Reset public releases & tags »** — elle effaçait toutes les Releases sauf la
  dernière, ce qui aurait invalidé les `sourceUrl` des versions encore référencées par les manifestes
  installés. Le `manifest.json` est maintenant mis à jour **en place**, dans le même commit que
  l'estampille de version.
- `dist/` remonté à la racine : le README public (anglais) devient le README du dépôt, avec `logo.png`,
  `screenshots/` et les templates d'issues. L'ancien README racine en français est supprimé — son contenu
  de développement vit dans `CONTRIBUTING.md` et dans ce dossier.
- `manifest.json` repris tel quel depuis le dépôt public, avec ses trois entrées de versions.

Reste à faire, à la main :

- **Désenregistrer le runner self-hosted** de `JellyCrowd-dev` : plus rien ne l'utilise.
- **Révoquer le PAT `DIST_TOKEN`** et retirer le secret du dépôt.
- **Archiver `LokLakh-s/JellyCrowd-dev`** (ne pas le supprimer : il reste le miroir de l'historique privé).
- Envisager la soumission au **catalogue officiel des plugins Jellyfin**, que l'AGPL rend possible.
