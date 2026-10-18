using SistemaMcp.Knowledge;
using SistemaMcp.Ssm;

namespace SistemaMcp.Spec;

/// <summary>Writes a <see cref="ResolvedProject"/> as SISTEMA 3.0 project file.</summary>
public static class ProjectBuilder
{
    public static SsmWriter Build(ResolvedProject p, string filePath)
    {
        var w = new SsmWriter();
        var info = p.Info;
        var projectOid = w.AddRow("projectops", new Dictionary<string, string?>
        {
            ["name"] = info.Name,
            ["author"] = info.Author,
            ["tester"] = info.Tester,
            ["manager"] = info.Manager,
            ["documentation"] = SsmFormat.EncodeText(info.Documentation),
            ["filename"] = Path.GetFullPath(filePath),
            ["machinename"] = info.Machine ?? "-",
            ["createdate"] = SsmFormat.Date(DateTime.Now),
            ["status"] = info.Status,
            ["number"] = info.Number,
            ["version"] = info.Version
        });

        foreach (var sf in p.SafetyFunctions)
        {
            var s = sf.Spec;
            var sfOid = w.AddRow("sfops", new Dictionary<string, string?>
            {
                ["projectopoid"] = projectOid,
                ["name"] = sf.Name,
                ["sftype"] = sf.Name,
                ["sfid"] = sf.Id,
                ["documentation"] = SsmFormat.EncodeText(s.Documentation),
                ["plr"] = SsmCodes.PlCode(sf.Plr),
                ["plrdet"] = "detDirect",
                ["plrdocumentation"] = SsmFormat.EncodeText(sf.PlrDocumentation),
                ["triggerevent"] = SsmFormat.EncodeText(s.Trigger),
                ["reaction"] = SsmFormat.EncodeText(s.Reaction),
                ["safestate"] = SsmFormat.EncodeText(s.SafeState),
                ["opmode"] = s.OperatingMode,
                ["requestfrequency"] = s.DemandRate,
                ["responsetime"] = s.ResponseTime
            });
            foreach (var ss in sf.Subsystems) AddSubsystem(w, sfOid, ss);
        }
        return w;
    }

    private static void AddSubsystem(SsmWriter w, string sfOid, ResolvedSubsystem ss)
    {
        var v = new Dictionary<string, string?>
        {
            ["sfopoid"] = sfOid,
            ["name"] = ss.Name,
            ["cat"] = ss.CatCode,
            ["catdocumentation"] = SsmFormat.EncodeText(ss.CategoryDocumentation),
            ["description"] = SsmFormat.EncodeText(ss.Description),
            ["missiontime"] = SsmFormat.Number(ss.MissionTime),
            ["plconditions"] = Iso13849.PlRequirementIds
        };
        Lib(v, ss.Library);
        if (ss.IsDirect)
        {
            v["pldet"] = "detDirect";
            v["pldirectnobind"] = SsmCodes.PlCode(ss.DirectPl ?? "a");
            v["isplpfhbind"] = ss.DirectPfh is null ? "0" : "1";
            // SISTEMA cannot read "-" here; without PFH the default stays and is ignored (isplpfhbind = 0)
            if (ss.DirectPfh is { } pfh) v["pfh"] = SsmFormat.Number(pfh);
            v["catconditionsrelevant"] = "0";
            v["mttfddet"] = "detDirect";
            v["dcavgdet"] = "detDirect";
        }
        else
        {
            v["pldet"] = "detSubItems";
            v["mttfddet"] = "detSubItems";
            v["dcavgdet"] = "detSubItems";
            v["catconditionsrelevant"] = "1";
            v["catconditions"] = ss.CategoryRequirementsMet ? Iso13849.CategoryRequirementIds.GetValueOrDefault(ss.CatCode, "") : "";
            v["ccfmeasures"] = string.Join(",", ss.CcfIds);
            v["ccfscoredet"] = "detMeasures";
            v["ccfdocumentation"] = SsmFormat.EncodeText(ss.CcfDocumentation);
        }
        var oid = w.AddRow("componentops", v);

        foreach (var (type, list) in new[] { ("ch1", ss.Ch1), ("ch2", ss.Ch2), ("chTest", ss.Test) })
        {
            var ch = w.AddRow("channelops", new Dictionary<string, string?>
            {
                ["componentopoid"] = oid, ["channeltype"] = type, ["name"] = "-"
            });
            foreach (var e in list) AddElement(w, ch, e, ss.MissionTime);
        }
    }

    private static void AddElement(SsmWriter w, string channelOid, ResolvedElement e, double missionTime)
    {
        var block = new Dictionary<string, string?>
        {
            ["parentopoid"] = channelOid,
            ["name"] = e.Name,
            ["dcdet"] = "detSubItems",
            ["mttfddet"] = "detSubItems",
            ["description"] = SsmFormat.EncodeText(e.Description),
            ["missiontime"] = SsmFormat.Number(missionTime)
        };
        Lib(block, e.Library);
        var blockOid = w.AddRow("blocops", block);

        var el = new Dictionary<string, string?>
        {
            ["parentopoid"] = blockOid,
            ["name"] = e.Name,
            ["technology"] = e.TechCode,
            ["dcdet"] = "detDirect",
            ["dc"] = SsmFormat.Number(e.Dc),
            ["dcdocumentation"] = SsmFormat.EncodeText(e.DcDocumentation),
            ["mttfddocumentation"] = SsmFormat.EncodeText(e.MttfdDocumentation),
            ["description"] = SsmFormat.EncodeText(e.Description),
            ["missiontime"] = SsmFormat.Number(missionTime)
        };
        if (e.Mttfd is { } m)
        {
            el["mttfddet"] = "detDirect";
            el["mttfd"] = SsmFormat.Number(Math.Round(m, 1));
        }
        else
        {
            el["mttfddet"] = "detB10D";
            el["b10d"] = SsmFormat.Number(e.B10d ?? 0);
            el["nop"] = SsmFormat.Number(Math.Round(e.Nop ?? 0, 1));
        }
        Lib(el, e.Library);
        w.AddRow("elementops", el);
    }

    private static void Lib(Dictionary<string, string?> v, LibraryInfo? l)
    {
        if (l is null) return;
        v["manufacturer"] = l.Manufacturer;
        v["partno"] = l.PartNumber;
        v["revision"] = l.Revision;
        v["deviceid"] = l.DeviceId;
        v["devicegroup"] = l.Group;
        v["libname"] = l.LibraryName;
        v["libversion"] = l.LibraryVersion;
        v["liburl"] = l.LibraryUrl;
        v["libfilename"] = l.LibraryFile;
        v["usecase"] = l.UseCase;
        if (!v.ContainsKey("description") || string.IsNullOrEmpty(v["description"]))
            v["description"] = SsmFormat.EncodeText(l.Description);
    }
}
