using System.Collections.Generic;

namespace TextToXml;

// Single source for the three Descripteur Bloc sections and how a Ligne Bloc maps onto them. The
// element name a section carries in the normalized XML is the same string, so this also names the
// XML elements. Block.File is not a Ligne role and has no section. Pure, no mutable static state (CC-6).
internal static class DescriptorSections
{
    public const string Footer = "footer";

    public const string Header = "header";

    public const string Message = "message";

    // The three sections in the order the skeleton [header] + message(s) + [footer] imposes, each
    // paired with the Bloc its Lignes are assigned to and the root marker attribute the Segment
    // control reads for it.
    public static readonly IReadOnlyList<(Block Bloc, string MarkerAttribute, string Section)> All =
    [
        (Block.Header, "headerMarker", Header),
        (Block.Detail, "messageMarker", Message),
        (Block.Footer, "footerMarker", Footer),
    ];

    // The section that declares a Ligne Bloc's Champs, or null for Block.File.
    public static string? For(Block bloc) => bloc switch
    {
        Block.Header => Header,
        Block.Detail => Message,
        Block.Footer => Footer,
        _ => null,
    };
}
