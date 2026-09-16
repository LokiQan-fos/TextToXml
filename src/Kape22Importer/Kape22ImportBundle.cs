using System.Collections.Generic;
using Kape22Importer.Persistence;
using TextToXml;

namespace Kape22Importer;

// Story 4.5 (FR-20, AD-6): the carrier Kape22ImportBundleMapper.Map returns and Story 4.6's persister
// will consume - one L_D_KAPE22 row plus the Story 4.3/4.4 downstream entities it dispatches to, and the
// FR-20 business-control outcome. Mirrors MapResult<T>'s Success/Errors/Warnings/NumeroFichier/OF shape
// so Story 3.2's orchestrator wiring stays familiar. Every downstream entity is nullable: either the
// upstream Kape22Mapper.Map failed (short-circuit, every entity null) or an individual SectionCharge*
// mapper found its own section not applicable to this OF (Story 4.4's per-OF applicability rule).
// Properties are declared in alphabetical order (CC-4).
public sealed record Kape22ImportBundle
{
    public List<L_D_CONSIGNES> Consignes { get; init; } = [];

    public L_D_COULEE? Coulee { get; init; }

    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public L_D_KAPE22? Kape22 { get; init; }

    public string? NumeroFichier { get; init; }

    public string? OF { get; init; }

    public L_D_ORDRE_FABRICATION? OrdreFabrication { get; init; }

    public L_D_SECTIONCHARGE_CHUTAGE? SectionChargeChutage { get; init; }

    public L_D_SECTIONCHARGE_DECOUPE? SectionChargeDecoupe { get; init; }

    public L_D_SECTIONCHARGE_LINGOT? SectionChargeLingot { get; init; }

    public L_D_SECTIONCHARGE_PITS? SectionChargePits { get; init; }

    public L_D_SECTIONCHARGE_POIDSMETRIQUE? SectionChargePoidsMetrique { get; init; }

    public L_D_SECTIONCHARGE_REFROIDISSOIRS? SectionChargeRefroidissoirs { get; init; }

    public L_D_SECTIONCHARGE_SVT? SectionChargeSvt { get; init; }

    // True exactly when Errors is empty - never set directly, so an upstream mapping failure and an
    // FR-20 business-rule violation are both reported the same way to Story 4.6's persister.
    public bool Success => this.Errors.Count == 0;

    public IReadOnlyList<ConversionError> Warnings { get; init; } = [];
}
