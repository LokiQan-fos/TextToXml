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

        // Load and validate the Descripteur once; the parsed root is reused by the stages below (FR-1).
        DescriptorValidationResult descriptorValidation = DescriptorValidator.Validate(descriptor);
        if (descriptorValidation.Error is not null)
        {
            return new ConversionResult { Errors = [descriptorValidation.Error] };
        }

        // Decode the Windows-1252 bytes with a strict decoder and split the Fichier into Lignes (FR-2).
        InputReadResult read = InputReader.Read(input);
        if (read.Error is not null)
        {
            return new ConversionResult { Errors = [read.Error] };
        }

        // Assign each Ligne to a Bloc, check the Ligne count and run the non-blocking Segment control (FR-3).
        BlockAssignmentResult blocks = BlockAssigner.Assign(read.Lines, descriptorValidation.Root!);
        if (blocks.Error is not null)
        {
            // A WrongBlockCount error short-circuits Champ analysis; no Warning is carried (AC-FR3-8).
            return new ConversionResult { Errors = [blocks.Error] };
        }

        // Later stories continue the pipeline: check Ligne lengths, extract and type the Champs,
        // then emit the normalized XML. Segment Warnings are carried through unchanged.
        return new ConversionResult { Warnings = blocks.Warnings };
    }
}
