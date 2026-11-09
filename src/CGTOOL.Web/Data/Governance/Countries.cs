namespace CGTOOL.Web.Data.Governance;

/// <summary>Nationalities offered wherever one is captured, so the same spelling comes back every
/// time. Free text produced "UAE", "U.A.E.", "Emirati" and "United Arab Emirates" for one country,
/// which no report can group.
///
/// The United Arab Emirates is first rather than alphabetical: it is the answer for most of the
/// people filling these in, and a list of two hundred is a long scroll to reach U.</summary>
public static class Countries
{
    public const string Default = "United Arab Emirates";

    public static readonly string[] All =
    [
        Default,
        "Afghanistan", "Albania", "Algeria", "Andorra", "Angola", "Antigua and Barbuda", "Argentina",
        "Armenia", "Australia", "Austria", "Azerbaijan", "Bahamas", "Bahrain", "Bangladesh",
        "Barbados", "Belarus", "Belgium", "Belize", "Benin", "Bhutan", "Bolivia",
        "Bosnia and Herzegovina", "Botswana", "Brazil", "Brunei", "Bulgaria", "Burkina Faso",
        "Burundi", "Cabo Verde", "Cambodia", "Cameroon", "Canada", "Central African Republic",
        "Chad", "Chile", "China", "Colombia", "Comoros", "Congo", "Congo (Democratic Republic)",
        "Costa Rica", "Côte d'Ivoire", "Croatia", "Cuba", "Cyprus", "Czechia", "Denmark",
        "Djibouti", "Dominica", "Dominican Republic", "Ecuador", "Egypt", "El Salvador",
        "Equatorial Guinea", "Eritrea", "Estonia", "Eswatini", "Ethiopia", "Fiji", "Finland",
        "France", "Gabon", "Gambia", "Georgia", "Germany", "Ghana", "Greece", "Grenada",
        "Guatemala", "Guinea", "Guinea-Bissau", "Guyana", "Haiti", "Honduras", "Hungary",
        "Iceland", "India", "Indonesia", "Iran", "Iraq", "Ireland", "Israel", "Italy", "Jamaica",
        "Japan", "Jordan", "Kazakhstan", "Kenya", "Kiribati", "Kosovo", "Kuwait", "Kyrgyzstan",
        "Laos", "Latvia", "Lebanon", "Lesotho", "Liberia", "Libya", "Liechtenstein", "Lithuania",
        "Luxembourg", "Madagascar", "Malawi", "Malaysia", "Maldives", "Mali", "Malta",
        "Marshall Islands", "Mauritania", "Mauritius", "Mexico", "Micronesia", "Moldova", "Monaco",
        "Mongolia", "Montenegro", "Morocco", "Mozambique", "Myanmar", "Namibia", "Nauru", "Nepal",
        "Netherlands", "New Zealand", "Nicaragua", "Niger", "Nigeria", "North Korea",
        "North Macedonia", "Norway", "Oman", "Pakistan", "Palau", "Palestine", "Panama",
        "Papua New Guinea", "Paraguay", "Peru", "Philippines", "Poland", "Portugal", "Qatar",
        "Romania", "Russia", "Rwanda", "Saint Kitts and Nevis", "Saint Lucia",
        "Saint Vincent and the Grenadines", "Samoa", "San Marino", "São Tomé and Príncipe",
        "Saudi Arabia", "Senegal", "Serbia", "Seychelles", "Sierra Leone", "Singapore", "Slovakia",
        "Slovenia", "Solomon Islands", "Somalia", "South Africa", "South Korea", "South Sudan",
        "Spain", "Sri Lanka", "Sudan", "Suriname", "Sweden", "Switzerland", "Syria", "Taiwan",
        "Tajikistan", "Tanzania", "Thailand", "Timor-Leste", "Togo", "Tonga",
        "Trinidad and Tobago", "Tunisia", "Türkiye", "Turkmenistan", "Tuvalu", "Uganda", "Ukraine",
        "United Kingdom", "United States of America", "Uruguay", "Uzbekistan", "Vanuatu",
        "Vatican City", "Venezuela", "Vietnam", "Yemen", "Zambia", "Zimbabwe",
    ];

