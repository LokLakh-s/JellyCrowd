# Jelly Crowd — Tests manuels à faire (en lot)

> À exécuter quand tu peux (utilisateurs en ligne → tu groupes tout d'un coup).
> Coche au fur et à mesure. Ordre conseillé : **Prérequis → nouveautés → régression**.
> Les tests unitaires/intégration automatiques passent déjà (282 C# + 24 JS) ; cette liste couvre
> uniquement ce qui ne se vérifie qu'**en live** (vraie chaîne Jellyfin + Radarr/Sonarr/RDT).

✅ - Test OK
🟨 - Test a révélé des problèmes -> choses à prendre en compte
🟥 - Test NOK
❌ - Test non faisable ou non pertinent

## 0. Prérequis

- ✅ **Mettre à jour** le plugin vers la **dernière version** publiée (Dashboard → Plugins → Jelly Crowd) puis **redémarrer Jellyfin**.
- ✅ Vérifier la version active : Dashboard → Plugins → *Jelly Crowd* = dernière version, statut **Active**.
    --> 0.51.0.0
- ✅ Onglet **Diagnostic** (config admin) → tout vert (TMDB, File Transformation, backend servarr, dossier data).
- 🟨 Diagnostic → ligne **« Indexers »** : nombre d'indexers activés côté Radarr/Sonarr (warning si 0).
  - Je trouve qu'il faudrait le détail de Prowlarr, Radarr et Sonarr, car actuellement je ne vois que Radarr et "servarr"
  - élargis un peu la première colonne du tableau des Diagnostics
- ✅ Diagnostic → ligne **« Storage growth »** : estimation ~/utilisateur · ~/mois affichée.

## 1. M15 — Relance de recherche & état « Bloqué »

- ✅ Faire une requête d'un titre **introuvable** par les indexers (ex. un film obscur/inexistant) → elle reste **Approuvée**.
    Elle est en Approved Missing
- 🟨 Si le dispatch échoue (backend mal configuré volontairement, ou titre non résolu) → un badge rouge **« Bloqué »** apparaît sur la requête, **raison au survol**.
  Quasi systématiquement, les requêtes se mettent en Bloqué, "400 Bad request", quand je les relance elles passent direct. J'ai fait une demande de Backrooms, qui n'est pas encore sorti, il s'est mis en Unreleased, mais là quand j'ai rouvert mes requêtes, elle s'était mise en bloqué (avec toujours le Unreleased). Il se remet régulièrement en Blocked même après que j'ai cliqué sur Retry et que le blocked ait disparu. Parfois même pendant des downloads, j'ai des 400 Bad request
  Log Prowlarr quand je lance un retry:
  6:32am	ReleaseSearchService	Searching indexer(s): [C411] for Term: [Backrooms 2026], Offset: 0, Limit: 100, Categories: [2000, 2020, 2010]	
	6:32am	ReleaseSearchService	Searching indexer(s): [Torrent9] for Term: [Backrooms 2026], Offset: 0, Limit: 100, Categories: [2000]	
	6:32am	ReleaseSearchService	Searching indexer(s): [C411] for Term: [] | ID(s): IMDbId:[26657236] TMDbId:[1083381], Offset: 0, Limit: 100, Categories: [2000, 2020, 2010]	
	6:32am	ReleaseSearchService	Searching indexer(s): [YggReborn (API)] for Term: [Backrooms 2026], Offset: 0, Limit: 100, Categories: [2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060, 2070, 2080]
- ✅ Cliquer **« Relancer la recherche »** sur une requête approuvée → pas d'erreur ; côté Radarr/Sonarr une **nouvelle recherche** est déclenchée (vérifier dans l'Activity/History de Radarr/Sonarr).
    Ca fonctionne nickel à première vue, mais je ne veux pas donner la possibilité aux users de faire la recherche eux même, car Sonarr/Radarr vont le faire eux même. Donne la possibilité à l'admin d'activer l'option, mais elle doit être désactivée par défaut.
- ✅ Après une relance réussie, le badge **« Bloqué » disparaît** (erreur effacée).
- ✅ Vérifier qu'une **requête de saison** lance bien une recherche ciblée (saison monitorée, pas de recherche vide chez Prowlarr).
- ✅ (Logs) Onglet **Logs** admin → une entrée `download` apparaît pour la relance (succès `info` / échec `error`).

## 2. M16 — Auto-approbation (taille + genre + confiance)

- 🟥 **Config admin** → champ **« Auto-approve genres »** visible (sous le seuil de taille). Y mettre p. ex. `Documentary, Animation`. Régler le **seuil de taille** > 0.
  - Les genres ne semblent pas s'enregistrer : j'enregistre une valeur, j'enregistre et reviens sur la page : le champ est vide.
  - Remplace le champ par un menu déroulant qui liste les 20 genres les plus populaires de TMDB, afin que l'on puisse en sélectionner un ou plusieurs, et éviter les fautes de frappe.
  - Chaque genre s'ajoute sous forme de pastille non éditable avec une croix pour le supprimer.
- ❌ Mode **approbation manuelle** activé (RequireApproval). Requêter un titre **du bon genre et sous le seuil** → **auto-approuvé** (pas en file d'attente).
  Je n'ai pas pu tester avec quelqu'un d'autre que mon admin car je ne veux pas l'activer pour tous les users. Ajoute une option pour l'activer granulairement pour certains users
- ❌ Requêter un titre **hors genre** (mais sous le seuil) → reste **En attente** (file admin).
  Ca avait l'air de fonctionner, sauf que quand j'ai demandé un film qui n'avait pas le genre défini, il s'est également auto approuvé
- ❌ Requêter un titre **du bon genre mais au-dessus du seuil** → reste **En attente**.
  Il a été auto approuvé alors que j'avais mis le "Auto-approve if estimated size ≤ (GiB)" à 2 et que c'était un film, qui au final allait faire 6.45 Go selon RDT
- 🟥 **Vider** la liste de genres → le seuil de taille seul décide à nouveau (tous genres).
  Je me demande si ce n'est pas la faute du "auto-approve genres", qui garde quand même mes choix, mais a son champ vide (que je ne peux du coup pas vider)
- 🟥 Un **utilisateur de confiance** (override AutoApprove) → toujours auto-approuvé, **quel que soit** le genre.
  Je coche l'auto-approve pour un utilisateur, mais quand je recharge la page il n'est plus coché, et les requêtes de cet utilisateur ne sont pas acceptées auto.
- ❌ Dépassement de **quota** : une requête qui serait auto-approuvée mais dépasse le quota → **mise en attente** (pas refusée).
  J'ai voulu régler un quota custom pour un user pour tester, mais le quota ne s'enregistre pas.

## 3. M26 — Journal d'activité (logs)

- 🟨 Onglet **Logs** : après quelques actions (requête, dispatch), des entrées apparaissent **du plus récent au plus ancien**.
  Je ne vois pas de notif admin quand je change des réglages du plugin, ni system, ni user (j'ai testé de renseigner mon email dans les notifs utilisateur, cela devrait générer un log par exemple, ou les demandes de suppression, etc.)
- ✅ Filtres **catégorie** (`request` / `download`) et **niveau** (`info` / `error`) → filtrent correctement.
- 🟥 Recherche par **terme** (titre) → filtre correctement
  Cela ne fait rien quand je valide la recherche

## 4. Régression — Cycle de requête (M16/M22)

- ✅ Créer une requête film → suivre **En attente → Approuvée → (téléchargement) → Disponible**.
- 🟨 Le statut **« en téléchargement »** + % se met à jour (polling 3 s) sans rechargement de page.
  Je trouve encore le taux de rafraichissement trop bas, mais le statut est là et se rafraichit. Le temps estimé est rarement bon, et le % est rarement bon également.
- 🟨 **Annuler** une requête approuvée → disparaît + (film) retirée de Radarr.
  En effet coté utilisateur ça fonctionne, mais coté admin je vois que le film se télécharge quand même dans RDT (il se retire du monitoring Radarr donc n'apparait pas dans Jellyfin à la fin du téléchargement)
- ✅ Dates **demande** et **mise à disposition** affichées sur la requête.

## 5. Régression — Quota & « Mes médias » (M21/M23)

- ✅ Barre de **quota** dans le bandeau : taille utilisée / quota, couleur (vert→rouge).
  - Il faut remonter un peu l'ensemble My Library + quota + barre de quota car ce bloc n'est pas centré en hauteur dans le bandeau
- ✅ **Mes médias** : taille par média + barre de quota.
- 🟥 **Ajouter un média déjà dispo** (demandé par un autre) → avertissement quota, compté dans mon quota.
  - Je ne peux pas, car dans le catalogue, le bouton Détails n'apparait pas sous les médias présents, je ne peux pas ouvrir le popup. Quand je clique sur la jaquette ça me ramène au média dans Jellyfin, et sur la page Jellyfin du média, pas de bouton pour se l'ajouter.
  - Si possible, il faudrait un bouton pour se l'ajouter sur la page du média Jellyfin également.
- ✅ **Demander la suppression** → compte à rebours affiché ; **annuler** la suppression tant qu'à > 1 min de l'échéance.
- ❌ Propriété partagée : un média demandé par 2 users n'est **réellement supprimé** que quand **plus personne** ne le possède.
- 🟨 Liens cliquables : un média/un *Available* ouvre bien la **fiche Jellyfin**.
  Finalement on va retirer ce lien de la jaquette et le mettre sur le popup du média dans le catalogue + l'injecter sur la page du média dans Jellyfin.

## 6. Régression — Notifications (M17)

- ✅ **Cloche** dans le bandeau avec **pastille rouge + compteur** quand une notif arrive.
- ✅ Notifié sur **Approved / Denied / Available**.
  - Mets un petit emoji selon si la notif est 🟥🟨✅
- 🟨 **Notif « Échec »** : quand un dispatch échoue (1ʳᵉ fois), le **demandeur** reçoit une notif in-app (+ canal perso) « Request needs attention… ». Pas de doublon aux tentatives suivantes.
  - J'ai eu plusieurs fois la notif de mon film Backrooms qui n'était pas disponible : "The movie request "Backrooms" could not be fulfilled yet (no release found or a backend error). You can retry the search."
- ✅ **Effacer tout** dans le panneau de notifs fonctionne.
- ✅ **Bouton « × » par notification** : efface une seule notif ; la liste se met à jour (état vide si c'était la dernière).
- 🟥 **Préférences perso** (e-mail) : reçoit bien la notif.
  L'email n'arrive pas. Aucun mail ne semble plus partir, quand je fais un Test email depuis le panneau admin :
  "Could not load file or assembly 'MailKit, Version=4.17.0.0, Culture=neutral, PublicKeyToken=4e064fe7c44a8f1b'. The system cannot find the file specified."

## 7. Régression — Catalogue, calendrier, communauté (M24/M25)

- 🟨 **Catalogue** : filtres (genre/année/note/tri), recherche, modale détails (genres, runtime, liens).
  Le fond du menu déroulant "Sort by" est blanc et la police blanche également. Utilise le même style que les menus déroulants du calendrier.
  Modale détails trop petite mais je crois que je te l'ai déjà dit.
- ✅ **« Voir plus → »** sur une sous-section
- 🟨 **« Demander toute la saga »** (collection TMDB).
  Ca a l'air de fonctionner, mais je n'ose pas tester car je ne sais pas ce que ça va demander, et je pense que ça sera pareil pour les utilisateurs. Trouve un palliatif (confirmation ? Aperçu de la liste avant ?)
- ✅ **Calendrier** des sorties s'affiche.
- 🟥 **Commentaires** sous le synopsis : poster, voir, (admin) masquer/supprimer.
  Je ne vois pas les commentaires, ni d'option pour les activer. Il me semble qu'on les a désactivé, mais il faut ajouter une option admin pour les activer ou non, en avertissant que les users verront les pseudos des autres (ce que je veux éviter pour ma part).
- 🟥 **Signalement** d'un souci média → arrive dans la file admin ; admin peut résoudre/supprimer.
  Je ne vois nulle part quelque chose pour signaler quoi que ce soit, ni coté admin pour recevoir les "tickets" utilisateurs

## 8. Régression — Mobile / responsive (rapide)

- 🟨 Ouvrir l'overlay sur **téléphone** : bandeau, onglets, modale, barre de quota lisibles et utilisables.
  - Dans le catalogue :
    - la liste de filtres de genres à un scrolling léger alors qu'elle pourrait s'afficher en entière
    - sur les popups des films, la ligne de la review + note est compressée. Rend le bloc "Ta note:" + étoiles + champ commentaire optionnel + bouton "publier l'avis" vertical ?
    - le bouton Demander peut être à droite de la case de date souhaitée plutot qu'en dessous
    - j'ouvre un popup de film et appuie sur le bouton Retour du téléphone pour revenir dans la liste des films, il sort de l'application (voir Note de Victor n°3)
  - Dans le calendrier :
    - la vue mensuelle sur téléphone est trop compacte, on ne pourrait afficher que les jaquettes en mosaiques pour que les jours soient plus "carrés". Il faudrait proposer une vue hebdo et journalière également.
    - Actuellement je dois scroller à droite et à gauche en plus d'en haut et en bas pour voir tous les boutons de navigation du calendrier, filtres et le calendrier
  - Dans la vue Mes demandes :
    - Les badges des différents statuts chevauchent les titres et jaquettes, s'il y en a plus d'un, en plus de réduire la colonne de titre au max
    - les boutons sortent de l'écran à droite
    - si je scrolle sur téléphone, le bandeau disparait et je vois le contenu jellyfin qui défile à sa place (voir Note de Victor n°3)
  - Dans la vue Ma bibliothèque :
    - Les badges des différents statuts chevauchent les titres et jaquettes, s'il y en a plus d'un, en plus de réduire la colonne de titre au max
    - les boutons sortent de l'écran à droite
  - Dans la navbar, "Ma bibliothèque" est écrit sur deux lignes
  - Le volet de notifications sort de l'écran à gauche
  - L'annonce est beaucoup trop compressée pour s'afficher, sans doute lui faudra t'il une ligne pour elle seule sur tel.
  - Sur le pannel admin :
    - Onglet Requests :
      - Je ne peux pas scroller à droite, donc ne peux pas voir toutes les infos ni approuver les requêtes (pourtant j'ai un ascenseur horizontal apparent)
    - Onglet User quotas :
      - Je ne peux pas scroller à droite, donc ne peux pas voir toutes les infos

## 9. Bandeau natif & intégration (custom CSS)

- ✅ Ouvrir un volet du plugin (Catalog/Calendar/My requests/My media) → le **bandeau natif Jellyfin reste affiché en haut** (avec ton custom CSS : couleur `.headerRight`, tailles d'onglets, image de fond…), **pas** de bandeau custom qui le recouvre.
- ✅ Le contenu du plugin s'affiche **sous** le bandeau (pas par-dessus).
- ✅ Les liens **Catalog / Calendar / My requests** s'intègrent à la rangée d'onglets native ; **quota** + **cloche** dans `.headerRight`.
- ✅ Re-cliquer le lien **actif** referme le volet (le bandeau natif seul reste).
- ✅ Les menus déroulants natifs (compte, recherche…) s'ouvrent **au-dessus** du volet.
- ✅ Vérifier sur **mobile** que le décalage sous le bandeau est correct (pas de chevauchement).

## 10. Mode config (cacher le plugin)

- ✅ Config admin → cocher **« Config mode — hide the plugin from regular users »**, sauvegarder.
- 🟥 Avec un compte **non-admin** : aucun lien/quota/cloche Jelly Crowd dans le bandeau ; appel direct API (ex. `GET /JellyCrowd/Catalog/Trending`) → **403**.
  Le bandeau n'apparait pas. Je pense que le config mode doit réactiver le bandeau natif pour les utilisateur et n'afficher celui du mod qu'à l'admin.
- ✅ Avec le compte **admin** : le plugin reste **visible et utilisable** (pour configurer/tester).
- ✅ Décocher l'option → les utilisateurs revoient le plugin (après rechargement de page).

## 11. Nouveautés récentes (v0.44 → v0.47+)

- ✅ **Avis & notes (M29)** : activer « Enable ratings & reviews » (admin). Sur un titre du catalogue (popup), noter (1–10 en demi-étoiles) + commentaire optionnel ; la **moyenne interne** + nb de votes s'affichent (et un badge à côté de la note TMDB). Re-noter **met à jour** (pas de doublon). Avis **anonymes** pour les non-admins ; l'admin voit l'auteur + peut masquer/supprimer.
- ✅ **Bandeau natif** : sur Home + chaque librairie, seuls les liens Jelly Crowd (Home, Catalog, Calendar, My requests) s'affichent ; les onglets natifs sont masqués. Vérifier **Other & Books** (nos liens présents).
- ✅ **Lien Home** : ferme le volet plugin + va à l'accueil. Cliquer ailleurs (recherche, menu, librairie) **ferme** le volet.
- 🟨 **Annonce admin** : l'admin édite l'annonce depuis le bandeau (✎) → bandeau coloré vert/jaune/rouge visible par tous.
  Ca fonctionne, l'utilisateur lambda la voit bien, par contre sa mise à jour ne se fait qu'au refresh de la page après avoir cliqué sur Save ou Clear, du coup on ne sait pas que l'opération à réussi.
- ✅ **Calendrier** : titres demandés (par n'importe qui) en **violet** + demandes à date future affichées ; épisodes des séries suivies/demandées.
- ✅ **Expiration / propriété (M27)** : « My library » montre « Expire dans … » + bouton **Conserver (renouveler)** (reset 90 j). Régler « Media ownership expiry (days) ».
- ✅ **Cleanup admin** : Diagnostics → « Library cleanup » → **Scan media** (orphelins) → **Delete** (confirmation) supprime du disque.
- ✅ **Anti-freeze** : naviguer entre pages/volets ne fige plus le navigateur (régression v0.47.0 corrigée en v0.47.1).
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
- [ ] **Popup catalogue retravaillé** (suite à tes retours §7/§8) :
  - [ ] **Média déjà disponible** : un clic sur la carte (ou le bouton **Détails**, désormais présent partout) ouvre le **popup** (avant : ça t'envoyait direct dans Jellyfin sans popup). Les **reviews** y sont donc visibles.
  - [ ] Dans ce popup d'un dispo : un bouton **« Open in Jellyfin »** (lien visible vers la fiche native) + un bouton **« Add to my library »** (= s'approprie le média : démarre le compteur d'expiration, permet la demande de suppression).
  - [ ] **Popup agrandi** (≈980 px) + **distribution (cast)** affichée (photos + nom + rôle, strip scrollable).
  - [ ] **Formulaire d'avis vertical** (plus compressé) : « Ta note » + étoiles au-dessus, zone de texte pleine largeur, bouton dessous.
  - [ ] **Survol des étoiles** : passer la souris sur les étoiles **prévisualise** la note qui sera posée à cet endroit ; sortir du bloc restaure la note courante.

---

## Notes de Victor
1. Sur l'écran des requêtes du panel admin, colore les boutons car actuellement on les voit mal. Si tu peux rendre les tableaux Requests et User quotas plus sexy, je prends.
2. Colore les boutons de test dans le panneau d'admin car on ne les voit pas.
3. Le fait que tout soit dans un volet qui recouvre Jellyfin est il obligatoire ? Est possible de tout déplacer sur des pages dédiées ? Ca serait plus fluide pour la navigation (page précédente, pas de scroll fantôme du background). Va t'on perdre des fonctionnalités ? Réponds moi ce avant de bouger sur ce sujet.
4. Sur l'écran des requêtes, un clic sur le titre ou la jaquette doit ouvrir le popup du média
5. Les requêtes "Unreleased" doivent afficher la date de sortie et la date de prochaine tentative
6. Le popup doit afficher la release date, la liste d'acteurs, le réalisateur, le titre original.
7. La liste d'acteurs et le réalisateur doivent être cliquables et amener sur le catalogue filtré par cet acteur/réalisateur
8. Le lien Home sur le logo Keeklah.tv dans le bandeau doit avoir le curseur qui change on hover, car actuellement on ne voit pas que c'est un lien. Le lien My library doit réagir on hover aussi
9. sur le popup, les liens TMDB et IMDb doivent être entre le synopsis et le cast
10. 