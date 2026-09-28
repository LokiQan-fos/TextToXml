using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using TextToXml.Tests;

namespace P89Converter.Tests;

// The embedded P89 Descripteur and schema (Story 5.1). P89.xsd is generated from P89.xml by
// scripts/gen.ps1 -Format P89; these tests keep the two in lockstep without calling the script, the
// same principle as Kape22Importer.Tests.P60XsdTests.
[Trait("Category", TestCategory.Unit)]
public class P89TemplatesTests
{
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    public static TheoryData<string> BlocNames() => new() { "header", "message", "footer" };

    // AC-FR22-1: the corrected layout (+5 from AnomaliePitsFour1) ends the message Ligne on LG24,
    // Position 3438, Size 4, that is 3442 characters.
    [Fact]
    [Trait("AC", "FR22-1")]
    public void Descriptor_LastMessageChampEndsAt3442_AcFr22_1()
    {
        XElement last = Descriptor().Element("message")!.Elements("value").Last();

        Assert.Equal("LG24", (string)last.Attribute("Id")!);
        Assert.Equal(3442, (int)last.Attribute("Position")! + (int)last.Attribute("Size")!);
    }

    // AC-FR22-1: each Bloc is contiguous, every Champ starting where the previous one ends, so the +5
    // layout correction left no gap or overlap.
    [Theory]
    [MemberData(nameof(BlocNames))]
    [Trait("AC", "FR22-1")]
    public void Descriptor_BlocIsContiguous_AcFr22_1(string bloc)
    {
        int expected = 0;
        foreach (XElement value in Descriptor().Element(bloc)!.Elements("value"))
        {
            Assert.True(
                (int)value.Attribute("Position")! == expected,
                $"{bloc}/{(string)value.Attribute("Id")!} starts at {(int)value.Attribute("Position")!}, expected {expected}.");
            expected = (int)value.Attribute("Position")! + (int)value.Attribute("Size")!;
        }
    }

    // AC-FR22-2: every <value> of a Bloc has an xs:element of the matching type, in descriptor order:
    // xs:int for int, xs:date or xs:dateTime for datetime, xs:string otherwise; typed ones are optional.
    [Theory]
    [MemberData(nameof(BlocNames))]
    [Trait("AC", "FR22-2")]
    public void Schema_MatchesTheDescriptorBloc_AcFr22_2(string bloc)
    {
        List<string> expected = Descriptor().Element(bloc)!.Elements("value")
            .Select(value => $"{(string)value.Attribute("Id")!}:{ExpectedType(value)}")
            .ToList();

        List<string> actual = SchemaElements(bloc)
            .Select(element => $"{(string)element.Attribute("name")!}:{Type(element)}")
            .ToList();

        Assert.Equal(expected, actual);
    }

    // AC-FR22-2: a typed element is minOccurs="0" because Step 1 omits a blank typed Champ (D27).
    [Theory]
    [MemberData(nameof(BlocNames))]
    [Trait("AC", "FR22-2")]
    public void Schema_TypedElementsAreOptional_AcFr22_2(string bloc)
    {
        string[] offenders = SchemaElements(bloc)
            .Where(element => Type(element) != "string" && (string?)element.Attribute("minOccurs") != "0")
            .Select(element => (string)element.Attribute("name")!)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static XElement Descriptor() => XDocument.Parse(P89Templates.DescriptorXml).Root!;

    // The xs type the generator emits for a descriptor <value>: datetime maps to xs:dateTime when its
    // convert pattern carries a time component, xs:date otherwise.
    private static string ExpectedType(XElement value) =>
        ((string?)value.Attribute("datatype")) switch
        {
            "datetime" => ((string?)value.Attribute("convert") ?? string.Empty).IndexOfAny(['H', 'h', 'm', 's']) >= 0
                ? "dateTime"
                : "date",
            "int" => "int",
            _ => "string",
        };

    private static IEnumerable<XElement> SchemaElements(string bloc)
    {
        // The generator declares each Bloc as a named complexType referenced from the <file> root.
        XElement schema = XDocument.Parse(P89Templates.SchemaXsd).Root!;
        return schema.Elements(Xs + "complexType")
            .First(complexType => (string?)complexType.Attribute("name") == bloc)
            .Element(Xs + "sequence")!
            .Elements(Xs + "element");
    }

    private static string Type(XElement element)
    {
        string qualified = (string?)element.Attribute("type") ?? "xs:string";
        return qualified[(qualified.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }
}
