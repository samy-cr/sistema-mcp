using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SistemaMcp.Library;

public sealed record ManufacturerEntry(string Key, string Name, string Url, bool Direct, string? Hint);

public sealed record LibrarySource(string Id, string Path, string Format, string Origin, int Components, string? Error);

/// <summary>
/// Library cache and index. Sources:
/// 1. downloaded/imported libraries in %LOCALAPPDATA%\SistemaMcp\libraries\&lt;id&gt;\ (VDMA 66413 XML, ZIP content, .slb),
/// 2. SISTEMA user libraries in Documents\SISTEMA\Libraries\*.slb,
/// 3. folders/files from SISTEMA_MCP_LIBRARY_PATHS.
/// Manufacturer data is never redistributed with the server – it is fetched on the user's machine.
/// </summary>
public sealed partial class LibraryStore
{
    private const long MaxDownloadBytes = 300L * 1024 * 1024;
    private readonly object _lock = new();
    private List<LibraryComponent>? _components;
    private List<LibrarySource> _sources = new();

    public static IReadOnlyList<ManufacturerEntry> Manufacturers { get; } = LoadManufacturers();

    public string PreferredLanguage { get; set; } = "de";

    public IReadOnlyList<LibraryComponent> Components
    {
        get { lock (_lock) { if (_components is null) Rebuild(); return _components!; } }
    }

    public IReadOnlyList<LibrarySource> Sources
    {
        get { lock (_lock) { if (_components is null) Rebuild(); return _sources; } }
    }

    public void Invalidate() { lock (_lock) _components = null; }

