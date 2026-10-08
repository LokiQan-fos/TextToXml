---
description: Lance la rétrospective d'un épic selon BMAD
argument-hint: "[N]"
disable-model-invocation: true
---

Argument reçu : $ARGUMENTS — numéro d'épic (ex. 4). Peut être vide.

Rôle dans le kit : /commit-review passe l'épic à done à la clôture de sa
dernière story ; /retro-epic écrit la rétro, ajoute ses actions et
re-clôture l'artefact de clôture du projet s'il existe.

Tu mènes la rétrospective d'un épic avec le skill bmad-retrospective
installé. Avant de l'invoquer, résous ces trois paramètres. Aucun chemin
en dur : {implementation_artifacts} se lit dans _bmad/bmm/config.yaml,
{output_folder} dans _bmad/core/config.yaml. Le profil projet est
{output_folder}/project-profile.md.

RÉSOLUTION 1 — Épic cible.

Si $ARGUMENTS est fourni :
  L'épic est ce nombre. Vérifie dans
  {implementation_artifacts}/sprint-status.yaml qu'une clé epic-<N>
  existe et que toutes ses stories (clés N-M-…) sont done. Sinon, arrête
  et signale les stories restantes.

Si $ARGUMENTS est vide :
  Prends le plus petit numéro d'épic dont toutes les stories sont done et
  qui n'a pas de rétro à jour : aucun fichier epic-<N>-retro-*.md, ou une
  rétro la plus récente commitée avant le dernier commit de story de
  l'épic (épic rouvert). Les rétros se font dans l'ordre. Aucun
  candidat : arrête et demande.

Note : si seule la clé epic-<N>-retrospective existe, sans epic-<N>,
c'est une entrée fantôme : signale-la comme incohérence avant de
continuer.

RÉSOLUTION 2 — Plages de commits.

- Plage de l'épic : exécute git log --format='%H %s' --reverse sur toute
  la branche ; le plus ancien commit de l'épic est celui dont le sujet
  matche (chore|feat|fix|refactor)(story-<N>.<M>) avec le plus petit M,
  comparé numériquement (le plus ancien par date en cas d'égalité). La
  plage est <oldest-sha>^..HEAD. Si aucun commit ne matche, arrête et
  signale — la plage est indispensable pour l'analyse.
- Plage d'analyse : si l'épic a déjà une rétro (épic rouvert), l'analyse
  porte sur le delta depuis le commit de la plus récente
  (git log -1 --format=%H -- <fichier de cette rétro>), soit
  <retro-sha>..HEAD ; la plage de l'épic reste citée pour référence.
  Sinon, la plage d'analyse est la plage de l'épic.

RÉSOLUTION 3 — Rétrospective précédente.

La plus récente de cet épic s'il en a déjà une, sinon la plus récente de
l'épic N-1 ({implementation_artifacts}/epic-<N>-retro-*.md, puis
epic-<N-1>-retro-*.md ; un épic peut en avoir plusieurs). Note son
chemin. Si aucune, note-le — l'épic <N> est le premier ou la rétro
précédente a été omise.

VÉRIFICATION DES PRÉREQUIS.

Avant d'invoquer le skill, exécute les commandes de la section
« Commandes » du profil (build strict, tests unitaires, tests
d'intégration), puis celles de la section « Dépôts liés » si elle
existe, dans l'ordre et avec les précautions qu'elle indique. Si une
section manque, demande les commandes. Note les totaux exacts et l'état
de chaque suite (passed / skipped / failed) : ce sont les chiffres de la
section vérification du rapport.

INVOCATION DU SKILL.

Invoque le skill bmad-retrospective en mode headless : -H <N>. Passe-lui :
  - la plage de l'épic et la plage d'analyse ;
  - le chemin de la rétrospective précédente (si trouvée) ;
  - le profil projet (critères transverses, invariants, commandes,
    conventions) ;
  - le contenu entier de {implementation_artifacts}/deferred-work.md,
    pour éviter que la rétro propose une action déjà tracée.

Le skill porte sa propre discipline (collecte de preuves, vues agrégées,
verdict). Laisse-le dérouler. Il met lui-même sprint-status.yaml à jour
avec son script ; ne modifie pas ce fichier à la main.

Si le skill échoue à charger sa discipline (fichier de référence
introuvable), arrête et signale-le.

APRÈS LA PRODUCTION DU RAPPORT.

Une fois le rapport produit par le skill (fichier
{implementation_artifacts}/epic-<N>-retro-<date>.md) :

1. Vérifie que le rapport contient bien :
   - un frontmatter YAML avec epic, date, verdict ;
   - les sections du skill : Epic summary, Findings, Behavior
     verification, Previous-retro follow-through, Action items,
     Acceptance verdict, Open questions, Assumptions ;
   - un plan d'actions (A-1, A-2, ...) avec owner et ref ;
   - les chiffres de vérification mesurés à l'instant, pas des
     estimations.

   Si une de ces sections manque, complète-la toi-même sans réécrire le
   reste.

2. Dans sprint-status.yaml, vérifie que le script du skill a passé
   epic-<N>-retrospective à done et ajouté les actions (id, epic, action,
   owner, status: open, ref : chemin du rapport). Si epic-<N> n'est pas
   done, passe cette seule valeur à done.

3. Re-clôture : si la section « Artefact de clôture » du profil nomme un
   fichier qui existe, re-clôture-le comme elle le décrit, dans le même
   commit. C'est la rétro qui re-clôture : /commit-review ne fait que
   poser le bandeau « rétrospective en attente ».

4. Committe. Message :

   chore(epic-<N>-retro): retrospective and sprint status

   <verdict, puis une ligne par action ouverte (3-5 lignes)>

   Termine par la ligne Co-Authored-By que Claude Code fournit pour la
   session ; jamais un nom de modèle codé en dur.

   Stage uniquement : le rapport de rétro, sprint-status.yaml et, s'il a
   été re-clôturé, l'artefact de clôture. Rien d'autre.

5. Rends à l'utilisateur :
   - le verdict (accepted / accepted-with-open-items / rejected) ;
   - la liste des actions ouvertes (IDs + titres) ;
   - le hash du commit ;
   - la commande à lancer ensuite : /build-story <N+1>.1 si l'épic
     suivant est planifié, la prochaine story s'il en reste, sinon
     /bmad-correct-course ou /bmad-sprint-planning.

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

Écrite pour bmad-retrospective 6.11.0 : après une mise à jour de BMAD,
revérifie le mode -H et les sections du rapport.
