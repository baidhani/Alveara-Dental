namespace Alveara.Api.Architecture.Patients;

/// <summary>A code and the name shown for it.</summary>
public sealed record CodeName(string Code, string Name);

/// <summary>
/// ALV-N025 (F-032, F-033; owner decisions DEM-1..DEM-4): the one definition of the demographic value lists, used by validation, the migration's normalization rules
/// (mirrored there in SQL, and a test holds the two in step), the review list and the client's selects.
/// - Sex: Female, Male, Other, Unknown/unspecified - stored exactly as shown.
/// - Country: ISO 3166-1 alpha-2, stored upper case; the practice's default when none is given, else "US".
/// - State: for the US, the 2-letter postal code of a state, DC, a territory or an armed-forces code; elsewhere free text ("State / Province / Region", up to 50).
/// Normalization is deliberately narrow (DEM-1): an exact match ignoring case and surrounding/inner extra spaces of a list value, or for US states its full name.
/// </summary>
public static class Demographics
{
    public const string UnitedStates = "US";

    public static readonly IReadOnlyList<string> SexValues = ["Female", "Male", "Other", "Unknown/unspecified"];

    public static readonly IReadOnlyList<CodeName> UsStates =
    [
        new("AL", "Alabama"), new("AK", "Alaska"), new("AZ", "Arizona"), new("AR", "Arkansas"), new("CA", "California"), new("CO", "Colorado"), new("CT", "Connecticut"),
        new("DE", "Delaware"), new("DC", "District of Columbia"), new("FL", "Florida"), new("GA", "Georgia"), new("HI", "Hawaii"), new("ID", "Idaho"), new("IL", "Illinois"),
        new("IN", "Indiana"), new("IA", "Iowa"), new("KS", "Kansas"), new("KY", "Kentucky"), new("LA", "Louisiana"), new("ME", "Maine"), new("MD", "Maryland"),
        new("MA", "Massachusetts"), new("MI", "Michigan"), new("MN", "Minnesota"), new("MS", "Mississippi"), new("MO", "Missouri"), new("MT", "Montana"), new("NE", "Nebraska"),
        new("NV", "Nevada"), new("NH", "New Hampshire"), new("NJ", "New Jersey"), new("NM", "New Mexico"), new("NY", "New York"), new("NC", "North Carolina"),
        new("ND", "North Dakota"), new("OH", "Ohio"), new("OK", "Oklahoma"), new("OR", "Oregon"), new("PA", "Pennsylvania"), new("RI", "Rhode Island"),
        new("SC", "South Carolina"), new("SD", "South Dakota"), new("TN", "Tennessee"), new("TX", "Texas"), new("UT", "Utah"), new("VT", "Vermont"), new("VA", "Virginia"),
        new("WA", "Washington"), new("WV", "West Virginia"), new("WI", "Wisconsin"), new("WY", "Wyoming"),
        new("PR", "Puerto Rico"), new("GU", "Guam"), new("VI", "U.S. Virgin Islands"), new("AS", "American Samoa"), new("MP", "Northern Mariana Islands"),
        new("AA", "Armed Forces Americas"), new("AE", "Armed Forces Europe"), new("AP", "Armed Forces Pacific"),
    ];

