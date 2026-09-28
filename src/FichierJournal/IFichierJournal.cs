namespace FichierJournal;

// The journal of a Fichier → XML import (D31, FR-23): the result of processing one Fichier, recorded for
// the target application. A format only knows this contract; each application brings its own
// implementation (AscoLsiJournal for LSI). Record writes on its own, outside any business transaction,
// and throws when the write fails, so the caller decides what happens to the Fichier.
public interface IFichierJournal
{
    // Whether the journal already holds the success entry that Record would write for this entry's
    // Fichier (its Reasons are ignored). Lets a caller complete a success whose journal write was lost
    // (D22, FR-24). Throws when the journal cannot be read.
    bool HasSuccess(FichierJournalEntry entry);

    void Record(FichierJournalEntry entry);
}
