using System;
using System.Collections.Generic;
using Kape22Importer.Persistence;
using TextToXml;

namespace Kape22Importer;

// Story 4.5 (FR-20): the single entry point composing every Epic 4 mapper into one Kape22ImportBundle,
// then running the 3 pure business controls FR-20 requires before persistence. Pure: no DB read, no I/O,
// no reflection (AD-2) - mirrors the Story 4.3/4.4 static-mapper style but as an instance method so the
// TimeProvider can be forwarded once to every collaborator that needs it (Kape22Mapper,
// OrdreFabricationMapper, CouleeMapper), the same optional-param convention those three already use.
// Story 4.6's Kape22Persister will call this instead of Kape22Mapper.Map directly.
public sealed class Kape22ImportBundleMapper(TimeProvider? timeProvider = null)
{
    // sourceFileName feeds Kape22Mapper's own FR-10 file-name coherence check, unchanged here.
    public Kape22ImportBundle Map(string normalizedXml, string sourceFileName)
    {
        MapResult<L_D_KAPE22> mapped = new Kape22Mapper(timeProvider).Map(normalizedXml, sourceFileName);
        if (mapped.Errors.Count > 0)
        {
            // Short-circuit: a Fichier that failed Kape22Mapper's Step 1/2 checks never reaches the
            // Story 4.3/4.4 mappers, which all assume a valid L_D_KAPE22 as their input.
            return new Kape22ImportBundle
            {
                Errors = mapped.Errors,
                NumeroFichier = mapped.NumeroFichier,
                OF = mapped.OF,
                Warnings = mapped.Warnings,
            };
        }

        L_D_KAPE22 kape22 = mapped.Value!;

        L_D_ORDRE_FABRICATION ordreFabrication = OrdreFabricationMapper.Map(kape22, timeProvider);
        L_D_COULEE coulee = CouleeMapper.Map(kape22, timeProvider);
        L_D_SECTIONCHARGE_CHUTAGE? chutage = SectionChargeChutageMapper.Map(kape22);
        L_D_SECTIONCHARGE_DECOUPE? decoupe = SectionChargeDecoupeMapper.Map(kape22);
        L_D_SECTIONCHARGE_LINGOT? lingot = SectionChargeLingotMapper.Map(kape22);
        L_D_SECTIONCHARGE_PITS? pits = SectionChargePitsMapper.Map(kape22);
        L_D_SECTIONCHARGE_POIDSMETRIQUE? poidsMetrique = SectionChargePoidsMetriqueMapper.Map(kape22);
        L_D_SECTIONCHARGE_REFROIDISSOIRS? refroidissoirs = SectionChargeRefroidissoirsMapper.Map(kape22);
        L_D_SECTIONCHARGE_SVT? svt = SectionChargeSvtMapper.Map(kape22);
        List<L_D_CONSIGNES> consignes = ConsignesMapper.Map(
            kape22, chutage, decoupe, lingot, pits, poidsMetrique, refroidissoirs, svt);

        // Accumulate-then-freeze, the same pattern Kape22Mapper.Map and CoherenceChecker already use:
        // every control runs regardless of an earlier one's outcome, so a Fichier failing two controls
        // reports both in one pass instead of stopping at the first.
        List<ConversionError> errors = [];
        AddIngotFurnaceDistributionViolation(errors, kape22, ordreFabrication, refroidissoirs);
        AddHotCouleeFormatViolation(errors, kape22);
        AddMissingEnfournementInstructionViolation(errors, kape22, pits);

        return new Kape22ImportBundle
        {
            Consignes = consignes,
            Coulee = coulee,
            Errors = errors,
            Kape22 = kape22,
            NumeroFichier = mapped.NumeroFichier,
            OF = mapped.OF,
            OrdreFabrication = ordreFabrication,
            SectionChargeChutage = chutage,
            SectionChargeDecoupe = decoupe,
            SectionChargeLingot = lingot,
            SectionChargePits = pits,
            SectionChargePoidsMetrique = poidsMetrique,
            SectionChargeRefroidissoirs = refroidissoirs,
            SectionChargeSvt = svt,
            Warnings = mapped.Warnings,
        };
    }

    // AC-FR20-2: the OF's two furnace ingot counts (already mapped onto SectionChargeRefroidissoirs)
    // must sum to the OF's own expected NombreDemiProduit. Not applicable when Refroidissoirs itself is
    // not applicable to this OF (Story 4.4's per-OF applicability rule) - nothing to compare then.
    private static void AddIngotFurnaceDistributionViolation(
        List<ConversionError> errors,
        L_D_KAPE22 kape22,
        L_D_ORDRE_FABRICATION ordreFabrication,
        L_D_SECTIONCHARGE_REFROIDISSOIRS? refroidissoirs)
    {
        if (refroidissoirs is null)
        {
            return;
        }

        // A blank int Champ is always zero-filled onto L_D_KAPE22 (Kape22Mapper.DefaultForNonNullable),
        // never left null - so Four1/Four2 are never genuinely missing here, only genuinely zero.
        int four1 = refroidissoirs.NombreLingotsFour1 ?? 0;
        int four2 = refroidissoirs.NombreLingotsFour2 ?? 0;
        int total = four1 + four2;
        if (total == ordreFabrication.NombreDemiProduit)
        {
            return;
        }

        errors.Add(new ConversionError
        {
            Block = Block.File,
            Code = ErrorCode.BusinessRuleViolation,
            Message = $"OF '{kape22.OF}' : la répartition des lingots aux fours (Four1={four1} + "
                + $"Four2={four2} = {total}) ne correspond pas au nombre de demi-produits attendu "
                + $"({ordreFabrication.NombreDemiProduit}).",
        });
    }

    // AC-FR20-3: a "hot" Coulee (CodeConsignePits != "1", read straight off L_D_KAPE22 rather than a
    // filtered L_D_CONSIGNES row - see spec Design Notes) must carry a Coulee number starting with '0'.
    private static void AddHotCouleeFormatViolation(List<ConversionError> errors, L_D_KAPE22 kape22)
    {
        bool isHot = kape22.CodeConsignePits != Kape22ImportBundle.ColdConsignePits;
        if (!isHot || kape22.Coulee.StartsWith('0'))
        {
            return;
        }

        errors.Add(new ConversionError
        {
            Block = Block.File,
            Code = ErrorCode.BusinessRuleViolation,
            Message = $"OF '{kape22.OF}' : la coulée chaude '{kape22.Coulee}' ne commence pas par '0' "
                + $"(CodeConsignePits '{kape22.CodeConsignePits}' différent de '1').",
        });
    }

    // AC-FR20-4: every OF needs an enfournement instruction (L_D_SECTIONCHARGE_PITS). A null result from
    // SectionChargePitsMapper - the section not applicable to this OF - is itself the violation.
    private static void AddMissingEnfournementInstructionViolation(
        List<ConversionError> errors, L_D_KAPE22 kape22, L_D_SECTIONCHARGE_PITS? pits)
    {
        if (pits is not null)
        {
            return;
        }

        errors.Add(new ConversionError
        {
            Block = Block.File,
            Code = ErrorCode.BusinessRuleViolation,
            Message = $"OF '{kape22.OF}' : aucune consigne d'enfournement (L_D_SECTIONCHARGE_PITS) n'a "
                + "été trouvée.",
        });
    }
}
