---
description: Commit de clôture post-review — applique patches, defers, sprint-status, puis commit
---

Tu appliques les findings d'une revue de code sur une story, puis tu commites
la clôture.

Argument reçu : $ARGUMENTS
Format attendu : N.M (ex. 4.5). Peut être vide — dans ce cas, la story est
déduite automatiquement.

RÉSOLUTION 1 — Story.

Si $ARGUMENTS est non vide :
  Le story key est la chaîne "N-M" construite à partir de l'argument
  (4.5 → 4-5). Vérifie dans _bmad-output/implementation-artifacts/sprint-
  status.yaml qu'une clé commençant par "4-5-" existe. Si non, arrête et
  signale. Si oui, note cette clé complète.

Si $ARGUMENTS est vide :
  Exécute git log --format='%H %s' -100 et parcours du plus récent au plus
  ancien. Le story key est le <N>-<M> du premier sujet matchant
  chore(story-<N>.<M>). Ignore les sujets purement administratifs
  (mark done, close review) s'il existe un commit non administratif pour le
  même key. Si aucun commit trouvé, arrête et demande.

Dans les deux cas, garde en mémoire : story_key (ex. 4-5), numéro complet
pour les messages (ex. 4.5).

RÉSOLUTION 2 — Rapport de revue.

Cherche sous _bmad-output/implementation-artifacts/ un fichier dont le
chemin contient le story key (<N>-<M>) ET dont le nom contient "review" ou
"aggregated". Candidats possibles :
  - reviews/story-<N>-<M>-*/aggregated-report.md
  - reviews/story-<N>.<M>*.md
  - spec-<N>-<M>-*-review*.md
  - toute autre convention locale.
Si plusieurs, prends le plus récent par LastWriteTime.

Si aucun fichier disque ne correspond :
  Cherche dans la conversation récente un bloc qui ressemble à un rapport
  de revue (contient "Patch", "Defer", "Decision", "Finding", ou "ACCEPTÉ"/
  "REFUSÉ" pour cette story).
  Si trouvé, utilise-le.
  Si rien, ARRÊTE et demande : "Aucun rapport de revue trouvé pour la
  story <N-M>. Colle-le ou indique le chemin, puis relance."

RÉSOLUTION 3 — Spec figé.

Cherche _bmad-output/implementation-artifacts/spec-<N>-<M>-*.md. Prends le
plus récent si plusieurs. Note le chemin.

Une fois les 3 résolus, exécute les étapes suivantes.

ÉTAPE 1 — Extraction des findings.
Depuis le rapport (RÉSOLUTION 2), extrais trois listes :
- Patchs (bloquants) — chaque patch a un fichier:ligne et une correction.
- Décisions (bloquantes) — chaque décision présente des options à trancher.
- Defers (non bloquants) — chaque defer est à tracer dans le ledger.

Si le rapport est marqué INCOMPLETE (lentilles manquantes), signale-le et
demande l'autorisation avant de continuer.

Si aucune Décision n'est en attente, passe à l'Étape 2.
Si des Décisions sont en attente, présente-les-moi avant toute action.

ÉTAPE 2 — Application des Patchs.
Pour chaque Patch : applique, build avec dotnet build TextToXml.sln
-warnaserror, test avec dotnet test TextToXml.sln --filter Category=Unit,
marque appliqué. Si un patch révèle un impact plus large que prévu, arrête
et signale.

ÉTAPE 3 — Traitement des Defers.
Pour chaque Defer, ajoute une entrée dans _bmad-output/implementation-
artifacts/deferred-work.md au format :
  - source_spec: <chemin du spec ou du rapport>
    summary: <factuel, une à deux phrases>
    evidence: <lentille source + comment le défaut a été constaté>

ÉTAPE 4 — Mise à jour du suivi.
Passe la story <N>-<M> de review à done dans _bmad-output/implementation-
artifacts/sprint-status.yaml. Vérifie la cohérence de l'épic parent (reste
in-progress tant que toutes les stories ne sont pas done).

ÉTAPE 5 — Vérification finale.
Exécute successivement :
  dotnet build TextToXml.sln -warnaserror
  dotnet test TextToXml.sln --filter Category=Unit
  dotnet test TextToXml.sln --filter Category=Integration
Arrête-toi à la première erreur. Puis git status --short pour confirmer les
fichiers à commiter.

ÉTAPE 6 — Commit.

Message si des Patchs ou Décisions ont été traités :

  chore(story-<N>.<M>): apply review patches

  <une ligne par finding traité :
   <FindingId> <Sévérité> → <action appliquée ou justification>
   Exemples :
     L1-F02 Patch → correction de la borne supérieure dans CheckXxx
     L2-F04 Patch → ajout du test manquant AC-FRx-3
     L3-F01 Defer → tracé dans deferred-work.md (cause : pré-existant)
     L4-F03 Decision → option A retenue par le demandeur, patch appliqué>

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>

Message si aucun Patch ni Decision (cas heureux) :

  chore(story-<N>.<M>): close review (no patches)

  <une ligne par Defer tracé, ou "aucun finding", si le ledger est le seul
   fichier modifié>

Stage uniquement :
- les fichiers touchés par les Patchs,
- _bmad-output/implementation-artifacts/deferred-work.md (si modifié),
- _bmad-output/implementation-artifacts/sprint-status.yaml.
Ne touche pas aux fichiers non trackés (.claude/commands/, _bmad/custom/*.toml)
et ne les stage pas.

Après le commit, rends :
- le hash du commit,
- la sortie de git log --oneline -5,
- un résumé : patchs appliqués, décisions tranchées, defers tracés, nouvel
  état de la story, prochain jalon (story suivante, ou clôture d'épic si
  toutes les stories sont done).

RÈGLES :
- Ne jamais appliquer un Patch mal compris — arrête et demande.
- Ne jamais ignorer un finding — soit appliqué, soit tracé, jamais silencieux.
- Ne pas mélanger corrections de revue et améliorations opportunistes.
- Ne pas modifier les fichiers hors scope sauf le ledger et le sprint-status.
- Si un des paramètres (RÉSOLUTION 1/2/3) échoue, arrête immédiatement —
  n'invente pas de valeur, ne tombe pas dans une cascade par défaut.