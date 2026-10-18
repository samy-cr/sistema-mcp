using SistemaMcp.Knowledge;
using SistemaMcp.Library;
using SistemaMcp.Spec;
using SistemaMcp.Ssm;
using Xunit;

namespace SistemaMcp.Tests;

public class FormatTests
{
    [Fact]
    public void Numbers_use_decimal_comma_and_round_trip()
    {
        Assert.Equal("0,99", SsmFormat.Number(0.99));
        Assert.Equal("1,2E-09", SsmFormat.Number(1.2e-9));
        Assert.Equal(1.2e-9, SsmFormat.ParseNumber("1,2E-09"));
        Assert.Null(SsmFormat.ParseNumber("FE"));
        Assert.Null(SsmFormat.ParseNumber("-"));
    }

    [Fact]
    public void Date_always_uses_slashes()
    {
        Assert.Equal("17/10/2026 20:51:18:000", SsmFormat.Date(new DateTime(2026, 10, 17, 20, 51, 18)));
    }

    [Fact]
    public void Text_line_breaks_are_encoded()
    {
        Assert.Equal("a&cr;&lf;b", SsmFormat.EncodeText("a\r\nb"));
        Assert.Equal("a\nb", SsmFormat.DecodeText("a&cr;&lf;b"));
    }
}

public class Iso13849Tests
{
    [Theory]
    [InlineData("cat3", "high", "medium", "d")]
    [InlineData("cat3", "medium", "low", "c")]
    [InlineData("cat2", "high", "low", "c")]
    [InlineData("cat1", "high", "none", "c")]
    [InlineData("cat1", "medium", "none", null)]
    [InlineData("cat4", "high", "high", "e")]
    [InlineData("catB", "low", "none", "a")]
    public void Simplified_procedure(string cat, string mttfd, string dc, string? pl) =>
        Assert.Equal(pl, Iso13849.EstimatePl(cat, mttfd, dc));

    [Fact]
    public void Classes_and_ccf()
    {
        Assert.Equal("high", Iso13849.DcClass(0.98999999999));
        Assert.Equal("medium", Iso13849.MttfdClass(29.9));
        Assert.Equal(85, new[] { "separation", "diversity", "overvoltage", "emc", "environment" }.Sum(k => Iso13849.FindCcf(k)!.Points));
        Assert.Equal("d", Iso13849.PlrFromRiskGraph["S2F1P2"]);
    }

    [Fact]
    public void Combination_lowers_pl_when_too_many_lowest() =>
        Assert.Equal("c", Iso13849.CombinePl(new[] { "d", "d", "d", "d" }));
}

public class VdmaTests
{
    private static string File => Path.Combine(AppContext.BaseDirectory, "TestData", "vdma", "example_vdma.xml");

    [Fact]
    public void Parses_devices_use_cases_and_texts()
    {
        var comps = VdmaParser.Parse(File, "example", "de");
        Assert.Equal(2, comps.Count);
        var relay = comps.Single(c => c.Identifier == "XR-100");
        Assert.Equal("Sicherheitsrelais XR", relay.Name);
        Assert.Equal("Example GmbH", relay.Manufacturer);
        Assert.Equal(UseCaseKind.SubsystemPlPfh, relay.UseCases[0].Kind);
        Assert.Equal("e", relay.UseCases[0].Pl);
        Assert.Equal(2.5e-9, relay.UseCases[0].Pfh);
        Assert.Equal("4", relay.UseCases[0].Category);
        Assert.Equal(500000, relay.UseCases[1].EffectiveB10d);
        Assert.Contains("nominal load", relay.UseCases[1].Path);

        var sensor = comps.Single(c => c.Identifier == "XS-7");
        Assert.True(sensor.Archived);
        Assert.Equal(100, sensor.UseCases[0].EffectiveMttfd!.Value, 6); // MTTF 50 / RDF 50 %
    }

