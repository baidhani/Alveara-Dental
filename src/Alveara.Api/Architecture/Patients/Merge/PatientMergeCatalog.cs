using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>A foreign key from one patient-owned table to the primary key of another: a row of <see cref="Child"/> that points at a row of <see cref="Parent"/> must belong to the same patient.</summary>
internal sealed record MergeEdge(string Child, IReadOnlyList<string> ChildColumns, string Parent, IReadOnlyList<string> ParentColumns);

/// <summary>A uniqueness rule that includes the patient id, as the database enforces it (including its filter), reduced to the columns other than the patient id.</summary>
internal sealed record MergeUnique(string Table, string Name, IReadOnlyList<string> OtherColumns, string? Filter);

/// <summary>
/// STORY-017: what the merge needs to know about the database's shape, read from the model and never typed in: how to name a row (its primary key as text), which uniqueness rules a moved row could break, and which tables point at which.
/// Identifiers come from the model, never from a request. <see cref="PatientOwnedData"/> says which columns hold a patient id; its tests keep it equal to the model.
/// </summary>
internal sealed class PatientMergeCatalog
{
    private readonly Dictionary<string, IEntityType> _entities = new(StringComparer.Ordinal);

    public IReadOnlyList<MergeUnique> Uniques { get; }
    public IReadOnlyList<MergeEdge> Edges { get; }

    public PatientMergeCatalog(IModel model)
    {
        var tables = PatientOwnedData.Tables.ToHashSet(StringComparer.Ordinal);
        foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is { } t && tables.Contains(t)))
            _entities.TryAdd(entity.GetTableName()!, entity);

        var uniques = new List<MergeUnique>(); var edges = new List<MergeEdge>();
        foreach (var (table, entity) in _entities)
        {
            if (!HasOwner(table)) continue;
            foreach (var index in entity.GetIndexes().Where(i => i.IsUnique && i.Properties.Any(p => p.Name == "PatientId")))
                uniques.Add(new MergeUnique(table, index.Name ?? "", index.Properties.Where(p => p.Name != "PatientId").Select(p => Column(entity, p)).ToList(), index.GetFilter()));
            var key = entity.FindPrimaryKey();
            if (key is not null && key.Properties.Any(p => p.Name == "PatientId"))
                uniques.Add(new MergeUnique(table, "PRIMARY KEY", key.Properties.Where(p => p.Name != "PatientId").Select(p => Column(entity, p)).ToList(), null));
            foreach (var fk in entity.GetForeignKeys())
            {
                var parent = fk.PrincipalEntityType.GetTableName();
                if (parent is null || parent == "Patients" || !tables.Contains(parent) || !HasOwner(parent)) continue;
                edges.Add(new MergeEdge(table, fk.Properties.Select(p => Column(entity, p)).ToList(), parent, fk.PrincipalKey.Properties.Select(p => Column(fk.PrincipalEntityType, p)).ToList()));
            }
        }
        Uniques = uniques; Edges = edges;
    }

    private static string Column(IEntityType entity, IProperty property) => property.GetColumnName(StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value)!;

    public bool HasOwner(string table) => PatientOwnedData.All.Any(c => c.Table == table && c.Column == "PatientId" && c.Role == PatientDataRole.Owner);

    public static string Q(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    /// <summary>The primary key of a row as text, the same expression everywhere so a key read from one query matches one computed in another.</summary>
    public string KeyExpr(string table, string alias)
    {
        var entity = _entities[table];
        var key = entity.FindPrimaryKey() ?? throw new InvalidOperationException($"{table} has no primary key.");
        return Concat(alias, key.Properties.Select(p => Column(entity, p)).ToList());
    }

    /// <summary>A (possibly composite) column list as text; null when any part is null, so a missing reference is never mistaken for a key.</summary>
    public static string Concat(string alias, IReadOnlyList<string> columns)
    {
        var parts = string.Join(", ", columns.Select(c => $"CONVERT(nvarchar(100), {alias}.{Q(c)})"));
        var anyNull = string.Join(" OR ", columns.Select(c => $"{alias}.{Q(c)} IS NULL"));
        return columns.Count == 1 ? $"CONVERT(nvarchar(100), {alias}.{Q(columns[0])})" : $"CASE WHEN {anyNull} THEN NULL ELSE CONCAT_WS(N'|', {parts}) END";
    }

    // ---------- running statements on the context's connection (and its transaction, when it has one) ----------

    private const int CommandSeconds = 60;

    public static async Task<List<object?[]>> RowsAsync(AlveraDbContext db, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql; command.CommandTimeout = CommandSeconds; command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var (name, value) in parameters) command.Parameters.Add(new SqlParameter(name, value));
            var rows = new List<object?[]>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++) row[i] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    public static async Task<int> CountAsync(AlveraDbContext db, string sql, CancellationToken ct, params (string Name, object Value)[] parameters) =>
        Convert.ToInt32((await RowsAsync(db, sql, ct, parameters))[0][0]);
}
