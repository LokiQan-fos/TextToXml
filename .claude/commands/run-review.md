---
description: Lance une revue BMAD sur la dernière story, sans paramètre
---

Tu lances le workflow bmad-code-review sur la dernière story ayant une
plage de commits dans l'historique. Avant de lire step-01-gather-context.md
ou de poser une question, exécute ces trois étapes.

ÉTAPE 1 — Story key.
Exécute la commande git log --format='%H %s' -100 et parcours la sortie
du plus récent au plus ancien. Le story key est le N-M du premier sujet
matchant chore(story-N.M). Ignore les sujets purement administratifs
(mark done, close review) s'il existe un commit non administratif pour
le même key dans la fenêtre.

ÉTAPE 2 — Range.
Depuis la même sortie, collecte tous les commits du même story key.
Prends le plus ancien. Le range est <oldest-sha>^..HEAD — le caret est
obligatoire pour inclure le commit lui-même.

ÉTAPE 3 — Spec.
Le story key N-M mappe un préfixe spec-N-M-. Cherche dans
_bmad-output/implementation-artifacts/ les fichiers matchant
spec-N-M-*.md. S'il y en a plusieurs, prends le plus récent. Positionne
{spec_file} à ce chemin absolu, {review_mode} à full.

Ensuite : construis {diff_output} avec git diff sur le range de l'ÉTAPE 2.
Va directement au CHECKPOINT de step-01 avec :
  Cible : <range>
  Mode : full
  Spec : <chemin>

NE POSE AUCUNE QUESTION sur le scope, le spec ou le mode. Le CHECKPOINT
step-01 reste la seule confirmation utilisateur. Si une des 3 étapes
échoue (aucun commit chore(story-*) trouvé, aucun spec), signale-le et
arrête — n'invente pas de valeur, ne tombe pas dans la cascade standard.
