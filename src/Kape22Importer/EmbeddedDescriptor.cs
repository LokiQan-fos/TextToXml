using System;
using System.IO;
using System.Reflection;

namespace Kape22Importer;

// The P60 Descripteur embedded in the importer assembly (AR-5). Read once here so the FR-8 startup
// compatibility check and the per-file required-field check share a single copy instead of each
// re-opening the manifest resource stream.
public static class EmbeddedDescriptor
{
    private const string ResourceName = "Kape22Importer.Templates.P60.xml";

    public static string Xml { get; } = Read();

    private static string Read()
    {
        Assembly assembly = typeof(EmbeddedDescriptor).Assembly;

        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{ResourceName}' is missing from {assembly.GetName().Name}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
