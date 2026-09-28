using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FichierJournal;

namespace P89Converter.Tests;

// Shared scaffolding for the P89Converter.Tests suite: the reference P89 Fichiers and their faulty
// variants, a deterministic clock, a disposable folder set and a recording journal. Import per
// file with `using static P89Converter.Tests.TestSupport;`.
internal static class TestSupport
{
    // The reference Fichier carrying an accented character (UTF-8 "é" = C3 A9).
    public const string AccentedFichierName = "LP89_682_617_013";

    // A fixed instant; the clock's local zone is UTC so the expected suffix does not depend on the host.
    public static readonly DateTimeOffset Instant = new(2026, 2, 10, 8, 0, 0, TimeSpan.Zero);

    public const string InstantSuffix = "20260210080000";

    public static readonly string[] ReferenceFichierNames =
        ["LP89_682_617_001", AccentedFichierName, "LP89_682_617_248"];

    // The first reference Fichier with its header NumeroFichier ("NumeroFichier") or its message OF ("OF")
    // blanked: an int Champ P89.xsd allows to be absent (minOccurs="0"), F-1 of the Story 5.0 review.
    public static byte[] BlankChampFichier(string champ)
    {
        (int ligne, int position, int size) = champ == "OF" ? (1, 26, 7) : (0, 6, 3);
        string[] lines = Utf8("LP89_682_617_001").Split("\r\n");
        lines[ligne] = lines[ligne][..position] + new string(' ', size) + lines[ligne][(position + size)..];
        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
    }

    // The accented reference Fichier with one byte of its message Ligne replaced by 0xFF, never valid UTF-8.
    public static byte[] InvalidUtf8Fichier()
    {
        byte[] bytes = ReadFixture(AccentedFichierName);
        bytes[Array.IndexOf(bytes, (byte)'\n') + 100] = 0xFF;
        return bytes;
    }

    // The accented reference Fichier with "é" swapped for "ő" (U+0151): same UTF-8 byte count and same
    // character count, valid UTF-8, but absent from Windows-1252.
    public static byte[] OutsideWindows1252Fichier() =>
        Encoding.UTF8.GetBytes(Utf8(AccentedFichierName).Replace("é", "ő", StringComparison.Ordinal));

    public static byte[] ReadFixture(string fichierName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", fichierName));

    // The first reference Fichier with its message Ligne ten characters short.
    public static byte[] TruncatedFichier()
    {
        string[] lines = Utf8("LP89_682_617_001").Split("\r\n");
        lines[1] = lines[1][..^10];
        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
    }

    private static string Utf8(string fichierName) => Encoding.UTF8.GetString(ReadFixture(fichierName));
}

// A TimeProvider frozen on one instant, with the given local zone (UTC by default).
internal sealed class FixedClock(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    private readonly TimeZoneInfo localTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
    private readonly DateTimeOffset utcNow = utcNow;

    public override TimeZoneInfo LocalTimeZone => this.localTimeZone;

    public override DateTimeOffset GetUtcNow() => this.utcNow;
}

// A throwaway source / xml / done / error folder set under the temp directory, deleted on dispose.
internal sealed class TempFolders : IDisposable
{
    public TempFolders()
    {
        this.Root = Path.Combine(Path.GetTempPath(), "p89-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.Source);
    }

    public string Done => Path.Combine(this.Root, "Done");

    public string Error => Path.Combine(this.Root, "error");

    public string Root { get; }

    public string Source => Path.Combine(this.Root, "raw");

    public string Xml => Path.Combine(this.Root, "xml");

    public void Dispose() => Directory.Delete(this.Root, recursive: true);

    public void Drop(string fichierName, byte[] content) =>
        File.WriteAllBytes(Path.Combine(this.Source, fichierName), content);

    public string[] Names(string folder) =>
        Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)]
            : [];
}

// An IFichierJournal that keeps every entry it is handed, or throws the given exception instead, the way a
// journal whose write fails does.
internal sealed class RecordingJournal(Exception? failure = null) : IFichierJournal
{
    private readonly Exception? failure = failure;

    public List<FichierJournalEntry> Entries { get; } = [];

    public void Record(FichierJournalEntry entry)
    {
        if (this.failure is not null)
        {
            throw this.failure;
        }

        this.Entries.Add(entry);
    }
}