    public static readonly IReadOnlyList<CodeName> Countries =
    [
        new("AF", "Afghanistan"), new("AX", "Åland Islands"), new("AL", "Albania"), new("DZ", "Algeria"), new("AS", "American Samoa"), new("AD", "Andorra"), new("AO", "Angola"),
        new("AI", "Anguilla"), new("AQ", "Antarctica"), new("AG", "Antigua and Barbuda"), new("AR", "Argentina"), new("AM", "Armenia"), new("AW", "Aruba"), new("AU", "Australia"),
        new("AT", "Austria"), new("AZ", "Azerbaijan"), new("BS", "Bahamas"), new("BH", "Bahrain"), new("BD", "Bangladesh"), new("BB", "Barbados"), new("BY", "Belarus"),
        new("BE", "Belgium"), new("BZ", "Belize"), new("BJ", "Benin"), new("BM", "Bermuda"), new("BT", "Bhutan"), new("BO", "Bolivia"), new("BQ", "Bonaire, Sint Eustatius and Saba"),
        new("BA", "Bosnia and Herzegovina"), new("BW", "Botswana"), new("BV", "Bouvet Island"), new("BR", "Brazil"), new("IO", "British Indian Ocean Territory"),
        new("BN", "Brunei Darussalam"), new("BG", "Bulgaria"), new("BF", "Burkina Faso"), new("BI", "Burundi"), new("CV", "Cabo Verde"), new("KH", "Cambodia"), new("CM", "Cameroon"),
        new("CA", "Canada"), new("KY", "Cayman Islands"), new("CF", "Central African Republic"), new("TD", "Chad"), new("CL", "Chile"), new("CN", "China"),
        new("CX", "Christmas Island"), new("CC", "Cocos (Keeling) Islands"), new("CO", "Colombia"), new("KM", "Comoros"), new("CG", "Congo"), new("CD", "Congo, Democratic Republic of the"),
        new("CK", "Cook Islands"), new("CR", "Costa Rica"), new("CI", "Côte d'Ivoire"), new("HR", "Croatia"), new("CU", "Cuba"), new("CW", "Curaçao"), new("CY", "Cyprus"),
        new("CZ", "Czechia"), new("DK", "Denmark"), new("DJ", "Djibouti"), new("DM", "Dominica"), new("DO", "Dominican Republic"), new("EC", "Ecuador"), new("EG", "Egypt"),
        new("SV", "El Salvador"), new("GQ", "Equatorial Guinea"), new("ER", "Eritrea"), new("EE", "Estonia"), new("SZ", "Eswatini"), new("ET", "Ethiopia"),
        new("FK", "Falkland Islands (Malvinas)"), new("FO", "Faroe Islands"), new("FJ", "Fiji"), new("FI", "Finland"), new("FR", "France"), new("GF", "French Guiana"),
        new("PF", "French Polynesia"), new("TF", "French Southern Territories"), new("GA", "Gabon"), new("GM", "Gambia"), new("GE", "Georgia"), new("DE", "Germany"),
        new("GH", "Ghana"), new("GI", "Gibraltar"), new("GR", "Greece"), new("GL", "Greenland"), new("GD", "Grenada"), new("GP", "Guadeloupe"), new("GU", "Guam"),
        new("GT", "Guatemala"), new("GG", "Guernsey"), new("GN", "Guinea"), new("GW", "Guinea-Bissau"), new("GY", "Guyana"), new("HT", "Haiti"),
        new("HM", "Heard Island and McDonald Islands"), new("VA", "Holy See"), new("HN", "Honduras"), new("HK", "Hong Kong"), new("HU", "Hungary"), new("IS", "Iceland"),
        new("IN", "India"), new("ID", "Indonesia"), new("IR", "Iran"), new("IQ", "Iraq"), new("IE", "Ireland"), new("IM", "Isle of Man"), new("IL", "Israel"), new("IT", "Italy"),
        new("JM", "Jamaica"), new("JP", "Japan"), new("JE", "Jersey"), new("JO", "Jordan"), new("KZ", "Kazakhstan"), new("KE", "Kenya"), new("KI", "Kiribati"),
        new("KP", "Korea, Democratic People's Republic of"), new("KR", "Korea, Republic of"), new("KW", "Kuwait"), new("KG", "Kyrgyzstan"), new("LA", "Lao People's Democratic Republic"),
        new("LV", "Latvia"), new("LB", "Lebanon"), new("LS", "Lesotho"), new("LR", "Liberia"), new("LY", "Libya"), new("LI", "Liechtenstein"), new("LT", "Lithuania"),
        new("LU", "Luxembourg"), new("MO", "Macao"), new("MG", "Madagascar"), new("MW", "Malawi"), new("MY", "Malaysia"), new("MV", "Maldives"), new("ML", "Mali"), new("MT", "Malta"),
        new("MH", "Marshall Islands"), new("MQ", "Martinique"), new("MR", "Mauritania"), new("MU", "Mauritius"), new("YT", "Mayotte"), new("MX", "Mexico"), new("FM", "Micronesia"),
        new("MD", "Moldova"), new("MC", "Monaco"), new("MN", "Mongolia"), new("ME", "Montenegro"), new("MS", "Montserrat"), new("MA", "Morocco"), new("MZ", "Mozambique"),
        new("MM", "Myanmar"), new("NA", "Namibia"), new("NR", "Nauru"), new("NP", "Nepal"), new("NL", "Netherlands"), new("NC", "New Caledonia"), new("NZ", "New Zealand"),
        new("NI", "Nicaragua"), new("NE", "Niger"), new("NG", "Nigeria"), new("NU", "Niue"), new("NF", "Norfolk Island"), new("MK", "North Macedonia"),
        new("MP", "Northern Mariana Islands"), new("NO", "Norway"), new("OM", "Oman"), new("PK", "Pakistan"), new("PW", "Palau"), new("PS", "Palestine, State of"), new("PA", "Panama"),
        new("PG", "Papua New Guinea"), new("PY", "Paraguay"), new("PE", "Peru"), new("PH", "Philippines"), new("PN", "Pitcairn"), new("PL", "Poland"), new("PT", "Portugal"),
        new("PR", "Puerto Rico"), new("QA", "Qatar"), new("RE", "Réunion"), new("RO", "Romania"), new("RU", "Russian Federation"), new("RW", "Rwanda"),
        new("BL", "Saint Barthélemy"), new("SH", "Saint Helena, Ascension and Tristan da Cunha"), new("KN", "Saint Kitts and Nevis"), new("LC", "Saint Lucia"),
        new("MF", "Saint Martin (French part)"), new("PM", "Saint Pierre and Miquelon"), new("VC", "Saint Vincent and the Grenadines"), new("WS", "Samoa"), new("SM", "San Marino"),
        new("ST", "Sao Tome and Principe"), new("SA", "Saudi Arabia"), new("SN", "Senegal"), new("RS", "Serbia"), new("SC", "Seychelles"), new("SL", "Sierra Leone"),
        new("SG", "Singapore"), new("SX", "Sint Maarten (Dutch part)"), new("SK", "Slovakia"), new("SI", "Slovenia"), new("SB", "Solomon Islands"), new("SO", "Somalia"),
        new("ZA", "South Africa"), new("GS", "South Georgia and the South Sandwich Islands"), new("SS", "South Sudan"), new("ES", "Spain"), new("LK", "Sri Lanka"), new("SD", "Sudan"),
        new("SR", "Suriname"), new("SJ", "Svalbard and Jan Mayen"), new("SE", "Sweden"), new("CH", "Switzerland"), new("SY", "Syrian Arab Republic"), new("TW", "Taiwan"),
        new("TJ", "Tajikistan"), new("TZ", "Tanzania"), new("TH", "Thailand"), new("TL", "Timor-Leste"), new("TG", "Togo"), new("TK", "Tokelau"), new("TO", "Tonga"),
        new("TT", "Trinidad and Tobago"), new("TN", "Tunisia"), new("TR", "Türkiye"), new("TM", "Turkmenistan"), new("TC", "Turks and Caicos Islands"), new("TV", "Tuvalu"),
        new("UG", "Uganda"), new("UA", "Ukraine"), new("AE", "United Arab Emirates"), new("GB", "United Kingdom"), new("US", "United States"),
        new("UM", "United States Minor Outlying Islands"), new("UY", "Uruguay"), new("UZ", "Uzbekistan"), new("VU", "Vanuatu"), new("VE", "Venezuela"), new("VN", "Viet Nam"),
        new("VG", "Virgin Islands (British)"), new("VI", "Virgin Islands (U.S.)"), new("WF", "Wallis and Futuna"), new("EH", "Western Sahara"), new("YE", "Yemen"),
        new("ZM", "Zambia"), new("ZW", "Zimbabwe"),
    ];

