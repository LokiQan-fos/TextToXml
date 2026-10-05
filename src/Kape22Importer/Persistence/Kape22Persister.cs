using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FichierJournal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using TextToXml;

namespace Kape22Importer.Persistence;

// Story 2.8 (FR-11), replaced by Story 4.6 (FR-21, AD-1/AD-6): turns a Kape22ImportBundleMapper.Map
// result into rows in AscoLSI, then records the outcome in the Fichier journal (Story 6.1, FR-24, D31).
// On a bundle whose FR-20 controls all pass (bundle.Success): the anti-duplicate guard (D22 revised)
// checks for an existing L_D_KAPE22 row with the same NumeroFichier + OF; if none, and - for a cold
// Coulee only - its L_D_COULEE row already exists, L_D_KAPE22 + every non-null downstream entity from the
// bundle are committed together in exactly one SaveChanges (AC-FR21-1, AD-1), then a success entry is
// recorded outside that transaction (AC-FR24-2). A Coulee already on file (several OF routinely dispatch
// from the same cast) is reused rather than re-inserted, hot or cold (Spec Change Log, 2026-09-16). If
// the guard finds a prior row, nothing is inserted, the success entry is recorded when the journal does
// not hold it yet, and the Fichier comes back as an already-imported skip: AlreadyImported true, Success
// true, InsertedId null, empty Errors (AC-FR11-6, AC-FR24-4). A cold Coulee whose L_D_COULEE row is
// missing is rejected instead, with a failure entry citing the missing Coulee (AC-FR20-5).
// Story 6.10 (D34): an OF already in L_D_ORDRE_FABRICATION is refused the same way when its Etat is ENC,
// EVC, ENFOURNE, LAMINAGE or LAMINE or when L_D_FOURS, L_D_PLANS_FOURS or L_D_PSO names it (AC-FR20-6);
// otherwise it is deleted the way the legacy DeleteOF does (the 9 downstream tables, L_D_MAM_QUAL,
// L_D_OF_SUIVI, L_D_PRODUITS_OUTIL and L_D_REBUT; review P-9) and the new rows inserted, in one explicit
// transaction around the single SaveChanges, L_D_COULEE and the earlier L_D_KAPE22 rows untouched
// (AC-FR21-6) - the only path that deletes AscoLSI rows.
// A same-bundle L_D_CONSIGNES natural-key collision (two sections sharing the same CodeOperation for
// this OF) is rejected the same way, citing the colliding CodeOperation, before ConsignesRows.AddRange
// is ever called (A-5, Epic 4 retro). A scaled decimal column whose value exceeds its DECIMAL(p,s)
// column's magnitude (DownstreamColumnMagnitudes) is rejected the same way too, before anything is
// staged (B-5, Story 4.10 hardening).
// On a bundle that already carries Errors (Kape22Mapper.Map failed upstream, or one of
// Kape22ImportBundleMapper's own FR-20 controls tripped): no L_D_KAPE22 row; a failure entry
// "<count> erreur(s) : ..." when the OF is readable (AC-FR11-4), nothing at all when it is not (D15).
// A SQL failure (DbUpdateException or DbException) is caught and returned as a PersistenceError; the
// failed SaveChanges has already rolled back its transaction, so none of the staged rows persist
// (AC-FR11-5, extended by AC-FR21-2), and a failure entry carries SQL Server's own message (AC-FR24-3).
// Other exception types from the business context are not swallowed. Any exception from the journal is
// caught (see JournalFailure) and adds a File-level PersistenceError, so the Fichier stays in processing/
// and is retried; after a commit the result also keeps InsertedId, and
// the next tick's guard completes the journal (AC-FR24-5). The connection string reaches the DbContext
// through configuration and is never hard-coded (AC-FR11-8, CC-7). The DbContext lifetime and the Epic 3
// host wiring are the orchestrator's concern; this type is constructed per Fichier with a fresh context.
public sealed class Kape22Persister(
    AscoLsiDbContext context,
    IConfiguration configuration,
    IFichierJournal journal,
    string fichierName,
    TimeProvider? timeProvider = null)
{
    // The journal Commande is a format variation point (AC-FR16-2): it comes from configuration, falling
    // back to the P60 command for the reference template.
    private const string CommandeKey = "Import:Commande";

    private const string DefaultCommande = "P60";

    // D34: the legacy EtatOF values (Lsi.Net DALLevel3 EtatOF) under which an existing OF is never
    // replaced - ENC, EVC, ENFOURNE, LAMINAGE, LAMINE. GPAO (0) and any other value allow the replace.
    private static readonly Dictionary<int, string> ProtectedEtats = new()
    {
        [1] = "ENC",
        [2] = "EVC",
        [3] = "ENFOURNE",
        [5] = "LAMINAGE",
        [8] = "LAMINE",
    };

    public ImportResult Persist(Kape22ImportBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return bundle.Success
            ? PersistMapped(bundle)
            : PersistRejected(bundle);
    }

    private ImportResult PersistMapped(Kape22ImportBundle bundle)
    {
        L_D_KAPE22 entity = bundle.Kape22!;

        // Map always populates both fields on a deserialized Fichier, and this path is success-only.
        string numeroFichier = bundle.NumeroFichier!;
        string of = bundle.OF!;

        try
        {
            // AC-FR11-6/11-7, AC-FR24-4 (D22 revised): a committed L_D_KAPE22 row for this NumeroFichier +
            // OF means the Fichier already imported (a crash between the commit and the file move, a lost
            // journal write, or the legacy chain). Skip it: no new row, InsertedId stays null, Success
            // stays true, AlreadyImported flags the skip so the orchestrator archives the Fichier and logs
            // the "already imported" warning. A missing success entry is recorded first.
            if (context.Kape22Rows.Any(row => row.NumeroFichier == numeroFichier && row.OF == of))
            {
                return CompleteAlreadyImported(bundle, numeroFichier, of);
            }

            // AC-FR20-5 (Story 6.9): a Coulee is cold when the first character of CodeConsignePits is "1" (the
            // TypeConsigne 12 slice, as the legacy reads it). A cold Coulee must already have its L_D_COULEE row -
            // it is tracked elsewhere in AscoLSI before a P60 dispatch ever names it. A hot Coulee carries
            // no such precondition. Either way, one real Coulee routinely dispatches through several P60
            // Fichiers (several OF from the same cast) sharing the same IdCoulee, so an already-present
            // Coulee is never re-added - only a genuinely new one is (AD-7's "no navigation properties"
            // keeps this a plain existence check, not a relationship).
            string coulee = entity.Coulee;

            // Business note (Q-5, Épic 4 retro #3): a Coulee is created once, by the first OF that
            // references it, and reused as-is by every later OF of the same Coulee - a later OF never
            // modifies the row a previous one created.
            bool couleeAlreadyExists = context.CouleeRows.Any(row => row.IdCoulee == coulee);
            bool cold = entity.CodeConsignePits?.StartsWith(Kape22ImportBundle.ColdConsignePits, StringComparison.Ordinal) == true;
            if (cold && !couleeAlreadyExists)
            {
                string couleeMessage = $"OF '{of}' : la coulée '{coulee}' est introuvable dans L_D_COULEE.";
                ConversionError couleeError = new() { Block = Block.File, Code = ErrorCode.BusinessRuleViolation, Message = couleeMessage };
                return Recorded(new ImportResult { Errors = [couleeError] }, Entry(numeroFichier, of, couleeMessage));
            }

            // AC-FR20-6 / AC-FR21-6 (Story 6.10, D34): an OF already in L_D_ORDRE_FABRICATION is refused
            // once production has started on it (a protected Etat, or the OF named in L_D_FOURS,
            // L_D_PLANS_FOURS or L_D_PSO) - same shape as the cold-Coulee rejection above, nothing written.
            // Otherwise it is replaced: deleted below, after the pre-checks, the way the legacy DeleteOF
            // does, then inserted again. Downstream OF columns are zero-padded (DownstreamOf.Pad).
            string paddedOf = DownstreamOf.Pad(of);
            int? existingEtat = context.OrdreFabricationRows
                .Where(row => row.OF == paddedOf)
                .Select(row => (int?)row.Etat)
                .SingleOrDefault();
            if (existingEtat is not null)
            {
                List<string> refusalReasons = ResendRefusalReasons(existingEtat.Value, paddedOf);
                if (refusalReasons.Count > 0)
                {
                    string resendMessage =
                        $"OF '{of}' : l'OF existe déjà et ne peut pas être remplacé ({string.Join(" ; ", refusalReasons)}).";
                    ConversionError resendError = new() { Block = Block.File, Code = ErrorCode.BusinessRuleViolation, Message = resendMessage };
                    return Recorded(new ImportResult { Errors = [resendError] }, Entry(numeroFichier, of, resendMessage));
                }
            }

            // A-5 (Epic 4 retro): a same-bundle L_D_CONSIGNES natural-key collision - two sections
            // sharing the same CodeOperation for this OF - would otherwise throw an uncaught
            // InvalidOperationException at AddRange time. Caught here, in-memory, before anything is
            // staged on the context, and routed through the same ConversionError/REJETÉ-log shape as the
            // missing-Coulee check above - never widening the DbUpdateException/DbException catch filter
            // to cover it.
            // The full 4-part tuple mirrors L_D_CONSIGNES' real natural key. OF is bundle-constant today
            // (every ConsignesMapper.Row call passes the same source.OF) and ConsigneGPAO is true or false
            // per decoded row since Story 4.13 (the working copy), so the two halves never collide with each
            // other, but TypeConsigne now varies per row (0-29, one full-code row plus
            // one row per decoded sub-field per applicable section - see ConsignesMapper): every
            // decodable section's own TypeConsigne=13 full-code row still shares the same (OF,
            // CodeOperation, TypeConsigne, ConsigneGPAO) key whenever two sections share one
            // CodeOperation, so the collision this check exists for remains reachable through that row,
            // not through the whole tuple degenerating to CodeOperation alone. C-2 (Épic 4 retro #3):
            // CodeOperation collides case-insensitively at the real SQL Server (case-insensitive
            // collation) even though plain tuple equality would
            // treat "XC1" and "xc1" as distinct - ConsignesNaturalKeyComparer makes only CodeOperation
            // case-insensitive, OF/TypeConsigne/ConsigneGPAO stay ordinal, and the message below still
            // reads collidingGroup[0]'s own original-case CodeOperation, never a normalized form.
            List<L_D_CONSIGNES>? collidingGroup = bundle.Consignes
                .GroupBy(
                    row => (row.OF, row.CodeOperation, row.TypeConsigne, row.ConsigneGPAO),
                    ConsignesNaturalKeyComparer.Instance)
                .FirstOrDefault(group => group.Count() > 1)
                ?.ToList();

            // B-5 (Story 4.10 hardening): DecimalScale.Apply corrects decimal placement but never checks
            // a column's total DECIMAL(p,s) magnitude - an out-of-gabarit raw KAPE22 int, once scaled,
            // can still exceed it and would otherwise overflow at SaveChanges as an undiagnosed SQL
            // exception. Checked here, before anything is staged, against DownstreamColumnMagnitudes;
            // same ConversionError/REJETÉ-log shape as the check above.
            string? magnitudeOverflow = FindMagnitudeOverflow(bundle);

            // C-4 (Épic 4 retro #3): a mapped downstream string column whose value exceeds its
            // DownstreamColumnLengths bound would otherwise overflow at SaveChanges as an undiagnosed SQL
            // truncation error - the same posture B-5 already established for decimal magnitude. Checked
            // here, before anything is staged, against DownstreamColumnLengths.
            string? lengthOverflow = FindLengthOverflow(bundle);

            // C-6 (Épic 4 retro #3): A-5, B-5 and C-4 are independent pre-checks, but used to reject
            // sequentially - a bundle failing more than one only ever reported whichever fired first, one
            // SaveChanges each (a code-review finding: C-4 originally shipped after this accumulation as
            // its own separate early return, silently reintroducing the same "first cause wins" symptom
            // one guard later). All three now run unconditionally above and accumulate into a single
            // REJETÉ log row / ConversionError when more than one fires. The missing-Coulee check above
            // stays its own separate, untouched early return - out of this accumulation's scope.
            List<string> businessRuleMessages = [];
            if (collidingGroup is not null)
            {
                businessRuleMessages.Add("plusieurs consignes partagent le même "
                    + $"CodeOperation '{collidingGroup[0].CodeOperation}'.");
            }

            if (magnitudeOverflow is not null)
            {
                businessRuleMessages.Add($"la valeur convertie {magnitudeOverflow} dépasse le gabarit de sa colonne.");
            }

            if (lengthOverflow is not null)
            {
                businessRuleMessages.Add($"la valeur '{lengthOverflow}' dépasse la longueur maximale de sa colonne.");
            }

            if (businessRuleMessages.Count > 0)
            {
                return RejectWithBusinessRuleViolation(of, numeroFichier, businessRuleMessages);
            }

            // D34 replace (review D-1/P-9, user decision 2026-10-05): the previous OF is really deleted, as
            // the legacy DeleteOF does, then inserted again, so no column of the old row survives - not even
            // one the entities do not map. The deletes run as SQL statements inside an explicit transaction
            // that the SaveChanges below joins: a SQL failure anywhere rolls all of it back and leaves the
            // previous OF intact (AC-FR21-6, AC-FR21-2). L_D_COULEE and the earlier L_D_KAPE22 rows are kept.
            using IDbContextTransaction? replace = existingEtat is null ? null : context.Database.BeginTransaction();
            if (existingEtat is not null)
            {
                DeleteOf(paddedOf);
            }

            context.Kape22Rows.Add(entity);
            context.OrdreFabricationRows.Add(bundle.OrdreFabrication!);
            if (!couleeAlreadyExists)
            {
                context.CouleeRows.Add(bundle.Coulee!);
            }

            AddIfPresent(context.SectionChargeChutageRows, bundle.SectionChargeChutage);
            AddIfPresent(context.SectionChargeDecoupeRows, bundle.SectionChargeDecoupe);
            AddIfPresent(context.SectionChargeLingotRows, bundle.SectionChargeLingot);

            // C-7 (Épic 4 retro #3): Pits is never actually null here - AC-FR20-4 already rejected the
            // bundle upstream (Kape22ImportBundleMapper) when SectionChargePitsMapper returned null - but
            // AddIfPresent's own null-check still guards it uniformly with its 6 siblings.
            AddIfPresent(context.SectionChargePitsRows, bundle.SectionChargePits);
            AddIfPresent(context.SectionChargePoidsMetriqueRows, bundle.SectionChargePoidsMetrique);
            AddIfPresent(context.SectionChargeRefroidissoirsRows, bundle.SectionChargeRefroidissoirs);
            AddIfPresent(context.SectionChargeSvtRows, bundle.SectionChargeSvt);
            context.ConsignesRows.AddRange(bundle.Consignes);

            // AC-FR21-1/AD-1: a single SaveChanges wraps L_D_KAPE22 and every downstream entity - including
            // as many Consignes rows as the bundle carries - in one transaction, so either all of them land
            // or none does.
            context.SaveChanges();
            replace?.Commit();
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            // AC-FR24-3: the failure entry carries SQL Server's own message, which names the table, the
            // column and the cause when the server provides them.
            ImportResult failure = PersistenceFailure(exception);
            return Recorded(failure, Entry(numeroFichier, of, Summarize(failure.Errors)));
        }

        // AC-FR24-2/24-5: the journal is written after the commit; if it fails, the committed rows stay
        // and InsertedId is kept, while the journal PersistenceError leaves the Fichier in processing/ for
        // the next tick's guard to complete.
        return Recorded(
            new ImportResult { InsertedId = entity.Id, Warnings = bundle.Warnings },
            Entry(numeroFichier, of, reason: null));
    }

    private ImportResult PersistRejected(Kape22ImportBundle bundle)
    {
        // D15: with no readable OF the journal cannot name the Fichier's OF (only MQTTnetServices.Logs,
        // an Epic 3 concern). The rejection still comes back for the orchestrator to move the Fichier
        // to error/.
        if (string.IsNullOrWhiteSpace(bundle.OF))
        {
            return new ImportResult { Errors = bundle.Errors };
        }

        // AC-FR11-4: one failure entry summarizing every rejection reason.
        return Recorded(
            new ImportResult { Errors = bundle.Errors },
            Entry(bundle.NumeroFichier, bundle.OF.Trim(), Summarize(bundle.Errors)));
    }

    // Business note (Q-3, Épic 4 retro #3, revised Story 6.10): the GPAO can re-send an OF under a new
    // NumeroFichier - that is the D34 replace-or-refuse path in PersistMapped. The same NumeroFichier/OF
    // pair, already in L_D_KAPE22, is never imported twice: it lands here.
    private ImportResult CompleteAlreadyImported(Kape22ImportBundle bundle, string numeroFichier, string of)
    {
        ImportResult skipped = new() { AlreadyImported = true, Warnings = bundle.Warnings };
        FichierJournalEntry success = Entry(numeroFichier, of, reason: null);
        try
        {
            if (journal.HasSuccess(success))
            {
                return skipped;
            }
        }
        catch (Exception exception)
        {
            return JournalFailure(skipped, exception);
        }

        return Recorded(skipped, success);
    }

    // Shared shape behind the Consignes-collision (A-5), magnitude-overflow (B-5) and length-overflow
    // (C-4) pre-checks: one BusinessRuleViolation ConversionError and one failure entry, the same way the
    // missing-Coulee block above (AC-FR20-5) does inline. C-6 (Épic 4 retro #3): messages is a list, not a
    // single string, so the A-5+B-5 caller can combine both causes into the one ConversionError/failure
    // entry a simultaneous failure of both must produce.
    private ImportResult RejectWithBusinessRuleViolation(string of, string numeroFichier, IReadOnlyList<string> messages)
    {
        string message = $"OF '{of}' : " + string.Join(" ; ", messages);
        ConversionError error = new() { Block = Block.File, Code = ErrorCode.BusinessRuleViolation, Message = message };
        return Recorded(new ImportResult { Errors = [error] }, Entry(numeroFichier, of, message));
    }

    // D34: why an existing OF cannot be replaced, empty when it can - its Etat when protected, then each
    // precondition table naming it, in that order. Legacy parity: OrdreFabricationController.AddRange2
    // refuses the same 5 EtatOF values; the 3 tables are this importer's own addition.
    private List<string> ResendRefusalReasons(int etat, string paddedOf)
    {
        List<string> reasons = [];
        if (ProtectedEtats.TryGetValue(etat, out string? name))
        {
            reasons.Add($"état {name} ({etat})");
        }

        if (context.FoursRows.Any(row => row.OFEnCours == paddedOf))
        {
            reasons.Add("présent dans L_D_FOURS");
        }

        if (context.PlansFoursRows.Any(row => row.OF == paddedOf))
        {
            reasons.Add("présent dans L_D_PLANS_FOURS");
        }

        if (context.PsoRows.Any(row => row.OF == paddedOf))
        {
            reasons.Add("présent dans L_D_PSO");
        }

        return reasons;
    }

    // D34 replace, legacy parity with OrdreFabricationController.DeleteOF: deletes the previous OF from
    // L_D_OF_SUIVI (every later Rang moves up by one), L_D_CONSIGNES, L_D_MAM_QUAL, L_D_PRODUITS_OUTIL,
    // L_D_REBUT, the 7 L_D_SECTIONCHARGE_* and last L_D_ORDRE_FABRICATION, which production's foreign keys
    // from L_D_OF_SUIVI, L_D_REBUT and the 7 sections reference. DeleteOF's L_D_PLANS_FOURS, L_D_PSO (with
    // its L_D_SOUS_PRODUITS) and L_D_FOURS steps never apply here: an OF named there is refused above.
    // Never L_D_COULEE nor L_D_KAPE22.
    private void DeleteOf(string paddedOf)
    {
        int? rang = context.OfSuiviRows.Where(row => row.OF == paddedOf).Select(row => (int?)row.Rang).SingleOrDefault();
        if (rang is not null)
        {
            context.OfSuiviRows.Where(row => row.OF == paddedOf).ExecuteDelete();
            context.OfSuiviRows
                .Where(row => row.Rang > rang.Value)
                .ExecuteUpdate(setters => setters.SetProperty(row => row.Rang, row => row.Rang - 1));
        }

        context.ConsignesRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.MamQualRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.ProduitsOutilRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.RebutRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.SectionChargeChutageRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.SectionChargeDecoupeRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.SectionChargeLingotRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.SectionChargePitsRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.SectionChargePoidsMetriqueRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.SectionChargeRefroidissoirsRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.SectionChargeSvtRows.Where(row => row.OF == paddedOf).ExecuteDelete();
        context.OrdreFabricationRows.Where(row => row.OF == paddedOf).ExecuteDelete();
    }

    // The journal entry for this Fichier; a null reason is a success (FichierJournalEntry: no reason means
    // success). Each P60 message tail is one reason, so the LSI Message stays word for word what P60 wrote
    // before the migration (AC-FR24-6).
    private FichierJournalEntry Entry(string? numeroFichier, string of, string? reason) => new()
    {
        Commande = configuration[CommandeKey] ?? DefaultCommande,
        FichierName = fichierName,
        Instant = (timeProvider ?? TimeProvider.System).GetUtcNow(),
        NumeroFichier = numeroFichier,
        OF = of,
        Reasons = reason is null ? [] : [reason],
    };

    // Records the entry and returns the result unchanged, or the result with a journal PersistenceError
    // when the journal fails.
    private ImportResult Recorded(ImportResult result, FichierJournalEntry entry)
    {
        try
        {
            journal.Record(entry);
            return result;
        }
        catch (Exception exception)
        {
            return JournalFailure(result, exception);
        }
    }

    // Any exception is caught (an unreachable server can surface as more than one type, the same choice as
    // P89FolderConverter.TryRecord). The File-level PersistenceError keeps the result's own Errors ahead of
    // it and sends the Fichier back through InboxScanner's retry path (AC-FR15-3), so the journal entry is
    // written by a later tick instead of being lost.
    private static ImportResult JournalFailure(ImportResult result, Exception exception)
    {
        ConversionError journalError = new()
        {
            Block = Block.File,
            Code = ErrorCode.PersistenceError,
            Message = $"Échec du journal : {(exception.InnerException ?? exception).Message}",
        };

        return result with { Errors = [.. result.Errors, journalError] };
    }

    // B-5: the first mapped downstream decimal property whose value's magnitude reaches or exceeds its
    // column's DownstreamColumnMagnitudes bound, formatted "<Entity>.<Property> = <value>", or null when
    // every scaled column is in gabarit. Reflection over the mapped entities' own properties, not the
    // AD-2 "zero reflection" mapper style - this is the persister's own one-shot check, run once per
    // Fichier, not a mapper.
    private static string? FindMagnitudeOverflow(Kape22ImportBundle bundle)
    {
        foreach (object entity in DownstreamDecimalEntities(bundle))
        {
            foreach (PropertyInfo property in entity.GetType().GetProperties())
            {
                if (!DownstreamColumnMagnitudes.MaxAbsoluteValues.TryGetValue(property.Name, out decimal bound))
                {
                    continue;
                }

                if (property.GetValue(entity) is decimal actual && Math.Abs(actual) >= bound)
                {
                    return $"{entity.GetType().Name}.{property.Name} = {actual.ToString(CultureInfo.InvariantCulture)}";
                }
            }
        }

        return null;
    }

    // The bundle's downstream entities that can carry a DownstreamColumnMagnitudes-registered column -
    // OrdreFabrication always (non-null on the success path this check runs on), the 4 SectionCharge*
    // tables only when the Story 4.4 per-OF applicability rule kept them.
    private static IEnumerable<object> DownstreamDecimalEntities(Kape22ImportBundle bundle)
    {
        yield return bundle.OrdreFabrication!;

        if (bundle.SectionChargeChutage is not null)
        {
            yield return bundle.SectionChargeChutage;
        }

        if (bundle.SectionChargeDecoupe is not null)
        {
            yield return bundle.SectionChargeDecoupe;
        }

        if (bundle.SectionChargeLingot is not null)
        {
            yield return bundle.SectionChargeLingot;
        }

        if (bundle.SectionChargePits is not null)
        {
            yield return bundle.SectionChargePits;
        }
    }

    // C-4 (Épic 4 retro #3): the first mapped downstream string property whose value exceeds its
    // column's DownstreamColumnLengths bound, formatted "<Entity>.<Property> = <value>", or null when
    // every bounded column is within length. Same reflection-walk shape as FindMagnitudeOverflow, run
    // once per Fichier, not a mapper.
    private static string? FindLengthOverflow(Kape22ImportBundle bundle)
    {
        foreach (object entity in DownstreamStringEntities(bundle))
        {
            foreach (PropertyInfo property in entity.GetType().GetProperties())
            {
                if (!DownstreamColumnLengths.MaxLengths.TryGetValue(property.Name, out int maxLength))
                {
                    continue;
                }

                if (property.GetValue(entity) is string actual && actual.Length > maxLength)
                {
                    return $"{entity.GetType().Name}.{property.Name} = {actual}";
                }
            }
        }

        return null;
    }

    // C-4: the bundle's downstream entities that can carry a DownstreamColumnLengths-registered string
    // column - all 10 Story 4.1 tables (string columns exist outside FindMagnitudeOverflow's 5-table
    // decimal subset above), not DownstreamDecimalEntities reused as-is. OrdreFabrication and Coulee
    // always (non-null on the success path this check runs on), the 7 SectionCharge* tables only when
    // the Story 4.4 per-OF applicability rule kept them, and every Consignes row the bundle carries.
    private static IEnumerable<object> DownstreamStringEntities(Kape22ImportBundle bundle)
    {
        yield return bundle.OrdreFabrication!;
        yield return bundle.Coulee!;

        if (bundle.SectionChargeChutage is not null)
        {
            yield return bundle.SectionChargeChutage;
        }

        if (bundle.SectionChargeDecoupe is not null)
        {
            yield return bundle.SectionChargeDecoupe;
        }

        if (bundle.SectionChargeLingot is not null)
        {
            yield return bundle.SectionChargeLingot;
        }

        if (bundle.SectionChargePits is not null)
        {
            yield return bundle.SectionChargePits;
        }

        if (bundle.SectionChargePoidsMetrique is not null)
        {
            yield return bundle.SectionChargePoidsMetrique;
        }

        if (bundle.SectionChargeRefroidissoirs is not null)
        {
            yield return bundle.SectionChargeRefroidissoirs;
        }

        if (bundle.SectionChargeSvt is not null)
        {
            yield return bundle.SectionChargeSvt;
        }

        foreach (L_D_CONSIGNES consigne in bundle.Consignes)
        {
            yield return consigne;
        }
    }

    // Adds a Story 4.1 downstream entity only when the Story 4.3/4.4 mapper found its section applicable
    // to this OF (AC-FR21-1: "every non-null SectionCharge*").
    private static void AddIfPresent<TEntity>(DbSet<TEntity> rows, TEntity? entity)
        where TEntity : class
    {
        if (entity is not null)
        {
            rows.Add(entity);
        }
    }

    // AC-FR11-5/AC-FR21-2: the failed SaveChanges has already rolled back its transaction - none of the
    // staged rows persist. Report the SQL cause as a File-level PersistenceError; no
    // DbUpdateException or DbException leaves the persister. Since Story 6.1 a rejection's own reasons
    // no longer ride along here (the journal failure path, JournalFailure, keeps them instead). The caller
    // Kape22FichierProcessor.Import re-sorts the ImportResult by LineNumber, which moves a File-level entry
    // (LineNumber 0) to the front (AC-FR6-4 extended to ImportResult). Since
    // Story 4.12, Kape22FichierProcessor.Import also calls it when the L_P_CONSIGNES_* reference read
    // fails, and sets NormalizedXml and Warnings on the result itself.
    internal static ImportResult PersistenceFailure(Exception exception)
    {
        string cause = (exception.InnerException ?? exception).Message;
        ConversionError persistenceError = new()
        {
            Block = Block.File,
            Code = ErrorCode.PersistenceError,
            Message = $"Échec de la persistance dans AscoLSI : {cause}",
        };

        return new ImportResult { Errors = [persistenceError] };
    }

    // The rejection summary carried in the journal entry, shaped "<count> erreur(s) : <message> ; ...".
    private static string Summarize(IReadOnlyList<ConversionError> errors) =>
        $"{errors.Count} erreur(s) : {string.Join(" ; ", errors.Select(error => error.Message))}";
}
