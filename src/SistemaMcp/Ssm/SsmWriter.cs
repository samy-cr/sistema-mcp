using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SistemaMcp.Ssm;

/// <summary>
/// Builds a SISTEMA project file from scratch: every table of <see cref="SsmSchema"/> with its field
/// definitions, rows filled with defaults and the given values.
/// </summary>
public sealed class SsmWriter
{
    private readonly Dictionary<string, XElement> _rows = new();
    private readonly XDocument _doc;

    public SsmWriter()
    {
        var tables = new XElement("tables");
        foreach (var name in SsmSchema.TableOrder)
        {
            var rows = new XElement("rows");
            _rows[name] = rows;
            tables.Add(new XElement("table",
                new XAttribute("table_name", name),
                new XElement("fields", SsmSchema.Tables[name].Select(f => new XElement("field",
                    new XAttribute("field_name", f.Name),
                    new XAttribute("field_kind", f.Kind),
                    new XAttribute("field_Size", f.Size)))),
                rows));
        }
        _doc = new XDocument(new XDeclaration("1.0", null, null),
            new XElement("xmldocdata", new XElement("tiopf", new XAttribute("version", "2.1")), tables));
    }

    public static string NewOid() => Guid.NewGuid().ToString().ToUpperInvariant();

    /// <summary>Adds a row; unknown field names throw so typos never end up silently in a file.</summary>
    public string AddRow(string table, IReadOnlyDictionary<string, string?> values)
    {
        var fields = SsmSchema.Tables[table];
        foreach (var key in values.Keys)
            if (fields.All(f => f.Name != key))
                throw new ArgumentException($"Field '{key}' does not exist in table '{table}'.");

        var oid = values.TryGetValue("oid", out var o) && !string.IsNullOrEmpty(o) ? o! : NewOid();
        var row = new XElement("row");
        foreach (var f in fields)
        {
            var v = f.Name == "oid" ? oid
                : values.TryGetValue(f.Name, out var given) && given is not null ? given
                : f.Default;
            if (f.Name is "ssmversion") v = SsmSchema.SsmVersion;
            if (f.Name is "normversion") v = SsmSchema.NormVersion;
            row.Add(new XAttribute(f.Name, v));
        }
        _rows[table].Add(row);
        return oid;
    }

    public XDocument Document => _doc;

    /// <summary>Writes the file the way SISTEMA does: UTF-8 without BOM, tab indentation, CRLF.</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "\t",
            NewLineChars = "\r\n",
            OmitXmlDeclaration = true
        };
        using var stream = new StreamWriter(path, false, new UTF8Encoding(false));
        stream.Write("<?xml version=\"1.0\"?>\r\n"); // exactly the header SISTEMA writes
        using var w = XmlWriter.Create(stream, settings);
        _doc.Root!.Save(w);
    }
}
