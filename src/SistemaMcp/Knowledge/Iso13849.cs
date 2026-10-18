namespace SistemaMcp.Knowledge;

public sealed record CcfMeasure(string Key, string SistemaId, int Points, string Group, string TextEn, string TextDe);

public sealed record DcMeasure(string Key, string Part, double DcMin, double DcMax, string TextEn, string TextDe);

/// <summary>
/// Normative values of ISO 13849-1 used for validation and the plausibility estimate.
/// The authoritative calculation is always the one SISTEMA performs when the project is opened.
/// </summary>
public static class Iso13849
{
    /// <summary>Measures against common cause failure (Annex F). SISTEMA ids CCFMID-001…008 in the same order.</summary>
    public static readonly CcfMeasure[] CcfMeasures =
    {
        new("separation", "CCFMID-001", 15, "Separation",
            "Physical separation between signal paths (wiring, clearances, creepage distances, piping)",
            "Trennung der Signalpfade (Verdrahtung, Luft- und Kriechstrecken, Verrohrung)"),
        new("diversity", "CCFMID-002", 20, "Diversity",
            "Different technologies/design or physical principles in the channels",
            "Unterschiedliche Technologien/Konstruktion oder physikalische Prinzipien in den Kanälen"),
        new("overvoltage", "CCFMID-003", 15, "Design/application/experience",
            "Protection against over-voltage, over-pressure, over-current, over-temperature etc.",
            "Schutz gegen Überspannung, Überdruck, Überstrom, Übertemperatur usw."),
        new("well-tried", "CCFMID-004", 5, "Design/application/experience",
            "Components used are well-tried",
            "Verwendung bewährter Bauteile"),
        new("fmea", "CCFMID-005", 5, "Assessment/analysis",
            "Results of a failure mode and effect analysis are taken into account to avoid CCF in design",
            "Ergebnisse einer Fehlerart- und Auswirkungsanalyse (FMEA) wurden zur Vermeidung von CCF berücksichtigt"),
        new("competence", "CCFMID-006", 5, "Competence/training",
            "Training of designers to understand the causes and consequences of CCF",
            "Schulung der Konstrukteure zu Ursachen und Folgen von CCF"),
        new("emc", "CCFMID-007", 25, "Environmental",
            "Prevention of contamination and EMC (fluid: filtration/purity; electric: EMC immunity checked)",
            "Schutz vor Verschmutzung und EMV (Fluidik: Filterung/Reinheit; Elektrik: EMV-Festigkeit geprüft)"),
        new("environment", "CCFMID-008", 10, "Environmental",
            "Immunity to other environmental influences (temperature, shock, vibration, humidity)",
            "Unempfindlichkeit gegen andere Umgebungseinflüsse (Temperatur, Schock, Vibration, Feuchte)"),
    };

    public const int CcfMinimumPoints = 65;

