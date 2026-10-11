# À faire

État au 11/10/2026, version 9.15.0. Tout le code est commité sur la branche
`feature/traduction-nommage-disposition`. La dernière version installée sur le NAS est plus ancienne :
presque rien de ce qui suit n'a encore tourné dans le vrai Emby.

## À vérifier sur le NAS (par l'utilisateur)

- [ ] **Installer la 9.15.0** et vérifier que le plugin se charge. S'il ne se charge plus, les deux premiers
      suspects sont la tâche planifiée (`NormalizeScheduledTask`) et l'authentification des adresses
      (`[Authenticated(Roles = "Admin")]`), ajoutées sans pouvoir être essayées dans Emby.
- [ ] **Fusion** sur un gros film (Les Deux Tours) : l'erreur « not valid JSON » doit avoir disparu.
- [ ] **TV Samsung** : disposition haut/bas sur une fusion refaite. Si une seule langue s'affiche,
      utiliser « Les deux en bas ».
- [ ] **Traduction** : un épisode court avec Google Traduction ; Claude n'a jamais fait d'appel réel.
- [ ] **Nettoyage** : aperçu puis exécution sur un seul film, dont le retrait de la note dans les titres
      de pistes `.mkv` (`mkvpropedit` jamais exécuté).
- [ ] **Panneau de fusion de la fiche d'un film** : vérifier qu'il s'affiche dans la fenêtre des sous-titres.
- [ ] **Tâche planifiée** : lui ajouter un déclencheur dans Emby et vérifier une exécution.
- [ ] **Fusion en série** : aperçu sur une saison.
- [ ] **Bilingue « par défaut »** : vérifier comment Emby le choisit face à une piste intégrée par défaut.
- [ ] Refaire les fusions des 5 films dont l'ancien bilingue a été retiré (Manhunter, Mission impossible 2,
      Les Deux Tours, King of New York, Sex and the City S01E10).

## Reste à faire

- [ ] **Clé API Emby à révoquer** : une clé du serveur Emby du PC a été publiée dans l'historique git
      (`MergeService.cs`, `SubtitleMerger.html`). Les fichiers sont corrigés ou retirés, mais l'historique
      la contient toujours : la supprimer dans Emby (Tableau de bord, Clés API).
- [ ] Fusionner la branche dans `main` une fois la version validée sur le NAS.
- [x] Accents dans les textes de la page (9.14.0). Si des caractères du type « Ã© » apparaissent, le serveur sert la page dans un autre encodage : le signaler.
- [x] Outil de renommage des anciens `.dual.srt` retiré (9.13.0) : il n'y avait plus rien à renommer.
- [ ] Captures d'écran du README à refaire (elles montrent l'ancienne page).
- [ ] Moteur de traduction gratuit de meilleure qualité (Gemini, palier gratuit avec clé) si Google
      Traduction ne suffit pas.
- [ ] Les trois boucles de suivi de la page (fusion, traduction, normalisation) et leurs trois formats de
      statut pourraient être unifiés ; à faire quand le tout aura été validé dans Emby.

## Décisions prises

- **Modes de fusion** : assumés. Les deux dispositions gardent les mêmes répliques ; seul le minutage
  diffère (une réplique commune en « tout en bas », chaque langue son minutage en haut/bas).
  Les libellés de la page le disent.
- **Traduction** : reprise après interruption (les répliques déjà traduites sont gardées 7 jours) ;
  un `Film.xx.srt` existant n'est remplacé que si la case est cochée.
- **Refusion** : l'ancien `Film.dual.srt` et l'autre forme du même bilingue sont retirés quand le nouveau est créé.
- **Réglages de traduction** (moteur, langue cible, clé Anthropic) : enregistrés sur le serveur ; la clé
  n'est jamais renvoyée au navigateur.
- **Fusion rapide et userscript** : retirés. Ils ne fonctionnaient qu'avec la clé écrite en dur du PC ;
  le panneau de la fiche d'un film les remplace.
- **Note « Normalisé pour Emby »** : plus ajoutée par défaut, retirable par le nettoyage. Elle reste dans le
  nom des fichiers générés, où elle sert à les reconnaître sans risque pour les fichiers d'origine.

## Fait

- [x] Traduction (Claude, puis Google Traduction gratuit), nommage bilingue, disposition haut/bas.
- [x] Renommage des anciens `.dual.srt` ; mise de côté de ceux qu'il ne sait pas identifier (`_anciens_dual`).
- [x] Normalisation ASS/SSA (reprise de `emby_subs.py`), ffmpeg d'Emby, détection de `mkvpropedit`,
      une seule lecture de la vidéo par normalisation, tâche planifiée.
- [x] Nettoyage par film ou pour toutes les médiathèques.
- [x] Fusion et traduction en tâche de fond, annulation, normalisation à la volée des pistes ASS.
- [x] Fusion en série, panneau de la fiche d'un film, page en onglets, thème clair.
- [x] Adresses du plugin réservées aux administrateurs ; plus de clé ni d'adresse écrite en dur.
- [x] README à jour.

## Outils retirés

- 9.13.0 : renommage des anciens `Film.dual.srt` (migration terminée).
- 9.15.0 : ajout de la note « Normalisé pour Emby » aux `.srt` et aux titres de pistes `.mkv`, extraction
  des pistes texte intégrées en `.srt`, mode cloud DoubleSub.io. Le nettoyage sait toujours retirer les
  fichiers et les notes que ces options avaient laissés.
