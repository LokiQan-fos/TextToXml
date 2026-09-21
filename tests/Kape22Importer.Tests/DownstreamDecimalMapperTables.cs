using System;
using System.Collections.Generic;

namespace Kape22Importer.Tests;

// Story 4.11 (C-3, Épic 4 retro #3): the mapper-file-name -> table-name pairing behind the 5 mappers
// Story 4.3-bis's DecimalScale.Apply touches - previously duplicated as two independent hardcoded lists,
// DownstreamColumnMagnitudesParityTests.TableByMapperFile and MappingAnnexCompletenessTests' own 5-call
// AddRange sequence. Both now derive from this one shared source.
internal static class DownstreamDecimalMapperTables
{
    public static readonly IReadOnlyDictionary<string, string> TableByMapperFile = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["OrdreFabricationMapper.cs"] = "L_D_ORDRE_FABRICATION",
        ["SectionChargeChutageMapper.cs"] = "L_D_SECTIONCHARGE_CHUTAGE",
        ["SectionChargeDecoupeMapper.cs"] = "L_D_SECTIONCHARGE_DECOUPE",
        ["SectionChargeLingotMapper.cs"] = "L_D_SECTIONCHARGE_LINGOT",
        ["SectionChargePitsMapper.cs"] = "L_D_SECTIONCHARGE_PITS",
    };
}