    private static readonly HashSet<string> CountryCodes = Countries.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> UsStateByKey = UsStates.SelectMany(s => new[] { (Key(s.Code), s.Code), (Key(s.Name), s.Code) })
        .ToDictionary(p => p.Item1, p => p.Item2, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> SexByKey = SexValues.ToDictionary(Key, v => v, StringComparer.Ordinal);

    /// <summary>The comparison key: trimmed, inner runs of spaces as one, upper case (invariant).</summary>
    public static string Key(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();

    /// <summary>The canonical Sex value for an exact (case/space-insensitive) match of the list; null when it is not one.</summary>
    public static string? CanonicalSex(string? value) => string.IsNullOrWhiteSpace(value) ? null : SexByKey.GetValueOrDefault(Key(value));

    /// <summary>The US code for an exact (case/space-insensitive) match of a code or a full name; null when it is not one.</summary>
    public static string? CanonicalUsState(string? value) => string.IsNullOrWhiteSpace(value) ? null : UsStateByKey.GetValueOrDefault(Key(value));

    /// <summary>The ISO alpha-2 code for a code typed in any case; null when it is not one.</summary>
    public static string? CanonicalCountry(string? value)
    {
        var v = value?.Trim().ToUpperInvariant();
        return v is { Length: 2 } && CountryCodes.Contains(v) ? v : null;
    }
}
