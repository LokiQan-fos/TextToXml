---
description: Lance bmad-build sur une story avec route plan-code-review forcée
---

Argument reçu : $ARGUMENTS
(Format attendu : N.M, par exemple 4.5, ou N.M-suffixe pour une story
comme 4.2-bis. Si vide, la story est choisie en ÉTAPE 1.)

Avant d'invoquer bmad-build, exécute ces étapes. Aucun chemin en dur :
{implementation_artifacts} se lit dans _bmad/bmm/config.yaml,
{output_folder} dans _bmad/core/config.yaml.

ÉTAPE 1 — Résolution du story key.
Dans {implementation_artifacts}/sprint-status.yaml, section
development_status, ne considère que les clés de story (N-M-…) : ignore
epic-N et epic-N-retrospective.
- Argument fourni : prends la clé dont les deux premiers segments valent
  exactement N et M, et le troisième le suffixe s'il y en a un. Si
  plusieurs clés correspondent (par exemple 6-4-… et 6-4-bis-…), arrête
  et demande laquelle. Si aucune, arrête et signale-le.
- Argument vide : prends la première clé de story, dans l'ordre du
  fichier, qui n'est pas done. En backlog, c'est la cible. En review,
  arrête : c'est /run-review qu'il faut lancer. En ready-for-dev ou
  in-progress, demande s'il faut la reprendre.

ÉTAPE 2 — Vérification des prérequis amont.
Vérifie dans le même fichier que toutes les stories qui précèdent la
cible dans son épic, dans l'ordre du fichier, sont en statut done. Si
l'une ne l'est pas, arrête et demande une confirmation explicite avant
de continuer.

ÉTAPE 3 — Invocation de bmad-build avec route imposée.

Lis la section "Discipline de routage bmad-build" de
{output_folder}/project-profile.md. Si le fichier ou la section est
absent, applique la discipline par défaut (route plan-code-review,
jamais one-shot).

Invoque le skill bmad-build en lui passant explicitement ce contexte :

  Story cible : <story key complet résolu en ÉTAPE 1>.
  Route imposée : plan-code-review.
  Motivation : <contenu de la section "Discipline de routage bmad-build"
  du project-profile.md, ou phrase par défaut : "la discipline spec-first
  de ce projet impose plan-code-review pour toute story ; le spec figé
  doit être produit au step-02 et validé par un checkpoint humain avant
  toute implémentation">.
  N'utilise pas la route one-shot, même si la story paraît à blast radius
  nul.

Laisse bmad-build dérouler ses cinq étapes. Il n'a qu'un checkpoint
humain natif, au step-02 (approbation du spec) : le step-04 applique les
patches sans demander et le step-05 committe sans demander. Ajoute donc
deux arrêts, sans en sauter aucun :
- step-04 : après le triage, avant d'appliquer les patches, présente les
  findings classés et attends l'accord humain ;
- step-05 : avant le commit, présente le message et attends l'accord
  humain.

Au step-04, un patch qui change un comportement est livré avec son test,
et le rouge observé avant le patch est consigné dans le Spec Change Log.
Si aucun test n'est possible (pas de seam, par exemple), la spec le
justifie au regard de la discipline TDD du profil projet. Au checkpoint
de triage, signale tout patch de comportement sans test ni justification
comme une non-conformité avant le commit du step-05 : les lentilles ont
relu le code d'avant les patches, personne ne relit les patches eux-mêmes.

Si step-01 choisit quand même la route one-shot malgré cette instruction,
arrête immédiatement et signale-le — ne laisse pas le workflow continuer
sur la mauvaise route.

Note : la revue intégrée à bmad-build (step-04) utilise les lentilles du
skill, surchargées par _bmad/custom/bmad-build.toml s'il existe. Ce n'est
pas bmad-code-review (le skill standalone invoqué par /run-review). Ne
pas confondre les deux systèmes.

Écrite pour bmad-build 6.11.0 : après une mise à jour de BMAD, revérifie
les numéros d'étapes, les noms de route et les checkpoints natifs.
