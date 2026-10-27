using Alveara.Api.Architecture.Storage;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02: where uploaded import files are kept. It is a folder of its own inside the storage root (<c>imports</c>), not the document store: the document integrity scan lists the store's own files, treats any file
/// no document refers to as an orphan, and an administrator can remove orphans, which would delete a file whose batch has not been committed yet. The scan does not look inside a sub-folder. The folder is created on the
/// first import, not at start-up, so a practice that never imports has nothing extra on disk. The files hold patient data: they are never listed, served or copied anywhere by this module.
/// </summary>
public sealed class ImportFileStorage(string root)
{
    private readonly Lazy<IBlobStorage> _blobs = new(() => new LocalDiskBlobStorage(root));

    public IBlobStorage Blobs => _blobs.Value;
}
