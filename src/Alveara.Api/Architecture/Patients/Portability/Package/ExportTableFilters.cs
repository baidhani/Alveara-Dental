using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: decides, for every table the package carries, which of its rows belong to the patients in scope, as a SQL condition worked out from the database model, so a new table needs no hand-written query.
/// The rule, in order: the patient table by its own id; a table that holds the patient's id in a column the merge registry (<see cref="PatientOwnedData"/>) calls owner data, by that column; a table that only points at a
/// patient (a message or notification about one) by that pointer; a child, version or event table that has no patient id of its own, through the foreign key to its parent (and its parent's rule, however many steps);
/// and a table the patient's rows refer to (a household, an operatory, a procedure definition), through the foreign keys that point at it. The condition names tables and columns from the model only; the patients in scope
/// arrive as <c>patientIdsSql</c>, which the caller builds from GUIDs and constants, never from text a user typed.
/// </summary>
public sealed class ExportTableFilters
{
    private readonly Dictionary<string, IEntityType> _entities;
    private readonly HashSet<string> _carried;
    private readonly HashSet<string> _lookups;
    private readonly HashSet<string> _related;
    private readonly string _patientIdsSql;
    private readonly Dictionary<string, string?> _memo = new(StringComparer.Ordinal);

    public ExportTableFilters(IModel model, string patientIdsSql)
    {
        _entities = model.GetEntityTypes().Where(e => e.GetTableName() is not null).GroupBy(e => e.GetTableName()!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _carried = ExportDomainCatalog.IncludedTables.ToHashSet(StringComparer.Ordinal);
        _lookups = ExportDomainCatalog.LookupTables.ToHashSet(StringComparer.Ordinal);
        _related = _carried.Union(_lookups).ToHashSet(StringComparer.Ordinal);                         // every table whose rows can lead to another table's rows
        _patientIdsSql = patientIdsSql;
    }

    /// <summary>The condition selecting the in-scope rows of <paramref name="table"/>, or null when no rule reaches it (the caller reports the domain as unavailable rather than guessing).</summary>
    public string? WhereFor(string table) => Resolve(table, []);

    /// <summary>The columns to order a table's rows by, so the same data always gives the same file.</summary>
    public IReadOnlyList<string> OrderBy(string table) =>
        _entities[table].FindPrimaryKey()?.Properties.Select(p => p.GetColumnName()).ToList() ?? [];

    private string? Resolve(string table, HashSet<string> visiting)
    {
        if (_memo.TryGetValue(table, out var known)) return known;
        if (!_entities.TryGetValue(table, out var entity) || !visiting.Add(table)) return null;
        // A lookup table (rows the patient's rows refer to) is selected by what refers to it first; a table of the patient's own record by its parent first.
        string? result = Direct(table) ?? (_lookups.Contains(table) ? ReferencedBy(entity, visiting) ?? Through(entity, visiting) : Through(entity, visiting) ?? ReferencedBy(entity, visiting));
        visiting.Remove(table);
        if (visiting.Count == 0 || result is not null) _memo[table] = result;
        return result;
    }

    private string? Direct(string table)
    {
        if (table == "Patients") return $"[Id] IN ({_patientIdsSql})";
        var owner = PatientOwnedData.Owned.Where(c => c.Table == table).Select(c => c.Column).ToList();
        var columns = owner.Count > 0 ? owner : PatientOwnedData.References.Where(c => c.Table == table).Select(c => c.Column).ToList();
        return columns.Count == 0 ? null : string.Join(" OR ", columns.Select(c => $"[{c}] IN ({_patientIdsSql})"));
    }

    /// <summary>A child: the rows whose parent (a table of the record or a lookup table) is in scope.</summary>
    private string? Through(IEntityType entity, HashSet<string> visiting)
    {
        var parts = new List<string>();
        foreach (var fk in entity.GetForeignKeys().Where(f => f.Properties.Count == 1 && f.PrincipalKey.Properties.Count == 1))
        {
            var parent = fk.PrincipalEntityType.GetTableName();
            if (parent is null || parent == entity.GetTableName() || !_related.Contains(parent)) continue;
            var parentWhere = Resolve(parent, visiting);
            if (parentWhere is null) continue;
            parts.Add($"[{fk.Properties[0].GetColumnName()}] IN (SELECT [{fk.PrincipalKey.Properties[0].GetColumnName()}] FROM [{parent}] WHERE {parentWhere})");
        }
        return parts.Count == 0 ? null : string.Join(" OR ", parts);
    }

    /// <summary>A table the carried rows refer to: the rows some in-scope row points at, directly or through another lookup table.</summary>
    private string? ReferencedBy(IEntityType entity, HashSet<string> visiting)
    {
        var table = entity.GetTableName()!;
        var parts = new List<string>();
        foreach (var dependent in _entities.Values.Where(e => _related.Contains(e.GetTableName()!) && e.GetTableName() != table))
        {
            foreach (var fk in dependent.GetForeignKeys().Where(f => f.Properties.Count == 1 && f.PrincipalKey.Properties.Count == 1 && f.PrincipalEntityType.GetTableName() == table))
            {
                var dependentWhere = Resolve(dependent.GetTableName()!, visiting);
                if (dependentWhere is null) continue;
                parts.Add($"[{fk.PrincipalKey.Properties[0].GetColumnName()}] IN (SELECT [{fk.Properties[0].GetColumnName()}] FROM [{dependent.GetTableName()}] WHERE {dependentWhere})");
            }
        }
        return parts.Count == 0 ? null : string.Join(" OR ", parts);
    }
}
