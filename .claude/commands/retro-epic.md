---
description: Lance la rétrospective d'un épic selon BMAD
---

Argument reçu : $ARGUMENTS — numéro d'épic (ex. 4). Peut être vide.

Tu mènes la rétrospective d'un épic selon la méthode BMAD, en t'appuyant
sur le skill bmad-retrospective installé. Avant d'invoquer le skill,
résous ces trois paramètres.

RÉSOLUTION 1 — Épic cible.

Si $ARGUMENTS est fourni :
  L'épic est ce nombre. Vérifie dans
  _bmad-output/implementation-artifacts/sprint-status.yaml qu'une clé
  epic-<N> existe et que toutes les stories <N>-* sont en statut done.
  Si une story n'est pas done, arrête et signale-la.

Si $ARGUMENTS est vide :
  Lis _bmad-output/implementation-artifacts/sprint-status.yaml.
  Cherche le dernier épic dont toutes les stories <N>-* sont en statut
  done ET pour lequel aucun fichier epic-<N>-retro-*.md n'existe dans
  _bmad-output/implementation-artifacts/. Si plusieurs candidats,
  prends le plus petit numéro d'épic — les rétros se font dans l'ordre.
  Si aucun candidat, arrête et demande.

Note : le story key epic-<N> doit exister dans sprint-status.yaml. Si
seulement epic-<N>-retrospective existe, c'est une entrée fantôme —
signale-la comme incohérence avant de continuer.

RÉSOLUTION 2 — Plage de commits.

Calcule la plage de l'épic <N> :
  - exécute git log --format='%H %s' --reverse sur toute la branche ;
  - le plus ancien commit de l'épic est celui dont le sujet matche
    (chore|feat|fix)(story-<N>.<M>) avec le plus petit M ;
  - si plusieurs commits matchent le même plus petit M, prends le plus
    ancien par date ;
  - la plage est <oldest-sha>^..HEAD.
  Si aucun commit ne matche, arrête et signale — la plage est
  indispensable pour l'analyse.

RÉSOLUTION 3 — Rétrospective précédente.

Cherche _bmad-output/implementation-artifacts/epic-<N-1>-retro-*.md.
Note son chemin pour permettre au skill de tracker les action_items
ouverts qu'il contenait. Si aucun fichier, note-le — l'épic <N> est le
premier ou la rétro précédente a été omise.

VÉRIFICATION DES PRÉREQUIS.

Avant d'invoquer le skill, exécute les commandes du project-profile pour
vérifier l'état réel :
  - dotnet build -warnaserror (ou l'équivalent du profil)
  - tests unitaires
  - tests d'intégration
Note les totaux exacts et l'état de chaque suite (passed / skipped /
failed). Ces chiffres serviront de référence pour la section vérification
du rapport.

INVOCATION DU SKILL.

Invoque maintenant le skill bmad-retrospective. Passe-lui :
  - le numéro d'épic résolu ;
  - la plage de commits calculée ;
  - le chemin de la rétrospective précédente (si trouvé) ;
  - le contexte projet chargé via _bmad/project-profile.md (CC, AD,
    commandes, conventions) ;
  - le contenu de _bmad-output/implementation-artifacts/deferred-work.md
    en entier, pour éviter que la rétro propose une action déjà tracée.

Le skill porte sa propre discipline (collecte de preuves, acceptance
verdict, agrégats, start/stop/continue). Laisse-le dérouler.

Si le skill échoue à charger sa discipline (fichier de référence
introuvable), signale-le et propose de basculer en mode autonome — dans
ce cas, reproduis la structure décrite dans les sections 1 à 12 de la
version longue du prompt, disponible dans l'historique de cette session.

APRÈS LA PRODUCTION DU RAPPORT.

Une fois le rapport produit par le skill (fichier
_bmad-output/implementation-artifacts/epic-<N>-retro-<date>.md) :

1. Vérifie que le rapport contient bien :
   - un en-tête frontmatter YAML avec epic, date, verdict ;
   - une section Start/Stop/Continue ;
   - un plan d'actions (A-1, A-2, ...) avec owner et ref ;
   - les chiffres de vérification mesurés à l'instant, pas des
     estimations.

   Si une de ces sections manque, complète-la toi-même sans réécrire le
   reste.

2. Dans _bmad-output/implementation-artifacts/sprint-status.yaml :
   - bascule epic-<N> de in-progress à done ;
   - bascule epic-<N>-retrospective à done ;
   - ajoute les action_items du plan (A-1 à A-n) dans la section
     action_items, chacune avec epic, action, owner, status: open,
     ref: chemin du rapport ;
   - rafraîchis last_updated.

3. Committe. Message obligatoire :

   chore(epic-<N>-retro): retrospective and sprint status

   <résumé du verdict et des actions ouvertes en 3-5 lignes, une ligne
    par action>

   Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>

   Stage uniquement :
   - _bmad-output/implementation-artifacts/epic-<N>-retro-<date>.md
   - _bmad-output/implementation-artifacts/sprint-status.yaml
   Ne stage pas les fichiers non trackés (.claude/, _bmad/custom/*.toml).

4. Rends à l'utilisateur :
   - le verdict (accepted / accepted-with-open-items / rejected) ;
   - la liste des actions ouvertes (IDs + titres) ;
   - le hash du commit ;
   - la commande à lancer ensuite : /build-story <prochain-épic.1> ou
     la prochaine story s'il en reste dans l'épic courant.

RÈGLES DE CONDUITE.

- Ne pose pas de question sur les valeurs déductibles des artefacts.
- Si une décision doit être tranchée par un humain (par exemple un
  arbitrage d'architecture que le rapport signale comme Q-n), présente-la
  avec les options et l'impact, puis HALT.
- Ne modifie pas les fichiers de code. La rétrospective est un acte
  d'analyse, pas de correction.
- Ne génère pas de statistiques décoratives (vélocité, burn-down,
  graphiques, émojis).
- Chaque affirmation factuelle du rapport doit citer sa source : un
  chemin fichier, un hash, une ligne de commande. Aucun constat sans
  preuve.
- Si le skill produit un rapport avec des sections vides ou des TODO,
  arrête et signale — un rapport incomplet ne doit pas être commité.