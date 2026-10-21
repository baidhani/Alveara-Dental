using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Storage;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>Shared by the document service tests: a real SQL Server database, a real on-disk store that counts what it is asked to do and can be made to fail, and a way to import a file.</summary>
public abstract class DocumentTestBase : SafetyTestBase
{
    protected string _storageRoot = string.Empty;
    protected RecordingBlobStorage _blobs = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _storageRoot = Path.Combine(AlveraTestEnvironment.StorageFolder, $"documents-{Guid.NewGuid():N}");
        _blobs = new RecordingBlobStorage(new LocalDiskBlobStorage(_storageRoot));
    }

    /// <summary>The real store, counting what it was asked to do and able to fail on request.</summary>
    protected sealed class RecordingBlobStorage(IBlobStorage inner) : IBlobStorage
    {
        public int Stored; public int Deleted; public bool FailStore; public bool FailDelete;
        private TaskCompletionSource? _gate;
        private int _arrived;
        private int _gateSize;
        private TimeSpan _gateTimeout;

        /// <summary>Holds the first callers that look at a file until <paramref name="callers"/> have arrived (or the timeout passes), then lets everyone through, so that simultaneous callers really overlap.</summary>
        public void ArmInfoGate(int callers, TimeSpan timeout) { _arrived = 0; (_gateSize, _gateTimeout) = (callers, timeout); _gate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public async Task<StoredBlob> StoreAsync(Stream content, CancellationToken cancellationToken = default)
        {
            if (FailStore) throw new IOException("disk full");
            var blob = await inner.StoreAsync(content, cancellationToken);
            Interlocked.Increment(ref Stored);
            return blob;
        }
        public Task<Stream> OpenReadAsync(Guid blobId, CancellationToken cancellationToken = default) => inner.OpenReadAsync(blobId, cancellationToken);
        public Task<bool> VerifyIntegrityAsync(Guid blobId, string expectedSha256, CancellationToken cancellationToken = default) => inner.VerifyIntegrityAsync(blobId, expectedSha256, cancellationToken);
        public async Task<BlobInfo?> GetInfoAsync(Guid blobId, CancellationToken cancellationToken = default)
        {
            if (_gate is { } gate)
            {
                if (Interlocked.Increment(ref _arrived) >= _gateSize) gate.TrySetResult();
                else await Task.WhenAny(gate.Task, Task.Delay(_gateTimeout, cancellationToken));
            }
            return await inner.GetInfoAsync(blobId, cancellationToken);
        }
        public IAsyncEnumerable<BlobInfo> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task DeleteAsync(Guid blobId, CancellationToken cancellationToken = default)
        {
            if (FailDelete) throw new IOException("locked");
            Interlocked.Increment(ref Deleted);
            return inner.DeleteAsync(blobId, cancellationToken);
        }
    }

    protected int FilesOnDisk() => Directory.Exists(_storageRoot) ? Directory.GetFiles(_storageRoot).Length : 0;

    // a real PNG header followed by filler, so that content detection recognizes it
    protected static byte[] Png(int extra = 100, byte fill = 7) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, extra)];
    protected static byte[] Pdf(int extra = 100) => [.. "%PDF-1.7\n"u8.ToArray(), .. Enumerable.Repeat((byte)'x', extra)];
    protected static byte[] Program() { var b = new byte[0x100]; b[0] = (byte)'M'; b[1] = (byte)'Z'; b[0x3C] = 0x80; b[0x80] = (byte)'P'; b[0x81] = (byte)'E'; return b; }

    protected DocumentImportRequest Request(byte[]? bytes = null, string name = "bitewing.png", Guid? patient = null, Guid? category = null, DateOnly? date = null, string? description = "Bitewing, left side.",
        string? key = null, Guid? confirm = null, long? length = null)
    {
        bytes ??= Png();
        return new DocumentImportRequest(patient ?? Ann, new MemoryStream(bytes), length ?? bytes.Length, name, category ?? DocumentCategorySeeds.Radiograph, date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            description, key ?? $"imp-{Guid.NewGuid():N}", confirm);
    }

    protected Task<DocumentImportResult> ImportAsync(DocumentImportRequest request, Guid? actor = null) =>
        WithDb(db => new DocumentImportService(db, _blobs, Clock).ImportAsync(request, actor ?? S.Actor, default));

    protected static async Task<DocumentException> Refused(Func<Task<DocumentImportResult>> action) => await Assert.ThrowsAsync<DocumentException>(action);

    protected async Task<(int Documents, int Events, int Audits)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Documents.CountAsync(), await db.DocumentEvents.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("Document")));
    }

    protected async Task AssertNothingRecordedAsync() { Assert.Equal((0, 0, 0), await CountsAsync()); Assert.Equal(0, FilesOnDisk()); }

    protected Task FailAuditAsync(string eventType) => WithDb(async db => { await db.Database.ExecuteSqlRawAsync($@"CREATE TRIGGER [TR_Test_RefuseAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{eventType}') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END"); return 0; });
}
