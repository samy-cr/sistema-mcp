using System.Data.Common;
using FirebirdSql.Data.FirebirdClient;
using SistemaMcp.Ssm;

namespace SistemaMcp.Library;

/// <summary>
/// Reads SISTEMA component libraries (.slb). An .slb file is a Firebird 5 database (ODS 13); it is opened with
/// the embedded 32-bit engine shipped with SISTEMA (Firebird\fb50\fbclient.dll), so the server process must be x86.
/// The file is copied first, the original is never opened or changed.
/// Indexed: subsystems with directly entered PL/PFH, blocks and elements (with device and manufacturer metadata).
/// </summary>
public static class SlbReader
{
    public static string? CheckAvailable()
    {
        if (Environment.Is64BitProcess)
            return "SLB libraries need the 32-bit server build (SISTEMA's embedded Firebird engine is 32-bit).";
        if (SistemaEnvironment.FirebirdClient is null)
            return "SISTEMA installation with Firebird\\fb50\\fbclient.dll not found (set SISTEMA_INSTALL_DIR).";
        return null;
    }

    public static List<LibraryComponent> Parse(string slbPath, string sourceId, string workDir)
    {
        if (CheckAvailable() is { } problem) throw new InvalidOperationException(problem);
        Directory.CreateDirectory(workDir);
        var copy = Path.Combine(workDir, $"{sourceId}.fdb");
        File.Copy(slbPath, copy, overwrite: true);
        File.SetAttributes(copy, FileAttributes.Normal);

        var cs = new FbConnectionStringBuilder
        {
            Database = copy,
            ServerType = FbServerType.Embedded,
            ClientLibrary = SistemaEnvironment.FirebirdClient,
            UserID = "SYSDBA",
            Password = "masterkey",
            Charset = "UTF8",
            Pooling = false
        }.ToString();

        try
        {
            using var con = new FbConnection(cs);
            con.Open();
            var meta = ReadRows(con, "LIBMETADATA").FirstOrDefault();
            var libName = meta?.GetValueOrDefault("NAME");
            var libVersion = meta?.GetValueOrDefault("LIBVERSION");
            var libUrl = meta?.GetValueOrDefault("URL");

            var components = ReadRows(con, "COMPONENTOPS");
            var channels = ReadRows(con, "CHANNELOPS");
            var blocks = ReadRows(con, "BLOCOPS");
            var elements = ReadRows(con, "ELEMENTOPS");
            var devices = ReadRows(con, "METADEVICECOMPONENTS").Concat(ReadRows(con, "METADEVICEBLOCS"))
                .Concat(ReadRows(con, "METADEVICEELEMENTS"))
                .Where(r => r.GetValueOrDefault("PARENTOID") is { Length: > 0 })
                .GroupBy(r => r["PARENTOID"]!).ToDictionary(g => g.Key, g => g.First());
            var makers = ReadRows(con, "METAMANUFACTURERCOMPONENTS").Concat(ReadRows(con, "METAMANUFACTURERBLOCS"))
                .Concat(ReadRows(con, "METAMANUFACTURERELEMENTS"))
                .Where(r => r.GetValueOrDefault("PARENTOID") is { Length: > 0 })
                .GroupBy(r => r["PARENTOID"]!).ToDictionary(g => g.Key, g => g.First());

            var channelOids = channels.Select(c => c["OID"]).ToHashSet();
            var result = new List<LibraryComponent>();

            LibraryComponent Make(Row op, string kindLabel, LibraryUseCase uc)
            {
                var oid = op["OID"] ?? "";
                devices.TryGetValue(oid, out var dev);
                makers.TryGetValue(oid, out var mk);
                var identifier = dev?.GetValueOrDefault("DEVICEID") is { Length: > 0 } id ? id : op.GetValueOrDefault("NAME") ?? oid;
                return new LibraryComponent
                {
                    Id = $"{sourceId}/{kindLabel}/{oid}",
                    SourceId = sourceId,
                    Format = "slb",
                    SourceFile = slbPath,
                    LibraryName = mk?.GetValueOrDefault("LIBNAME") ?? libName,
                    LibraryVersion = mk?.GetValueOrDefault("LIBVERSION") ?? libVersion,
                    LibraryUrl = mk?.GetValueOrDefault("LIBURL") ?? libUrl,
                    Manufacturer = mk?.GetValueOrDefault("MANUFACTURER"),
                    Identifier = identifier,
                    PartNumber = dev?.GetValueOrDefault("PARTNO"),
                    Revision = dev?.GetValueOrDefault("REVISION"),
                    Name = op.GetValueOrDefault("NAME"),
                    Group = dev?.GetValueOrDefault("DEVICEGROUP"),
                    Description = Decode(dev?.GetValueOrDefault("DESCRIPTION")),
                    Archived = dev?.GetValueOrDefault("ARCHIVE") == "1",
                    UseCases = new List<LibraryUseCase> { uc }
                };
            }

            foreach (var c in components.Where(c => string.IsNullOrEmpty(c.GetValueOrDefault("SFOPOID"))))
            {
                if (c.GetValueOrDefault("PLDET") != "detDirect") continue; // subsystems built from sub-items: not yet supported
                devices.TryGetValue(c["OID"] ?? "", out var dev);
                result.Add(Make(c, "subsystem", new LibraryUseCase
                {
                    Index = 0,
                    Path = dev?.GetValueOrDefault("USECASE") ?? "",
                    Kind = Num(c, "PFH") is > 0 ? UseCaseKind.SubsystemPlPfh : UseCaseKind.SubsystemPl,
                    Pl = SsmCodes.Pl(c.GetValueOrDefault("PLDIRECTNOBIND")) is { Length: 1 } pl ? pl : null,
                    Pfh = Num(c, "PFH"),
                    Category = SsmCodes.Cat(dev?.GetValueOrDefault("CAT")) is { Length: 1 } cat ? cat : null,
                    MissionTime = SsmFormat.ParseNumber(dev?.GetValueOrDefault("MISSIONTIME"))
                }));
            }

            foreach (var b in blocks.Where(b => !channelOids.Contains(b.GetValueOrDefault("PARENTOPOID"))))
                if (ElementUseCase(b, devices) is { } uc) result.Add(Make(b, "block", uc));

            foreach (var e in elements)
                if (ElementUseCase(e, devices) is { } uc) result.Add(Make(e, "element", uc));

            return result;
        }
        finally
        {
            FbConnection.ClearAllPools();
            try { File.Delete(copy); } catch (IOException) { }
        }
    }

