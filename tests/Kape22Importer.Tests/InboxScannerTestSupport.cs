using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Kape22Importer.Tests;

// In-memory IFileSource for the Story 3.1 tests, the in-memory implementation the PRD calls for. No
// disk: every folder is a relative string key, the inbox root is the empty string, and content plus
// LastWriteTimeUtc are held in a dictionary. Helper methods let the tests seed and inspect state.
internal sealed class InMemoryFileSource : IFileSource
{
    private readonly Dictionary<(string Folder, string Name), Entry> entries = new();

    // Names whose next List("") call reports an inflated Length once, so the scanner's two size
    // probes disagree and the Fichier is treated as still being written (AC-FR12-5).
    private readonly HashSet<string> unstableOnce = new(StringComparer.Ordinal);

    // When set, every List / ListRecursive call throws it, standing in for a reception folder that is
    // unreachable for a tick - an IOException for a dead network share, an UnauthorizedAccessException
    // for a locked-down ACL. Cleared by the test to model the next tick finding it reachable again
    // (AC-FR15-2).
    public Exception? ListingFault { get; set; }

    // When set, every Move call throws it, standing in for processing/ being unreachable while an inbox
    // Fichier is taken in, or archive/ / error/ being unreachable while a Fichier's outcome is filed
    // (AC-FR15-2).
    public Exception? MoveFault { get; set; }

    // When set, every Read call throws it, standing in for a Fichier in processing/ that cannot be read
    // this tick - locked by another process, or removed between the listing and the read (AC-FR15-2).
    public Exception? ReadFault { get; set; }

    // When set, every Write call throws it, standing in for a sidecar (.xml / .errors.json) that cannot
    // be written before its Fichier is filed (AC-FR12-4, AC-FR15-2).
    public Exception? WriteFault { get; set; }

    public void Add(string folder, string name, byte[] content, DateTimeOffset lastWriteUtc) =>
        this.entries[(folder, name)] = new Entry(content, lastWriteUtc);

    public bool Exists(string folder, string name) => this.entries.ContainsKey((folder, name));

    public byte[] Content(string folder, string name) => this.entries[(folder, name)].Content;

    public IReadOnlyList<string> Names(string folder) =>
        this.entries.Keys.Where(key => key.Folder == folder).Select(key => key.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    // Marks a Fichier as still being written: the next List("") call inflates its reported Length once.
    public void MarkUnstableOnce(string name) => this.unstableOnce.Add(name);

    public void Delete(string folder, string name) => this.entries.Remove((folder, name));

    public IReadOnlyList<FichierEntry> List(string folder)
    {
        if (this.ListingFault is not null)
        {
            throw this.ListingFault;
        }

        List<FichierEntry> result = [];
        foreach (((string Folder, string Name) key, Entry entry) in this.entries.Where(pair => pair.Key.Folder == folder))
        {
            long length = entry.Content.LongLength;
            if (folder.Length == 0 && this.unstableOnce.Remove(key.Name))
            {
                length += 1;
            }

            result.Add(new FichierEntry
            {
                Folder = folder,
                LastWriteTimeUtc = entry.LastWriteUtc,
                Length = length,
                Name = key.Name,
            });
        }

        return result;
    }

    public IReadOnlyList<FichierEntry> ListRecursive(string folder)
    {
        if (this.ListingFault is not null)
        {
            throw this.ListingFault;
        }

        string prefix = folder.Length == 0 ? string.Empty : folder + "/";
        return this.entries
            .Where(pair => pair.Key.Folder == folder || pair.Key.Folder.StartsWith(prefix, StringComparison.Ordinal))
            .Select(pair => new FichierEntry
            {
                Folder = pair.Key.Folder,
                LastWriteTimeUtc = pair.Value.LastWriteUtc,
                Length = pair.Value.Content.LongLength,
                Name = pair.Key.Name,
            })
            .ToList();
    }

    public void Move(string sourceFolder, string sourceName, string targetFolder, string targetName)
    {
        if (this.MoveFault is not null)
        {
            throw this.MoveFault;
        }

        Entry entry = this.entries[(sourceFolder, sourceName)];
        this.entries.Remove((sourceFolder, sourceName));
        this.entries[(targetFolder, targetName)] = entry;
    }

    public byte[] Read(string folder, string name)
    {
        if (this.ReadFault is not null)
        {
            throw this.ReadFault;
        }

        return this.entries[(folder, name)].Content;
    }

    public void Write(string folder, string name, byte[] content)
    {
        if (this.WriteFault is not null)
        {
            throw this.WriteFault;
        }

        this.entries[(folder, name)] = new Entry(content, DateTimeOffset.UtcNow);
    }

    private sealed record Entry(byte[] Content, DateTimeOffset LastWriteUtc);
}

// IFichierProcessor fake: returns a scripted FichierProcessingResult per Fichier name and records the
// call order plus the bytes it was handed.
internal sealed class FakeFichierProcessor : IFichierProcessor
{
    private readonly Func<string, byte[], FichierProcessingResult> handler;

    public FakeFichierProcessor(Func<string, byte[], FichierProcessingResult> handler) => this.handler = handler;

    public List<string> Calls { get; } = [];

    public FichierProcessingResult Process(string fichierName, byte[] content)
    {
        this.Calls.Add(fichierName);
        return this.handler(fichierName, content);
    }
}

// Minimal ILogger that keeps every entry with its level and its rendered message, so a test can assert
// nothing was logged at Warning/Error (AC-FR12-5, AC-FR12-7) or inspect the double-logging lines
// (Story 3.3, FR-14). Set Throw to make every Log call fail, so a test can prove the import still
// succeeds when the MQTTnetServices.Logs sink is down (AC-FR14-7).
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public bool Throw { get; init; }

    public IReadOnlyList<(LogLevel Level, string Message)> AtLevel(LogLevel level) =>
        [.. this.Entries.Where(entry => entry.Level == level)];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (this.Throw)
        {
            throw new InvalidOperationException("MQTTnetServices.Logs is unavailable.");
        }

        this.Entries.Add((logLevel, formatter(state, exception)));
    }
}
