using System;
using System.Collections.Generic;

namespace FichierJournal;

// The outcome of one processed Fichier, as handed to an IFichierJournal. No reason means success.
// Properties are declared in alphabetical order (CC-4).
public sealed class FichierJournalEntry
{
    // The format that produced the entry, e.g. "P89".
    public string Commande { get; init; } = string.Empty;

    public string FichierName { get; init; } = string.Empty;

    // When the Fichier was processed, in UTC; each implementation converts it to its own time zone.
    public DateTimeOffset Instant { get; init; }

    // Raw Fichier value, null when it could not be read.
    public string? NumeroFichier { get; init; }

    // Raw Fichier value, null when it could not be read.
    public string? OF { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];
}