    public static CcfMeasure? FindCcf(string keyOrId) => CcfMeasures.FirstOrDefault(m =>
        m.Key.Equals(keyOrId, StringComparison.OrdinalIgnoreCase) ||
        m.SistemaId.Equals(keyOrId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Diagnostic coverage estimates (Annex E). Ranges mean "depending on the application".</summary>
    public static readonly DcMeasure[] DcMeasures =
    {
        new("in-cyclic-test", "input", 0.90, 0.90, "Cyclic test stimulus by dynamic change of the input signals", "Zyklischer Testimpuls durch dynamische Änderung der Eingangssignale"),
        new("in-plausibility", "input", 0.99, 0.99, "Plausibility check, e.g. NO and NC mechanically linked contacts", "Plausibilitätsprüfung, z. B. Schließer und Öffner zwangsgeführter Kontakte"),
        new("in-cross-static", "input", 0.0, 0.99, "Cross monitoring of inputs without dynamic test (depends on how often the signal changes)", "Kreuzvergleich der Eingänge ohne dynamischen Test (abhängig von der Häufigkeit des Signalwechsels)"),
        new("in-cross-dynamic", "input", 0.90, 0.90, "Cross monitoring of inputs with dynamic test, if short circuits are not detectable", "Kreuzvergleich der Eingänge mit dynamischem Test, wenn Kurzschlüsse nicht erkennbar sind"),
        new("in-cross-full", "input", 0.99, 0.99, "Cross monitoring of input signals and intermediate results in the logic, temporal/logical program-flow monitoring, detection of static faults and short circuits", "Kreuzvergleich von Eingangssignalen und Zwischenergebnissen in der Logik, zeitliche/logische Programmlaufüberwachung, Erkennung statischer Fehler und Kurzschlüsse"),
        new("in-indirect", "input", 0.90, 0.99, "Indirect monitoring (e.g. pressure switch, electrical position monitoring of actuators)", "Indirekte Überwachung (z. B. Druckschalter, elektrische Stellungsüberwachung von Antrieben)"),
        new("in-direct", "input", 0.99, 0.99, "Direct monitoring (e.g. electrical position monitoring of control valves, mechanically linked contacts)", "Direkte Überwachung (z. B. elektrische Stellungsüberwachung von Steuerventilen, zwangsgeführte Kontakte)"),
        new("in-process", "input", 0.0, 0.99, "Fault detection by the process", "Fehlererkennung durch den Prozess"),
        new("in-sensor-characteristics", "input", 0.60, 0.60, "Monitoring of some characteristics of the sensor (response time, range of analogue signals)", "Überwachung einiger Eigenschaften des Sensors (Reaktionszeit, Bereich analoger Signale)"),
        new("logic-indirect", "logic", 0.90, 0.99, "Indirect monitoring (e.g. position switch monitoring the working stroke of a valve)", "Indirekte Überwachung (z. B. Positionsschalter überwacht den Arbeitshub eines Ventils)"),
        new("logic-direct", "logic", 0.99, 0.99, "Direct monitoring (e.g. electrical position monitoring of control valves, mechanically linked contacts)", "Direkte Überwachung (z. B. elektrische Stellungsüberwachung von Steuerventilen, zwangsgeführte Kontakte)"),
        new("logic-watchdog-simple", "logic", 0.60, 0.60, "Simple temporal monitoring of the logic (e.g. timer as watchdog)", "Einfache zeitliche Überwachung der Logik (z. B. Zeitglied als Watchdog)"),
        new("logic-watchdog-plausibility", "logic", 0.90, 0.90, "Temporal and logical monitoring by watchdog with plausibility checks by the test equipment", "Zeitliche und logische Überwachung durch Watchdog mit Plausibilitätsprüfung durch die Testeinrichtung"),
        new("logic-startup-selftest", "logic", 0.90, 0.90, "Start-up self-test to detect latent faults (program/data memory, I/O ports, interfaces)", "Selbsttest beim Anlauf zur Erkennung latenter Fehler (Programm-/Datenspeicher, E/A, Schnittstellen)"),
        new("logic-dynamic-principle", "logic", 0.99, 0.99, "Dynamic principle (all logic components change state ON-OFF-ON on demand), e.g. relay interlocking", "Dynamisches Prinzip (alle Logikbauteile wechseln bei Anforderung EIN-AUS-EIN), z. B. Relais-Verriegelung"),
        new("logic-rom-8bit", "logic", 0.90, 0.90, "Invariable memory: signature of one word (8 bit)", "Invarianter Speicher: Signatur einfacher Wortbreite (8 Bit)"),
        new("logic-rom-16bit", "logic", 0.99, 0.99, "Invariable memory: signature of a double word (16 bit)", "Invarianter Speicher: Signatur doppelter Wortbreite (16 Bit)"),
        new("logic-ram-readwrite", "logic", 0.60, 0.60, "Variable memory: check of readability and writability of used memory cells", "Varianter Speicher: Test der Les- und Beschreibbarkeit der verwendeten Speicherzellen"),
        new("logic-ram-hamming", "logic", 0.99, 0.99, "Variable memory: RAM monitoring with modified Hamming code or RAM self-test (e.g. galpat, Abraham)", "Varianter Speicher: RAM-Überwachung mit modifiziertem Hamming-Code oder RAM-Selbsttest (z. B. galpat, Abraham)"),
        new("logic-cpu-selftest", "logic", 0.60, 0.90, "Processing unit: self-test by software", "Verarbeitungseinheit: Selbsttest durch Software"),
        new("logic-cpu-coded", "logic", 0.90, 0.99, "Processing unit: coded processing", "Verarbeitungseinheit: codierte Verarbeitung"),
        new("logic-process", "logic", 0.0, 0.99, "Fault detection by the process", "Fehlererkennung durch den Prozess"),
        new("out-single-static", "output", 0.0, 0.99, "Monitoring of outputs by one channel without dynamic test", "Überwachung der Ausgänge durch einen Kanal ohne dynamischen Test"),
        new("out-cross-static", "output", 0.0, 0.99, "Cross monitoring of outputs without dynamic test", "Kreuzvergleich der Ausgänge ohne dynamischen Test"),
        new("out-cross-dynamic", "output", 0.90, 0.90, "Cross monitoring of output signals with dynamic test, without detection of short circuits", "Kreuzvergleich der Ausgangssignale mit dynamischem Test, ohne Erkennung von Kurzschlüssen"),
        new("out-cross-full", "output", 0.99, 0.99, "Cross monitoring of output signals and intermediate results in the logic, program-flow monitoring, detection of static faults and short circuits", "Kreuzvergleich von Ausgangssignalen und Zwischenergebnissen in der Logik, Programmlaufüberwachung, Erkennung statischer Fehler und Kurzschlüsse"),
        new("out-redundant-shutoff", "output", 0.99, 0.99, "Redundant shut-off path with monitoring of the actuators by logic and test equipment", "Redundanter Abschaltpfad mit Überwachung der Antriebselemente durch Logik und Testeinrichtung"),
        new("out-indirect", "output", 0.90, 0.99, "Indirect monitoring (e.g. pressure switch, electrical position monitoring of actuators)", "Indirekte Überwachung (z. B. Druckschalter, elektrische Stellungsüberwachung von Antrieben)"),
        new("out-process", "output", 0.0, 0.99, "Fault detection by the process", "Fehlererkennung durch den Prozess"),
        new("out-direct", "output", 0.99, 0.99, "Direct monitoring (e.g. electrical position monitoring of control valves, mechanically linked contacts / EDM)", "Direkte Überwachung (z. B. elektrische Stellungsüberwachung von Steuerventilen, zwangsgeführte Kontakte / Rückführkreis)"),
    };

    public static DcMeasure? FindDc(string key) =>
        DcMeasures.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// SISTEMA ids of the category requirement check boxes, as written by SISTEMA when all boxes are ticked.
    /// Only categories verified against SISTEMA 3.0.4 are listed; for the others the user ticks them in SISTEMA.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CategoryRequirementIds = new Dictionary<string, string>
    {
        ["cat1"] = "CATReq10,CATReq01,CATReq02,CATReq03",
        ["cat3"] = "CATReq04, CATReq03, CATReq01, CATReq10",
    };

    public const string PlRequirementIds = "PLReq01, PLReq02, PLReq03, PLReq04";

    public static readonly IReadOnlyDictionary<string, string> CategoryRequirementText = new Dictionary<string, string>
    {
        ["catB"] = "Basic safety principles applied; SRP/CS designed to withstand expected influences (relevant standards).",
        ["cat1"] = "Requirements of B; well-tried components and well-tried safety principles are used.",
        ["cat2"] = "Requirements of B; well-tried safety principles; safety function is checked at suitable intervals by the machine control system (test equipment, test rate ≥ 100 × demand rate or immediate reaction).",
        ["cat3"] = "Requirements of B; well-tried safety principles; a single fault does not lead to the loss of the safety function; the single fault is detected whenever reasonably practicable.",
        ["cat4"] = "Requirements of B; well-tried safety principles; a single fault does not lead to the loss of the safety function; the single fault is detected at or before the next demand, otherwise an accumulation of undetected faults must not lead to the loss of the safety function.",
    };

    public static readonly IReadOnlyDictionary<string, string> PlrFromRiskGraph = new Dictionary<string, string>
    {
        ["S1F1P1"] = "a", ["S1F1P2"] = "b", ["S1F2P1"] = "b", ["S1F2P2"] = "c",
        ["S2F1P1"] = "c", ["S2F1P2"] = "d", ["S2F2P1"] = "d", ["S2F2P2"] = "e",
    };

    // ---------------- classes ----------------

    public static string MttfdClass(double years) => years switch
    {
        < 3 => "not acceptable",
        < 10 => "low",
        < 30 => "medium",
        _ => "high"
    };

    public static string DcClass(double dc) => Math.Round(dc, 6) switch // DCavg is a weighted mean – avoid 0.98999… for 99 %
    {
        < 0.60 => "none",
        < 0.90 => "low",
        < 0.99 => "medium",
        _ => "high"
    };

    /// <summary>Maximum MTTFd per channel taken into account (ISO 13849-1:2023: 100 years, 2500 years for category 4).</summary>
    public static double MttfdCap(string catCode) => catCode == "cat4" ? 2500 : 100;

    public static (double low, double high) PfhBand(string pl) => pl switch
    {
        "a" => (1e-5, 1e-4), "b" => (3e-6, 1e-5), "c" => (1e-6, 3e-6), "d" => (1e-7, 1e-6), "e" => (1e-8, 1e-7),
        _ => (double.NaN, double.NaN)
    };

    public static string? PlFromPfh(double pfh) => pfh switch
    {
        < 1e-8 => "e", // better than PL e, still PL e
        < 1e-7 => "e",
        < 1e-6 => "d",
        < 3e-6 => "c",
        < 1e-5 => "b",
        < 1e-4 => "a",
        _ => null
    };

    /// <summary>
    /// Simplified procedure (ISO 13849-1 Figure/Table "relationship between categories, DCavg, MTTFd and PL").
    /// Conservative: the exact Annex K values (used by SISTEMA) may give a better PL, e.g. category 3 with DCavg high.
    /// Returns null when the combination is not permitted for the category.
    /// </summary>
    public static string? EstimatePl(string catCode, string mttfdClass, string dcClass)
    {
        if (mttfdClass == "not acceptable") return null;
        int m = mttfdClass switch { "low" => 0, "medium" => 1, _ => 2 };
        return catCode switch
        {
            "catB" => new[] { "a", "b", "b" }[m], // MTTFd above "medium" brings no further credit in category B
            "cat1" => m == 2 ? "c" : null,
            "cat2" => dcClass switch
            {
                "none" => null,
                "low" => new[] { "a", "b", "c" }[m],
                _ => new[] { "b", "c", "d" }[m]
            },
            "cat3" => dcClass switch
            {
                "none" => null,
                "low" => new[] { "b", "c", "d" }[m],
                _ => new[] { "c", "d", "d" }[m]
            },
            "cat4" => dcClass == "high" && m == 2 ? "e" : null,
            _ => null
        };
    }

    /// <summary>
    /// Combination of subsystems without PFH values (ISO 13849-1:2015 Table 11): lowest PL, lowered by one
    /// step if too many subsystems share the lowest PL.
    /// </summary>
    public static string? CombinePl(IReadOnlyList<string> pls)
    {
        if (pls.Count == 0) return null;
        var order = "abcde";
        var low = pls.OrderBy(p => order.IndexOf(p)).First();
        int n = pls.Count(p => p == low);
        int limit = low switch { "a" => 3, "b" => 2, "c" => 2, "d" => 3, _ => 3 };
        if (n <= limit) return low;
        return low == "a" ? null : order[order.IndexOf(low) - 1].ToString();
    }
}
