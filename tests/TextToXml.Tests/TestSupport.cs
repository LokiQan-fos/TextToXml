using System.IO;
using System.Text;
using System.Xml.Linq;

namespace TextToXml.Tests;

// Shared test helpers, folded here so each test class stops carrying its own verbatim copy.
// Imported per file with "using static TextToXml.Tests.TestSupport;".
internal static class TestSupport
{
    private static readonly Encoding Cp1252;

    static TestSupport()
    {
        // Windows-1252 lives in the code-pages provider; register it before resolving the encoding so
        // the helpers use the real code page rather than a byte cast that is only right for ASCII.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1252 = Encoding.GetEncoding(1252);
    }

    // Encodes test text to its Windows-1252 bytes, the wire form Converter.Convert expects.
    public static byte[] Windows1252(string text) => Cp1252.GetBytes(text);

    // Alias kept for call sites that named the same operation Ascii; the bytes are identical for the
    // ASCII text those tests pass.
    public static byte[] Ascii(string text) => Cp1252.GetBytes(text);

    // Parses a Descripteur or a normalized XML document and returns its root element.
    public static XElement Root(string xml) => XDocument.Parse(xml).Root!;

    // Same as Root; kept for call sites that distinguish the normalized <file> document by name.
    public static XElement FileRoot(string xml) => XDocument.Parse(xml).Root!;

    // Builds a fixed-width Ligne of the given length by writing each field's text at its Position over
    // a space-filled buffer; text that overruns the length is clipped.
    public static string Row(int length, params (int Position, string Text)[] fields)
    {
        char[] buffer = new string(' ', length).ToCharArray();
        foreach ((int position, string text) in fields)
        {
            for (int i = 0; i < text.Length && position + i < length; i++)
            {
                buffer[position + i] = text[i];
            }
        }

        return new string(buffer);
    }

    // Reads a synthetic non-P60 Descripteur from the generic fixtures directory.
    public static string ReadDescriptor(string name) =>
        File.ReadAllText(Path.Combine(RepoLayout.FixturesDirectory, "generic", name));

    // Reads a synthetic non-P60 input Fichier from the generic fixtures directory.
    public static byte[] ReadInput(string name) =>
        File.ReadAllBytes(Path.Combine(RepoLayout.FixturesDirectory, "generic", name));
}
