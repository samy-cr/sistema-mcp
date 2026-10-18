using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Server;
using SistemaMcp.Spec;
using SistemaMcp.Ssm;

namespace SistemaMcp.Tools;

[McpServerToolType]
public static class ProjectTools
{
    internal static string Json(object o) => JsonSerializer.Serialize(o, ProjectSpec.JsonOptions);

    [McpServerTool(Name = "sistema_list_projects", ReadOnly = true, OpenWorld = false)]
    [Description("Finds SISTEMA project files (.ssm) in a folder (recursive) and lists project name, standard version, number of safety functions and path.")]
    public static string ListProjects(
        [Description("Folder to search. Empty = Documents\\SISTEMA\\Projects.")] string? folder = null)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? SistemaEnvironment.ProjectsDir : folder;
        if (!Directory.Exists(folder))
            return $"Folder not found: {folder}";

        var result = new List<object>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.ssm", SearchOption.AllDirectories))
        {
            try
            {
                var doc = SsmDocument.Load(file);
                var p = doc.Rows("projectops").FirstOrDefault();
                result.Add(new
                {
                    name = p?.Name,
                    standard = p?["normversion"],
                    ssmVersion = p?["ssmversion"],
                    safetyFunctions = doc.Rows("sfops").Count,
                    path = file,
                    modified = File.GetLastWriteTime(file)
                });
            }
            catch (Exception ex)
            {
                result.Add(new { path = file, error = ex.Message });
            }
        }
        return Json(new { folder, count = result.Count, projects = result });
    }

    [McpServerTool(Name = "sistema_read_project", ReadOnly = true, OpenWorld = false)]
    [Description("Reads a SISTEMA project (.ssm) and returns the tree project → safety functions → subsystems → channels → blocks → elements " +
                 "with PLr, PL, PFH, category, MTTFd, DC, B10D, CCF measures etc. PL and PFH are the results SISTEMA stored on the last save " +
                 "(a freshly generated file contains none until it was opened and saved in SISTEMA).")]
    public static string ReadProject(
        [Description("Full path of the .ssm file.")] string path,
        [Description("Include description and documentation texts (default false).")] bool includeTexts = false)
    {
        var doc = SsmDocument.Load(path);
        var project = doc.Rows("projectops").FirstOrDefault();
        var components = doc.Rows("componentops");
        var channels = doc.Rows("channelops");
        var blocks = doc.Rows("blocops");
        var elements = doc.Rows("elementops");

        string? T(SsmRow r, string name) => includeTexts ? r.Text(name) : null;

        object Element(SsmRow e) => new
        {
            name = e.Name,
            technology = SsmCodes.Technology(e["technology"]),
            mttfdYears = ElementMttfd(e),
            mttfdFrom = SsmCodes.Determination(e["mttfddet"]),
            b10d = e["mttfddet"] == "detB10D" ? e.Number("b10d") : null,
            nopPerYear = e["mttfddet"] == "detB10D" ? e.Number("nop") : null,
            dc = e["dcdet"] == "detMeasures" ? e.Number("dcmeasuredc") : e.Number("dc"),
            dcMeasure = NullIfEmpty(e["dcmeasure"]),
            manufacturer = NullIfEmpty(e["manufacturer"]),
            partNumber = NullIfEmpty(e["partno"]),
            description = T(e, "description")
        };

        // With "detSubItems" SISTEMA calculates the value at runtime only – the attribute then holds stale data.
        object Block(SsmRow b) => new
        {
            name = b.Name,
            mttfdYears = Stored(b, "mttfd", "mttfddet"),
            dc = Stored(b, b["dcdet"] == "detMeasures" ? "dcmeasuredc" : "dc", "dcdet"),
            elements = elements.Where(e => e["parentopoid"] == b.Oid).Select(Element).ToList()
        };

        object Channel(SsmRow ch) => new
        {
            type = SsmCodes.Channel(ch["channeltype"]),
            blocks = blocks.Where(b => b["parentopoid"] == ch.Oid).Select(Block).ToList()
        };

        object Component(SsmRow c) => new
        {
            name = c.Name,
            category = SsmCodes.Cat(c["cat"]),
            pl = c["pldet"] == "detDirect" ? SsmCodes.Pl(c["pldirectnobind"]) : SsmCodes.Pl(c["plsubitemscal"]),
            plFrom = SsmCodes.Determination(c["pldet"]),
            pfh = c["pldet"] == "detDirect" ? c.Number("pfh") : c.Number("pfhsubitemscal"),
            // for subsystems with PL entered directly these fields hold meaningless defaults
            mttfdYears = c["pldet"] == "detDirect" ? null : Stored(c, "mttfd", "mttfddet"),
            dcAvg = c["pldet"] == "detDirect" ? null : Stored(c, "dcavg", "dcavgdet"),
            missionTimeYears = c.Number("missiontime"),
            ccfMeasures = Split(c["ccfmeasures"]),
            categoryConditions = Split(c["catconditions"]),
            plConditions = Split(c["plconditions"]),
            manufacturer = NullIfEmpty(c["manufacturer"]),
            partNumber = NullIfEmpty(c["partno"]),
            documentation = T(c, "catdocumentation"),
            channels = channels.Where(ch => ch["componentopoid"] == c.Oid).Select(Channel).ToList()
        };

        object SafetyFunction(SsmRow sf) => new
        {
            id = NullIfEmpty(sf["sfid"]),
            name = sf.Name,
            plr = SsmCodes.Pl(sf["plr"]),
            pl = SsmCodes.Pl(sf["plcal"]),
            pfh = sf.Number("pfhcal"),
            plrReached = SsmCodes.PlReached(sf["plcal"], sf["plr"]),
            trigger = sf.Text("triggerevent"),
            reaction = T(sf, "reaction"),
            safeState = T(sf, "safestate"),
            subsystems = components.Where(c => c["sfopoid"] == sf.Oid).Select(Component).ToList()
        };

        var sfs = doc.Rows("sfops");
        return Json(new
        {
            file = path,
            modified = File.GetLastWriteTime(path),
            resultsStored = sfs.Any(s => s["plcal"] is { Length: > 0 } v && v != "plN"),
            project = new
            {
                name = project?.Name,
                machine = project?.Text("machinename"),
                author = project?["author"],
                tester = project?["tester"],
                standard = project?["normversion"],
                ssmVersion = project?["ssmversion"],
                documentation = project is null ? null : T(project, "documentation")
            },
            safetyFunctions = sfs.Select(SafetyFunction).ToList()
        });
    }

    [McpServerTool(Name = "sistema_open_in_sistema", ReadOnly = true, OpenWorld = false)]
    [Description("Opens a .ssm file in the installed SISTEMA application. SISTEMA calculates PL/PFH on opening; after the user saves, " +
                 "sistema_read_project returns the official results.")]
    public static string OpenInSistema([Description("Full path of the .ssm file.")] string path)
    {
        if (!File.Exists(path)) return $"File not found: {path}";
        if (SistemaEnvironment.SistemaExe is not { } exe)
            return "SISTEMA installation not found. Set SISTEMA_INSTALL_DIR to the folder containing SISTEMA.exe.";
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(Path.GetFullPath(path));
        // VS Code / Electron hosts may leak this variable; it breaks Electron based child processes
        psi.Environment.Remove("ELECTRON_RUN_AS_NODE");
        Process.Start(psi);
        return Json(new
        {
            started = exe,
            file = path,
            next = "Check the project in SISTEMA (messages pane), save it (Ctrl+S), then call sistema_read_project to get the calculated PL/PFH."
        });
    }

    /// <summary>
    /// MTTFd of an element. With "detB10D" the stored attribute is stale, so it is calculated
    /// (ISO 13849-1 Annex C: MTTFd = B10D / (0.1 · nop)).
    /// </summary>
    private static double? ElementMttfd(SsmRow e) => e["mttfddet"] switch
    {
        "detB10D" when e.Number("b10d") is > 0 and var b10d && e.Number("nop") is > 0 and var nop
            => Math.Round(b10d / (0.1 * nop), 1),
        "detB10D" or "detMTTF" => null,
        _ => e.Number("mttfd")
    };

    private static double? Stored(SsmRow r, string valueField, string detField) =>
        r[detField] is "detSubItems" or "detSubItemsSimple" ? null : r.Number(valueField);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    internal static List<string>? Split(string? list) =>
        string.IsNullOrWhiteSpace(list) ? null
            : list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
