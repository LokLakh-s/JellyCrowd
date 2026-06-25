# Jelly Crowd — Tests manuels à faire (en lot)

> À exécuter quand tu peux (utilisateurs en ligne → tu groupes tout d'un coup).
> Coche au fur et à mesure. Ordre conseillé : **Prérequis → nouveautés → régression**.
> Les tests unitaires/intégration automatiques passent déjà (343 C# + 24 JS) ; cette liste couvre
> uniquement ce qui ne se vérifie qu'**en live** (vraie chaîne Jellyfin + Radarr/Sonarr/RDT).

✅ - Test OK
🟨 - Test a révélé des problèmes -> choses à prendre en compte
🟥 - Test NOK
❌ - Test non faisable ou non pertinent
🔧 - Corrigé par Claude, à revérifier en live (réinstaller la dernière version)

## 0. Prérequis

- 🟨 Diagnostic → ligne **« Indexers »** : nombre d'indexers activés côté Radarr/Sonarr (warning si 0).
  - Je trouve qu'il faudrait le détail de Prowlarr, Radarr et Sonarr, car actuellement je ne vois que Radarr et "servarr"
  - élargis un peu la première colonne du tableau des Diagnostics
  - 🔧 Corrigé : la ligne Indexers liste maintenant **Radarr ET Sonarr séparément** (« Radarr: N enabled · Sonarr: M enabled »). Première colonne du tableau Diagnostics élargie (15em).
  - 🔧 **Prowlarr configurable** : nouveaux champs **Prowlarr URL + API key** (onglet Download, sous Sonarr). S'ils sont renseignés, la ligne Indexers affiche aussi **« Prowlarr: N enabled »** (interrogé sur son API v1).

## 1. M15 — Relance de recherche & état « Bloqué »

- 🟨 Si le dispatch échoue (backend mal configuré volontairement, ou titre non résolu) → un badge rouge **« Bloqué »** apparaît sur la requête, **raison au survol**.
  Quasi systématiquement, les requêtes se mettent en Bloqué, "400 Bad request", quand je les relance elles passent direct. J'ai fait une demande de Backrooms, qui n'est pas encore sorti, il s'est mis en Unreleased, mais là quand j'ai rouvert mes requêtes, elle s'était mise en bloqué (avec toujours le Unreleased). Il se remet régulièrement en Blocked même après que j'ai cliqué sur Retry et que le blocked ait disparu. Parfois même pendant des downloads, j'ai des 400 Bad request
  - 🔧 Corrigé (cause racine) : le dispatch **ré-ajoutait** systématiquement le film/série ; Radarr/Sonarr renvoient **400 "already exists"** → l'échec ne marquait pas la requête comme dispatchée → la tâche planifiée la ré-ajoutait en boucle (= flapping Blocked, même pendant un download). Le dispatch est maintenant **idempotent** : s'il est déjà dans Radarr/Sonarr → on lance une **recherche** (pas de ré-ajout) ; sinon on l'ajoute. Les requêtes coincées se débloquent toutes seules au prochain cycle (le succès efface l'erreur). À revérifier en live.
  Log Prowlarr quand je lance un retry:
  6:32am	ReleaseSearchService	Searching indexer(s): [C411] for Term: [Backrooms 2026], Offset: 0, Limit: 100, Categories: [2000, 2020, 2010]	
	6:32am	ReleaseSearchService	Searching indexer(s): [Torrent9] for Term: [Backrooms 2026], Offset: 0, Limit: 100, Categories: [2000]	
	6:32am	ReleaseSearchService	Searching indexer(s): [C411] for Term: [] | ID(s): IMDbId:[26657236] TMDbId:[1083381], Offset: 0, Limit: 100, Categories: [2000, 2020, 2010]	
	6:32am	ReleaseSearchService	Searching indexer(s): [YggReborn (API)] for Term: [Backrooms 2026], Offset: 0, Limit: 100, Categories: [2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060, 2070, 2080]
- ✅ Cliquer **« Relancer la recherche »** sur une requête approuvée → pas d'erreur ; côté Radarr/Sonarr une **nouvelle recherche** est déclenchée (vérifier dans l'Activity/History de Radarr/Sonarr).
    Ca fonctionne nickel à première vue, mais je ne veux pas donner la possibilité aux users de faire la recherche eux même, car Sonarr/Radarr vont le faire eux même. Donne la possibilité à l'admin d'activer l'option, mais elle doit être désactivée par défaut.
    - 🔧 Corrigé : nouvelle option admin **« Let users retry the search themselves »** (OFF par défaut, onglet Settings). Quand OFF, le bouton Retry n'apparaît que pour l'admin et l'endpoint renvoie 403 aux users ; quand ON, les users le retrouvent.

