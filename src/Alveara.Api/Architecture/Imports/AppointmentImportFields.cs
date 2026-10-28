using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 4a: the fields of an appointment file. The patient is named by first name, last name and date of birth (matched exactly to one registered patient, so patients are imported first); the provider, the
/// operatory and the appointment type are named as they are in the practice's configuration; the start is a practice-local time; the duration is optional (the appointment type's own duration otherwise); notes are optional.
/// The keys are internal names (not the front desk's patient field names): they appear in the mapping and in a rejected row's messages.
/// </summary>
public static class AppointmentImportFields
{
    private static readonly (string Key, string Label, bool Required, string[] Names)[] Catalog =
    [
        ("patientFirstName", "Patient first name", true, ["patientfirstname", "firstname", "first", "patientfirst", "givenname"]),
        ("patientLastName", "Patient last name", true, ["patientlastname", "lastname", "last", "patientlast", "surname", "familyname"]),
        ("patientDateOfBirth", "Patient date of birth (yyyy-MM-dd)", true, ["patientdateofbirth", "dateofbirth", "dob", "patientdob", "birthdate", "birthday"]),
        ("provider", "Provider", true, ["provider", "providername", "dentist", "doctor", "clinician"]),
        ("operatory", "Operatory", true, ["operatory", "operatoryname", "room", "chair", "op"]),
        ("appointmentType", "Appointment type", true, ["appointmenttype", "type", "visittype", "appointmentreason"]),
        ("start", "Start (yyyy-MM-dd HH:mm, practice time)", true, ["start", "starttime", "startdatetime", "appointmentstart", "datetime", "appointmentdatetime"]),
        ("durationMinutes", "Duration in minutes", false, ["duration", "durationminutes", "minutes", "length", "lengthminutes"]),
        ("notes", "Notes", false, ["notes", "note", "comments", "comment"]),
    ];

    public static IReadOnlyList<ImportField> All { get; } = Catalog.Select(f => new ImportField(f.Key, f.Label, f.Required)).ToList();

    public static IReadOnlyDictionary<string, string> Suggest(IReadOnlyList<string> columns) => ImportFieldMatching.Suggest(Catalog.Select(f => (f.Key, f.Names)), columns);

    public static IReadOnlyList<ImportProblem> CheckMapping(IReadOnlyDictionary<string, string?> mapping, IReadOnlyList<string> columns) => ImportFieldMatching.Check(All, mapping, columns);
}

/// <summary>The fields of each kind of file and how a mapping is checked, so the mapping step works for every kind through one door.</summary>
public static class ImportFields
{
    public static IReadOnlyList<ImportField> For(string kind, PatientRequirements requirements) =>
        kind == ImportSourceKind.Appointments ? AppointmentImportFields.All : PatientImportFields.For(requirements);

    public static IReadOnlyDictionary<string, string> Suggest(string kind, IReadOnlyList<string> columns) =>
        kind == ImportSourceKind.Appointments ? AppointmentImportFields.Suggest(columns) : PatientImportFields.Suggest(columns);

    public static IReadOnlyList<ImportProblem> CheckMapping(string kind, IReadOnlyDictionary<string, string?> mapping, IReadOnlyList<string> columns, PatientRequirements requirements) =>
        kind == ImportSourceKind.Appointments ? AppointmentImportFields.CheckMapping(mapping, columns) : PatientImportFields.CheckMapping(mapping, columns, requirements);
}
