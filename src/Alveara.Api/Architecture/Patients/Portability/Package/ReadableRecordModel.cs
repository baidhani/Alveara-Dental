namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>One part of the readable record: a title, the column headings and the rows, already turned into plain text a person reads (dates formatted, names looked up, codes spelled out). A section with no rows is shown as "None recorded".</summary>
public sealed record ReadableSection(string Key, string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, string? Note = null);

/// <summary>A label and its value in the summary at the top of the record.</summary>
public sealed record ReadableFact(string Label, string Value);

/// <summary>
/// ALV-018-C01: one patient's whole record as a person reads it: who the patient is, then the sections of <see cref="ReadableRecordLoader"/>. It holds only text, so how it is shown (HTML today) is decided in one place
/// (<see cref="ReadableRecordHtml"/>) and the loader can be tested without any markup.
/// </summary>
public sealed record ReadableRecord(Guid PatientId, string PatientName, IReadOnlyList<ReadableFact> Summary, IReadOnlyList<ReadableSection> Sections);

/// <summary>What the page says about itself: when it was made (already written in the practice's time zone, which is named) and by which export, and whether the document files travel with it. These words are what stop a readable record from looking more complete than it is.</summary>
public sealed record ReadableRecordContext(Guid ExportId, string GeneratedAt, string TimeZoneId, bool InsidePackage);
