using SistemaMcp.Knowledge;
using SistemaMcp.Library;
using SistemaMcp.Ssm;

namespace SistemaMcp.Spec;

/// <summary>
/// Checks a <see cref="ProjectSpec"/>, resolves library references and collects everything that is
/// missing as questions. Nothing safety-relevant is assumed; the only default is the mission time of
/// 20 years given by ISO 13849-1, which is reported as an assumption.
/// </summary>
public sealed class SpecResolver
{
    private readonly LibraryStore? _library;
    public List<Question> Questions { get; } = new();
    public List<Finding> Errors { get; } = new();
    public List<Finding> Warnings { get; } = new();
    public List<Finding> Assumptions { get; } = new();

    public SpecResolver(LibraryStore? library) => _library = library;

    public bool IsComplete => Questions.Count == 0 && Errors.Count == 0;

    public ResolvedProject Resolve(ProjectSpec spec)
    {
        var info = spec.Project ?? new ProjectInfo();
        if (string.IsNullOrWhiteSpace(info.Name))
            Ask("project", "name", "What is the name of the SISTEMA project?", "Usually machine name and type, e.g. \"Assembly cell MZ-100\".");
        var project = new ResolvedProject { Info = info };

        if (spec.SafetyFunctions is not { Count: > 0 })
        {
            Ask("safetyFunctions", "safetyFunctions", "Which safety functions does the machine have?",
                "For each: name, trigger event, reaction, safe state, required PL (or risk graph S/F/P) and the parts that perform it (sensors, logic, actuators).");
            return project;
        }

        for (int i = 0; i < spec.SafetyFunctions.Count; i++)
            project.SafetyFunctions.Add(ResolveSf(spec.SafetyFunctions[i], $"safetyFunctions[{i}]", spec.Defaults));
        return project;
    }

    private ResolvedSafetyFunction ResolveSf(SafetyFunctionSpec sf, string path, SpecDefaults? defaults)
    {
        var label = sf.Name ?? sf.Id ?? path;
        if (string.IsNullOrWhiteSpace(sf.Name))
            Ask(path, "name", $"Name of safety function {sf.Id ?? path}?", "e.g. \"Emergency stop\", \"Guard door interlocking\".");

        string plr = "";
        string? plrDoc = sf.PlrJustification;
        if (!string.IsNullOrWhiteSpace(sf.Plr))
        {
            plr = NormalizePl(sf.Plr, path, "plr") ?? "";
        }
        else if (sf.RiskGraph is { } rg)
        {
            var s = Norm(rg.Severity, "S"); var f = Norm(rg.Frequency, "F"); var p = Norm(rg.Possibility, "P");
            if (s is null) Ask(path, "riskGraph.severity", $"\"{label}\": severity of injury?", null, new[] { "S1 (slight, reversible)", "S2 (serious, irreversible or death)" });
            if (f is null) Ask(path, "riskGraph.frequency", $"\"{label}\": frequency/duration of exposure to the hazard?", null, new[] { "F1 (seldom to less often and/or short)", "F2 (frequent to continuous and/or long)" });
            if (p is null) Ask(path, "riskGraph.possibility", $"\"{label}\": possibility of avoiding the hazard or limiting the harm?", null, new[] { "P1 (possible under specific conditions)", "P2 (scarcely possible)" });
            if (s is not null && f is not null && p is not null)
            {
                plr = Iso13849.PlrFromRiskGraph[s + f + p];
                plrDoc = $"Risk graph ISO 13849-1 Annex A: {s} {f} {p} → PLr {plr}." + (plrDoc is null ? "" : "\n" + plrDoc);
            }
        }
        else
        {
            Ask(path, "plr", $"\"{label}\": required performance level PLr (a–e), or the risk graph parameters S/F/P?",
                "From the risk assessment. Risk graph: S1/S2 severity, F1/F2 exposure, P1/P2 possibility of avoidance.");
        }

        foreach (var (field, value, q) in new[]
                 {
                     ("trigger", sf.Trigger, "What triggers the safety function (e.g. opening the guard door)?"),
                     ("reaction", sf.Reaction, "What is the reaction (e.g. motor M1 is switched off, STO)?"),
                     ("safeState", sf.SafeState, "What is the safe state (e.g. standstill, drive torque-free)?")
                 })
            if (string.IsNullOrWhiteSpace(value)) Ask(path, field, $"\"{label}\": {q}");

        var result = new ResolvedSafetyFunction
        {
            Id = sf.Id, Name = sf.Name ?? "", Plr = plr, PlrDocumentation = plrDoc, Spec = sf
        };

        if (sf.Subsystems is not { Count: > 0 })
        {
            Ask(path, "subsystems", $"\"{label}\": which parts perform the safety function?",
                "Usually input (sensor), logic and output (actuator) subsystems. For each: device (manufacturer/type or library id), category, channel structure, diagnostics.");
            return result;
        }

        for (int i = 0; i < sf.Subsystems.Count; i++)
            if (ResolveSubsystem(sf.Subsystems[i], $"{path}.subsystems[{i}]", label, defaults) is { } ss)
                result.Subsystems.Add(ss);
        return result;
    }

