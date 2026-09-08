namespace Kape22Importer;

// What InboxScanner calls once per Fichier, after the Fichier has been moved to processing/. The real
// implementation is the per-Fichier orchestrator of Story 3.2 (Converter -> Kape22Mapper ->
// persistence); the Story 3.1 tests supply an in-memory fake.
public interface IFichierProcessor
{
    FichierProcessingResult Process(string fichierName, byte[] content);
}
