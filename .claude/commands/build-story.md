---
description: Lance bmad-build sur une story avec route plan-code-review forcée
---

Argument reçu : $ARGUMENTS
(Format attendu : N.M — par exemple 4.5. Si vide, choisis la première
story en backlog dont tous les prérequis amont sont done.)

Avant d'invoquer bmad-build, exécute ces étapes.

ÉTAPE 1 — Résolution du story key.
Dans _bmad-output/implementation-artifacts/sprint-status.yaml, cherche la
clé development_status commençant par "N-M-" où N.M est l'argument.
Note cette clé complète.

ÉTAPE 2 — Vérification des prérequis amont.
Vérifie dans le même fichier que toutes les stories précédentes de l'épic
courant sont en statut done. Si l'une ne l'est pas, arrête et demande une
confirmation explicite avant de continuer.

ÉTAPE 3 — Contexte de continuité.
Cherche dans _bmad-output/implementation-artifacts/ le dernier fichier
spec-N-*.md de l'épic dont le statut est done. Note son chemin comme
contexte de continuité pour bmad-build.

ÉTAPE 4 — Invocation de bmad-build avec route imposée.
Invoque le skill bmad-build en lui passant explicitement ce contexte :

  Story cible : <story key complet résolu en ÉTAPE 1>.
  Route imposée : plan-code-review.
  Motivation : le projet TextToXml impose la discipline spec-first pour
  toutes les stories de l'Épic 4. Un spec figé doit être produit au
  step-02 et validé par un checkpoint humain avant toute implémentation.
  N'utilise pas la route one-shot, même si la story paraît à blast radius
  nul. Le fichier epics.md contient la définition exacte de la story.

Laisse bmad-build dérouler ses cinq étapes. Les checkpoints humains
restent obligatoires : step-02 (approbation du spec), step-04 (triage des
findings), step-05 (commit). Ne saute aucun.

Si step-01 choisit quand même la route one-shot malgré cette instruction,
arrête immédiatement et signale-le — ne laisse pas le workflow continuer
sur la mauvaise route.