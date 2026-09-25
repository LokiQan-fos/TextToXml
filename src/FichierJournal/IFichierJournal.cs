namespace FichierJournal;

// The journal of a Fichier → XML import (D31, FR-23): the result of processing one Fichier, recorded for
// the target application. A format only knows this contract; each application brings its own
// implementation (AscoLsiJournal for LSI). Record writes on its own, outside any business transaction,
// and throws when the write fails, so the caller decides what happens to the Fichier.
public interface IFichierJournal
{
    void Record(FichierJournalEntry entry);
}
