namespace SistemaMcp;

/// <summary>
/// Locations used by the server. Overridable with environment variables:
/// SISTEMA_INSTALL_DIR, SISTEMA_MCP_DATA_DIR (cache for downloaded libraries),
/// SISTEMA_MCP_LIBRARY_PATHS (additional folders or files with libraries, separated by ';').
/// </summary>
public static class SistemaEnvironment
{
    public static string? InstallDir { get; } = FindInstallDir();

    public static string? SistemaExe =>
        InstallDir is null ? null : Path.Combine(InstallDir, "SISTEMA.exe") is var p && File.Exists(p) ? p : null;

    public static string? FirebirdClient =>
        InstallDir is null ? null : Path.Combine(InstallDir, "Firebird", "fb50", "fbclient.dll") is var p && File.Exists(p) ? p : null;

    /// <summary>Documents\SISTEMA – default folders for projects and libraries.</summary>
    public static string UserDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SISTEMA");

    public static string ProjectsDir => Path.Combine(UserDir, "Projects");

    public static string DataDir =>
        Environment.GetEnvironmentVariable("SISTEMA_MCP_DATA_DIR") is { Length: > 0 } d ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SistemaMcp");

    public static string LibraryCacheDir => Path.Combine(DataDir, "libraries");

    public static IEnumerable<string> ExtraLibraryPaths =>
        (Environment.GetEnvironmentVariable("SISTEMA_MCP_LIBRARY_PATHS") ?? "")
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? FindInstallDir()
    {
        if (Environment.GetEnvironmentVariable("SISTEMA_INSTALL_DIR") is { Length: > 0 } env && Directory.Exists(env))
            return env;
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        // newest version first, e.g. "SISTEMA 3.0.4"
        return roots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateDirectories(r, "SISTEMA*"))
            .Where(d => File.Exists(Path.Combine(d, "SISTEMA.exe")))
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
