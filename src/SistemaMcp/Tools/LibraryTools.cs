using System.ComponentModel;
using ModelContextProtocol.Server;
using SistemaMcp.Library;
using SistemaMcp.Spec;

namespace SistemaMcp.Tools;

[McpServerToolType]
public static class LibraryTools
{
    [McpServerTool(Name = "library_list_manufacturers", ReadOnly = true, OpenWorld = false)]
    [Description("Lists manufacturers that publish VDMA 66413 characteristic value libraries (VDMA reference list), with download URL, " +
                 "whether the server can download it directly ('direct') and whether it is already in the local cache.")]
    public static string ListManufacturers(LibraryStore library, [Description("Optional filter on the name.")] string? filter = null)
    {
        var cached = library.Sources.Select(s => s.Id.Split(':')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = LibraryStore.Manufacturers
            .Where(m => string.IsNullOrWhiteSpace(filter) || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || m.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(m => new { m.Key, m.Name, m.Url, direct = m.Direct, m.Hint, cached = cached.Contains(m.Key) });
        return ProjectTools.Json(new
        {
            manufacturers = list,
            note = "direct=false: the manufacturer offers the file on a web page (search, login or licence agreement). Ask the user to download it in the browser and call library_import with the downloaded file."
        });
    }

    [McpServerTool(Name = "library_download", Destructive = false, OpenWorld = true)]
    [Description("Downloads a manufacturer library (VDMA 66413 ZIP/XML or SISTEMA .slb) into the local cache and indexes it. " +
                 "Accepts a manufacturer key from library_list_manufacturers or an https URL of a library file. " +
                 "If the URL is a web page, the result says that a manual download is needed.")]
    public static async Task<string> Download(LibraryStore library,
        [Description("Manufacturer key (e.g. 'turck') or https URL.")] string manufacturerOrUrl,
        CancellationToken ct,
        [Description("Optional source id used in component ids (default: manufacturer key, e.g. 'schmersal').")] string? id = null)
    {
        try { return ProjectTools.Json(await library.DownloadAsync(manufacturerOrUrl, ct, id)); }
        catch (HttpRequestException ex) { return ProjectTools.Json(DownloadResult.Fail($"Download failed: {ex.Message}")); }
        catch (TaskCanceledException) { return ProjectTools.Json(DownloadResult.Fail("Download timed out.")); }
    }

    [McpServerTool(Name = "library_import", Destructive = false, OpenWorld = false)]
    [Description("Imports a library the user downloaded manually: a .zip, a VDMA 66413 .xml, a SISTEMA .slb or a folder. " +
                 "The content is copied into the local cache (the original is not changed) and indexed.")]
    public static string Import(LibraryStore library,
        [Description("Path of the file or folder.")] string path,
        [Description("Optional source id, e.g. the manufacturer key.")] string? id = null)
    {
        try { return ProjectTools.Json(library.Import(path, id)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ProjectTools.Json(new { imported = false, error = ex.Message });
        }
    }

    [McpServerTool(Name = "library_list_sources", ReadOnly = true, OpenWorld = false)]
    [Description("Lists all indexed library sources (cache, SISTEMA user libraries, SISTEMA_MCP_LIBRARY_PATHS) with number of components and read errors.")]
    public static string ListSources(LibraryStore library,
        [Description("Re-scan all sources (after files were added manually).")] bool refresh = false)
    {
        if (refresh) library.Invalidate();
        return ProjectTools.Json(new
        {
            cacheFolder = SistemaEnvironment.LibraryCacheDir,
            slbSupport = SlbReader.CheckAvailable() ?? "available",
            sources = library.Sources,
            totalComponents = library.Components.Count
        });
    }

    [McpServerTool(Name = "library_search", ReadOnly = true, OpenWorld = false)]
    [Description("Searches the indexed libraries by type, part number, name or description (all words must match). " +
                 "Returns component ids and a summary of the use cases. Use the id and use case index in the specification " +
                 "('library': {componentId, useCase}).")]
    public static string Search(LibraryStore library,
        [Description("Search words, e.g. 'PSEN 2.1' or '6814053' or 'emergency stop'.")] string? query = null,
        [Description("Filter manufacturer name or source id.")] string? manufacturer = null,
        [Description("input, logic or output.")] string? function = null,
        [Description("subsystem (PL/PFH), element, mttfd or b10d.")] string? kind = null,
        [Description("Maximum number of results (default 25).")] int limit = 25,
        [Description("Include archived (discontinued) devices.")] bool includeArchived = false)
    {
        var hits = library.Search(query, manufacturer, function, kind, includeArchived).ToList();
        return ProjectTools.Json(new
        {
            total = hits.Count,
            results = hits.Take(Math.Clamp(limit, 1, 200)).Select(c => new
            {
                id = c.Id,
                c.Manufacturer,
                c.Identifier,
                c.PartNumber,
                c.Name,
                c.Group,
                archived = c.Archived ? true : (bool?)null,
                useCases = c.UseCases.Select(u => $"{u.Index}: {SpecResolver.Describe(u)}")
            }),
            hint = library.Components.Count == 0 ? "No libraries indexed yet – use library_list_manufacturers and library_download / library_import." : null
        });
    }

    [McpServerTool(Name = "library_get_component", ReadOnly = true, OpenWorld = false)]
    [Description("Returns all data of a library component: device data, every use case with function, category, PL/PFH, MTTFd, B10D, RDF, mission time.")]
    public static string GetComponent(LibraryStore library, [Description("Component id from library_search.")] string id)
    {
        var c = library.Find(id);
        if (c is null) return $"Component '{id}' not found.";
        return ProjectTools.Json(new
        {
            c.Id, c.Manufacturer, c.Identifier, c.PartNumber, c.Revision, c.Name, c.Group, c.Description, c.Archived,
            library = new { c.LibraryName, c.LibraryVersion, c.LibraryUrl, c.Format, c.SourceFile },
            useCases = c.UseCases.Select(u => new
            {
                index = u.Index, path = u.Path, kind = u.Kind.ToString(),
                function = new { input = u.Input, logic = u.Logic, output = u.Output },
                category = u.Category, pl = u.Pl, sil = u.Sil, pfh = u.Pfh,
                mttfd = u.Mttfd, lambdaD = u.LambdaD, mttf = u.Mttf, mtbf = u.Mtbf, b10d = u.B10d, b10 = u.B10, rdf = u.Rdf,
                effectiveMttfd = u.Kind == UseCaseKind.ElementMttfd ? u.EffectiveMttfd : null,
                effectiveB10d = u.Kind == UseCaseKind.ElementB10d ? u.EffectiveB10d : null,
                dc = u.Dc, missionTime = u.MissionTime, technology = u.Technology,
                usage = u.Kind is UseCaseKind.SubsystemPl or UseCaseKind.SubsystemPlPfh
                    ? "use as subsystem: {\"library\": {\"componentId\": \"" + c.Id + "\", \"useCase\": " + u.Index + "}}"
                    : "use as element in a channel: {\"library\": {\"componentId\": \"" + c.Id + "\", \"useCase\": " + u.Index + "}}" +
                      (u.Kind == UseCaseKind.ElementB10d ? " plus nop or operatingProfile" : "")
            })
        });
    }
}
