using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: writes the rows of one table as a JSON array of objects, each column under its database name with its stored value: text as text, numbers as numbers, GUIDs and dates as text in a fixed form (dates as
/// ISO 8601, a date without a time as yyyy-MM-dd), binary as base64, and SQL NULL as JSON null. It reads with a forward-only reader, so a large table is never held in memory, in a fixed order so the same data gives the
/// same file, and every read has an explicit time limit. The table, columns and condition come from the database model (see <see cref="ExportTableFilters"/>), never from user input.
/// </summary>
public static class ExportRowWriter
{
    public static async Task<int> WriteTableAsync(DbConnection connection, Utf8JsonWriter json, string table, string where, IReadOnlyList<string> orderBy, int timeoutSeconds, DbTransaction? transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;                                                                 // the export's snapshot transaction, when it has one
        command.CommandText = $"SELECT * FROM [{table}] WHERE {where}" + (orderBy.Count == 0 ? "" : " ORDER BY " + string.Join(", ", orderBy.Select(c => $"[{c}]")));
        command.CommandTimeout = timeoutSeconds;
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = 0;
        json.WriteStartArray();
        while (await reader.ReadAsync(ct))
        {
            json.WriteStartObject();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                json.WritePropertyName(reader.GetName(i));
                WriteValue(json, reader, i);
            }
            json.WriteEndObject();
            rows++;
        }
        json.WriteEndArray();
        return rows;
    }

    private static void WriteValue(Utf8JsonWriter json, DbDataReader reader, int i)
    {
        if (reader.IsDBNull(i)) { json.WriteNullValue(); return; }
        switch (reader.GetValue(i))
        {
            case string s: json.WriteStringValue(s); break;
            case bool b: json.WriteBooleanValue(b); break;
            case byte n: json.WriteNumberValue(n); break;
            case short n: json.WriteNumberValue(n); break;
            case int n: json.WriteNumberValue(n); break;
            case long n: json.WriteNumberValue(n); break;
            case decimal n: json.WriteNumberValue(n); break;
            case double n when double.IsFinite(n): json.WriteNumberValue(n); break;
            case float n when float.IsFinite(n): json.WriteNumberValue(n); break;
            case Guid g: json.WriteStringValue(g); break;
            case DateTimeOffset t: json.WriteStringValue(t.ToString("O", CultureInfo.InvariantCulture)); break;
            case DateTime t when reader.GetDataTypeName(i) == "date": json.WriteStringValue(t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); break;
            case DateTime t: json.WriteStringValue(t.ToString("O", CultureInfo.InvariantCulture)); break;
            case TimeSpan t: json.WriteStringValue(t.ToString("c", CultureInfo.InvariantCulture)); break;
            case byte[] bytes: json.WriteBase64StringValue(bytes); break;
            default: json.WriteStringValue(Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)); break;
        }
    }
}
