using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N025 slice 4 (F-032, F-033; brief section 6, the second of the forward migrations; owner decisions DEM-1..DEM-4): Country, and the normalization of
    /// recognizable Sex and State values.
    /// Up:
    /// 1. adds <c>Patients.Country</c> (char(2), NOT NULL, default 'US': every existing patient is a US patient) and <c>PracticeSettings.DefaultCountry</c> (char(2), null = 'US');
    /// 2. DEM-1: rewrites ONLY exact matches - ignoring letter case, surrounding spaces and runs of inner spaces - of the Sex list and (Country US) of a US state code or full
    ///    name to the canonical value (Sex as listed, State as its code). Each change gets a PatientHistory row with ChangeType 'SystemNormalized' and NO actor
    ///    (ChangedByUserId NULL): an automated action of this migration, never attributed to a staff member. Values that do not match are left exactly as they are and appear
    ///    on the demographic-review list. Merged-away records are not touched (they never change). The lists are a frozen copy (below), so the result never depends on a
    ///    later edit of <c>Demographics</c>; DemographicsMigrationTests holds the copy in step with it;
    /// 3. adds <c>UPDATE([Country])</c> to the patient-merge guard TR_Patients_MergeScope by a text edit of the current trigger (never a re-create).
    /// Down removes the guard term and the two columns. It does NOT undo the normalization: the normalized values are valid either way, and the History rows stay - data
    /// preserving by design (disclosed, DEM-1).
    /// </summary>
    public partial class AddPatientCountry : Migration
    {
        // frozen copy of Demographics.SexValues (S) and of Demographics.UsStates codes and full names (T): (kind, comparison key, canonical value)
        private const string Mapping = @"(N'S', N'FEMALE', N'Female'),
(N'S', N'MALE', N'Male'),
(N'S', N'OTHER', N'Other'),
(N'S', N'UNKNOWN/UNSPECIFIED', N'Unknown/unspecified'),
(N'T', N'AL', N'AL'),
(N'T', N'ALABAMA', N'AL'),
(N'T', N'AK', N'AK'),
(N'T', N'ALASKA', N'AK'),
(N'T', N'AZ', N'AZ'),
(N'T', N'ARIZONA', N'AZ'),
(N'T', N'AR', N'AR'),
(N'T', N'ARKANSAS', N'AR'),
(N'T', N'CA', N'CA'),
(N'T', N'CALIFORNIA', N'CA'),
(N'T', N'CO', N'CO'),
(N'T', N'COLORADO', N'CO'),
(N'T', N'CT', N'CT'),
(N'T', N'CONNECTICUT', N'CT'),
(N'T', N'DE', N'DE'),
(N'T', N'DELAWARE', N'DE'),
(N'T', N'DC', N'DC'),
(N'T', N'DISTRICT OF COLUMBIA', N'DC'),
(N'T', N'FL', N'FL'),
(N'T', N'FLORIDA', N'FL'),
(N'T', N'GA', N'GA'),
(N'T', N'GEORGIA', N'GA'),
(N'T', N'HI', N'HI'),
(N'T', N'HAWAII', N'HI'),
(N'T', N'ID', N'ID'),
(N'T', N'IDAHO', N'ID'),
(N'T', N'IL', N'IL'),
(N'T', N'ILLINOIS', N'IL'),
(N'T', N'IN', N'IN'),
(N'T', N'INDIANA', N'IN'),
(N'T', N'IA', N'IA'),
(N'T', N'IOWA', N'IA'),
(N'T', N'KS', N'KS'),
(N'T', N'KANSAS', N'KS'),
(N'T', N'KY', N'KY'),
(N'T', N'KENTUCKY', N'KY'),
(N'T', N'LA', N'LA'),
(N'T', N'LOUISIANA', N'LA'),
(N'T', N'ME', N'ME'),
(N'T', N'MAINE', N'ME'),
(N'T', N'MD', N'MD'),
(N'T', N'MARYLAND', N'MD'),
(N'T', N'MA', N'MA'),
(N'T', N'MASSACHUSETTS', N'MA'),
(N'T', N'MI', N'MI'),
(N'T', N'MICHIGAN', N'MI'),
(N'T', N'MN', N'MN'),
(N'T', N'MINNESOTA', N'MN'),
(N'T', N'MS', N'MS'),
(N'T', N'MISSISSIPPI', N'MS'),
(N'T', N'MO', N'MO'),
(N'T', N'MISSOURI', N'MO'),
(N'T', N'MT', N'MT'),
(N'T', N'MONTANA', N'MT'),
(N'T', N'NE', N'NE'),
(N'T', N'NEBRASKA', N'NE'),
(N'T', N'NV', N'NV'),
(N'T', N'NEVADA', N'NV'),
(N'T', N'NH', N'NH'),
(N'T', N'NEW HAMPSHIRE', N'NH'),
(N'T', N'NJ', N'NJ'),
(N'T', N'NEW JERSEY', N'NJ'),
(N'T', N'NM', N'NM'),
(N'T', N'NEW MEXICO', N'NM'),
(N'T', N'NY', N'NY'),
(N'T', N'NEW YORK', N'NY'),
(N'T', N'NC', N'NC'),
(N'T', N'NORTH CAROLINA', N'NC'),
(N'T', N'ND', N'ND'),
(N'T', N'NORTH DAKOTA', N'ND'),
(N'T', N'OH', N'OH'),
(N'T', N'OHIO', N'OH'),
(N'T', N'OK', N'OK'),
(N'T', N'OKLAHOMA', N'OK'),
(N'T', N'OR', N'OR'),
(N'T', N'OREGON', N'OR'),
(N'T', N'PA', N'PA'),
(N'T', N'PENNSYLVANIA', N'PA'),
(N'T', N'RI', N'RI'),
(N'T', N'RHODE ISLAND', N'RI'),
(N'T', N'SC', N'SC'),
(N'T', N'SOUTH CAROLINA', N'SC'),
(N'T', N'SD', N'SD'),
(N'T', N'SOUTH DAKOTA', N'SD'),
(N'T', N'TN', N'TN'),
(N'T', N'TENNESSEE', N'TN'),
(N'T', N'TX', N'TX'),
(N'T', N'TEXAS', N'TX'),
(N'T', N'UT', N'UT'),
(N'T', N'UTAH', N'UT'),
(N'T', N'VT', N'VT'),
(N'T', N'VERMONT', N'VT'),
(N'T', N'VA', N'VA'),
(N'T', N'VIRGINIA', N'VA'),
(N'T', N'WA', N'WA'),
(N'T', N'WASHINGTON', N'WA'),
(N'T', N'WV', N'WV'),
(N'T', N'WEST VIRGINIA', N'WV'),
(N'T', N'WI', N'WI'),
(N'T', N'WISCONSIN', N'WI'),
(N'T', N'WY', N'WY'),
(N'T', N'WYOMING', N'WY'),
(N'T', N'PR', N'PR'),
(N'T', N'PUERTO RICO', N'PR'),
(N'T', N'GU', N'GU'),
(N'T', N'GUAM', N'GU'),
(N'T', N'VI', N'VI'),
(N'T', N'U.S. VIRGIN ISLANDS', N'VI'),
(N'T', N'AS', N'AS'),
(N'T', N'AMERICAN SAMOA', N'AS'),
(N'T', N'MP', N'MP'),
(N'T', N'NORTHERN MARIANA ISLANDS', N'MP'),
(N'T', N'AA', N'AA'),
(N'T', N'ARMED FORCES AMERICAS', N'AA'),
(N'T', N'AE', N'AE'),
(N'T', N'ARMED FORCES EUROPE', N'AE'),
(N'T', N'AP', N'AP'),
(N'T', N'ARMED FORCES PACIFIC', N'AP')";

        /// <summary>The comparison key of a stored value in SQL, the same as Demographics.Key: trimmed, runs of spaces as one, upper case.</summary>
        private static string KeyOf(string column) => $"UPPER(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM({column})), N' ', N'<>'), N'><', N''), N'<>', N' '))";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "DefaultCountry", table: "PracticeSettings", type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: true);

            migrationBuilder.AddColumn<string>(name: "Country", table: "Patients", type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false, defaultValue: "US");

            migrationBuilder.Sql($@"CREATE TABLE #DemographicMap ([Kind] nchar(1) NOT NULL, [KeyValue] nvarchar(100) NOT NULL, [Canonical] nvarchar(30) NOT NULL);
INSERT INTO #DemographicMap ([Kind], [KeyValue], [Canonical]) VALUES
{Mapping};
DECLARE @now datetimeoffset = SYSDATETIMEOFFSET();
-- a value holding '<' or '>' could be bent by the space-collapsing key, so it is never normalized (it goes to the review list)
SELECT p.[Id], CAST(N'sex' AS nvarchar(20)) AS [FieldName], CAST(p.[Sex] AS nvarchar(50)) AS [OldValue], CAST(m.[Canonical] AS nvarchar(30)) AS [NewValue] INTO #DemographicChanges
FROM [Patients] p JOIN #DemographicMap m ON m.[Kind] = N'S' AND m.[KeyValue] COLLATE Latin1_General_BIN2 = {KeyOf("p.[Sex]")} COLLATE Latin1_General_BIN2
WHERE p.[MergedAtUtc] IS NULL AND p.[Sex] IS NOT NULL AND CHARINDEX(N'<', p.[Sex]) = 0 AND CHARINDEX(N'>', p.[Sex]) = 0
  AND p.[Sex] COLLATE Latin1_General_BIN2 <> m.[Canonical] COLLATE Latin1_General_BIN2;
INSERT INTO #DemographicChanges ([Id], [FieldName], [OldValue], [NewValue])
SELECT p.[Id], N'state', p.[State], m.[Canonical]
FROM [Patients] p JOIN #DemographicMap m ON m.[Kind] = N'T' AND m.[KeyValue] COLLATE Latin1_General_BIN2 = {KeyOf("p.[State]")} COLLATE Latin1_General_BIN2
WHERE p.[MergedAtUtc] IS NULL AND p.[Country] = 'US' AND CHARINDEX(N'<', p.[State]) = 0 AND CHARINDEX(N'>', p.[State]) = 0
  AND p.[State] COLLATE Latin1_General_BIN2 <> m.[Canonical] COLLATE Latin1_General_BIN2;
INSERT INTO [PatientHistory] ([Id], [PatientId], [ChangedAtUtc], [ChangedByUserId], [ChangeType], [FieldName], [OldValue], [NewValue])
SELECT NEWID(), c.[Id], @now, NULL, N'SystemNormalized', c.[FieldName], c.[OldValue], c.[NewValue] FROM #DemographicChanges c;
UPDATE p SET p.[Sex] = c.[NewValue] FROM [Patients] p JOIN #DemographicChanges c ON c.[Id] = p.[Id] AND c.[FieldName] = N'sex';
UPDATE p SET p.[State] = c.[NewValue] FROM [Patients] p JOIN #DemographicChanges c ON c.[Id] = p.[Id] AND c.[FieldName] = N'state';
DROP TABLE #DemographicChanges;
DROP TABLE #DemographicMap;");

            migrationBuilder.Sql(@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[TR_Patients_MergeScope]'));
IF @def IS NULL THROW 50000, 'TR_Patients_MergeScope is missing; the Country cannot be added to it.', 1;
IF CHARINDEX(N'UPDATE([Country])', @def) = 0
BEGIN
    IF CHARINDEX(N'UPDATE([', @def) = 0 OR NOT (LEFT(@def, 14) = N'CREATE TRIGGER' OR LEFT(@def, 13) = N'ALTER TRIGGER')
        THROW 50000, 'TR_Patients_MergeScope has a shape this migration does not know how to extend.', 1;
    SET @def = STUFF(@def, CHARINDEX(N'UPDATE([', @def), 0, N'UPDATE([Country]) OR ');
    IF LEFT(@def, 6) = N'CREATE' SET @def = STUFF(@def, 1, 6, N'ALTER');
    EXEC (@def);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[TR_Patients_MergeScope]'));
IF @def IS NOT NULL AND CHARINDEX(N'UPDATE([Country]) OR ', @def) > 0
BEGIN
    SET @def = REPLACE(@def, N'UPDATE([Country]) OR ', N'');
    IF LEFT(@def, 6) = N'CREATE' SET @def = STUFF(@def, 1, 6, N'ALTER');
    EXEC (@def);
END");

            migrationBuilder.DropColumn(name: "DefaultCountry", table: "PracticeSettings");

            migrationBuilder.DropColumn(name: "Country", table: "Patients");
        }
    }
}
