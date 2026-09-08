using System;
using System.Collections.Generic;

namespace Kape22Importer;

// The reception-folder abstraction behind FR-12: DirectoryFileSource over a real share in production,
// an in-memory fake in the Story 3.1 tests. Folders are relative names under the reception root; the
// inbox root itself is the empty string. The MQTT evolution target (D1) swaps this implementation
// without touching TextToXml or InboxScanner.
public interface IFileSource
{
    // Deletes one entry. A missing entry is not an error.
    void Delete(string folder, string name);

    // Lists the entries directly inside one folder. Never recurses; order is unspecified.
    IReadOnlyList<FichierEntry> List(string folder);

    // Lists every entry inside a folder and its sub-folders, for the retention purge over
    // archive/<yyyy>/<MM> and error/ (AC-FR12-9).
    IReadOnlyList<FichierEntry> ListRecursive(string folder);

    // Moves one entry to another folder, creating the target folder tree as needed.
    void Move(string sourceFolder, string sourceName, string targetFolder, string targetName);

    // Reads the full content of one entry.
    byte[] Read(string folder, string name);

    // Writes a sidecar file (normalized XML, errors.json), overwriting any existing one.
    void Write(string folder, string name, byte[] content);
}

// One Fichier seen through IFileSource. Properties are declared in alphabetical order (CC-4).
public sealed record FichierEntry
{
    public string Folder { get; init; } = string.Empty;

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public long Length { get; init; }

    public string Name { get; init; } = string.Empty;
}
