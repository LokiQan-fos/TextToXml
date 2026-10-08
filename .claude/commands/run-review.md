---
description: Lance une revue BMAD sur une story (par défaut celle en review), sans question
---

Argument reçu : $ARGUMENTS
(Format attendu : N.M ou N.M-suffixe, par exemple 6.12 ou 4.2-bis. Peut
être vide.)

Rôle dans le kit : /build-story livre la story en review ; /run-review
écrit seulement le rapport de revue ; /commit-review est le seul à écrire
l'état de clôture (patches, registre de dette, statuts).

Tu lances le workflow bmad-code-review sur une story. Avant de lire
step-01-gather-context.md ou de poser une question, exécute ces trois
étapes. Aucun chemin en dur : {implementation_artifacts} se lit dans
_bmad/bmm/config.yaml.

ÉTAPE 1 — Story key.
Dans {implementation_artifacts}/sprint-status.yaml, section
development_status, ne considère que les clés de story (N-M-…) : ignore
epic-N et epic-N-retrospective.
- Argument fourni : prends la clé dont les deux premiers segments valent
  exactement N et M, et le troisième le suffixe s'il y en a un.
- Argument vide : prends l'unique story en statut review (bmad-build l'y
  met à la fin du build).
Si plusieurs clés correspondent, arrête et demande laquelle. Si aucune
story n'est en review, repli : dans git log --format='%H %s' -100, le
N-M du premier sujet matchant (chore|feat|fix|refactor)(story-N.M) ; si
rien ne matche, arrête et signale-le.

ÉTAPE 2 — Spec.
Prends {implementation_artifacts}/spec-<clé complète>.md s'il existe,
sinon les fichiers spec-N-M-*.md (avec le suffixe s'il y en a un). S'il
y en a plusieurs, arrête et demande lequel — ne choisis jamais par date :
spec-6-4-* couvre aussi spec-6-4-bis-*. Aucun : arrête et signale-le.

ÉTAPE 3 — Range.
Lis baseline_commit dans le frontmatter de la spec : le range est
<baseline_commit>..HEAD. Repli si absent : le plus ancien commit dont le
sujet matche (chore|feat|fix|refactor)(story-N.M) ; le range est
<oldest-sha>^..HEAD (le caret inclut ce commit).

Ensuite : invoque bmad-code-review en lui donnant le range (commit range)
et le chemin de la spec, mode full, puis construis {diff_output} avec git
diff sur ce range. Va directement au CHECKPOINT de step-01 avec :
  Cible : <range>
  Mode : full
  Spec : <chemin>

NE POSE AUCUNE QUESTION sur le scope, le spec ou le mode. Le CHECKPOINT
step-01 reste la seule confirmation utilisateur. Si une des 3 étapes
échoue, signale-le et arrête — n'invente pas de valeur, ne tombe pas dans
la cascade standard.

APRÈS LE CHECKPOINT — À la fin de la revue.

Du step-04 du skill, n'exécute que la présentation des findings. Pas de
résolution des décisions, pas de patch, aucune écriture dans la spec,
dans deferred-work.md ou dans sprint-status.yaml, aucun changement de
statut : tout cela appartient à /commit-review, qui lit le rapport.

Écris le rapport complet dans
{implementation_artifacts}/reviews/story-<N>-<M>[-suffixe]/aggregated-report.md
(crée le dossier si nécessaire). Si un rapport existe déjà à ce chemin
(nouvelle passe de revue), renomme-le d'abord aggregated-report.prev.md.

Verdict :
- REFUSÉ si un finding viole un critère transverse du profil projet (CC)
  ou un AC de la spec, ou s'il est de sévérité high ou medium ;
- ACCEPTÉ sinon, même avec des findings low.
Si une lentille a échoué ou n'a rien rendu, ajoute INCOMPLETE au verdict
(par exemple « REFUSÉ INCOMPLETE ») : /commit-review demandera alors
l'autorisation de continuer.

Le fichier doit contenir, dans cet ordre :
  1. Le verdict et le nombre de findings par sévérité.
  2. La liste des findings Decision, chacun avec son ID, sa description,
     sa localisation fichier:ligne, et l'état "à trancher par l'humain".
  3. La liste des findings Patch, chacun avec son ID, sa description, sa
     localisation fichier:ligne, et l'action corrective proposée.
  4. La liste des findings Defer, avec justification.
  5. La liste des findings rejetés (bruit), avec justification du rejet.
  6. Les auto-vérifications : lentilles lancées, lentilles en échec,
     diff stats.

Ce fichier sera la source de vérité pour /commit-review, qui doit pouvoir
le retrouver même dans une session neuve. Utilise un format Markdown
lisible, avec un en-tête :

  # Review report — Story <N>.<M>

  Range : <range>
  Spec : <chemin>
  Date : <date ISO>
  Verdict : ACCEPTÉ | REFUSÉ [INCOMPLETE]
  Findings : D=<n> P=<n> F=<n> R=<n>  (Decision, Patch, Defer, Rejetés)
  Lentilles : <ids lancées> ; en échec : <ids ou aucune>

Une fois le fichier écrit, confirme son chemin absolu dans ta réponse à
l'utilisateur, présente le rapport, puis propose /commit-review <N.M>.

Écrite pour bmad-code-review 6.11.0 : après une mise à jour de BMAD,
revérifie le CHECKPOINT de step-01 et le contenu du step-04.
