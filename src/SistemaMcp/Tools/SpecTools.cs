using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using SistemaMcp.Knowledge;
using SistemaMcp.Library;
using SistemaMcp.Spec;

namespace SistemaMcp.Tools;

[McpServerToolType]
public static class SpecTools
{
    private const string SpecParam =
        "Project specification as JSON text or path to a .json file (format: sistema_get_spec_format).";

    [McpServerTool(Name = "sistema_get_spec_format", ReadOnly = true, OpenWorld = false)]
    [Description("Returns the JSON format used to describe a SISTEMA project (safety functions, subsystems, channels, components, library references) " +
                 "with a complete example. Call this first before writing a specification.")]
    public static string GetSpecFormat()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("spec-format.md");
        return s is null ? "spec-format.md not embedded." : new StreamReader(s).ReadToEnd();
    }

    [McpServerTool(Name = "sistema_validate_spec", ReadOnly = true, OpenWorld = false)]
    [Description("Checks a project specification without writing anything. Returns 'questions' (information that is still missing – ask the user, " +
                 "all at once, and never invent safety values), 'errors', 'warnings', 'assumptions' and a conservative PL estimate per safety function. " +
                 "Repeat until 'complete' is true, then call sistema_create_project.")]
    public static string ValidateSpec(LibraryStore library, [Description(SpecParam)] string spec)
    {
        var (resolver, project, parseError) = Resolve(library, spec);
        if (parseError is not null) return parseError;
        return ProjectTools.Json(Report(resolver!, project!));
    }

    [McpServerTool(Name = "sistema_create_project", Destructive = false, OpenWorld = false)]
    [Description("Generates a SISTEMA 3.0 project file (.ssm) from a complete specification. Refuses if questions or errors remain " +
                 "(see sistema_validate_spec) and never overwrites an existing file unless overwrite=true. Afterwards open it with " +
                 "sistema_open_in_sistema – SISTEMA performs the authoritative PL/PFH calculation.")]
    public static string CreateProject(LibraryStore library,
        [Description(SpecParam)] string spec,
        [Description("Target .ssm path. Empty = Documents\\SISTEMA\\Projects\\<project name>.ssm.")] string? outputPath = null,
        [Description("Overwrite an existing file (default false).")] bool overwrite = false)
    {
        var (resolver, project, parseError) = Resolve(library, spec);
        if (parseError is not null) return parseError;
        if (!resolver!.IsComplete)
            return ProjectTools.Json(new { created = false, reason = "Specification incomplete – ask the user for the missing information.", report = Report(resolver, project!) });

        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(SistemaEnvironment.ProjectsDir, LibraryStore.Sanitize(project!.Info.Name ?? "project") + ".ssm")
            : Path.GetFullPath(outputPath);
        if (!path.EndsWith(".ssm", StringComparison.OrdinalIgnoreCase)) path += ".ssm";
        if (File.Exists(path) && !overwrite)
            return ProjectTools.Json(new { created = false, reason = $"File exists: {path}. Choose another path or set overwrite=true after asking the user." });

        ProjectBuilder.Build(project!, path).Save(path);
        return ProjectTools.Json(new
        {
            created = true,
            path,
            report = Report(resolver, project!),
            next = "Call sistema_open_in_sistema, let the user check the messages in SISTEMA and save; then sistema_read_project returns SISTEMA's results."
        });
    }

    [McpServerTool(Name = "sistema_list_measures", ReadOnly = true, OpenWorld = false)]
    [Description("Lists the keys that can be used in a specification: CCF measures with points (ISO 13849-1 Annex F, min. 65 points for categories 2–4), " +
                 "DC measures with diagnostic coverage (Annex E), category requirements and the risk graph (Annex A).")]
    public static string ListMeasures([Description("Text language: en or de (default de).")] string language = "de")
    {
        bool de = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        return ProjectTools.Json(new
        {
            ccfMeasures = Iso13849.CcfMeasures.Select(m => new { key = m.Key, points = m.Points, group = m.Group, text = de ? m.TextDe : m.TextEn }),
            ccfMinimumPoints = Iso13849.CcfMinimumPoints,
            dcMeasures = Iso13849.DcMeasures.Select(m => new
            {
                key = m.Key, part = m.Part,
                dc = Math.Abs(m.DcMax - m.DcMin) < 1e-9 ? $"{m.DcMax:P0}" : $"{m.DcMin:P0}–{m.DcMax:P0} (justify the value)",
                text = de ? m.TextDe : m.TextEn
            }),
            categories = Iso13849.CategoryRequirementText.Select(kv => new { category = kv.Key[3..], requirements = kv.Value }),
            riskGraph = Iso13849.PlrFromRiskGraph.Select(kv => new { parameters = kv.Key, plr = kv.Value }),
            classes = new
            {
                mttfd = "low 3–<10 a, medium 10–<30 a, high 30–100 a (category 4: up to 2500 a)",
                dcAvg = "none <60 %, low 60–<90 %, medium 90–<99 %, high ≥99 %",
                pfh = "a <1E-4, b <1E-5, c <3E-6, d <1E-6, e <1E-7 (per hour)"
            }
        });
    }

    private static (SpecResolver?, ResolvedProject?, string?) Resolve(LibraryStore library, string spec)
    {
        ProjectSpec parsed;
        try { parsed = ProjectSpec.Parse(spec); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (null, null, ProjectTools.Json(new { complete = false, error = $"Specification could not be read: {ex.Message}", format = "see sistema_get_spec_format" }));
        }
        var resolver = new SpecResolver(library);
        var project = resolver.Resolve(parsed);
        return (resolver, project, null);
    }

    private static object Report(SpecResolver r, ResolvedProject p) => new
    {
        complete = r.IsComplete,
        questions = r.Questions,
        errors = r.Errors,
        warnings = r.Warnings,
        assumptions = r.Assumptions,
        estimate = r.Errors.Count == 0 ? Estimator.Estimate(p) : null,
        estimateNote = "Conservative estimate (simplified procedure of ISO 13849-1). The authoritative result is calculated by SISTEMA."
    };
}