    public LibraryComponent? Find(string id) =>
        Components.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// All search words must match. A word matches the text, the text without spaces/hyphens ("AZM201" finds
    /// "AZM 201"), or – for order codes from a bill of materials – the type stem ("AZM201Z-I2-ST2-T-1P2PW" finds
    /// "AZM 201", "RSS260-D-LSTM12" finds "RSS 260"). Best matches (longest common prefix with the type) first.
    /// </summary>
    public IEnumerable<LibraryComponent> Search(string? query, string? manufacturer = null, string? function = null,
        string? kind = null, bool includeArchived = false)
    {
        var terms = (query ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<(LibraryComponent c, int score)>();
        foreach (var c in Components)
        {
            if (!includeArchived && c.Archived) continue;
            if (manufacturer is { Length: > 0 } &&
                !(c.Manufacturer ?? "").Contains(manufacturer, StringComparison.OrdinalIgnoreCase) &&
                !c.SourceId.Contains(manufacturer, StringComparison.OrdinalIgnoreCase)) continue;
            var text = c.SearchText;
            var compact = Compact(text);
            var ids = new[] { c.Identifier, c.PartNumber, c.Name }.Where(s => !string.IsNullOrEmpty(s)).Select(s => Compact(s!.ToLowerInvariant())).ToList();
            int score = 0;
            bool all = true;
            foreach (var t in terms)
            {
                var ct = Compact(t);
                if (text.Contains(t) || (ct.Length > 0 && compact.Contains(ct))) { score += 100; continue; }
                var stem = TypeStem(ct);
                var best = stem is null ? 0 : ids.Where(id => id.StartsWith(stem) && (id.Length == stem.Length || !char.IsDigit(id[stem.Length])))
                    .Select(id => CommonPrefix(id, ct)).DefaultIfEmpty(0).Max();
                if (best == 0) { all = false; break; }
                score += best;
            }
            if (!all) continue;
            if (function is { Length: > 0 } && !c.UseCases.Any(u => function.ToLowerInvariant() switch
                {
                    "input" => u.Input, "logic" => u.Logic, "output" => u.Output, _ => true
                })) continue;
            if (kind is { Length: > 0 } && !c.UseCases.Any(u => KindMatches(u.Kind, kind))) continue;
            hits.Add((c, score));
        }
        return hits.OrderByDescending(h => h.score).Select(h => h.c);
    }

    private static string Compact(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Letters followed by digits at the start of an order code, e.g. "azm201", "rss260", "bdf100".</summary>
    private static string? TypeStem(string compact)
    {
        var m = TypeStemRegex().Match(compact);
        return m.Success ? m.Value : null;
    }

    private static int CommonPrefix(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return i;
    }

    [GeneratedRegex("^[a-z]{2,}[0-9]+")]
    private static partial Regex TypeStemRegex();

    private static bool KindMatches(UseCaseKind k, string filter) => filter.ToLowerInvariant() switch
    {
        "subsystem" => k is UseCaseKind.SubsystemPl or UseCaseKind.SubsystemPlPfh,
        "element" => k is UseCaseKind.ElementMttfd or UseCaseKind.ElementB10d,
        "b10d" => k == UseCaseKind.ElementB10d,
        "mttfd" => k == UseCaseKind.ElementMttfd,
        _ => true
    };

    // ---------------- download / import ----------------

    public async Task<DownloadResult> DownloadAsync(string keyOrUrl, CancellationToken ct, string? sourceId = null)
    {
        var entry = Manufacturers.FirstOrDefault(m => m.Key.Equals(keyOrUrl, StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains(keyOrUrl, StringComparison.OrdinalIgnoreCase));
        var url = entry?.Url ?? keyOrUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return DownloadResult.Fail($"Unknown manufacturer or not an https URL: '{keyOrUrl}'. Use library_list_manufacturers.");

        // a file URL on a manufacturer's server gets the manufacturer key as source id (e.g. "schmersal")
        entry ??= Manufacturers.FirstOrDefault(m => uri.Host.Contains(m.Key.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        var id = Sanitize(sourceId ?? entry?.Key ?? Path.GetFileNameWithoutExtension(uri.AbsolutePath));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SistemaMcp", "0.1"));
        using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            return DownloadResult.Manual(url, $"HTTP {(int)resp.StatusCode} – download the library in the browser and import it with library_import.");
        if (resp.Content.Headers.ContentLength > MaxDownloadBytes)
            return DownloadResult.Fail("File larger than 300 MB, not downloaded.");

        var tmp = Path.Combine(Path.GetTempPath(), $"sistemamcp-{Guid.NewGuid():N}");
        try
        {
            await using (var fs = File.Create(tmp))
            await using (var s = await resp.Content.ReadAsStreamAsync(ct))
            {
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await s.ReadAsync(buffer, ct)) > 0)
                {
                    total += n;
                    if (total > MaxDownloadBytes) return DownloadResult.Fail("File larger than 300 MB, aborted.");
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }
            var kind = Sniff(tmp);
            if (kind == "html")
                return DownloadResult.Manual(url, entry?.Hint is { } h
                    ? $"This is a web page, not a file ({h}). Download the library in the browser and import it with library_import."
                    : "This is a web page, not a file. Download the library in the browser and import it with library_import.");
            if (kind is null)
                return DownloadResult.Fail("Downloaded content is neither ZIP, VDMA XML nor SLB.");
            var target = Path.Combine(SistemaEnvironment.LibraryCacheDir, id);
            var fileName = Path.GetFileName(uri.AbsolutePath) is { Length: > 0 } f ? f : $"{id}.{kind}";
            InstallFile(tmp, kind, target, fileName);
            Invalidate();
            return DownloadResult.Ok(id, target, Sources.FirstOrDefault(s => s.Id == id)?.Components ?? 0);
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
        }
    }

    /// <summary>Copies a local .zip, .xml, .slb or a folder into the cache.</summary>
    public LibrarySource Import(string path, string? id = null)
    {
        path = Path.GetFullPath(path);
        id = Sanitize(id ?? Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/')));
        var target = Path.Combine(SistemaEnvironment.LibraryCacheDir, id);
        if (Directory.Exists(path))
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyDirectory(path, target);
        }
        else if (File.Exists(path))
        {
            var kind = Sniff(path) ?? throw new InvalidDataException("File is neither ZIP, VDMA XML nor SLB.");
            if (kind == "html") throw new InvalidDataException("File is an HTML page, not a library.");
            InstallFile(path, kind, target, Path.GetFileName(path));
        }
        else throw new FileNotFoundException($"Not found: {path}");
        Invalidate();
        return Sources.FirstOrDefault(s => s.Id == id) ?? new LibrarySource(id, target, "?", "cache", 0, "nothing readable found");
    }

    public bool Remove(string id)
    {
        var target = Path.Combine(SistemaEnvironment.LibraryCacheDir, Sanitize(id));
        if (!Directory.Exists(target)) return false;
        Directory.Delete(target, true);
        Invalidate();
        return true;
    }

    private static void InstallFile(string file, string kind, string target, string fileName)
    {
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.CreateDirectory(target);
        if (kind == "zip") ExtractZipSafely(file, target);
        else File.Copy(file, Path.Combine(target, Sanitize(Path.GetFileNameWithoutExtension(fileName)) + "." + kind), true);
    }

    /// <summary>Extracts with path-traversal and size protection; nested ZIPs are extracted as well.</summary>
    private static void ExtractZipSafely(string zipPath, string target, int depth = 0)
    {
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        long total = 0;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue;
            var dest = Path.GetFullPath(Path.Combine(target, e.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            total += e.Length;
            if (total > 2 * MaxDownloadBytes) throw new InvalidDataException("ZIP content too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
            if (depth < 2 && dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractZipSafely(dest, Path.Combine(Path.GetDirectoryName(dest)!, Path.GetFileNameWithoutExtension(dest)), depth + 1);
                File.Delete(dest);
            }
        }
    }

    private static string? Sniff(string file)
    {
        var head = new byte[512];
        int n;
        using (var fs = File.OpenRead(file)) n = fs.Read(head, 0, head.Length);
        if (n >= 4 && head[0] == 'P' && head[1] == 'K') return "zip";
        var text = System.Text.Encoding.UTF8.GetString(head, 0, n).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (text.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "html";
        if (text.StartsWith("<")) return VdmaParser.IsVdmaFile(file) ? "xml" : text.Contains("<html", StringComparison.OrdinalIgnoreCase) ? "html" : null;
        // Firebird database header: page type 1 at offset 0, ODS version at offset 18
        if (n > 20 && head[0] == 1 && (head[18] | head[19] << 8) is >= 0x800A and <= 0x800F) return "slb";
        return null;
    }

    // ---------------- index ----------------

    private void Rebuild()
    {
        var comps = new List<LibraryComponent>();
        var sources = new List<LibrarySource>();
        var work = Path.Combine(SistemaEnvironment.DataDir, "work");

        void AddFile(string file, string id, string origin)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            try
            {
                List<LibraryComponent> parsed;
                string format;
                if (ext == ".slb") { format = "slb"; parsed = SlbReader.Parse(file, id, work); }
                else if (ext == ".xml" && VdmaParser.IsVdmaFile(file)) { format = "vdma66413"; parsed = VdmaParser.Parse(file, id, PreferredLanguage); }
                else return;
                comps.AddRange(parsed);
                sources.Add(new LibrarySource(id, file, format, origin, parsed.Count, null));
            }
            catch (Exception ex)
            {
                sources.Add(new LibrarySource(id, file, ext.TrimStart('.'), origin, 0, ex.Message));
            }
        }

        void AddFolder(string folder, string idPrefix, string origin)
        {
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slb", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f).ToList();
            foreach (var f in files)
            {
                var id = files.Count(x => x.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".slb", StringComparison.OrdinalIgnoreCase)) == 1
                    ? idPrefix : $"{idPrefix}:{Sanitize(Path.GetFileNameWithoutExtension(f))}";
                AddFile(f, id, origin);
            }
        }

        if (Directory.Exists(SistemaEnvironment.LibraryCacheDir))
            foreach (var dir in Directory.EnumerateDirectories(SistemaEnvironment.LibraryCacheDir))
                AddFolder(dir, Path.GetFileName(dir), "cache");

        var userLibs = Path.Combine(SistemaEnvironment.UserDir, "Libraries");
        if (Directory.Exists(userLibs))
            foreach (var slb in Directory.EnumerateFiles(userLibs, "*.slb"))
                AddFile(slb, "sistema:" + Sanitize(Path.GetFileNameWithoutExtension(slb)), "SISTEMA user library");

        foreach (var p in SistemaEnvironment.ExtraLibraryPaths)
        {
            var id = "path:" + Sanitize(Path.GetFileNameWithoutExtension(p.TrimEnd('\\', '/')));
            if (Directory.Exists(p)) AddFolder(p, id, "SISTEMA_MCP_LIBRARY_PATHS");
            else if (File.Exists(p)) AddFile(p, id, "SISTEMA_MCP_LIBRARY_PATHS");
        }

        // VDMA files may contain only a language part – those are not separate sources
        sources.RemoveAll(s => s.Format == "vdma66413" && s.Components == 0 && s.Error is null);
        _components = comps;
        _sources = sources;
    }

    private static IReadOnlyList<ManufacturerEntry> LoadManufacturers()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("vdma-manufacturers.json");
        if (s is null) return Array.Empty<ManufacturerEntry>();
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.GetProperty("manufacturers").EnumerateArray().Select(m => new ManufacturerEntry(
            m.GetProperty("key").GetString()!,
            m.GetProperty("name").GetString()!,
            m.GetProperty("url").GetString()!,
            m.TryGetProperty("direct", out var d) && d.GetBoolean(),
            m.TryGetProperty("hint", out var h) ? h.GetString() : null)).ToList();
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, true);
        }
    }

    internal static string Sanitize(string s)
    {
        var r = InvalidChars().Replace(s.Trim().ToLowerInvariant(), "-").Trim('-');
        return r.Length == 0 ? "library" : r.Length > 60 ? r[..60] : r;
    }

    [GeneratedRegex("[^a-z0-9._-]+")]
    private static partial Regex InvalidChars();
}

public sealed record DownloadResult(string Status, string? SourceId, string? Folder, int Components, string? Url, string Message)
{
    public static DownloadResult Ok(string id, string folder, int n) =>
        new("downloaded", id, folder, n, null, $"{n} components indexed.");
    public static DownloadResult Manual(string url, string msg) => new("manual-download-required", null, null, 0, url, msg);
    public static DownloadResult Fail(string msg) => new("failed", null, null, 0, null, msg);
}
