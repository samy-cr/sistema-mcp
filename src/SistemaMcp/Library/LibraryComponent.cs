namespace SistemaMcp.Library;

/// <summary>How a use case of a library device enters a SISTEMA project.</summary>
public enum UseCaseKind
{
    /// <summary>Complete subsystem with PL and PFH (VDMA DeviceType 1, SISTEMA subsystem "entered directly").</summary>
    SubsystemPlPfh,
    /// <summary>Subsystem with PL only (VDMA DeviceType 4).</summary>
    SubsystemPl,
    /// <summary>Element with MTTFd, lambda_d, MTTF or MTBF (VDMA DeviceType 2).</summary>
    ElementMttfd,
    /// <summary>Element with B10D or B10 (VDMA DeviceType 3).</summary>
    ElementB10d
}

public sealed class LibraryUseCase
{
    public int Index { get; init; }
    public string Path { get; init; } = "";
    public bool Input { get; init; }
    public bool Logic { get; init; }
    public bool Output { get; init; }
    public string? Category { get; init; }
    public UseCaseKind Kind { get; init; }
    public string? Pl { get; init; }
    public string? Sil { get; init; }
    public double? Pfh { get; init; }
    public double? Mttfd { get; init; }
    public double? LambdaD { get; init; }
    public double? Mttf { get; init; }
    public double? Mtbf { get; init; }
    /// <summary>Ratio of dangerous failures as a fraction (0..1).</summary>
    public double? Rdf { get; init; }
    public double? B10d { get; init; }
    public double? B10 { get; init; }
    public double? Dc { get; init; }
    public double? MissionTime { get; init; }
    public string? Technology { get; init; }

    /// <summary>MTTFd in years, derived from whatever the manufacturer specified.</summary>
    public double? EffectiveMttfd =>
        Mttfd ?? (LambdaD is > 0 ? 1 / (LambdaD.Value * 8760) : (double?)null)
               ?? (Mttf is > 0 ? Mttf / (Rdf ?? 0.5) : null)
               ?? (Mtbf is > 0 ? Mtbf / (Rdf ?? 0.5) : null);

    public double? EffectiveB10d => B10d ?? (B10 is > 0 ? B10 / (Rdf ?? 0.5) : null);
}

public sealed class LibraryComponent
{
    public string Id { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string Format { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public string? LibraryName { get; init; }
    public string? LibraryVersion { get; init; }
    public string? LibraryUrl { get; init; }
    public string? Manufacturer { get; init; }
    public string Identifier { get; init; } = "";
    public string? PartNumber { get; init; }
    public string? Revision { get; init; }
    public string? Name { get; init; }
    public string? Group { get; init; }
    public string? Description { get; init; }
    public string? DocumentFile { get; init; }
    public bool Archived { get; init; }
    public List<LibraryUseCase> UseCases { get; init; } = new();

    public string SearchText =>
        string.Join(' ', Manufacturer, Identifier, PartNumber, Name, Group, Description,
            string.Join(' ', UseCases.Select(u => u.Path))).ToLowerInvariant();
}
