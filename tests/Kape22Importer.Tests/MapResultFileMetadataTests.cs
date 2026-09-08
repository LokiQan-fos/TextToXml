using System.Xml.Linq;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 2.8 support: Kape22Mapper.Map exposes the Header roulette NumeroFichier and the trimmed Detail
// OF on MapResult, so the persister can write the "REJETÉ" L_D_LOG_COMMANDE line for a rejected Fichier
// (AC-FR11-4) and can key the D22 anti-duplicate guard. Both stay null when deserialization itself
// failed, which is the D15 "OF unreadable" path with no L_D_LOG_COMMANDE row. Written test-first (CC-1):
// red until Map populates the two fields. Unit-only (AR-12), no database.
[Trait("Category", TestCategory.Unit)]
public class MapResultFileMetadataTests
{
    // A successful map exposes the Header NumeroFichier and the Detail OF alongside the entity.
    [Fact]
    public void Map_Success_ExposesHeaderNumeroFichierAndDetailOf()
    {
        string xml = ConvertReferenceFichier();
        XDocument document = XDocument.Parse(xml);
        string expectedRoulette = (string)document.Root!.Element("header")!.Element("NumeroFichier")!;
        string expectedOf = (string)document.Root!.Element("message")!.Element("OF")!;

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Equal(expectedRoulette, result.NumeroFichier);
        Assert.Equal(expectedOf.Trim(), result.OF);
    }

    // A Fichier rejected by a required-field check still deserialized, so NumeroFichier and OF are known
    // and the persister can log the rejection (AC-FR11-4).
    [Fact]
    public void Map_RejectedButDeserialized_StillExposesNumeroFichierAndOf()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        string expectedRoulette = (string)document.Root!.Element("header")!.Element("NumeroFichier")!;
        string expectedOf = (string)document.Root!.Element("message")!.Element("OF")!;

        MapResult<L_D_KAPE22> result = MapMutatedFichier(d => SetChamp(d, "message", "Client", string.Empty));

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Equal(expectedRoulette, result.NumeroFichier);
        Assert.Equal(expectedOf.Trim(), result.OF);
    }

    // A blank Detail OF is a rejected Fichier whose OF is not usable: the field comes back blank so the
    // persister takes the D15 "no L_D_LOG_COMMANDE row" path.
    [Fact]
    public void Map_BlankDetailOf_LeavesOfBlank()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(d => SetChamp(d, "message", "OF", string.Empty));

        Assert.False(result.Success);
        Assert.True(string.IsNullOrWhiteSpace(result.OF));
    }

    // When the normalized XML cannot be deserialized at all (PersistenceError), neither field is known
    // and the persister writes nothing to L_D_LOG_COMMANDE (D15).
    [Fact]
    public void Map_DeserializationFails_LeavesNumeroFichierAndOfNull()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map("<file>not valid against P60.xsd</file>", ReferenceFichierName, WinterClock());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.PersistenceError);
        Assert.Null(result.NumeroFichier);
        Assert.Null(result.OF);
    }
}
