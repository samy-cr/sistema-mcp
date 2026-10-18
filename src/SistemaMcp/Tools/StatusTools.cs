using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using SistemaMcp.Library;

namespace SistemaMcp.Tools;

[McpServerToolType]
public static class StatusTools
{
    [McpServerTool(Name = "sistema_status", ReadOnly = true, OpenWorld = false)]
    [Description("Shows the server environment: server version, SISTEMA installation, project and library folders, SLB support and number of indexed library components. " +
                 "Also describes the recommended workflow.")]
    public static string Status(LibraryStore library) => ProjectTools.Json(new
    {
        server = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
        sistemaInstallDir = SistemaEnvironment.InstallDir ?? "not found (set SISTEMA_INSTALL_DIR)",
        projectsDir = SistemaEnvironment.ProjectsDir,
        libraryCache = SistemaEnvironment.LibraryCacheDir,
        slbSupport = SlbReader.CheckAvailable() ?? "available",
        librarySources = library.Sources.Count,
        libraryComponents = library.Components.Count,
        workflow = new[]
        {
            "1. sistema_get_spec_format – learn the JSON format.",
            "2. Collect machine and safety function data from the user; find devices with library_search (library_download / library_import first).",
            "3. sistema_validate_spec – ask the user ALL returned questions; never invent PLr, categories, DC, CCF or reliability values.",
            "4. sistema_create_project – writes the .ssm file.",
            "5. sistema_open_in_sistema – user checks and saves in SISTEMA.",
            "6. sistema_read_project – official PL/PFH results."
        },
        disclaimer = "Not affiliated with IFA/DGUV. Results must be verified by a competent person in SISTEMA."
    });
}
