---
description: Commit de clôture post-review — applique patches, defers, sprint-status, puis commit
argument-hint: "[N.M]"
disable-model-invocation: true
---

Tu appliques les findings d'une revue de code sur une story, puis tu commites
la clôture.

Argument reçu : $ARGUMENTS
(Format attendu : N.M ou N.M-suffixe, par exemple 4.5 ou 4.2-bis. Peut être
vide : la story est alors déduite.)

Rôle dans le kit : /build-story livre la story en review ; /run-review écrit
le rapport ; /commit-review est le seul à écrire l'état de clôture (patches,
registre de dette, statuts, cases de la spec, artefact de clôture) ;
/retro-epic re-clôture.

Aucun chemin en dur : {implementation_artifacts} se lit dans
_bmad/bmm/config.yaml, {output_folder} dans _bmad/core/config.yaml. Le profil
projet est {output_folder}/project-profile.md ; ses sections « Commandes »,
« Conventions de commit », « Ledger de dette » et « Artefact de clôture » sont
lues ci-dessous. Si une section manque, demande ce qu'elle aurait fourni.

RÉSOLUTION 1 — Story.

Dans {implementation_artifacts}/sprint-status.yaml, section
development_status, ne considère que les clés de story (N-M-…).
- Argument fourni : la clé dont les deux premiers segments valent exactement
  N et M, et le troisième le suffixe s'il y en a un.
- Argument vide : la story en review ou in-progress dont le rapport
  (RÉSOLUTION 2) existe et n'a pas encore de section « ## Clôture ».
Plusieurs candidates : arrête et demande. Aucune : arrête et signale-le.
Garde en mémoire la clé complète, la clé courte (N-M ou N-M-suffixe) et le
numéro pour les messages (N.M ou N.M-suffixe).

RÉSOLUTION 2 — Rapport de revue.

Le rapport est {implementation_artifacts}/reviews/story-<clé courte>/aggregated-report.md,
écrit par /run-review (jamais aggregated-report.prev.md, qui est la passe
précédente). S'il n'existe pas, ARRÊTE et dis : « Aucun rapport de revue pour
la story <N.M>. Lance /run-review <N.M>, puis relance. »

RÉSOLUTION 3 — Spec figé.

