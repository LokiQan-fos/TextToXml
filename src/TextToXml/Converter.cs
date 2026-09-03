using System;

namespace TextToXml;

// Entry point of the library. Pure: no disk, no network, no mutable static state (CC-6).
public static class Converter
{
    // Input is always the raw Windows-1252 bytes of the Fichier; descriptor is the only format parameter.
    public static ConversionResult Convert(ReadOnlySpan<byte> input, string descriptor)
    {
        // The only exception the library is allowed to raise (AC-FR1-8).
        ArgumentNullException.ThrowIfNull(descriptor);

        ConversionError? layoutError = DescriptorValidator.Validate(descriptor);
        if (layoutError is not null)
        {
            return new ConversionResult { Errors = [layoutError] };
        }

        // Decode the Windows-1252 bytes with a strict decoder and split the Fichier into Lignes (FR-2).
        InputReadResult read = InputReader.Read(input);
        if (read.Error is not null)
        {
            return new ConversionResult { Errors = [read.Error] };
        }

        // Later stories continue the pipeline: assign Blocs, extract and type the Champs,
        // then emit the normalized XML.
        return new ConversionResult();
    }
}
