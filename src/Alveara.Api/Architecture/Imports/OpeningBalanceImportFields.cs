namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 5a: the fields of an opening-balance file. The patient is named by first name, last name and date of birth (matched exactly to one registered patient, so patients are imported first); the amount is what the
/// patient owes the practice at the move, in dollars and cents; the "as of" date (when the old system's balance was struck) and the currency are optional. The currency, when given, must be the practice's own (USD).
/// The keys are internal names: they appear in the mapping and in a rejected row's messages.
/// </summary>
public static class OpeningBalanceImportFields
{
    private static readonly (string Key, string Label, bool Required, string[] Names)[] Catalog =
    [
        ("patientFirstName", "Patient first name", true, ["patientfirstname", "firstname", "first", "patientfirst", "givenname"]),
        ("patientLastName", "Patient last name", true, ["patientlastname", "lastname", "last", "patientlast", "surname", "familyname"]),
        ("patientDateOfBirth", "Patient date of birth (yyyy-MM-dd)", true, ["patientdateofbirth", "dateofbirth", "dob", "patientdob", "birthdate", "birthday"]),
        ("amount", "Amount owed (dollars and cents)", true, ["amount", "amountowed", "balance", "openingbalance", "balanceowed", "owed", "totalowed"]),
        ("asOfDate", "Balance as of (yyyy-MM-dd)", false, ["asof", "asofdate", "balancedate", "balanceasof", "statementdate"]),
        ("currency", "Currency", false, ["currency", "currencycode"]),
    ];

    public static IReadOnlyList<ImportField> All { get; } = Catalog.Select(f => new ImportField(f.Key, f.Label, f.Required)).ToList();

    public static IReadOnlyDictionary<string, string> Suggest(IReadOnlyList<string> columns) => ImportFieldMatching.Suggest(Catalog.Select(f => (f.Key, f.Names)), columns);

    public static IReadOnlyList<ImportProblem> CheckMapping(IReadOnlyDictionary<string, string?> mapping, IReadOnlyList<string> columns) => ImportFieldMatching.Check(All, mapping, columns);
}
