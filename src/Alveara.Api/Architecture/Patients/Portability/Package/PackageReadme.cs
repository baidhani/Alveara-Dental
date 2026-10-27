namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: the text of <c>README.txt</c> in a package, for a person who finds the ZIP later with no one to ask. It says what the package is, what is in it and what is not, how to check it, how patient folders are
/// named and how the data was read (one consistent snapshot, or table by table). Plain text, no patient detail. The wording follows the choices actually made for this export.
/// </summary>
public static class PackageReadme
{
    public static string Text(Guid exportId, string folderNaming, bool documentFilesIncluded, bool allPatients, string readMode)
    {
        var howRead = readMode == ReadMode.Snapshot
            ? "All tables were read from one database snapshot, so the data is consistent as of one moment."
            : "The tables were read one after another. A change made while the export ran can show in some tables and not in others; manifest.json gives the time the export started.";
        return $"""
        ALVEARA DENTAL - PATIENT DATA PACKAGE
        Export id: {exportId}

        This ZIP file is a copy of {(allPatients ? "the records of every patient" : "a patient's record")} taken out of Alveara Dental. It holds personal health
        information. Keep it where only people allowed to see these records can reach it, and delete it
        when it is no longer needed.

        WHAT IS IN IT
          manifest.json     What the package holds, what it leaves out and why, how many rows of each table,
                            a SHA-256 hash of every file, and anything that could not be included.
                            Read this first. If "result" is "Partial", the "problems" list says what is missing.
          data/<domain>.json  One file per part of the record (registration, appointments, billing, ...).
                            Each holds the rows of its tables exactly as the system stores them.
          {(allPatients ? "readable-records/<patient folder>.html  One page per patient" : "readable-record.html  The patient's record")} as a person reads it: open it in a web browser, or print it.
                            It lists the documents; the data files are the complete machine-readable copy.
          documents/<patient folder>/<document id>/<file>
                            The patients' document files (scans, images, letters), when the export included them.
                            {(documentFilesIncluded ? "This package includes them." : "This package was made WITHOUT the document files: they are listed in manifest.json only.")}
                            Patient folders are named by {(folderNaming == FolderNaming.IdOnly ? "the patient's id" : "the patient's name and id")};
                            manifest.json gives the folder of every patient, so no one has to read it from the name.
          README.txt        This file.

        WHAT IS NOT IN IT
          The manifest lists every part of the system that is deliberately left out (sign-in accounts,
          security settings, the audit log, practice settings, technical bookkeeping) and the reason.

        HOW THE DATA WAS READ
          {howRead}

        CHECKING IT
          Each file's SHA-256 hash is in manifest.json. A file whose hash differs has been changed or damaged.
          For every document, manifest.json gives the hash recorded when it was imported and the hash found
          at export, and a status: Included, HashMismatch (the file differs from what was imported; it is
          included as found, do not rely on it), Missing, Unreadable or NotIncluded.

        This package is in Alveara's own layout (format alveara.patient-data-package, version {DataPackageFormat.CurrentVersion}).
        It cannot yet be imported back into Alveara. A standard-format (FHIR) copy is not included.
        """.Replace("\r\n", "\n");
    }
}