    private ResolvedSubsystem? ResolveSubsystem(SubsystemSpec ss, string path, string sfLabel, SpecDefaults? defaults)
    {
        var label = $"{sfLabel} / {ss.Name ?? path}";
        if (string.IsNullOrWhiteSpace(ss.Name)) Ask(path, "name", $"Name of subsystem {path} in \"{sfLabel}\"?");

        var mission = ss.MissionTimeYears ?? defaults?.MissionTimeYears;
        if (mission is null)
        {
            mission = 20;
            Assumptions.Add(new(path, "Mission time 20 years assumed (ISO 13849-1 default)."));
        }
        var profile = ss.OperatingProfile ?? defaults?.OperatingProfile;

        // ---- complete subsystem from library or data sheet ----
        if (ss.Library is { } lib)
        {
            var (comp, uc) = ResolveLibrary(lib, path, label);
            if (comp is null || uc is null) return null;
            if (uc.Kind is UseCaseKind.ElementB10d or UseCaseKind.ElementMttfd)
            {
                Error(path, $"\"{label}\": library use case {uc.Index} of {comp.Identifier} is an element (MTTFd/B10D), not a subsystem – put it into channels.ch1/ch2 as an element and give the category.");
                return null;
            }
            if (uc.Pl is null) { Error(path, $"\"{label}\": library use case has no PL."); return null; }
            var catCode = uc.Category is { } c ? SsmCodes.CatCode(c) : ss.Category is { } c2 ? SafeCat(c2, path) : null;
            if (catCode is null)
                Ask(path, "category", $"\"{label}\": the library entry gives no category – which category does the manufacturer state?", null, new[] { "B", "1", "2", "3", "4" });
            if (uc.MissionTime is { } tm && tm < mission)
                Warn(path, $"\"{label}\": manufacturer mission time {tm} years is shorter than {mission} years.");
            CheckPlAgainstCategory(path, label, uc.Pl, catCode, fromLibrary: true);
            return new ResolvedSubsystem
            {
                Name = ss.Name ?? comp.Name ?? comp.Identifier, Role = ss.Role, IsDirect = true,
                CatCode = catCode ?? "catB", CategoryKnown = catCode is not null,
                DirectPl = uc.Pl, DirectPfh = uc.Pfh, MissionTime = uc.MissionTime ?? mission.Value,
                Description = ss.Description ?? comp.Description, Library = Info(comp, uc), CategoryRequirementsMet = true,
                CategoryDocumentation = $"PL {uc.Pl}{(uc.Pfh is { } pfh ? $", PFH {pfh:0.##E+0} 1/h" : "")}, category {SsmCodes.Cat(catCode)} according to manufacturer library {comp.LibraryName} {comp.LibraryVersion}."
            };
        }

        if (ss.Direct is { } d)
        {
            var pl = string.IsNullOrWhiteSpace(d.Pl) ? null : NormalizePl(d.Pl, path, "direct.pl");
            if (string.IsNullOrWhiteSpace(d.Pl)) Ask(path, "direct.pl", $"\"{label}\": PL of the device according to the manufacturer?");
            if (d.Pfh is null) Ask(path, "direct.pfh", $"\"{label}\": PFH (1/h) of the device according to the manufacturer?", "Needed to calculate the PFH of the safety function. Data sheet / safety manual.");
            var catText = d.Category ?? ss.Category;
            if (catText is null) Ask(path, "direct.category", $"\"{label}\": category according to the manufacturer?", null, new[] { "B", "1", "2", "3", "4" });
            if (d.Pfh is { } pfhv && pl is not null && Iso13849.PlFromPfh(pfhv) is { } fromPfh && fromPfh != pl)
                Warn(path, $"\"{label}\": PFH {pfhv:0.##E+0} corresponds to PL {fromPfh}, but PL {pl} was given.");
            var directCat = catText is null ? null : SafeCat(catText, path);
            CheckPlAgainstCategory(path, label, pl, directCat, fromLibrary: false);
            return new ResolvedSubsystem
            {
                Name = ss.Name ?? "", Role = ss.Role, IsDirect = true,
                CatCode = directCat ?? "catB", CategoryKnown = directCat is not null,
                DirectPl = pl, DirectPfh = d.Pfh, MissionTime = mission.Value, CategoryRequirementsMet = true,
                Description = ss.Description,
                Library = d.Manufacturer is null && d.PartNumber is null ? null : new LibraryInfo { Manufacturer = d.Manufacturer, PartNumber = d.PartNumber },
                CategoryDocumentation = d.Source is null ? "PL/PFH according to manufacturer data." : $"PL/PFH according to {d.Source}."
            };
        }

        // ---- subsystem built from elements ----
        string? cat = null;
        if (string.IsNullOrWhiteSpace(ss.Category))
            Ask(path, "category", $"\"{label}\": which category (designated architecture) does the subsystem have?",
                "B/1: single channel; 2: single channel + test equipment; 3/4: two channels with fault detection.", new[] { "B", "1", "2", "3", "4" });
        else cat = SafeCat(ss.Category, path);

        // With an unknown category nothing is concluded from it; the dependent questions are asked in the same
        // round, phrased conditionally, so the user does not get a second round only because of the category.
        bool catUnknown = cat is null;

        if (ss.CategoryRequirementsMet is null)
        {
            if (cat is not null)
                Ask(path, "categoryRequirementsMet", $"\"{label}\": are the requirements of category {SsmCodes.Cat(cat)} fulfilled?",
                    Iso13849.CategoryRequirementText[cat], new[] { "yes", "no" });
            else
                Ask(path, "categoryRequirementsMet", $"\"{label}\": are the requirements of the chosen category fulfilled?",
                    string.Join(" | ", Iso13849.CategoryRequirementText.Select(kv => $"{SsmCodes.Cat(kv.Key)}: {kv.Value}")), new[] { "yes", "no" });
        }
        else if (ss.CategoryRequirementsMet == false)
            Error(path, $"\"{label}\": category requirements not met – choose a lower category or change the design.");

        var ccfIds = new List<string>();
        int ccfPoints = 0;
        if (cat is "cat2" or "cat3" or "cat4" || (catUnknown && ss.CcfMeasures is { Count: > 0 }))
        {
            if (ss.CcfMeasures is not { Count: > 0 })
                Ask(path, "ccfMeasures", $"\"{label}\": which measures against common cause failure (CCF) are applied? At least {Iso13849.CcfMinimumPoints} points are required.",
                    string.Join("; ", Iso13849.CcfMeasures.Select(m => $"{m.Key} ({m.Points}): {m.TextEn}")),
                    Iso13849.CcfMeasures.Select(m => m.Key).ToList());
            else
            {
                foreach (var m in ss.CcfMeasures)
                {
                    var found = Iso13849.FindCcf(m);
                    if (found is null) { Error(path, $"Unknown CCF measure '{m}'. Valid: {string.Join(", ", Iso13849.CcfMeasures.Select(x => x.Key))}."); continue; }
                    if (ccfIds.Contains(found.SistemaId)) continue;
                    ccfIds.Add(found.SistemaId);
                    ccfPoints += found.Points;
                }
                if (ccfPoints < Iso13849.CcfMinimumPoints && !catUnknown)
                    Error(path, $"\"{label}\": CCF score {ccfPoints} < {Iso13849.CcfMinimumPoints} points – category {SsmCodes.Cat(cat)} is not reached. Add measures or reduce the category.");
            }
        }
        else if (catUnknown)
            Ask(path, "ccfMeasures", $"\"{label}\": only if the category is 2, 3 or 4 – which CCF measures are applied (at least {Iso13849.CcfMinimumPoints} points)?",
                string.Join("; ", Iso13849.CcfMeasures.Select(m => $"{m.Key} ({m.Points}): {m.TextEn}")),
                Iso13849.CcfMeasures.Select(m => m.Key).ToList());

        var result = new ResolvedSubsystem
        {
            Name = ss.Name ?? "", Role = ss.Role, IsDirect = false, CatCode = cat ?? "catB", CategoryKnown = !catUnknown, MissionTime = mission.Value,
            CategoryRequirementsMet = ss.CategoryRequirementsMet == true, CcfIds = ccfIds, CcfPoints = ccfPoints,
            CcfDocumentation = ss.CcfJustification, CategoryDocumentation = ss.CategoryJustification, Description = ss.Description
        };

        var ch = ss.Channels;
        bool twoChannels = cat is "cat3" or "cat4";
        if (ch?.Ch1 is not { Count: > 0 })
            Ask(path, "channels.ch1", $"\"{label}\": which components are in channel 1?",
                "Each component: name, technology and MTTFd, B10D (+ operations) or a library id" + (cat is "catB" or "cat1" ? "." : ", plus its diagnostic coverage DC."));
        if (twoChannels && ch?.Ch2 is not { Count: > 0 })
            Ask(path, "channels.ch2", $"\"{label}\": category {SsmCodes.Cat(cat)} needs two channels – which components are in channel 2?");
        if (!twoChannels && !catUnknown && ch?.Ch2 is { Count: > 0 })
            Warn(path, $"\"{label}\": channel 2 is ignored for category {SsmCodes.Cat(cat!)}.");
        if (cat == "cat2" && ch?.Test is not { Count: > 0 })
            Ask(path, "channels.test", $"\"{label}\": category 2 needs test equipment – which components form the test channel (TE/OTE)?");

        // unknown category: resolve all given channels so their questions come in this round
        bool needDc = cat is "cat2" or "cat3" or "cat4" || catUnknown;
        void Add(List<ElementSpec>? list, List<ResolvedElement> target, string chName, bool dcRelevant)
        {
            if (list is null) return;
            for (int i = 0; i < list.Count; i++)
                if (ResolveElement(list[i], $"{path}.channels.{chName}[{i}]", label, profile, dcRelevant, mission.Value) is { } e)
                    target.Add(e);
        }
        Add(ch?.Ch1, result.Ch1, "ch1", needDc);
        if (twoChannels || catUnknown) Add(ch?.Ch2, result.Ch2, "ch2", needDc);
        if (cat == "cat2" || catUnknown) Add(ch?.Test, result.Test, "test", false);
        return result;
    }

    /// <summary>
    /// The PL cannot exceed what the category allows (ISO 13849-1: B → b, 1 → c, 2 → d, 3/4 → e).
    /// User-entered values are an error; manufacturer library values a warning (check the use case).
    /// </summary>
    private void CheckPlAgainstCategory(string path, string label, string? pl, string? catCode, bool fromLibrary)
    {
        if (pl is null || catCode is null) return;
        var max = catCode switch { "catB" => "b", "cat1" => "c", "cat2" => "d", _ => "e" };
        if (string.CompareOrdinal(pl, max) <= 0) return;
        var text = $"\"{label}\": PL {pl} is not possible with category {SsmCodes.Cat(catCode)} (maximum PL {max}). Check category or PL.";
        if (fromLibrary) Warn(path, text + " (value from the manufacturer library – check the selected use case)");
        else Error(path, text);
    }

    private ResolvedElement? ResolveElement(ElementSpec e, string path, string ssLabel, OperatingProfile? inherited, bool dcRelevant, double mission)
    {
        var label = $"{ssLabel} / {e.Name ?? path}";
        LibraryComponent? comp = null;
        LibraryUseCase? uc = null;
        if (e.Library is { } lib)
        {
            (comp, uc) = ResolveLibrary(lib, path, label);
            if (comp is null || uc is null) return null;
            if (uc.Kind is UseCaseKind.SubsystemPl or UseCaseKind.SubsystemPlPfh)
            {
                Error(path, $"\"{label}\": library use case {uc.Index} of {comp.Identifier} is a complete subsystem (PL/PFH) – use it as subsystem with \"library\" instead of an element.");
                return null;
            }
        }

        var name = e.Name ?? comp?.Name ?? comp?.Identifier;
        if (string.IsNullOrWhiteSpace(name)) Ask(path, "name", $"Name of component {path}?");

        string tech = "tecUnknown";
        var techText = e.Technology ?? uc?.Technology;
        if (string.IsNullOrWhiteSpace(techText))
            Ask(path, "technology", $"\"{label}\": technology of the component?", null,
                new[] { "electromechanical", "electronic", "pneumatic", "hydraulic", "mechanical", "other" });
        else
            try { tech = SsmCodes.TechnologyCode(techText); }
            catch (ArgumentException ex) { Error(path, ex.Message); }

        double? mttfd = e.Mttfd, b10d = e.B10d;
        string? mttfdDoc = e.MttfdJustification;
        if (uc is not null && mttfd is null && b10d is null)
        {
            if (uc.Kind == UseCaseKind.ElementB10d) b10d = uc.EffectiveB10d;
            else mttfd = uc.EffectiveMttfd;
            mttfdDoc ??= uc.Mttfd is not null || uc.B10d is not null
                ? $"Manufacturer value from library {comp!.LibraryName} {comp.LibraryVersion}."
                : $"Converted from manufacturer value (MTTF/MTBF/λ/B10, RDF {(uc.Rdf ?? 0.5):P0}) of library {comp!.LibraryName}.";
        }
        double? nop = null;
        if (mttfd is null && b10d is null)
            Ask(path, "mttfd", $"\"{label}\": reliability value of the component: MTTFd (years) or B10D (operations)?",
                "From the manufacturer's data sheet or a library (library_search). Without manufacturer data, ISO 13849-1 Annex C gives typical values (e.g. contactor at nominal load B10D 1 300 000; positive-opening position switch 20 000 000).");
        else if (mttfd is <= 0) Error(path, $"\"{label}\": MTTFd must be > 0.");
        else if (b10d is not null && mttfd is null)
        {
            if (b10d <= 0) Error(path, $"\"{label}\": B10D must be > 0.");
            nop = e.Nop ?? (e.OperatingProfile ?? inherited)?.Nop;
            if (nop is null)
                Ask(path, "nop", $"\"{label}\": how often does the component switch? Needed: operating days per year, hours per day and cycle time (seconds between two operations) – or nop (operations per year) directly.",
                    "Can be given once in defaults.operatingProfile or per subsystem/component.");
            else if (nop <= 0) Error(path, $"\"{label}\": nop must be > 0.");
        }

        double dc = 0;
        string? dcDoc = e.DcJustification;
        var measure = e.DcMeasure is null ? null : Iso13849.FindDc(e.DcMeasure);
        if (e.DcMeasure is not null && measure is null)
            Error(path, $"Unknown DC measure '{e.DcMeasure}' – see sistema_list_measures.");
        if (e.Dc is { } given)
        {
            dc = given > 1 ? given / 100 : given;
            if (dc is < 0 or > 0.99) Error(path, $"\"{label}\": DC must be between 0 and 99 %.");
            if (measure is not null && (dc < measure.DcMin - 1e-9 || dc > measure.DcMax + 1e-9))
                Warn(path, $"\"{label}\": DC {dc:P0} outside the range {measure.DcMin:P0}–{measure.DcMax:P0} of measure '{measure.Key}'.");
        }
        else if (measure is not null)
        {
            if (Math.Abs(measure.DcMax - measure.DcMin) < 1e-9) dc = measure.DcMax;
            else Ask(path, "dc", $"\"{label}\": measure '{measure.Key}' allows DC {measure.DcMin:P0}–{measure.DcMax:P0} depending on the application – which value applies and why?");
        }
        else if (uc?.Dc is { } libDc) dc = libDc;
        else if (dcRelevant)
            Ask(path, "dc", $"\"{label}\": how are faults of this component detected (diagnostic coverage DC)?",
                "Give a measure key from sistema_list_measures (e.g. out-direct = 99 % via mechanically linked contacts/EDM, in-plausibility = 99 %), or a DC value with justification. 0 if not monitored.");
        if (measure is not null)
            dcDoc = $"{measure.TextEn} (ISO 13849-1 Annex E)" + (dcDoc is null ? "" : "\n" + dcDoc);

        var resolved = new ResolvedElement
        {
            Name = name ?? "", TechCode = tech, Mttfd = mttfd, B10d = mttfd is null ? b10d : null, Nop = nop, Dc = dc,
            DcDocumentation = dcDoc, MttfdDocumentation = mttfdDoc, Description = e.Description ?? comp?.Description,
            Library = comp is null
                ? e.Manufacturer is null && e.PartNumber is null ? null : new LibraryInfo { Manufacturer = e.Manufacturer, PartNumber = e.PartNumber }
                : Info(comp, uc!, e.PartNumber)
        };
        if (resolved.EffectiveMttfd is { } m && m < 3)
            Warn(path, $"\"{label}\": MTTFd {m:0.#} years is below 3 years – not acceptable for any category.");
        if (resolved.T10d is { } t10 && t10 < mission)
            Warn(path, $"\"{label}\": T10D = B10D/nop = {t10:0.#} years < mission time {mission} years – the component must be replaced after {t10:0.#} years (state this in the information for use).");
        return resolved;
    }

    private (LibraryComponent?, LibraryUseCase?) ResolveLibrary(LibraryRef lib, string path, string label)
    {
        if (_library is null) { Error(path, "Library access not available."); return (null, null); }
        if (string.IsNullOrWhiteSpace(lib.ComponentId))
        {
            Ask(path, "library.componentId", $"\"{label}\": which library component? Search with library_search.");
            return (null, null);
        }
        var comp = _library.Find(lib.ComponentId);
        if (comp is null)
        {
            Error(path, $"Library component '{lib.ComponentId}' not found. Download/import the manufacturer library (library_download / library_import) and search again.");
            return (null, null);
        }
        if (comp.UseCases.Count == 0) { Error(path, $"Library component '{comp.Id}' has no usable data."); return (null, null); }
        if (lib.UseCase is null)
        {
            if (comp.UseCases.Count == 1) return (comp, comp.UseCases[0]);
            Ask(path, "library.useCase", $"\"{label}\": {comp.Identifier} has several use cases – which one applies?", null,
                comp.UseCases.Select(u => $"{u.Index}: {Describe(u)}").ToList());
            return (comp, null);
        }
        var uc = comp.UseCases.FirstOrDefault(u => u.Index == lib.UseCase);
        if (uc is null) Error(path, $"Use case {lib.UseCase} does not exist for {comp.Id} (0…{comp.UseCases.Max(u => u.Index)}).");
        return (comp, uc);
    }

    internal static string Describe(LibraryUseCase u)
    {
        var fn = string.Join("+", new[] { u.Input ? "input" : null, u.Logic ? "logic" : null, u.Output ? "output" : null }.Where(x => x is not null));
        var values = u.Kind switch
        {
            UseCaseKind.SubsystemPlPfh => $"PL {u.Pl}, PFH {u.Pfh:0.##E+0}",
            UseCaseKind.SubsystemPl => $"PL {u.Pl}",
            UseCaseKind.ElementB10d => u.B10d is not null ? $"B10D {u.B10d:0}" : $"B10 {u.B10:0}, RDF {u.Rdf:P0}",
            _ => u.Mttfd is not null ? $"MTTFd {u.Mttfd:0.#} a" : $"MTTFd ≈ {u.EffectiveMttfd:0.#} a (converted)"
        };
        return $"{u.Path} [{fn}] {values}{(u.Category is null ? "" : $", cat. {u.Category}")}";
    }

    /// <summary>Library metadata; a part number from the bill of materials wins over the library value (often "0").</summary>
    private static LibraryInfo Info(LibraryComponent c, LibraryUseCase u, string? partNumber = null) => new()
    {
        ComponentId = c.Id, Manufacturer = c.Manufacturer, DeviceId = c.Identifier,
        PartNumber = partNumber ?? (c.PartNumber is null or "0" ? null : c.PartNumber),
        Revision = c.Revision, Group = c.Group, LibraryName = c.LibraryName, LibraryVersion = c.LibraryVersion,
        LibraryUrl = c.LibraryUrl, LibraryFile = Path.GetFileName(c.SourceFile), UseCase = u.Path, Description = c.Description
    };

    private string? NormalizePl(string pl, string path, string field)
    {
        var p = pl.Trim().ToLowerInvariant().Replace("pl", "").Replace("_", "").Trim();
        if (p is "a" or "b" or "c" or "d" or "e") return p;
        Error(path, $"{field}: '{pl}' is not a PL (a–e).");
        return null;
    }

    private string? SafeCat(string cat, string path)
    {
        try { return SsmCodes.CatCode(cat); }
        catch (ArgumentException ex) { Error(path, ex.Message); return null; }
    }

    private static string? Norm(string? v, string prefix)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim().ToUpperInvariant();
        if (!t.StartsWith(prefix)) t = prefix + t;
        return t is var x && (x == prefix + "1" || x == prefix + "2") ? x : null;
    }

    private void Ask(string path, string field, string text, string? hint = null, IReadOnlyList<string>? options = null) =>
        Questions.Add(new Question(path, field, text, hint, options));
    private void Error(string path, string text) => Errors.Add(new Finding(path, text));
    private void Warn(string path, string text) => Warnings.Add(new Finding(path, text));
}
