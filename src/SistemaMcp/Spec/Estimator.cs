using SistemaMcp.Knowledge;
using SistemaMcp.Ssm;

namespace SistemaMcp.Spec;

public sealed record SubsystemEstimate(
    string Name, string Category, string? Pl, double? Pfh, double? MttfdChannel, string? MttfdClass,
    double? DcAvg, string? DcClass, int? CcfPoints, IReadOnlyList<string> Notes);

public sealed record SafetyFunctionEstimate(
    string? Id, string Name, string Plr, string? Pl, double? Pfh, bool? PlrReached,
    IReadOnlyList<SubsystemEstimate> Subsystems, IReadOnlyList<string> Notes);

/// <summary>
/// Plausibility estimate following ISO 13849-1 (simplified procedure). It is conservative and does not replace
/// the calculation in SISTEMA – open the generated file in SISTEMA, save it and read the results back.
/// </summary>
public static class Estimator
{
    public static List<SafetyFunctionEstimate> Estimate(ResolvedProject p) =>
        p.SafetyFunctions.Select(Estimate).ToList();

    public static SafetyFunctionEstimate Estimate(ResolvedSafetyFunction sf)
    {
        var subs = sf.Subsystems.Select(Estimate).ToList();
        var notes = new List<string>();
        string? pl = null;
        double? pfh = null;
        if (subs.Count > 0 && subs.All(s => s.Pl is not null))
        {
            if (subs.All(s => s.Pfh is not null))
            {
                pfh = subs.Sum(s => s.Pfh!.Value);
                var fromPfh = Iso13849.PlFromPfh(pfh.Value);
                // the PL of the safety function cannot exceed the lowest subsystem PL
                var lowest = subs.Select(s => s.Pl!).OrderBy(x => x).First();
                pl = fromPfh is null ? null : string.CompareOrdinal(fromPfh, lowest) > 0 ? lowest : fromPfh;
            }
            else
            {
                pl = Iso13849.CombinePl(subs.Select(s => s.Pl!).ToList());
                notes.Add("PFH not known for all subsystems – combined via lowest PL (ISO 13849-1:2015 Table 11).");
            }
        }
        else if (subs.Any(s => s.Pl is null))
            notes.Add("At least one subsystem does not reach a PL – see subsystem notes.");

        bool? reached = string.IsNullOrEmpty(sf.Plr) ? null
            : pl is not null && string.CompareOrdinal(pl, sf.Plr) >= 0;
        return new SafetyFunctionEstimate(sf.Id, sf.Name, sf.Plr, pl, pfh, reached, subs, notes);
    }

    public static SubsystemEstimate Estimate(ResolvedSubsystem ss)
    {
        var notes = new List<string>();
        var cat = ss.CategoryKnown ? SsmCodes.Cat(ss.CatCode) ?? "?" : "unknown";
        if (ss.IsDirect)
            return new SubsystemEstimate(ss.Name, cat, ss.DirectPl, ss.DirectPfh, null, null, null, null, null,
                new[]
                {
                    ss.DirectPl is null ? "PL missing – no estimate."
                    : ss.DirectPfh is null ? "PL from manufacturer data, PFH missing."
                    : ss.Library?.LibraryName is { } lib ? $"PL/PFH from library {lib}." : "PL/PFH entered directly (manufacturer data)."
                });
        if (!ss.CategoryKnown)
            return new SubsystemEstimate(ss.Name, cat, null, null, null, null, null, null, null, new[] { "Category missing – no estimate." });

        var cap = Iso13849.MttfdCap(ss.CatCode);
        double? Channel(List<ResolvedElement> list)
        {
            if (list.Count == 0 || list.Any(e => e.EffectiveMttfd is null)) return null;
            return 1 / list.Sum(e => 1 / e.EffectiveMttfd!.Value);
        }

        var c1 = Channel(ss.Ch1);
        double? mttfd = c1;
        if (ss.CatCode is "cat3" or "cat4")
        {
            var c2 = Channel(ss.Ch2);
            if (c1 is null || c2 is null) mttfd = null;
            else
            {
                double a = Math.Min(c1.Value, cap), b = Math.Min(c2.Value, cap);
                mttfd = 2.0 / 3.0 * (a + b - 1 / (1 / a + 1 / b));
            }
        }
        if (mttfd is null)
            return new SubsystemEstimate(ss.Name, cat, null, null, null, null, null, null, ss.CcfPoints, new[] { "MTTFd values incomplete." });
        if (mttfd > cap) { notes.Add($"MTTFd per channel capped to {cap} years."); mttfd = cap; }

        var all = ss.Ch1.Concat(ss.CatCode is "cat3" or "cat4" ? ss.Ch2 : Enumerable.Empty<ResolvedElement>()).ToList();
        var sumInv = all.Sum(e => 1 / e.EffectiveMttfd!.Value);
        var dcAvg = all.Sum(e => e.Dc / e.EffectiveMttfd!.Value) / sumInv;

        var mClass = Iso13849.MttfdClass(mttfd.Value);
        var dClass = Iso13849.DcClass(dcAvg);
        var pl = Iso13849.EstimatePl(ss.CatCode, mClass, dClass);

        if (ss.CatCode == "cat1" && mClass != "high") notes.Add("Category 1 requires MTTFd high (≥ 30 years).");
        if (ss.CatCode == "catB" && mClass == "high") notes.Add("Category B: MTTFd high gives no further credit (max. PL b).");
        if (ss.CatCode is "cat2" or "cat3" && dClass == "none") notes.Add($"Category {cat} requires DCavg ≥ 60 %.");
        if (ss.CatCode == "cat4" && (dClass != "high" || mClass != "high")) notes.Add("Category 4 requires DCavg ≥ 99 % and MTTFd high.");
        if (ss.CatCode is "cat2" or "cat3" or "cat4" && ss.CcfPoints < Iso13849.CcfMinimumPoints)
        {
            notes.Add($"CCF {ss.CcfPoints} < {Iso13849.CcfMinimumPoints} points.");
            pl = null;
        }
        if (ss.CatCode == "cat2" && ss.Test.Count > 0 && Channel(ss.Test) is { } te && te < mttfd / 2)
            notes.Add("Category 2: MTTFd of the test channel should be at least half of the functional channel.");
        if (ss.CatCode == "cat3" && dClass == "high" && mClass == "high")
            notes.Add("Simplified procedure gives PL d; with the Annex K values SISTEMA usually reaches PL e for this combination.");
        if (!ss.CategoryRequirementsMet) notes.Add("Category requirements not confirmed.");

        return new SubsystemEstimate(ss.Name, cat, pl, null, Math.Round(mttfd.Value, 1), mClass,
            Math.Round(dcAvg, 4), dClass, ss.CatCode is "cat2" or "cat3" or "cat4" ? ss.CcfPoints : null, notes);
    }
}
