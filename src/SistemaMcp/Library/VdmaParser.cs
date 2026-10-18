using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace SistemaMcp.Library;

/// <summary>
/// Reads characteristic value libraries in the VDMA 66413 XML format (schema VDMA66413_2012-07).
/// Texts are referenced by keys and resolved from the &lt;Language&gt; section of the file or from a
/// separate language file (root element VDMA66413_Language) in the same folder.
/// </summary>
public static class VdmaParser
{
    public static bool IsVdmaFile(string path)
    {
        try
        {
            using var r = XmlReader.Create(path, SafeSettings);
            r.MoveToContent();
            return r.LocalName == "VDMA66413";
        }
        catch { return false; }
    }

    internal static readonly XmlReaderSettings SafeSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 200_000_000
    };

    public static List<LibraryComponent> Parse(string path, string sourceId, string preferredLanguage = "de")
    {
        XDocument doc;
        using (var r = XmlReader.Create(path, SafeSettings)) doc = XDocument.Load(r);
        var root = doc.Root ?? throw new InvalidDataException("Empty XML file.");
        if (root.Name.LocalName != "VDMA66413") throw new InvalidDataException("Not a VDMA 66413 file.");

        var manufacturer = Child(root, "Manufacturer") ?? throw new InvalidDataException("<Manufacturer> missing.");
        var texts = new Texts(preferredLanguage);
        texts.Load(Child(manufacturer, "Language") ?? Child(root, "Language"));
        // separate language files next to the data file
        var dbFile = Value(manufacturer, "DBFileName") ?? Path.GetFileName(path);
        foreach (var lf in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.xml"))
        {
            if (string.Equals(lf, path, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var r = XmlReader.Create(lf, SafeSettings);
                var ld = XDocument.Load(r);
                if (ld.Root?.Name.LocalName != "VDMA66413_Language") continue;
                var origin = ld.Descendants().FirstOrDefault(e => e.Name.LocalName is "DBFileName_Origin" or "DBFileName")?.Value;
                if (origin is null || origin.Equals(dbFile, StringComparison.OrdinalIgnoreCase))
                    texts.Load(ld.Root);
            }
            catch (XmlException) { }
        }

        var libName = texts.Resolve(Child(manufacturer, "Name")) ?? Value(manufacturer, "Name");
        var libVersion = (string?)manufacturer.Attribute("Version");
        var url = Value(manufacturer, "URL");
        var info = texts.Resolve(Child(manufacturer, "Information"));
        var manufacturerName = GuessManufacturer(info, libName);

        var result = new List<LibraryComponent>();
        var devices = Child(manufacturer, "Devices")?.Elements().Where(e => e.Name.LocalName == "Device")
                      ?? Enumerable.Empty<XElement>();
        foreach (var d in devices)
        {
            var identifier = Value(d, "Identifier") ?? "";
            var useCases = new List<LibraryUseCase>();
            int i = 0;
            foreach (var uc in Child(d, "UseCases")?.Elements().Where(e => e.Name.LocalName == "UseCase") ?? Enumerable.Empty<XElement>())
            {
                var parsed = ParseUseCase(uc, i, texts);
                if (parsed is not null) useCases.Add(parsed);
                i++;
            }
            result.Add(new LibraryComponent
            {
                Id = $"{sourceId}/{identifier}",
                SourceId = sourceId,
                Format = "vdma66413",
                SourceFile = path,
                LibraryName = libName,
                LibraryVersion = libVersion,
                LibraryUrl = url,
                Manufacturer = manufacturerName,
                Identifier = identifier,
                PartNumber = Value(d, "PartNumber"),
                Revision = Value(d, "Revision"),
                Name = texts.Resolve(Child(d, "Name")) ?? Value(d, "Name"),
                Group = texts.Resolve(Child(d, "Group")),
                Description = texts.Resolve(Child(d, "Description")),
                DocumentFile = texts.Resolve(Child(d, "DocFileName")),
                Archived = string.Equals(Value(d, "Archive"), "true", StringComparison.OrdinalIgnoreCase),
                UseCases = useCases
            });
        }
        return result;
    }

    private static LibraryUseCase? ParseUseCase(XElement uc, int index, Texts texts)
    {
        var constraints = Child(uc, "Constraints");
        var path = constraints is null ? "" : string.Join(" / ", constraints.Elements()
            .Select(h => texts.Resolve(h) ?? (string?)h.Attribute("Key"))
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "#NotApplicable"));
        var fn = Child(uc, "Function");
        bool F(string n) => string.Equals(Value(fn, n), "true", StringComparison.OrdinalIgnoreCase);
        var cat = Value(uc, "InfoConfig") switch
        {
            "CAT_B" => "B", "CAT_1" => "1", "CAT_2" => "2", "CAT_3" => "3", "CAT_4" => "4", _ => null
        };
        var p = Child(uc, "Parameter")?.Elements().FirstOrDefault();
        if (p is null) return null;
        string? pl = Value(p, "PL") is { } plv && plv.StartsWith("PL_") && plv != "PL_na" ? plv[3..] : null;
        string? sil = Value(p, "SILCL") is { } s && s.StartsWith("SIL_") && s != "SIL_na" ? s[4..] : null;
        double? rdf = Num(Value(p, "RDF")) is { } r ? r / 100.0 : null;
        var kind = p.Name.LocalName switch
        {
            "DeviceType1" => UseCaseKind.SubsystemPlPfh,
            "DeviceType2" => UseCaseKind.ElementMttfd,
            "DeviceType3" => UseCaseKind.ElementB10d,
            "DeviceType4" => UseCaseKind.SubsystemPl,
            _ => (UseCaseKind?)null
        };
        if (kind is null) return null;
        return new LibraryUseCase
        {
            Index = index,
            Path = path,
            Input = F("InputFunction"),
            Logic = F("LogicFunction"),
            Output = F("OutputFunction"),
            Category = cat,
            Kind = kind.Value,
            Pl = pl,
            Sil = sil,
            Pfh = Num(Value(p, "PFH_d")),
            Mttfd = Num(Value(p, "MTTF_d")),
            LambdaD = Num(Value(p, "Lambda_d")),
            Mttf = Num(Value(p, "MTTF")),
            Mtbf = Num(Value(p, "MTBF")),
            B10d = Num(Value(p, "B10d")),
            B10 = Num(Value(p, "B10")),
            Rdf = rdf,
            MissionTime = Num(Value(p, "TM_T1"))
        };
    }

    private static string GuessManufacturer(string? info, string? libName)
    {
        // The information text usually starts with the company name and address ("Hans Turck GmbH + Co KG, Witzlebenstr. 7, …").
        if (!string.IsNullOrWhiteSpace(info))
        {
            var first = info.Split(',', '\n')[0].Trim();
            if (first.Length is > 1 and < 120) return first;
        }
        return libName ?? "unknown";
    }

    internal static double? Num(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim().Replace(',', '.');
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(c => c.Name.LocalName == name);
    private static string? Value(XElement? e, string name) => Child(e, name)?.Value.Trim() is { Length: > 0 } v ? v : null;

    /// <summary>Key → text lookup with language preference (preferred, then English, then any).</summary>
    private sealed class Texts
    {
        private readonly string _lang;
        private readonly Dictionary<string, Dictionary<string, string>> _map = new(StringComparer.OrdinalIgnoreCase);
        public Texts(string lang) => _lang = lang;

        public void Load(XElement? language)
        {
            if (language is null) return;
            foreach (var e in language.Descendants().Where(x => x.Attribute("Key") is not null))
            {
                var key = (string)e.Attribute("Key")!;
                foreach (var t in e.Elements().Where(t => t.Name.LocalName == "Text"))
                {
                    var lang = ((string?)t.Attribute("LanguageKey") ?? "").ToLowerInvariant();
                    if (!_map.TryGetValue(key, out var byLang)) _map[key] = byLang = new();
                    byLang[lang] = t.Value.Trim();
                }
            }
        }

        public string? Resolve(XElement? e)
        {
            if (e is null) return null;
            var key = (string?)e.Attribute("Key");
            if (key is null) return string.IsNullOrWhiteSpace(e.Value) ? null : e.Value.Trim();
            if (!_map.TryGetValue(key, out var byLang)) return key.StartsWith('#') ? null : key;
            return byLang.GetValueOrDefault(_lang) ?? byLang.GetValueOrDefault("en") ?? byLang.Values.FirstOrDefault();
        }
    }
}
