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
Note cette clé complète. Si $ARGUMENTS est vide, prends la première clé
en statut backlog dont toutes les stories amont de l'épic sont done.

ÉTAPE 2 — Vérification des prérequis amont.
Vérifie dans le même fichier que toutes les stories précédentes de l'épic
courant sont en statut done. Si l'une ne l'est pas, arrête et demande une
confirmation explicite avant de continuer.

ÉTAPE 3 — Contexte de continuité.
Cherche dans _bmad-output/implementation-artifacts/ le dernier fichier
spec-N-*.md de l'épic dont le statut est done. Note son chemin comme
contexte de continuité pour bmad-build.

ÉTAPE 4 — Invocation de bmad-build avec route imposée.

Lis la section "Discipline de routage bmad-build" de
_bmad-output/project-profile.md. Si elle est absente, applique la
discipline par défaut (route plan-code-review, jamais one-shot).

Invoque le skill bmad-build en lui passant explicitement ce contexte :

  Story cible : <story key complet résolu en ÉTAPE 1>.
  Route imposée : plan-code-review.
  Motivation : <contenu de la section "Discipline de routage bmad-build"
  du project-profile.md, ou phrase par défaut : "la discipline spec-first
  de ce projet impose plan-code-review pour toute story ; le spec figé
  doit être produit au step-02 et validé par un checkpoint humain avant
  toute implémentation">.
  N'utilise pas la route one-shot, même si la story paraît à blast radius
  nul. Le fichier _bmad-output/planning-artifacts/epics.md contient la
  définition exacte de la story.

Laisse bmad-build dérouler ses cinq étapes. Les checkpoints humains
restent obligatoires : step-02 (approbation du spec), step-04 (triage des
findings), step-05 (commit). Ne saute aucun.

Si step-01 choisit quand même la route one-shot malgré cette instruction,
arrête immédiatement et signale-le — ne laisse pas le workflow continuer
sur la mauvaise route.

Note : la revue intégrée à bmad-build (step-04) utilise les lentilles
configurées dans _bmad/custom/bmad-build.toml. Ce n'est pas bmad-code-review
(qui est le skill standalone invoqué par /run-review). Ne pas confondre les
deux systèmes.