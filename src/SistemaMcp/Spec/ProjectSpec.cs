using System.Text.Json;
using System.Text.Json.Serialization;

namespace SistemaMcp.Spec;

/// <summary>
/// Input format for generating a SISTEMA project. Everything that is not given is asked for by the
/// validator instead of being guessed – safety-relevant values are never filled in silently.
/// See docs/spec-format.md.
/// </summary>
public sealed class ProjectSpec
{
    public ProjectInfo? Project { get; set; }
    /// <summary>Defaults inherited by all subsystems and elements.</summary>
    public SpecDefaults? Defaults { get; set; }
    public List<SafetyFunctionSpec>? SafetyFunctions { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Accepts JSON text or a path to a JSON file.</summary>
    public static ProjectSpec Parse(string specOrPath)
    {
        var text = specOrPath.TrimStart().StartsWith('{') ? specOrPath : File.ReadAllText(specOrPath);
        return JsonSerializer.Deserialize<ProjectSpec>(text, JsonOptions)
               ?? throw new JsonException("Empty specification.");
    }
}

public sealed class ProjectInfo
{
    public string? Name { get; set; }
    public string? Machine { get; set; }
    public string? Author { get; set; }
    public string? Tester { get; set; }
    public string? Manager { get; set; }
    public string? Documentation { get; set; }
    public string? Number { get; set; }
    public string? Version { get; set; }
    public string? Status { get; set; }
}

public sealed class SpecDefaults
{
    public double? MissionTimeYears { get; set; }
    public OperatingProfile? OperatingProfile { get; set; }
}

/// <summary>Used to calculate the number of operations per year (nop) for B10D components.</summary>
public sealed class OperatingProfile
{
    public double? DaysPerYear { get; set; }
    public double? HoursPerDay { get; set; }
    /// <summary>Mean time between two operations of the component, in seconds.</summary>
    public double? CycleTimeSeconds { get; set; }
    /// <summary>Alternative to cycleTimeSeconds.</summary>
    public double? OperationsPerHour { get; set; }

    public double? Nop =>
        DaysPerYear is > 0 && HoursPerDay is > 0
            ? CycleTimeSeconds is > 0 ? DaysPerYear * HoursPerDay * 3600 / CycleTimeSeconds
            : OperationsPerHour is > 0 ? DaysPerYear * HoursPerDay * OperationsPerHour : null
            : null;
}

public sealed class RiskGraph
{
    /// <summary>S1 (slight injury) or S2 (serious injury).</summary>
    public string? Severity { get; set; }
    /// <summary>F1 (seldom/short exposure) or F2 (frequent/long exposure).</summary>
    public string? Frequency { get; set; }
    /// <summary>P1 (avoidance possible) or P2 (scarcely possible).</summary>
    public string? Possibility { get; set; }
}

public sealed class SafetyFunctionSpec
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    /// <summary>Required performance level a–e. Alternative: riskGraph.</summary>
    public string? Plr { get; set; }
    public RiskGraph? RiskGraph { get; set; }
    public string? PlrJustification { get; set; }
    public string? Trigger { get; set; }
    public string? Reaction { get; set; }
    public string? SafeState { get; set; }
    public string? OperatingMode { get; set; }
    public string? DemandRate { get; set; }
    public string? ResponseTime { get; set; }
    public string? Documentation { get; set; }
    public List<SubsystemSpec>? Subsystems { get; set; }
}

public sealed class SubsystemSpec
{
    public string? Name { get; set; }
    /// <summary>input, logic or output – used for hints only.</summary>
    public string? Role { get; set; }
    /// <summary>A complete subsystem from a library (VDMA DeviceType 1/4 use case).</summary>
    public LibraryRef? Library { get; set; }
    /// <summary>A subsystem with PL/PFH taken from a data sheet.</summary>
    public DirectSubsystem? Direct { get; set; }
    /// <summary>Designated architecture B, 1, 2, 3 or 4.</summary>
    public string? Category { get; set; }
    public string? CategoryJustification { get; set; }
    /// <summary>The user confirms the requirements of the category (well-tried principles, fault detection …).</summary>
    public bool? CategoryRequirementsMet { get; set; }
    /// <summary>CCF measures (keys from sistema_list_measures), required for category 2, 3, 4.</summary>
    public List<string>? CcfMeasures { get; set; }
    public string? CcfJustification { get; set; }
    public double? MissionTimeYears { get; set; }
    public OperatingProfile? OperatingProfile { get; set; }
    public ChannelSet? Channels { get; set; }
    public string? Description { get; set; }
}

public sealed class DirectSubsystem
{
    public string? Pl { get; set; }
    public double? Pfh { get; set; }
    public string? Category { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? Source { get; set; }
}

public sealed class ChannelSet
{
    public List<ElementSpec>? Ch1 { get; set; }
    public List<ElementSpec>? Ch2 { get; set; }
    public List<ElementSpec>? Test { get; set; }
}

public sealed class LibraryRef
{
    /// <summary>Component id from library_search.</summary>
    public string? ComponentId { get; set; }
    /// <summary>Index of the use case (library_get_component). Required when the device has several.</summary>
    public int? UseCase { get; set; }
}

/// <summary>A block with one element (SISTEMA "BL" + "EL").</summary>
public sealed class ElementSpec
{
    public string? Name { get; set; }
    public string? Technology { get; set; }
    public LibraryRef? Library { get; set; }
    public double? Mttfd { get; set; }
    public double? B10d { get; set; }
    /// <summary>Operations per year; alternatively operatingProfile.</summary>
    public double? Nop { get; set; }
    public OperatingProfile? OperatingProfile { get; set; }
    /// <summary>Diagnostic coverage 0…0.99 (or 0…99 %).</summary>
    public double? Dc { get; set; }
    /// <summary>Measure key from sistema_list_measures; sets dc if dc is not given and the measure has a fixed value.</summary>
    public string? DcMeasure { get; set; }
    public string? DcJustification { get; set; }
    public string? MttfdJustification { get; set; }
    public string? Description { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
}
