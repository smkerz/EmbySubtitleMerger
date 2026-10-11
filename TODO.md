# À faire

## En cours

- [x] **Renommage des anciens `.dual.srt`** : les 5 fichiers que l'outil ignorait ont été retirés du NAS
      le 11/10/2026 (Manhunter, Mission impossible 2, Les Deux Tours, King of New York,
      Sex and the City S01E10).
- [ ] **Refaire ces 5 fusions** avec la 9.1.0 si un bilingue est toujours voulu. Pour Les Deux Tours,
      l'ancien fichier n'affichait que le russe : vérifier que la seconde piste est bien un sous-titre
      texte avant de fusionner.
- [x] **9.1.0 installée** sur le NAS le 11/10/2026 (détection russe / ukrainien corrigée, normalisation).
- [ ] **TV Samsung** : vérifier la disposition haut/bas (deux répliques simultanées) sur une fusion refaite.
      Si une seule langue s'affiche, utiliser « Tout en bas ».
- [ ] **Traduction** : premier essai réel sur un épisode court (aucun appel à l'API n'a encore été fait).

## Retrait des bilingues défaillants depuis le plugin

Remplace le script lancé à la main dans le Planificateur de tâches du NAS.

- [ ] Dans l'aperçu du renommage, une case à cocher sur chaque fichier ignoré.
- [ ] Bouton « Retirer les fichiers cochés » : déplacement dans un dossier `_anciens_dual` de la
      médiathèque (pas de suppression définitive), puis analyse de la médiathèque.
- [ ] Le serveur ne déplace que des fichiers `.dual.srt` situés dans un dossier de médiathèque.

## Normalisation des sous-titres (reprise de `emby_subs.py`, version 9.1.0)

Carte « Normaliser les sous-titres » dans la page du plugin : conversion des pistes ASS/SSA en SRT
sur le NAS, sans PC ni Python.

- [x] Par film ou pour toutes les médiathèques, avec « Aperçu » puis « Normaliser », en tâche de fond.
- [x] Règles du script reprises : langues réglables (en, fr, ru par défaut), première piste de chaque
      langue marquée `default`, note `Normalisé-pour-Emby(titre)`, migration de l'ancienne note à
      espaces, conversion des `.ass`/`.ssa` externes, extraction des pistes texte (option), note sur
      les `.srt` existants (option). Les bilingues du plugin ne reçoivent pas la note.
- [x] Le plugin utilise le ffmpeg et le ffprobe livrés avec Emby quand il les trouve.
- [x] Annotation des pistes intégrées aux `.mkv` (option) : proposée seulement si `mkvpropedit` est
      installé ; sinon la page affiche un avertissement, qui disparaît une fois le paquet installé.
- [ ] **Tester sur le NAS** : rien de tout cela n'a tourné dans Emby (testé hors serveur avec le
      ffmpeg d'Emby, sans `mkvpropedit`).
- [ ] Valider dans Emby l'affichage de la note à tirets et de la note sur la piste anglaise SDH.
- [x] MKVToolNix installé sur le NAS (paquet SynoCommunity, DSM 7.3.2) : le plugin le trouve et
      l'avertissement disparaît (11/10/2026).
- [ ] Tester l'annotation des pistes `.mkv` sur un seul film, puis vérifier la note dans Emby
      (dont la piste anglaise SDH).
- [ ] Automatique : tâche planifiée Emby pour les nouveaux films.
- Non repris du script : le nettoyage des anciens fichiers par date (opération ponctuelle déjà
  faite) et l'installation automatique de MKVToolNix.

## Décisions en attente

- [ ] Fusion : les modes « chevauchement » et « priorité » n'ont pas exactement le même sens en
      disposition haut/bas et en « tout en bas ». Unifier ou assumer.
- [ ] Traduction : reprise après échec et écriture partielle (un échec au dernier lot perd tout).
- [ ] Traduction : faut-il refuser d'écraser un `Film.fr.srt` existant ?
- [ ] Refusion : supprimer automatiquement l'ancien `Film.dual.srt` quand le nouveau nom est créé ?
- [ ] Écran de réglages (clé Anthropic, modèle, langue cible) au lieu du stockage dans le navigateur.
- [ ] Renommage en tâche de fond si l'aperçu est trop lent sur le NAS.
- [ ] Fusion rapide, script injecté et userscript : leur faire envoyer les langues et la disposition.

## Dette

- [ ] `MergeService.cs` : clé API Emby, identifiant utilisateur et `localhost:8096` écrits en dur,
      dans un dépôt public.
- [ ] README : décrire la traduction, le nommage, la disposition et le renommage ; corriger la
      structure du projet et le changelog (arrêté à v8.6).
- [ ] Sécuriser les endpoints du plugin (aucun ne vérifie l'authentification Emby).
- [ ] Commiter les versions 9.0.0 à 9.1.0, puis fusionner la branche dans `main` après validation
      sur le NAS.