    /// <summary>The name for a three-letter code out of a machine-readable zone, or null for one this
    /// doesn't know. Every name here is one of All's, so what comes back can be selected in the
    /// nationality dropdown -- a code left as "ARE" could not be.
    ///
    /// A few codes are not ISO 3166-1 alpha-3: German passports write "D" for the nationality, and
    /// Kosovo's passports are issued as "RKS", with "XKX" used where a system needs three letters.</summary>
    /// <summary>The name from this list that a written-down nationality means, or null when none
    /// of them does.
    ///
    /// Files are written by people and by other systems, and the same country arrives spelt
    /// several ways: an Emirates ID export writes the United Nations long form -- "State of
    /// Palestine", "Iran (Islamic Republic of)", "Commonwealth of Dominica" -- where this list
    /// holds the short name a person would pick from a dropdown.
    ///
    /// Resolving rather than adding the long forms to the list on purpose. Two entries for one
    /// country is the thing this class exists to prevent: it was written because free text had
    /// produced "UAE", "U.A.E.", "Emirati" and "United Arab Emirates" for one country, which no
    /// report can group. A name that resolves to nothing is left for the caller to reject, rather
    /// than guessed at.</summary>
    public static string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();

        // Already one of ours, whatever the casing.
        if (All.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return exact;
        }

        if (Aliases.TryGetValue(text, out var alias)) return alias;

        // "Iran (Islamic Republic of)" -> "Iran"; "State of Palestine" -> "Palestine".
        var trimmed = text;
        var bracket = trimmed.IndexOf('(', StringComparison.Ordinal);
        if (bracket > 0) trimmed = trimmed[..bracket].Trim();