    private static LibraryUseCase? ElementUseCase(Row r, Dictionary<string, Row> devices)
    {
        devices.TryGetValue(r["OID"] ?? "", out var dev);
        var det = r.GetValueOrDefault("MTTFDDET");
        double? rdf = Num(r, det == "detB10D" ? "RDFB10" : "RDFMTTF");
        var uc = new LibraryUseCase
        {
            Index = 0,
            Path = dev?.GetValueOrDefault("USECASE") ?? "",
            Kind = det == "detB10D" ? UseCaseKind.ElementB10d : UseCaseKind.ElementMttfd,
            Mttfd = det == "detDirect" ? Num(r, "MTTFD") : null,
            B10d = det == "detB10D" && r.GetValueOrDefault("CALCB10DDET") != "calcB10dB10" ? Num(r, "B10D") : null,
            B10 = det == "detB10D" && r.GetValueOrDefault("CALCB10DDET") == "calcB10dB10" ? Num(r, "B10") : null,
            Mttf = det == "detMTTF" && r.GetValueOrDefault("CALCMTTFDDET") == "calcMTTFdMTTF" ? Num(r, "MTTF") : null,
            Mtbf = det == "detMTTF" && r.GetValueOrDefault("CALCMTTFDDET") == "calcMTTFdMTBF" ? Num(r, "MTBF") : null,
            LambdaD = det == "detMTTF" && r.GetValueOrDefault("CALCMTTFDDET") == "calcMTTFdLambda" ? Num(r, "LAMBDA") : null,
            Rdf = rdf,
            Dc = r.GetValueOrDefault("DCDET") == "detMeasures" ? Num(r, "DCMEASUREDC") : Num(r, "DC"),
            Technology = SsmCodes.Technology(r.GetValueOrDefault("TECHNOLOGY")),
            MissionTime = SsmFormat.ParseNumber(dev?.GetValueOrDefault("MISSIONTIME"))
        };
        return uc.EffectiveMttfd is null && uc.EffectiveB10d is null ? null : uc;
    }

    private sealed class Row : Dictionary<string, string?>
    {
        public Row() : base(StringComparer.OrdinalIgnoreCase) { }
    }

    private static double? Num(Row r, string field) => SsmFormat.ParseNumber(r.GetValueOrDefault(field));

    private static string? Decode(string? s) => string.IsNullOrEmpty(s) ? null : SsmFormat.DecodeText(s);

    private static List<Row> ReadRows(DbConnection con, string table)
    {
        var rows = new List<Row>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"select * from \"{table}\"";
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new Row();
                for (int i = 0; i < r.FieldCount; i++)
                    row[r.GetName(i)] = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)?.Trim();
                rows.Add(row);
            }
        }
        catch (FbException) { /* table missing in older library versions */ }
        return rows;
    }
}
