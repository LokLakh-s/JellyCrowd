# Jelly Crowd — Tests manuels à faire (en lot)

> À exécuter quand tu peux (utilisateurs en ligne → tu groupes tout d'un coup).
> Coche au fur et à mesure. Ordre conseillé : **Prérequis → nouveautés → régression**.
> Les tests unitaires/intégration automatiques passent déjà (282 C# + 24 JS) ; cette liste couvre
> uniquement ce qui ne se vérifie qu'**en live** (vraie chaîne Jellyfin + Radarr/Sonarr/RDT).

## 0. Prérequis

- [ ] **Mettre à jour** le plugin vers la **dernière version** publiée (Dashboard → Plugins → Jelly Crowd) puis **redémarrer Jellyfin**.
- [ ] Vérifier la version active : Dashboard → Plugins → *Jelly Crowd* = dernière version, statut **Active**.
- [ ] Onglet **Diagnostic** (config admin) → tout vert (TMDB, File Transformation, backend servarr, dossier data).
- [ ] Diagnostic → ligne **« Indexers »** : nombre d'indexers activés côté Radarr/Sonarr (warning si 0).
- [ ] Diagnostic → ligne **« Storage growth »** : estimation ~/utilisateur · ~/mois affichée.

## 1. M15 — Relance de recherche & état « Bloqué »

- [ ] Faire une requête d'un titre **introuvable** par les indexers (ex. un film obscur/inexistant) → elle reste **Approuvée**.
- [ ] Si le dispatch échoue (backend mal configuré volontairement, ou titre non résolu) → un badge rouge **« Bloqué »** apparaît sur la requête, **raison au survol**.
- [ ] Cliquer **« Relancer la recherche »** sur une requête approuvée → pas d'erreur ; côté Radarr/Sonarr une **nouvelle recherche** est déclenchée (vérifier dans l'Activity/History de Radarr/Sonarr).
- [ ] Après une relance réussie, le badge **« Bloqué » disparaît** (erreur effacée).
- [ ] Vérifier qu'une **requête de saison** lance bien une recherche ciblée (saison monitorée, pas de recherche vide chez Prowlarr).
- [ ] (Logs) Onglet **Logs** admin → une entrée `download` apparaît pour la relance (succès `info` / échec `error`).

## 2. M16 — Auto-approbation (taille + genre + confiance)

- [ ] **Config admin** → champ **« Auto-approve genres »** visible (sous le seuil de taille). Y mettre p. ex. `Documentary, Animation`. Régler le **seuil de taille** > 0.
- [ ] Mode **approbation manuelle** activé (RequireApproval). Requêter un titre **du bon genre et sous le seuil** → **auto-approuvé** (pas en file d'attente).
- [ ] Requêter un titre **hors genre** (mais sous le seuil) → reste **En attente** (file admin).
- [ ] Requêter un titre **du bon genre mais au-dessus du seuil** → reste **En attente**.
- [ ] **Vider** la liste de genres → le seuil de taille seul décide à nouveau (tous genres).
- [ ] Un **utilisateur de confiance** (override AutoApprove) → toujours auto-approuvé, **quel que soit** le genre.
- [ ] Dépassement de **quota** : une requête qui serait auto-approuvée mais dépasse le quota → **mise en attente** (pas refusée).

## 3. M26 — Journal d'activité (logs)

- [ ] Onglet **Logs** : après quelques actions (requête, dispatch), des entrées apparaissent **du plus récent au plus ancien**.
- [ ] Filtres **catégorie** (`request` / `download`) et **niveau** (`info` / `error`) → filtrent correctement.
- [ ] Recherche par **terme** (titre) → filtre correctement.

## 4. Régression — Cycle de requête (M16/M22)

- [ ] Créer une requête film → suivre **En attente → Approuvée → (téléchargement) → Disponible**.
- [ ] Le statut **« en téléchargement »** + % se met à jour (polling 3 s) sans rechargement de page.
- [ ] **Annuler** une requête approuvée → disparaît + (film) retirée de Radarr.
- [ ] Dates **demande** et **mise à disposition** affichées sur la requête.

## 5. Régression — Quota & « Mes médias » (M21/M23)

- [ ] Barre de **quota** dans le bandeau : taille utilisée / quota, couleur (vert→rouge).
- [ ] **Mes médias** : taille par média + barre de quota.
- [ ] **Ajouter un média déjà dispo** (demandé par un autre) → avertissement quota, compté dans mon quota.
- [ ] **Demander la suppression** → compte à rebours affiché ; **annuler** la suppression tant qu'à > 1 min de l'échéance.
- [ ] Propriété partagée : un média demandé par 2 users n'est **réellement supprimé** que quand **plus personne** ne le possède.
- [ ] Liens cliquables : un média/un *Available* ouvre bien la **fiche Jellyfin**.

## 6. Régression — Notifications (M17)

- [ ] **Cloche** dans le bandeau avec **pastille rouge + compteur** quand une notif arrive.
- [ ] Notifié sur **Approved / Denied / Available**.
- [ ] **Notif « Échec »** : quand un dispatch échoue (1ʳᵉ fois), le **demandeur** reçoit une notif in-app (+ canal perso) « Request needs attention… ». Pas de doublon aux tentatives suivantes.
- [ ] **Effacer tout** dans le panneau de notifs fonctionne.
- [ ] **Bouton « × » par notification** : efface une seule notif ; la liste se met à jour (état vide si c'était la dernière).
- [ ] **Préférences perso** (e-mail / ntfy) : un canal perso reçoit bien la notif.

## 7. Régression — Catalogue, calendrier, communauté (M24/M25)

- [ ] **Catalogue** : filtres (genre/année/note/tri), recherche, modale détails (genres, runtime, liens).
- [ ] **« Voir plus → »** sur une sous-section ; **« Demander toute la saga »** (collection TMDB).
- [ ] **Calendrier** des sorties s'affiche.
- [ ] **Commentaires** sous le synopsis : poster, voir, (admin) masquer/supprimer.
- [ ] **Signalement** d'un souci média → arrive dans la file admin ; admin peut résoudre/supprimer.

## 8. Régression — Mobile / responsive (rapide)

- [ ] Ouvrir l'overlay sur **téléphone** : bandeau, onglets, modale, barre de quota lisibles et utilisables.

## 9. Bandeau natif & intégration (custom CSS)

- [ ] Ouvrir un volet du plugin (Catalog/Calendar/My requests/My media) → le **bandeau natif Jellyfin reste affiché en haut** (avec ton custom CSS : couleur `.headerRight`, tailles d'onglets, image de fond…), **pas** de bandeau custom qui le recouvre.
- [ ] Le contenu du plugin s'affiche **sous** le bandeau (pas par-dessus).
- [ ] Les liens **Catalog / Calendar / My requests** s'intègrent à la rangée d'onglets native ; **quota** + **cloche** dans `.headerRight`.
- [ ] Re-cliquer le lien **actif** referme le volet (le bandeau natif seul reste).
- [ ] Les menus déroulants natifs (compte, recherche…) s'ouvrent **au-dessus** du volet.
- [ ] Vérifier sur **mobile** que le décalage sous le bandeau est correct (pas de chevauchement).

## 10. Mode config (cacher le plugin)

- [ ] Config admin → cocher **« Config mode — hide the plugin from regular users »**, sauvegarder.
- [ ] Avec un compte **non-admin** : aucun lien/quota/cloche Jelly Crowd dans le bandeau ; appel direct API (ex. `GET /JellyCrowd/Catalog/Trending`) → **403**.
- [ ] Avec le compte **admin** : le plugin reste **visible et utilisable** (pour configurer/tester).
- [ ] Décocher l'option → les utilisateurs revoient le plugin (après rechargement de page).

## 11. Nouveautés récentes (v0.44 → v0.47+)

- [ ] **Avis & notes (M29)** : activer « Enable ratings & reviews » (admin). Sur un titre du catalogue (popup), noter (1–10 en demi-étoiles) + commentaire optionnel ; la **moyenne interne** + nb de votes s'affichent (et un badge à côté de la note TMDB). Re-noter **met à jour** (pas de doublon). Avis **anonymes** pour les non-admins ; l'admin voit l'auteur + peut masquer/supprimer.
- [ ] **Bandeau natif** : sur Home + chaque librairie, seuls les liens Jelly Crowd (Home, Catalog, Calendar, My requests) s'affichent ; les onglets natifs sont masqués. Vérifier **Other & Books** (nos liens présents).
- [ ] **Lien Home** : ferme le volet plugin + va à l'accueil. Cliquer ailleurs (recherche, menu, librairie) **ferme** le volet.
- [ ] **Annonce admin** : l'admin édite l'annonce depuis le bandeau (✎) → bandeau coloré vert/jaune/rouge visible par tous.
- [ ] **Calendrier** : titres demandés (par n'importe qui) en **violet** + demandes à date future affichées ; épisodes des séries suivies/demandées.
- [ ] **Expiration / propriété (M27)** : « My library » montre « Expire dans … » + bouton **Conserver (renouveler)** (reset 90 j). Régler « Media ownership expiry (days) ».
- [ ] **Cleanup admin** : Diagnostics → « Library cleanup » → **Scan media** (orphelins) → **Delete** (confirmation) supprime du disque.
- [ ] **Anti-freeze** : naviguer entre pages/volets ne fige plus le navigateur (régression v0.47.0 corrigée en v0.47.1).

---

### Notes
- Si un test échoue, noter le **comportement observé** + capture, et me le remonter — je corrige et republie.
- Rappel : créer une requête déclenche un **vrai téléchargement** (mode auto-approbation) — privilégier des titres légers ou déjà présents pour les tests.
