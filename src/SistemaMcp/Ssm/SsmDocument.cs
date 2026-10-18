using System.Globalization;
using System.Xml.Linq;

namespace SistemaMcp.Ssm;

/// <summary>
/// Reads a SISTEMA project file (.ssm). Layout (see ssm_30.xsd in the SISTEMA installation):
/// xmldocdata/tables/table[@table_name]/rows/row – every row carries its values as attributes.
/// Hierarchy: projectops → sfops (projectopoid) → componentops (sfopoid)
///   → channelops (componentopoid) → blocops (parentopoid = channel) → elementops (parentopoid = block).
/// </summary>
public sealed class SsmDocument
{
    public string FilePath { get; }
    public XDocument Xml { get; }

    private SsmDocument(string filePath, XDocument xml)
    {
        FilePath = filePath;
        Xml = xml;
    }

    public static SsmDocument Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");
        var xml = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        if (xml.Root?.Name.LocalName != "xmldocdata")
            throw new InvalidDataException("Not a SISTEMA file (root element 'xmldocdata' missing).");
        return new SsmDocument(path, xml);
    }

    public IReadOnlyList<SsmRow> Rows(string tableName) =>
        Xml.Root!.Element("tables")?.Elements("table")
            .FirstOrDefault(t => (string?)t.Attribute("table_name") == tableName)?
            .Element("rows")?.Elements("row").Select(r => new SsmRow(r)).ToList()
        ?? new List<SsmRow>();
}

public sealed class SsmRow
{
    public XElement Element { get; }
    public SsmRow(XElement element) => Element = element;

    public string? this[string name] => Element.Attribute(name)?.Value;

    public string Oid => this["oid"] ?? "";
    public string Name => Text("name") ?? "";

    /// <summary>Text with SISTEMA line breaks (&amp;cr;&amp;lf;) converted to real line breaks.</summary>
    public string? Text(string name)
    {
        var v = this[name];
        if (string.IsNullOrEmpty(v)) return null;
        return SsmFormat.DecodeText(v);
    }

    public double? Number(string name) => SsmFormat.ParseNumber(this[name]);

    public bool? Bool(string name) => this[name] switch { "1" => true, "0" => false, _ => null };
}

/// <summary>Value notation used inside .ssm files.</summary>
public static class SsmFormat
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Number in SISTEMA notation (decimal comma, "-" or "FE" = no value).</summary>
    public static double? ParseNumber(string? v)
    {
        if (string.IsNullOrWhiteSpace(v) || v == "-" || v == "FE") return null;
        return double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }

    /// <summary>Formats a number the way SISTEMA stores it (decimal comma, shortest round-trip form).</summary>
    public static string Number(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string EncodeText(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "&cr;&lf;");

    public static string DecodeText(string text) =>
        text.Replace("&cr;&lf;", "\n").Replace("&cr;", "\n").Replace("&lf;", "\n");

    /// <summary>SISTEMA always expects "dd/MM/yyyy HH:mm:ss:zzz" with literal slashes, independent of the OS culture.</summary>
    public static string Date(DateTime d) => d.ToString("dd'/'MM'/'yyyy HH':'mm':'ss':'fff", CultureInfo.InvariantCulture);
}

/// <summary>Translates SISTEMA codes into readable values and back.</summary>
public static class SsmCodes
{
    public static readonly string[] PlOrder = { "plA", "plB", "plC", "plD", "plE" };

    public static string? Pl(string? v) => v switch
    {
        null or "" => null,
        "plA" => "a", "plB" => "b", "plC" => "c", "plD" => "d", "plE" => "e",
        "plN" => "none", "plU" => "< a",
        _ => v
    };

    public static string PlCode(string pl) => pl.Trim().ToLowerInvariant().Replace("pl", "").Replace("_", "") switch
    {
        "a" => "plA", "b" => "plB", "c" => "plC", "d" => "plD", "e" => "plE",
        var x => throw new ArgumentException($"Unknown PL '{pl}' (expected a–e).")
    };

    public static string? Cat(string? v) => v switch
    {
        null or "" => null,
        "catB" => "B", "cat1" => "1", "cat2" => "2", "cat3" => "3", "cat4" => "4", "catN" => "none",
        _ => v
    };

    public static string CatCode(string cat) => cat.Trim().ToUpperInvariant().Replace("CAT", "").Replace("_", "") switch
    {
        "B" => "catB", "1" => "cat1", "2" => "cat2", "3" => "cat3", "4" => "cat4",
        _ => throw new ArgumentException($"Unknown category '{cat}' (expected B, 1, 2, 3 or 4).")
    };

    public static string? Channel(string? v) => v switch
    {
        "ch1" => "channel 1", "ch2" => "channel 2", "chTest" => "test channel", _ => v
    };

    public static string? Determination(string? v) => v switch
    {
        null or "" => null,
        "detDirect" => "entered directly",
        "detSubItems" => "from sub-items",
        "detSubItemsSimple" => "from sub-items (simplified)",
        "detMeasures" => "from measures",
        "detB10D" => "from B10D",
        "detMTTF" => "from MTTF/MTBF/lambda",
        "detSILDirect" => "SIL entered directly",
        _ => v
    };

    public static string? Technology(string? v) => v switch
    {
        "tecElectromechanic" => "electromechanical", "tecElectronic" => "electronic",
        "tecHydraulic" => "hydraulic", "tecMechanic" => "mechanical",
        "tecPneumatic" => "pneumatic", "tecOther" => "other", "tecUnknown" => null,
        _ => v
    };

    public static string TechnologyCode(string? t) => (t ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "unknown" => "tecUnknown",
        "electromechanical" or "electromechanic" or "elektromechanisch" => "tecElectromechanic",
        "electronic" or "elektronisch" or "programmable" => "tecElectronic",
        "hydraulic" or "hydraulisch" => "tecHydraulic",
        "mechanical" or "mechanic" or "mechanisch" => "tecMechanic",
        "pneumatic" or "pneumatisch" => "tecPneumatic",
        "other" or "andere" => "tecOther",
        _ => throw new ArgumentException($"Unknown technology '{t}' (electromechanical, electronic, pneumatic, hydraulic, mechanical, other).")
    };

    public static bool? PlReached(string? pl, string? plr)
    {
        int i = Array.IndexOf(PlOrder, pl), r = Array.IndexOf(PlOrder, plr);
        if (r < 0) return null;
        return i >= r;
    }
}