        foreach (var prefix in LongFormPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[prefix.Length..].Trim();
                break;
            }
        }

        if (!string.Equals(trimmed, text, StringComparison.Ordinal))
        {
            if (All.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)) is { } stripped)
            {
                return stripped;
            }

            if (Aliases.TryGetValue(trimmed, out var strippedAlias)) return strippedAlias;
        }

        return null;
    }

    private static readonly string[] LongFormPrefixes =
        ["State of ", "Commonwealth of ", "Republic of ", "Kingdom of ", "The "];

    /// <summary>Spellings that no amount of trimming turns into one of All's names. Every value
    /// here is one of All's -- src/CGTOOL.Checks fails if one stops being.</summary>
    public static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UAE"] = Default, ["U.A.E."] = Default, ["U.A.E"] = Default, ["Emirati"] = Default,
        ["Syrian Arab Republic"] = "Syria",
        ["Russian Federation"] = "Russia",
        ["Republic of Korea"] = "South Korea", ["Korea, Republic of"] = "South Korea",
        ["Viet Nam"] = "Vietnam",
        // The list spells these the way the passport does, so the everyday spelling is the alias
        // rather than the other way round.
        ["Turkiye"] = "T\u00fcrkiye", ["Turkey"] = "T\u00fcrkiye",
        ["Czech Republic"] = "Czechia",
        ["Burma"] = "Myanmar",
        ["Great Britain"] = "United Kingdom", ["Britain"] = "United Kingdom", ["England"] = "United Kingdom",
        ["United States"] = "United States of America", ["USA"] = "United States of America",
        ["U.S.A."] = "United States of America", ["US"] = "United States of America",
        ["Holy See"] = "Vatican City", ["Vatican"] = "Vatican City",
        ["Lao People's Democratic Republic"] = "Laos",
        ["Brunei Darussalam"] = "Brunei",
    };

    public static string? FromMrzCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && ByMrzCode.TryGetValue(code.Trim(), out var name) ? name : null;

    private static readonly Dictionary<string, string> ByMrzCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AFG"] = "Afghanistan", ["ALB"] = "Albania", ["DZA"] = "Algeria", ["AND"] = "Andorra",
        ["AGO"] = "Angola", ["ATG"] = "Antigua and Barbuda", ["ARG"] = "Argentina", ["ARM"] = "Armenia",
        ["AUS"] = "Australia", ["AUT"] = "Austria", ["AZE"] = "Azerbaijan", ["BHS"] = "Bahamas",
        ["BHR"] = "Bahrain", ["BGD"] = "Bangladesh", ["BRB"] = "Barbados", ["BLR"] = "Belarus",
        ["BEL"] = "Belgium", ["BLZ"] = "Belize", ["BEN"] = "Benin", ["BTN"] = "Bhutan",
        ["BOL"] = "Bolivia", ["BIH"] = "Bosnia and Herzegovina", ["BWA"] = "Botswana", ["BRA"] = "Brazil",
        ["BRN"] = "Brunei", ["BGR"] = "Bulgaria", ["BFA"] = "Burkina Faso", ["BDI"] = "Burundi",
        ["CPV"] = "Cabo Verde", ["KHM"] = "Cambodia", ["CMR"] = "Cameroon", ["CAN"] = "Canada",
        ["CAF"] = "Central African Republic", ["TCD"] = "Chad", ["CHL"] = "Chile", ["CHN"] = "China",
        ["COL"] = "Colombia", ["COM"] = "Comoros", ["COG"] = "Congo",
        ["COD"] = "Congo (Democratic Republic)", ["CRI"] = "Costa Rica", ["HRV"] = "Croatia",
        ["CUB"] = "Cuba", ["CYP"] = "Cyprus", ["CZE"] = "Czechia", ["CIV"] = "Côte d'Ivoire",
        ["DNK"] = "Denmark", ["DJI"] = "Djibouti", ["DMA"] = "Dominica", ["DOM"] = "Dominican Republic",
        ["ECU"] = "Ecuador", ["EGY"] = "Egypt", ["SLV"] = "El Salvador", ["GNQ"] = "Equatorial Guinea",
        ["ERI"] = "Eritrea", ["EST"] = "Estonia", ["SWZ"] = "Eswatini", ["ETH"] = "Ethiopia",
        ["FJI"] = "Fiji", ["FIN"] = "Finland", ["FRA"] = "France", ["GAB"] = "Gabon", ["GMB"] = "Gambia",
        ["GEO"] = "Georgia", ["D"] = "Germany", ["DEU"] = "Germany", ["GHA"] = "Ghana", ["GRC"] = "Greece",
        ["GRD"] = "Grenada", ["GTM"] = "Guatemala", ["GIN"] = "Guinea", ["GNB"] = "Guinea-Bissau",
        ["GUY"] = "Guyana", ["HTI"] = "Haiti", ["HND"] = "Honduras", ["HUN"] = "Hungary",
        ["ISL"] = "Iceland", ["IND"] = "India", ["IDN"] = "Indonesia", ["IRN"] = "Iran", ["IRQ"] = "Iraq",
        ["IRL"] = "Ireland", ["ISR"] = "Israel", ["ITA"] = "Italy", ["JAM"] = "Jamaica", ["JPN"] = "Japan",
        ["JOR"] = "Jordan", ["KAZ"] = "Kazakhstan", ["KEN"] = "Kenya", ["KIR"] = "Kiribati",
        ["RKS"] = "Kosovo", ["XKX"] = "Kosovo", ["KWT"] = "Kuwait", ["KGZ"] = "Kyrgyzstan",
        ["LAO"] = "Laos", ["LVA"] = "Latvia", ["LBN"] = "Lebanon", ["LSO"] = "Lesotho",
        ["LBR"] = "Liberia", ["LBY"] = "Libya", ["LIE"] = "Liechtenstein", ["LTU"] = "Lithuania",
        ["LUX"] = "Luxembourg", ["MDG"] = "Madagascar", ["MWI"] = "Malawi", ["MYS"] = "Malaysia",
        ["MDV"] = "Maldives", ["MLI"] = "Mali", ["MLT"] = "Malta", ["MHL"] = "Marshall Islands",
        ["MRT"] = "Mauritania", ["MUS"] = "Mauritius", ["MEX"] = "Mexico", ["FSM"] = "Micronesia",
        ["MDA"] = "Moldova", ["MCO"] = "Monaco", ["MNG"] = "Mongolia", ["MNE"] = "Montenegro",
        ["MAR"] = "Morocco", ["MOZ"] = "Mozambique", ["MMR"] = "Myanmar", ["NAM"] = "Namibia",
        ["NRU"] = "Nauru", ["NPL"] = "Nepal", ["NLD"] = "Netherlands", ["NZL"] = "New Zealand",
        ["NIC"] = "Nicaragua", ["NER"] = "Niger", ["NGA"] = "Nigeria", ["PRK"] = "North Korea",
        ["MKD"] = "North Macedonia", ["NOR"] = "Norway", ["OMN"] = "Oman", ["PAK"] = "Pakistan",
        ["PLW"] = "Palau", ["PSE"] = "Palestine", ["PAN"] = "Panama", ["PNG"] = "Papua New Guinea",
        ["PRY"] = "Paraguay", ["PER"] = "Peru", ["PHL"] = "Philippines", ["POL"] = "Poland",
        ["PRT"] = "Portugal", ["QAT"] = "Qatar", ["ROU"] = "Romania", ["RUS"] = "Russia",
        ["RWA"] = "Rwanda", ["KNA"] = "Saint Kitts and Nevis", ["LCA"] = "Saint Lucia",
        ["VCT"] = "Saint Vincent and the Grenadines", ["WSM"] = "Samoa", ["SMR"] = "San Marino",
        ["SAU"] = "Saudi Arabia", ["SEN"] = "Senegal", ["SRB"] = "Serbia", ["SYC"] = "Seychelles",
        ["SLE"] = "Sierra Leone", ["SGP"] = "Singapore", ["SVK"] = "Slovakia", ["SVN"] = "Slovenia",
        ["SLB"] = "Solomon Islands", ["SOM"] = "Somalia", ["ZAF"] = "South Africa",
        ["KOR"] = "South Korea", ["SSD"] = "South Sudan", ["ESP"] = "Spain", ["LKA"] = "Sri Lanka",
        ["SDN"] = "Sudan", ["SUR"] = "Suriname", ["SWE"] = "Sweden", ["CHE"] = "Switzerland",
        ["SYR"] = "Syria", ["STP"] = "São Tomé and Príncipe", ["TWN"] = "Taiwan", ["TJK"] = "Tajikistan",
        ["TZA"] = "Tanzania", ["THA"] = "Thailand", ["TLS"] = "Timor-Leste", ["TGO"] = "Togo",
        ["TON"] = "Tonga", ["TTO"] = "Trinidad and Tobago", ["TUN"] = "Tunisia", ["TKM"] = "Turkmenistan",
        ["TUV"] = "Tuvalu", ["TUR"] = "Türkiye", ["UGA"] = "Uganda", ["UKR"] = "Ukraine",
        ["ARE"] = "United Arab Emirates", ["GBR"] = "United Kingdom", ["USA"] = "United States of America",
        ["URY"] = "Uruguay", ["UZB"] = "Uzbekistan", ["VUT"] = "Vanuatu", ["VAT"] = "Vatican City",
        ["VEN"] = "Venezuela", ["VNM"] = "Vietnam", ["YEM"] = "Yemen", ["ZMB"] = "Zambia",
        ["ZWE"] = "Zimbabwe",
    };
}
