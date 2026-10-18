namespace SistemaMcp.Spec;

/// <summary>Validated, fully determined project – the input for the .ssm writer and the estimator.</summary>
public sealed class ResolvedProject
{
    public ProjectInfo Info { get; init; } = new();
    public List<ResolvedSafetyFunction> SafetyFunctions { get; } = new();
}

public sealed class ResolvedSafetyFunction
{
    public string? Id { get; init; }
    public string Name { get; init; } = "";
    public string Plr { get; init; } = "";
    public string? PlrDocumentation { get; init; }
    public SafetyFunctionSpec Spec { get; init; } = new();
    public List<ResolvedSubsystem> Subsystems { get; } = new();
}

public sealed class ResolvedSubsystem
{
    public string Name { get; init; } = "";
    public string? Role { get; init; }
    public bool IsDirect { get; init; }
    public string CatCode { get; init; } = "catB";
    /// <summary>False while the category is still being asked for; CatCode is then only a placeholder.</summary>
    public bool CategoryKnown { get; init; } = true;
    public string? DirectPl { get; init; }
    public double? DirectPfh { get; init; }
    public double MissionTime { get; init; } = 20;
    public bool CategoryRequirementsMet { get; init; }
    public List<string> CcfIds { get; init; } = new();
    public int CcfPoints { get; init; }
    public string? CcfDocumentation { get; init; }
    public string? CategoryDocumentation { get; init; }
    public string? Description { get; init; }
    public LibraryInfo? Library { get; init; }
    public List<ResolvedElement> Ch1 { get; } = new();
    public List<ResolvedElement> Ch2 { get; } = new();
    public List<ResolvedElement> Test { get; } = new();
}

public sealed class ResolvedElement
{
    public string Name { get; init; } = "";
    public string TechCode { get; init; } = "tecUnknown";
    /// <summary>MTTFd entered directly (or converted from MTTF/lambda).</summary>
    public double? Mttfd { get; init; }
    public double? B10d { get; init; }
    public double? Nop { get; init; }
    public double Dc { get; init; }
    public string? DcDocumentation { get; init; }
    public string? MttfdDocumentation { get; init; }
    public string? Description { get; init; }
    public LibraryInfo? Library { get; init; }

    public double? EffectiveMttfd => Mttfd ?? (B10d is > 0 && Nop is > 0 ? B10d / (0.1 * Nop) : null);

    /// <summary>T10D = B10D / nop: after this time the component has to be replaced.</summary>
    public double? T10d => B10d is > 0 && Nop is > 0 ? B10d / Nop : null;
}

public sealed class LibraryInfo
{
    public string? ComponentId { get; init; }
    public string? Manufacturer { get; init; }
    public string? DeviceId { get; init; }
    public string? PartNumber { get; init; }
    public string? Revision { get; init; }
    public string? Group { get; init; }
    public string? LibraryName { get; init; }
    public string? LibraryVersion { get; init; }
    public string? LibraryUrl { get; init; }
    public string? LibraryFile { get; init; }
    public string? UseCase { get; init; }
    public string? Description { get; init; }
}

/// <summary>Information the user still has to provide.</summary>
public sealed record Question(string Path, string Field, string Text, string? Hint = null, IReadOnlyList<string>? Options = null);

public sealed record Finding(string Path, string Text);