{implementation_artifacts}/spec-<clé complète>.md s'il existe, sinon les
fichiers spec-N-M-*.md (avec le suffixe s'il y en a un). Plusieurs : arrête
et demande — ne choisis jamais par date. Aucun : arrête et signale-le.

Une fois les 3 résolus, exécute les étapes suivantes.

ÉTAPE 1 — Extraction des findings.
Depuis le rapport (RÉSOLUTION 2), extrais trois listes :
- Patchs (bloquants) — chaque patch a un fichier:ligne et une correction.
- Décisions (bloquantes) — chaque décision présente des options à trancher.
- Defers (non bloquants) — chaque defer est à tracer dans le ledger.

Si le verdict du rapport porte INCOMPLETE (lentilles en échec), signale-le et
demande l'autorisation avant de continuer.

Si aucune Décision n'est en attente, passe à l'Étape 2.
Si des Décisions sont en attente, présente-les-moi avant toute action.

ÉTAPE 2 — Application des Patchs.
Pour chaque Patch : applique, lance le build strict et les tests unitaires
de la section « Commandes » du profil, marque appliqué. Un patch qui change
un comportement est livré avec son test, et le rouge observé avant le patch
est noté dans sa ligne de Clôture (ÉTAPE 7) ; si aucun test n'est possible
(pas de seam, par exemple), la ligne le justifie au regard de la discipline
TDD du profil. Ces patches ne seront relus par personne : cette règle est la
seule garde. Si un patch révèle un impact plus large que prévu, arrête et
signale.

ÉTAPE 3 — Traitement des Defers.
Pour chaque Defer qui n'y figure pas déjà, ajoute une entrée dans
{implementation_artifacts}/deferred-work.md, sous le titre daté et au format
de la section « Ledger de dette » du profil (repli :
source_spec / summary / evidence).

ÉTAPE 4 — Mise à jour du suivi.
- sprint-status.yaml : passe la story à done, quel que soit son statut
  courant (review ou in-progress). L'épic parent reste in-progress tant que
  toutes ses stories ne sont pas done.
- Spec : passe le frontmatter à status: 'done' et coche les cases [Review]
  des findings traités, s'il y en a.

ÉTAPE 5 — Vérification finale.
Exécute successivement le build strict, les tests unitaires et les tests
d'intégration de la section « Commandes » du profil. Arrête-toi à la
première erreur. Note les comptes de tests : ils servent aux ÉTAPES 6 à 8.

ÉTAPE 6 — Clôture d'épic (seulement si cette story est la dernière).
Si, après l'ÉTAPE 4, toutes les stories de l'épic <N> sont done, passe
epic-<N> à done et mets à jour dans le même commit :
- l'artefact de clôture nommé par la section « Artefact de clôture » du
  profil, s'il existe, comme elle le décrit pour une fin d'épic. Ne touche
  pas à son statut de clôture : c'est /retro-epic qui re-clôture ;
- {implementation_artifacts}/epic-<N>-context.md : toute phrase d'état
  périmée (story « in review », « in progress ») passe à done.
Si l'un de ces fichiers n'existe pas, signale-le et continue. Si l'artefact
de clôture existe et que git status --short ne le montre pas modifié,
ARRÊTE et signale : c'est le fichier qui reste périmé quand cette étape est
oubliée.

ÉTAPE 7 — Mise à jour du rapport de revue.
Ajoute à la fin du rapport une section :
  ## Clôture (<date du jour>)
  - une ligne par finding : <FindingId> → appliqué (avec le rouge observé
    pour un patch de comportement) / tranché (option) / tracé dans
    deferred-work.md (story cible si planifiée) ;
  - vérification finale : build et comptes de tests de l'ÉTAPE 5 ;
  - statut : story <clé courte> → done ; commit de clôture = celui qui
    ajoute cette section.
Ne réécris pas les sections existantes du rapport.

ÉTAPE 8 — Commit.

Sujet, au format de la section « Conventions de commit » du profil (repli :
chore(story-<N.M>): …) :
- des Patchs ou Décisions ont été traités : « apply review patches » ;
- sinon : « close review (no patches) ».

Corps :
- une ligne par finding traité : <FindingId> <Sévérité> → <action appliquée
  ou justification> (par exemple « P-2 Patch → ajout du test manquant de
  l'AC », « F-1 Defer → tracé dans deferred-work.md, pré-existant ») ;
- la ligne de vérification finale (build et comptes de tests) ;
- les lignes que la section « Conventions de commit » exige pour le corps
  (une CI peut refuser un corps qui ne les contient pas).
Termine par la ligne Co-Authored-By que Claude Code fournit pour la session ;
jamais un nom de modèle codé en dur.

Stage uniquement :
- les fichiers touchés par les Patchs,
- deferred-work.md (si modifié),
- sprint-status.yaml,
- la spec (ÉTAPE 4),
- le rapport de revue (ÉTAPE 7),
- l'artefact de clôture et epic-<N>-context.md si l'ÉTAPE 6 s'applique.
Rien d'autre.

Après le commit, rends :
- le hash du commit,
- la sortie de git log --oneline -5,
- un résumé : patchs appliqués, décisions tranchées, defers tracés, nouvel
  état de la story,
- la commande suivante : /build-story <story suivante>, ou /retro-epic <N>
  si l'épic est passé à done.

RÈGLES :
- Ne jamais appliquer un Patch mal compris — arrête et demande.
- Ne jamais ignorer un finding — soit appliqué, soit tracé, jamais silencieux.
- Ne pas mélanger corrections de revue et améliorations opportunistes.
- Ne pas modifier les fichiers hors scope sauf le ledger, le sprint-status,
  la spec, le rapport de revue et, à la clôture d'un épic, l'artefact de
  clôture et epic-<N>-context.md (ÉTAPE 6).
- Si un des paramètres (RÉSOLUTION 1/2/3) échoue, arrête immédiatement —
  n'invente pas de valeur, ne tombe pas dans une cascade par défaut.
