using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kape22Importer;

// The production IFileSource: a thin adapter over System.IO rooted at Import:InboxPath. Folders are
// relative paths under that root, "/" separated whatever the platform; the inbox root is the empty
// string. Missing folders read as empty rather than throwing, so a first tick before any Fichier has
// arrived is a no-op (AC-FR12-7).
public sealed class DirectoryFileSource(string rootPath) : IFileSource
{
    public void Delete(string folder, string name)
    {
        string path = Resolve(folder, name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public IReadOnlyList<FichierEntry> List(string folder)
    {
        string directory = ResolveFolder(folder);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory).Select(path => ToEntry(folder, path)).ToList()
            : [];
    }

    public IReadOnlyList<FichierEntry> ListRecursive(string folder)
    {
        string directory = ResolveFolder(folder);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => ToEntry(RelativeFolder(path), path)).ToList()
            : [];
    }

    public void Move(string sourceFolder, string sourceName, string targetFolder, string targetName)
    {
        string target = Resolve(targetFolder, targetName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(Resolve(sourceFolder, sourceName), target, overwrite: true);
    }

    public byte[] Read(string folder, string name) => File.ReadAllBytes(Resolve(folder, name));

    public void Write(string folder, string name, byte[] content)
    {
        string path = Resolve(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private string Resolve(string folder, string name) => Path.Combine(ResolveFolder(folder), name);

    private string ResolveFolder(string folder) =>
        folder.Length == 0 ? rootPath : Path.Combine(rootPath, folder.Replace('/', Path.DirectorySeparatorChar));

    // The "/" separated folder of a file found by ListRecursive, relative to the root ("" for the root
    // itself), so the caller can address it back through Delete.
    private string RelativeFolder(string filePath)
    {
        string relative = Path.GetRelativePath(rootPath, Path.GetDirectoryName(filePath)!);
        return relative == "." ? string.Empty : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static FichierEntry ToEntry(string folder, string filePath)
    {
        FileInfo info = new(filePath);
        return new FichierEntry
        {
            Folder = folder,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
            Length = info.Length,
            Name = info.Name,
        };
    }
}