## 2. M16 — Auto-approbation (taille + genre + confiance)

- 🟥 **Config admin** → champ **« Auto-approve genres »** visible (sous le seuil de taille). Y mettre p. ex. `Documentary, Animation`. Régler le **seuil de taille** > 0.
  - Les genres ne semblent pas s'enregistrer : j'enregistre une valeur, j'enregistre et reviens sur la page : le champ est vide.
    - 🔧 Corrigé : `AutoApproveGenres`/`QuotaOverrides` étaient des collections *get-only* → System.Text.Json les ignorait au save. Rendues settables. Les genres (et les overrides par user) s'enregistrent désormais.
  - Remplace le champ par un menu déroulant qui liste les 20 genres les plus populaires de TMDB, afin que l'on puisse en sélectionner un ou plusieurs, et éviter les fautes de frappe. *(amélioration UI : à faire)*
  - Chaque genre s'ajoute sous forme de pastille non éditable avec une croix pour le supprimer. *(à faire avec le point ci-dessus)*
- ❌ Mode **approbation manuelle** activé (RequireApproval). Requêter un titre **du bon genre et sous le seuil** → **auto-approuvé** (pas en file d'attente).
  Je n'ai pas pu tester avec quelqu'un d'autre que mon admin car je ne veux pas l'activer pour tous les users. Ajoute une option pour l'activer granulairement pour certains users
- ❌ Requêter un titre **hors genre** (mais sous le seuil) → reste **En attente** (file admin).
  Ca avait l'air de fonctionner, sauf que quand j'ai demandé un film qui n'avait pas le genre défini, il s'est également auto approuvé
- ❌ Requêter un titre **du bon genre mais au-dessus du seuil** → reste **En attente**.
  Il a été auto approuvé alors que j'avais mis le "Auto-approve if estimated size ≤ (GiB)" à 2 et que c'était un film, qui au final allait faire 6.45 Go selon RDT
- 🟥 **Vider** la liste de genres → le seuil de taille seul décide à nouveau (tous genres).
  Je me demande si ce n'est pas la faute du "auto-approve genres", qui garde quand même mes choix, mais a son champ vide (que je ne peux du coup pas vider)
  - 🔧 Corrigé (même cause) : vider le champ + enregistrer efface bien la liste maintenant.
- 🟥 Un **utilisateur de confiance** (override AutoApprove) → toujours auto-approuvé, **quel que soit** le genre.
  Je coche l'auto-approve pour un utilisateur, mais quand je recharge la page il n'est plus coché, et les requêtes de cet utilisateur ne sont pas acceptées auto.
  - 🔧 Corrigé (même cause : `QuotaOverrides` settable) : la case auto-approve par user persiste désormais.
- ❌ Dépassement de **quota** : une requête qui serait auto-approuvée mais dépasse le quota → **mise en attente** (pas refusée).
  J'ai voulu régler un quota custom pour un user pour tester, mais le quota ne s'enregistre pas.
  - 🔧 Corrigé (même cause : `QuotaOverrides` settable) : le quota custom par user s'enregistre désormais. (Reste à revérifier le comportement de mise en attente.)

## 3. M26 — Journal d'activité (logs)

- 🟨 Onglet **Logs** : après quelques actions (requête, dispatch), des entrées apparaissent **du plus récent au plus ancien**.
  Je ne vois pas de notif admin quand je change des réglages du plugin, ni system, ni user (j'ai testé de renseigner mon email dans les notifs utilisateur, cela devrait générer un log par exemple, ou les demandes de suppression, etc.)
  - 🔧 Corrigé : nouvelles entrées de log — **admin** : « Plugin configuration saved » (à chaque sauvegarde des réglages, y compris l'annonce) ; **user** : email/préférences mises à jour, demande de suppression / annulation, ajout à la bibliothèque (claim). (Les catégories admin/user/system existaient dans le filtre mais n'étaient jamais écrites.)
- 🟥 Recherche par **terme** (titre) → filtre correctement
  Cela ne fait rien quand je valide la recherche
  - 🔧 Corrigé : la zone de recherche des logs filtre maintenant sur **Entrée** ET en direct (au fil de la frappe, débounce 350 ms). Avant, elle n'était lue qu'au clic sur « Refresh ».

## 4. Régression — Cycle de requête (M16/M22)

- 🟨 Le statut **« en téléchargement »** + % se met à jour (polling 3 s) sans rechargement de page.
  Je trouve encore le taux de rafraichissement trop bas, mais le statut est là et se rafraichit. Le temps estimé est rarement bon, et le % est rarement bon également.
  - 🔧 Cadence de polling passée de 3 s → **2 s**. ⚠️ La précision du **%/ETA** vient des valeurs renvoyées par Radarr/Sonarr (calcul `(size-sizeleft)/size`, correct) : avec RDT/debrid ces valeurs sont intrinsèquement approximatives — pas corrigeable côté plugin.
- 🟨 **Annuler** une requête approuvée → disparaît + (film) retirée de Radarr.
  En effet coté utilisateur ça fonctionne, mais coté admin je vois que le film se télécharge quand même dans RDT (il se retire du monitoring Radarr donc n'apparait pas dans Jellyfin à la fin du téléchargement)
  - 🔧 Corrigé : à l'annulation, on **retire d'abord le téléchargement de la file Radarr** (`DELETE /queue/{id}?removeFromClient=true`) — donc le grab RDT s'arrête — puis on supprime le film. À revérifier en live.

## 5. Régression — Quota & « Mes médias » (M21/M23)

- ✅ Barre de **quota** dans le bandeau : taille utilisée / quota, couleur (vert→rouge).
  - Il faut remonter un peu l'ensemble My Library + quota + barre de quota car ce bloc n'est pas centré en hauteur dans le bandeau
    - 🔧 Bloc quota : `align-self:center` + `height:100%` sur le conteneur (centrage vertical robuste dans le bandeau natif) + line-height resserré. À confirmer visuellement.
- 🟥 **Ajouter un média déjà dispo** (demandé par un autre) → avertissement quota, compté dans mon quota.
  - Je ne peux pas, car dans le catalogue, le bouton Détails n'apparait pas sous les médias présents, je ne peux pas ouvrir le popup. Quand je clique sur la jaquette ça me ramène au média dans Jellyfin, et sur la page Jellyfin du média, pas de bouton pour se l'ajouter.
    - 🔧 Corrigé : tout média (y compris dispo) a un bouton **Détails** et la carte ouvre le **popup** (ne renvoie plus direct dans Jellyfin). Le popup d'un dispo a **Open in Jellyfin** + **Add to my library**.
  - Si possible, il faudrait un bouton pour se l'ajouter sur la page du média Jellyfin également.
    - 🔧 Corrigé : un bouton **« Add to my library »** est injecté sur la **fiche native** (film/série avec TMDB), à côté des boutons d'action ; il claim le média (POST Claim). À revérifier en live.
- ❌ Propriété partagée : un média demandé par 2 users n'est **réellement supprimé** que quand **plus personne** ne le possède.
- 🟨 Liens cliquables : un média/un *Available* ouvre bien la **fiche Jellyfin**.
  Finalement on va retirer ce lien de la jaquette et le mettre sur le popup du média dans le catalogue + l'injecter sur la page du média dans Jellyfin.
  - 🔧 Partiellement fait : la jaquette ouvre le **popup** (plus de saut direct vers Jellyfin) ; le lien **Open in Jellyfin** est dans le popup. *(reste : bouton Add to library sur la page native Jellyfin)*

## 6. Régression — Notifications (M17)

- ✅ Notifié sur **Approved / Denied / Available**.
  - Mets un petit emoji selon si la notif est 🟥🟨✅
    - 🔧 Corrigé : chaque notif du panneau cloche affiche un emoji de statut — ✅ (Approved/Available), 🟥 (Denied/Failed), 🟨 (quota/expiration), 🔔 (autre).
- 🟨 **Notif « Échec »** : quand un dispatch échoue (1ʳᵉ fois), le **demandeur** reçoit une notif in-app (+ canal perso) « Request needs attention… ». Pas de doublon aux tentatives suivantes.
  - J'ai eu plusieurs fois la notif de mon film Backrooms qui n'était pas disponible : "The movie request "Backrooms" could not be fulfilled yet (no release found or a backend error). You can retry the search."
- 🟥 **Préférences perso** (e-mail) : reçoit bien la notif.
  L'email n'arrive pas. Aucun mail ne semble plus partir, quand je fais un Test email depuis le panneau admin :
  "Could not load file or assembly 'MailKit, Version=4.17.0.0, Culture=neutral, PublicKeyToken=4e064fe7c44a8f1b'. The system cannot find the file specified."
  - 🔧 Corrigé : le zip de release ne contenait pas MailKit/MimeKit/BouncyCastle (un build *library* ne copie pas les deps NuGet). Ajout de `CopyLocalLockFileAssemblies` → les DLL sont maintenant packagées. À revérifier (Test email + notifs perso).

## 7. Régression — Catalogue, calendrier, communauté (M24/M25)

- 🟨 **Catalogue** : filtres (genre/année/note/tri), recherche, modale détails (genres, runtime, liens).
  Le fond du menu déroulant "Sort by" est blanc et la police blanche également. Utilise le même style que les menus déroulants du calendrier.
  Modale détails trop petite mais je crois que je te l'ai déjà dit.
  - 🔧 Corrigé : menus Sort/Lang/Country (+ "act as" admin) fond sombre + options lisibles. Modale agrandie (~980 px), refondue (cast, avis vertical, Request sous l'affiche, report sur la ligne du titre).
- 🟨 **« Demander toute la saga »** (collection TMDB).
  Ca a l'air de fonctionner, mais je n'ose pas tester car je ne sais pas ce que ça va demander, et je pense que ça sera pareil pour les utilisateurs. Trouve un palliatif (confirmation ? Aperçu de la liste avant ?)
  - 🔧 Corrigé : 1er clic = **aperçu** (liste des films + statut + nb réellement demandés) avec **Confirmer/Annuler** ; rien n'est envoyé sans confirmation.
- 🟥 **Commentaires** sous le synopsis : poster, voir, (admin) masquer/supprimer.
  Je ne vois pas les commentaires, ni d'option pour les activer. Il me semble qu'on les a désactivé, mais il faut ajouter une option admin pour les activer ou non, en avertissant que les users verront les pseudos des autres (ce que je veux éviter pour ma part).
- 🟥 **Signalement** d'un souci média → arrive dans la file admin ; admin peut résoudre/supprimer.
  Je ne vois nulle part quelque chose pour signaler quoi que ce soit, ni coté admin pour recevoir les "tickets" utilisateurs
  - 🔧 Corrigé (visibilité) : le bouton **⚠ Report a problem** est maintenant bien visible sur la ligne du titre du popup (tout titre, y compris dispo). Côté admin la file **Reports** est en bas de l'onglet **Requests** + un **badge rouge** sur l'onglet quand des tickets non résolus arrivent. (Le flux existait, il était juste invisible.)

## 8. Régression — Mobile / responsive (rapide)

- 🟨 Ouvrir l'overlay sur **téléphone** : bandeau, onglets, modale, barre de quota lisibles et utilisables.
  - 🔧 **Overlay** : le fond ne défile plus derrière (body verrouillé tant qu'un volet est ouvert → fin du « scroll fantôme »).
  - Dans le catalogue :
    - la liste de filtres de genres à un scrolling léger alors qu'elle pourrait s'afficher en entière
      - 🔧 Sur mobile, la liste de genres s'affiche **en entier** (plus de scroll interne).
    - sur les popups des films, la ligne de la review + note est compressée. Rend le bloc "Ta note:" + étoiles + champ commentaire optionnel + bouton "publier l'avis" vertical ?
      - 🔧 Déjà rendu **vertical** lors de la refonte du popup (étoiles au-dessus, texte pleine largeur, bouton dessous).
    - le bouton Demander peut être à droite de la case de date souhaitée plutot qu'en dessous
      - ℹ️ Layout du popup revu depuis : la zone Request est sous l'affiche (colonne gauche). À réévaluer en live si encore gênant.
    - j'ouvre un popup de film et appuie sur le bouton Retour du téléphone pour revenir dans la liste des films, il sort de l'application (voir Note de Victor n°3)
      - ℹ️ Lié à l'architecture overlay (Note #3) — à traiter avec ce sujet.
  - Dans le calendrier :
    - la vue mensuelle sur téléphone est trop compacte, on ne pourrait afficher que les jaquettes en mosaiques pour que les jours soient plus "carrés". Il faudrait proposer une vue hebdo et journalière également.
      - 🔧 Corrigé : **sélecteur Mois / Semaine / Jour**. Vue **Mois** = mosaïque de jaquettes sur mobile (jours plus carrés). Vues **Semaine** et **Jour** = liste verticale par jour (pas de scroll horizontal). ‹ › et Aujourd'hui naviguent selon la vue active.
    - Actuellement je dois scroller à droite et à gauche en plus d'en haut et en bas pour voir tous les boutons de navigation du calendrier, filtres et le calendrier
      - 🔧 La barre de navigation **wrappe** (plus de scroll horizontal) ; les vues Semaine/Jour sont verticales.
  - Dans la vue Mes demandes :
    - Les badges des différents statuts chevauchent les titres et jaquettes, s'il y en a plus d'un, en plus de réduire la colonne de titre au max
    - les boutons sortent de l'écran à droite
      - 🔧 Sur mobile, les lignes (Mes demandes ET Ma bibliothèque) **wrappent** : titre sur sa ligne, badges + boutons passent en dessous (plus de chevauchement ni de débordement à droite).
    - si je scrolle sur téléphone, le bandeau disparait et je vois le contenu jellyfin qui défile à sa place (voir Note de Victor n°3)
      - 🔧 Corrigé par le verrouillage du scroll du fond (voir §8 overlay ci-dessus).
  - Dans la vue Ma bibliothèque :
    - Les badges des différents statuts chevauchent les titres et jaquettes, s'il y en a plus d'un, en plus de réduire la colonne de titre au max
    - les boutons sortent de l'écran à droite
      - 🔧 Même correctif que Mes demandes (lignes qui wrappent sur mobile).
  - Dans la navbar, "Ma bibliothèque" est écrit sur deux lignes
    - 🔧 Corrigé : `white-space:nowrap` sur le libellé (reste sur une ligne).
  - Le volet de notifications sort de l'écran à gauche
    - 🔧 Corrigé : sur mobile le panneau de notifs s'affiche en pleine largeur (0.5em de marge), jamais hors écran.
  - L'annonce est beaucoup trop compressée pour s'afficher, sans doute lui faudra t'il une ligne pour elle seule sur tel.
    - 🔧 Corrigé : sur mobile l'annonce passe sur **sa propre ligne** sous le logo (headerLeft wrap) + texte qui peut s'enrouler.
  - Sur le pannel admin :
    - Onglet Requests :
      - Je ne peux pas scroller à droite, donc ne peux pas voir toutes les infos ni approuver les requêtes (pourtant j'ai un ascenseur horizontal apparent)
    - Onglet User quotas :
      - Je ne peux pas scroller à droite, donc ne peux pas voir toutes les infos
    - 🔧 Corrigé : les tableaux admin (Requests / User quotas / Reports) ont un **conteneur scrollable horizontalement** + largeur min, donc on accède à toutes les colonnes (et au bouton Approuver) sur mobile.

## 10. Mode config (cacher le plugin)

- 🟥 Avec un compte **non-admin** : aucun lien/quota/cloche Jelly Crowd dans le bandeau ; appel direct API (ex. `GET /JellyCrowd/Catalog/Trending`) → **403**.
  Le bandeau n'apparait pas. Je pense que le config mode doit réactiver le bandeau natif pour les utilisateur et n'afficher celui du mod qu'à l'admin.
  - 🔧 Corrigé : on ne masque les onglets natifs **que** si le plugin est visible. En config mode, le non-admin retrouve le **bandeau natif** normal.

## 11. Nouveautés récentes (v0.44 → v0.47+)

- 🟨 **Annonce admin** : l'admin édite l'annonce depuis le bandeau (✎) → bandeau coloré vert/jaune/rouge visible par tous.
  Ca fonctionne, l'utilisateur lambda la voit bien, par contre sa mise à jour ne se fait qu'au refresh de la page après avoir cliqué sur Save ou Clear, du coup on ne sait pas que l'opération à réussi.
  - 🔧 Corrigé : l'endpoint renvoyait 204 et le client tentait de parser du JSON (rejet → pas de MAJ). Le bandeau se met à jour **en direct** + un **✓** confirme le succès.
- 🟨 **Notifs e-mail par catégorie (opt-in)** : cloche → ⚙ Réglages. Renseigner une **adresse e-mail** + cocher des catégories (toutes **OFF** par défaut). Avec SMTP admin configuré :
  Les emails ne partant pas, je ne peux pas tester
  - ❌ cocher **« requête classique disponible »** seulement → recevoir l'e-mail quand un titre **déjà sorti** devient dispo, **mais pas** les décisions/quota.
  - ❌ cocher **« titre demandé avant sortie »** → un titre demandé **avant** sa date de sortie qui devient dispo déclenche l'e-mail (et **pas** un titre déjà sorti).
  - ❌ cocher **« approuvées / refusées / échec »** → e-mail sur ces décisions.
  - ❌ cocher **« quota & expiration »** → e-mail quand une requête est **bloquée pour cause de quota**, et quand un média **expire** de ma bibliothèque (tâche de purge).
  - ❌ la **cloche in-app** continue d'afficher toutes les notifs quelle que soit la config e-mail.
- 🟨 **Avis sur la fiche native (M25.2)** : avec « Enable ratings & reviews » activé, ouvrir la **fiche d'un film/série** dans le client Jellyfin natif → un panneau **« Reviews »** apparaît sous la page (moyenne ★ + nb, curseur 1–10 + texte pour poster/mettre à jour, liste anonyme ; admin voit les auteurs). Naviguer vers une autre fiche met à jour le panneau ; une fiche **sans TMDB** ou un **épisode/saison** → pas de panneau. Désactiver l'option → plus de panneau.
  C'est pas mal, elle apparait bien, mais plusieurs choses :
  - Le slider pour choisir la note doit être remplacé par les mêmes étoiles que sur le popup
  - il faut pouvoir aller à la ligne, le champ de texte est trop petit
  - ce panneau doit apparaitre plus haut, si possible sous l'affiche du film, colonne de gauche, ou bien dans une deuxième colonne au même niveau que le synopsis.
- 🟥 **Popup mobile (portrait)** : depuis le téléphone, le popup s'affiche correctement pendant un quart de seconde, puis tout semble changer de format et sort de l'écran des deux côtés (uniquement en **portrait** ; le **paysage** fonctionne bien).
  - 🔧 Re-corrigé : sur mobile le modal est **clampé** (`width/max-width:100%`, `overflow-x:hidden`, padding réduit) et `.jellycrowd-modal-content` passe en `min-width:0` → un enfant large (strip cast) ne peut plus pousser le popup hors écran. À revérifier en portrait.

## 12. Lot de correctifs récents (à revérifier en live)

- 🟥 🔧 **Boutons admin Requests colorés** : **Approve** (vert) / **Deny** (rouge) bien visibles.
  ils sont toujours noirs, comme les boutons de Test sur l'onglet des notifications.
  - 🔧 Re-corrigé : mes classes étaient écrasées par `.emby-button.raised` (plus spécifique). Règles **préfixées par `#JellyCrowdConfigPage`** (spécificité ID) → Approve **vert**, Deny/Delete **rouge**, et **tous les boutons Test** (`.jcTestRow`) en **bleu Jellyfin**.
- 🟥 🔧 **Tableau User quotas, 1ʳᵉ ligne** : alignée comme les autres (avatar = initiales pour les users sans photo, donc plus de décalage dû à la photo de l'admin).
  C'est toujours pareil
  - 🔧 Re-corrigé : **toutes** les lignes affichent désormais le même élément (cercle d'initiales, **plus aucune photo**) → la ligne de l'admin (seule à avoir une photo) ne diffère plus. Si ça diffère encore, c'est autre chose → envoie une capture.
- 🟥 🔧 **Taille finale pendant le téléchargement** : l'écran **Mes demandes** affiche la taille (connue via RDT) à côté du %/ETA.
  Test : la taille affichée sur une requête en cours de téléchargement (5.1 GiB) n'est pas la même que celle de RDT (5.47 GB)
  - 🔧 Re-corrigé : c'était la **même taille en unités différentes** (5,1 **GiB** binaire = 5,47 **GB** décimal). On affiche maintenant en **GB décimaux** (/1000) pour coller à RDT.
- 🟨 🔧 **Popup** : le bouton **⚠ Report a problem** (ligne du titre) ne chevauche plus la croix de fermeture.
- [ ] 🔧 **Calendrier** : vues **Mois / Semaine / Jour** (voir §8).
  Pas mal, mais je voudrais que sur les différents affichage, un clic sur Today ouvre un popup de calendrier pour choisir la période voulue

---

## 13. Lot juin 2026 — N15→N36, quotas adaptatifs & suppression (à vérifier en live)

> Tout ci-dessous est **livré + couvert par des tests auto** mais demande une **vérif live** (vraie
> chaîne Jellyfin + Radarr/Sonarr/RDT) avant la 1.0. Réinstalle la dernière version d'abord.

### Suppression & quota (gros correctif — à tester en priorité)

- [ ] 🔧 **Suppression d'une saison** (le bug For All Mankind S1) : demander la suppression d'une saison → après la rétention, la saison disparaît **de Ma bibliothèque ET de Jellyfin** (dossier `Season 0X` supprimé du disque), **et de Sonarr** (saison plus monitorée, fichiers supprimés), **et le quota se libère** de la taille de cette saison.
- [ ] 🔧 **Suppression d'un film** : retiré de Radarr + dossier `Titre (année) [imdbid-…]` supprimé du disque + Jellyfin + quota libéré.
- [ ] 🔧 **Suppression d'un épisode** (depuis *Ma bibliothèque* → arbre série › saison › épisode) : seul cet épisode part (fichier supprimé, épisode démonitoré dans Sonarr), le reste de la saison reste.
- [ ] 🔧 **Suppression série entière** (demande au niveau série) : toute la série retirée de Sonarr + dossier supprimé.
- [ ] 🔧 **Taille par saison/épisode** : le quota d'une série reflète la somme **par saison/épisode possédé** (plus la série entière) ; *Ma bibliothèque* affiche la taille par saison, et le total série = somme.
- [ ] 🔧 **Purge vérifiée (N18)** : si un backend (Radarr/Sonarr) est **injoignable** au moment de la purge, la requête **reste flaggée** et la suppression se **réessaie** au passage horaire suivant (rien n'est supprimé localement tant que la purge n'a pas réussi).

### Quotas adaptatifs (M27.A)

- [ ] **Activation** : onglet *Settings* → activer « Adaptive quota » + régler plancher/plafond (% du quota par défaut) et seuils. Désactivé = comportement fixe inchangé.
- [ ] **Montée** : un utilisateur qui regarde assez (volume + jours distincts) voit son quota monter vers le **plafond** (badge ★ « Bonus storage » sur la barre).
- [ ] **Sursis** : un utilisateur récompensé puis inactif passe en **sursis** (badge ⏳ + compte à rebours), puis retombe au **base** s'il ne revient pas (notif).
- [ ] **Suivi d'activité** : la lecture dans Jellyfin alimente bien les agrégats (pas de collecte si l'option est OFF).

### Requêtes & téléchargement

- [ ] 🔧 **Saison en cours** : demander une saison dont seuls quelques épisodes sont sortis → **une seule ligne** « … · Saison N » dans *Mes demandes* (compteur X/Y + date du prochain épisode), pas une ligne par épisode ni un flot de notifs ; seuls les épisodes réellement présents passent *Available*.
- [ ] 🔧 **Multi-saisons (le bug Hannibal)** : demander plusieurs saisons → **toutes** sont monitorées + recherchées dans Sonarr (pas seulement la S1).
- [ ] 🔧 **Auto-retry** : si les indexeurs sont morts au moment de la demande, la recherche se **relance toute seule** (≤ 6 h) sans action manuelle ; abandon après 14 j.
- [ ] 🔧 **Unreleased multi-demandeurs** : un titre non sorti demandé par plusieurs personnes → à la sortie, **tous** les demandeurs le possèdent.
- [ ] 🔧 **Autosort *Mes demandes*** : ordre Approved → Downloading → Deletion requested → Unreleased → Available (le tier Downloading suit la file Sonarr en direct).
- [ ] 🔧 **Quota provisoire** : à la demande, le quota se pré-incrémente (5 Go/film, 1 Go/épisode) puis se recale sur la taille réelle à l'arrivée.

### UI / popup / navigation

- [ ] 🔧 **Suppression granulaire** : *Ma bibliothèque* → série dépliable (saisons › épisodes) avec bouton supprimer à chaque niveau.
- [ ] 🔧 **Popup série** : une série partiellement en biblio propose toujours les **autres saisons** (plus de comportement « film déjà dispo »).
- [ ] 🔧 **Add to my library** n'apparaît **que** si le média n'est pas déjà possédé (fiche native).
- [ ] 🔧 **Fond → Home** : ouvrir un volet puis le fermer (×, Échap, nav) ramène toujours sur **Home**.
- [ ] 🔧 **Filtre acteur** : clic sur un acteur/réalisateur/**scénariste** → filmographie ; **vignette de filtre** avec croix + bouton Reset rouge ; on en sort facilement.
- [ ] 🔧 **Casting** trié comme IMDb ; **Réalisateur + Scénariste** affichés et cliquables.
- [ ] 🔧 **Related media** : bande de suggestions cliquables en bas du popup.
- [ ] 🔧 **Titres cliquables** dans le menu « Demander la saga ».
- [ ] 🔧 **Bandeau annonce** : s'affiche sur **jusqu'à 3 lignes** (plus tronqué).
- [ ] 🔧 **Alignement navbar (N36)** : cloche + annonce centrées verticalement comme les onglets (sinon capture).

### Admin & notifications

- [ ] 🔧 **Quota par utilisateur** visible dans l'onglet *User quotas* (colonne Usage : utilisé / quota + palier).
- [ ] 🔧 **Retry par requête** dans l'onglet *Requests* admin (relance le backend, marche aussi sur les requêtes on-behalf).
- [ ] 🔧 **Boîte ops e-mail** : ne reçoit plus tous les événements de tous les users — seulement « Created » par défaut (toggles par événement). Vérifier qu'une demande on-behalf n'inonde plus l'admin.

## Notes de Victor

> Intégrées à la **ROADMAP** → section **« Patches de finition pré-1.0 → B. À faire »**
> (boutons admin bleus, clic titre→popup sur l'écran Requests, dates Unreleased, popup
> release/réalisateur/titre original + acteurs cliquables, liens TMDB/IMDb repositionnés,
> hover Home/My library, statut Downloading côté admin, toggle pseudos sur les avis,
> disclaimer latence d'affichage, reconcile plus rapide, suite de non-régression).
> Cette liste de tests ne garde que les points **encore ouverts en live** ci-dessus.