    [Theory]
    [InlineData("XR-100")]
    [InlineData("XR100")]
    [InlineData("xr 100")]
    [InlineData("XR100-ST2-T-1P2PW")] // order code from a bill of materials → type stem
    [InlineData("4711")]
    public void Search_ignores_spaces_hyphens_and_order_code_suffixes(string query)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sistemamcp-lib-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("SISTEMA_MCP_DATA_DIR", dir);
        try
        {
            var store = new LibraryStore();
            store.Import(File);
            Assert.Equal("XR-100", store.Search(query).First().Identifier);
            Assert.Empty(store.Search("XR1000"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SISTEMA_MCP_DATA_DIR", null);
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

public class SpecTests
{
    private static string Example(string name) => Path.Combine(AppContext.BaseDirectory, "examples", name);

    [Fact]
    public void Incomplete_spec_produces_questions_not_guesses()
    {
        var r = new SpecResolver(null);
        r.Resolve(ProjectSpec.Parse("""
            { "project": { "name": "X" },
              "safetyFunctions": [ { "name": "SF", "riskGraph": { "severity": "S2", "frequency": "F1" },
                "trigger": "t", "reaction": "r", "safeState": "s",
                "subsystems": [ { "name": "Sensors", "category": "3",
                   "channels": { "ch1": [ { "name": "B1", "technology": "electromechanical", "b10d": 1000000 } ] } } ] } ] }
            """));
        Assert.False(r.IsComplete);
        var fields = r.Questions.Select(q => q.Field).ToList();
        Assert.Contains("riskGraph.possibility", fields);
        Assert.Contains("categoryRequirementsMet", fields);
        Assert.Contains("ccfMeasures", fields);
        Assert.Contains("channels.ch2", fields);
        Assert.Contains("nop", fields);
        Assert.Contains("dc", fields);
    }

    [Fact]
    public void Low_ccf_score_is_an_error()
    {
        var r = new SpecResolver(null);
        r.Resolve(ProjectSpec.Parse("""
            { "project": { "name": "X" },
              "safetyFunctions": [ { "name": "SF", "plr": "d", "trigger": "t", "reaction": "r", "safeState": "s",
                "subsystems": [ { "name": "S", "category": "3", "categoryRequirementsMet": true, "ccfMeasures": ["separation"],
                   "channels": { "ch1": [ { "name": "A", "technology": "electronic", "mttfd": 50, "dc": 0.9 } ],
                                 "ch2": [ { "name": "B", "technology": "electronic", "mttfd": 50, "dc": 0.9 } ] } } ] } ] }
            """));
        Assert.Contains(r.Errors, e => e.Text.Contains("CCF score 15"));
    }

    [Fact]
    public void Complete_spec_writes_a_readable_ssm_file()
    {
        var spec = ProjectSpec.Parse("""
            { "project": { "name": "Test" },
              "defaults": { "operatingProfile": { "daysPerYear": 220, "hoursPerDay": 8, "cycleTimeSeconds": 3600 } },
              "safetyFunctions": [ { "id": "SF-1", "name": "Door", "plr": "d", "trigger": "t", "reaction": "r", "safeState": "s",
                "subsystems": [
                  { "name": "Input", "category": "3", "categoryRequirementsMet": true,
                    "ccfMeasures": ["separation", "diversity", "overvoltage", "emc", "environment"],
                    "channels": { "ch1": [ { "name": "B1", "technology": "electromechanical", "b10d": 20000000, "dcMeasure": "in-plausibility" } ],
                                  "ch2": [ { "name": "B2", "technology": "electromechanical", "b10d": 1000000, "dcMeasure": "in-plausibility" } ] } },
                  { "name": "Logic", "direct": { "pl": "e", "pfh": 1e-9, "category": "4" } } ] } ] }
            """);
        var r = new SpecResolver(null);
        var project = r.Resolve(spec);
        Assert.True(r.IsComplete, string.Join("; ", r.Questions.Select(q => q.Text).Concat(r.Errors.Select(e => e.Text))));

        var path = Path.Combine(Path.GetTempPath(), $"sistemamcp-test-{Guid.NewGuid():N}.ssm");
        try
        {
            ProjectBuilder.Build(project, path).Save(path);
            var doc = SsmDocument.Load(path);
            Assert.Single(doc.Rows("sfops"));
            Assert.Equal("plD", doc.Rows("sfops")[0]["plr"]);
            Assert.Equal(2, doc.Rows("componentops").Count);
            Assert.Equal(6, doc.Rows("channelops").Count);
            var b2 = doc.Rows("elementops").Single(e => e.Name == "B2");
            Assert.Equal("detB10D", b2["mttfddet"]);
            Assert.Equal("1760", b2["nop"]);
            Assert.Equal("0,99", b2["dc"]);
            // SISTEMA refuses "-" in numeric fields
            Assert.DoesNotContain(doc.Rows("componentops"), c => c["pfh"] == "-" || c["mttfd"] == "FE");
            var text = File.ReadAllText(path);
            Assert.StartsWith("<?xml version=\"1.0\"?>", text);
        }
        finally { File.Delete(path); }

        var est = Estimator.Estimate(project).Single();
        Assert.True(est.PlrReached);
    }

    [Fact]
    public void Unknown_category_asks_dependent_questions_in_the_same_round()
    {
        var r = new SpecResolver(null);
        var p = r.Resolve(ProjectSpec.Parse("""
            { "project": { "name": "X" },
              "safetyFunctions": [ { "id": "SF", "name": "SF", "plr": "d", "trigger": "t", "reaction": "r", "safeState": "s",
                "subsystems": [ { "name": "E-stop",
                   "channels": { "ch1": [ { "name": "S1.1", "technology": "electromechanical", "b10d": 100000 } ],
                                 "ch2": [ { "name": "S1.2", "technology": "electromechanical", "b10d": 100000 } ] } } ] } ] }
            """));
        var q = r.Questions.Select(x => x.Path + "|" + x.Field).ToList();
        Assert.Contains(q, x => x.EndsWith("|category"));
        Assert.Contains(q, x => x.EndsWith("|ccfMeasures"));
        Assert.Contains(q, x => x.EndsWith("|categoryRequirementsMet"));
        Assert.Contains(q, x => x.Contains("ch2[0]") && x.EndsWith("|nop"));
        Assert.Contains(q, x => x.Contains("ch2[0]") && x.EndsWith("|dc"));
        Assert.DoesNotContain(r.Warnings, w => w.Text.Contains("ignored"));
        Assert.Equal("unknown", Estimator.Estimate(p).Single().Subsystems.Single().Category);
    }

    [Theory]
    [InlineData("B", "d", true)]
    [InlineData("1", "d", true)]
    [InlineData("2", "e", true)]
    [InlineData("2", "d", false)]
    [InlineData("3", "e", false)]
    public void Pl_above_category_maximum_is_an_error(string cat, string pl, bool error)
    {
        var r = new SpecResolver(null);
        r.Resolve(ProjectSpec.Parse($$"""
            { "project": { "name": "X" },
              "safetyFunctions": [ { "name": "SF", "plr": "c", "trigger": "t", "reaction": "r", "safeState": "s",
                "subsystems": [ { "name": "Device", "direct": { "pl": "{{pl}}", "pfh": 1e-9, "category": "{{cat}}" } } ] } ] }
            """));
        Assert.Equal(error, r.Errors.Any(e => e.Text.Contains("not possible with category")));
    }

    [Fact]
    public void Missing_direct_values_are_not_shown_as_category_b()
    {
        var r = new SpecResolver(null);
        var p = r.Resolve(ProjectSpec.Parse("""
            { "project": { "name": "X" },
              "safetyFunctions": [ { "id": "SF", "name": "SF", "plr": "d", "trigger": "t", "reaction": "r", "safeState": "s",
                "subsystems": [ { "name": "F-CPU", "direct": { "manufacturer": "Any" } } ] } ] }
            """));
        var ss = Estimator.Estimate(p).Single().Subsystems.Single();
        Assert.Equal("unknown", ss.Category);
        Assert.Contains("PL missing", ss.Notes.Single());
    }

    [Fact]
    public void Example_without_library_parts_is_complete()
    {
        var r = new SpecResolver(null);
        var spec = ProjectSpec.Parse(Example("assembly-cell.json"));
        spec.SafetyFunctions!.RemoveAt(1); // SF-02 needs the Turck library
        r.Resolve(spec);
        Assert.True(r.IsComplete);
    }
}
