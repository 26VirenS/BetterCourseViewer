using System.Globalization;

namespace Simpl.Tools;

// The Cell Explorer's checks of its geometry (spec §4.1, §4.1.1, §5.1.5, §10.2's "animal, plant and bacterium build
// deterministically"), the Mac's CellExplorerChecks.swift check for check: each cell built from its seed has the counts
// §5.1 gives (or says why not), stays under the teaching-scale budget of 250k triangles, keeps every free organelle
// inside its membrane and clear of the others, leaves the cutaway's wedge empty, comes out the same twice; the explode
// rule and the shells' fade; the plant's chloroplasts and dented vacuole; the bacterium's nucleoid and left-handed
// flagella; the true scale's 20,000 ribosomes; the membrane patch's lipids, cholesterol and labels (and, Windows' own,
// its triangles within what Viewport3D draws smoothly). Lines "bio-check cell.… <value> OK|FAIL".

public static partial class BioSelfCheck
{
    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static void CellBuildChecks(BioCheckReport r)
    {
        var animal = CEBuild.Build(new CEBuildOptions(CEKind.Animal));
        var plant = CEBuild.Build(new CEBuildOptions(CEKind.Plant));
        var bacterium = CEBuild.Build(new CEBuildOptions(CEKind.Bacterium));
        // (a scene point back in the cell's own frame)
        static BioVec CellFrame(CEModel m, BioVec p) => new(BioV.Dot(p, m.Display.X), BioV.Dot(p, m.Display.Y), BioV.Dot(p, m.Display.Z));

        // (§5.1.1's counts: what was placed; free placements may give up, and the line says how many did)
        var a = animal.Placed;
        var want = new Dictionary<string, int>
        {
            ["nucleus"] = 1, ["pore"] = 110, ["nucleolus"] = 1, ["chromatin"] = 12, ["rer"] = 5, ["golgi"] = 1, ["mito"] = 24, ["lysosome"] = 12,
            ["peroxisome"] = 8, ["centrosome"] = 1, ["microtubule"] = 40, ["intermediate"] = 30, ["flagellum"] = 1,
        };
        var countsOk = want.All(kv => a.GetValueOrDefault(kv.Key) == kv.Value);
        r.Check("cell.animal.counts", string.Join(",", want.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => $"{k}={a.GetValueOrDefault(k)}")), countsOk);
        var ribo = (animal.Extra.GetValueOrDefault("ribosome.free"), animal.Extra.GetValueOrDefault("ribosome.er"));
        r.Check("cell.animal.ribosomes", $"{ribo.Item1}+{ribo.Item2}", ribo.Item1 == 1500 && ribo.Item2 == 2500);
        r.Check("cell.animal.vesicles", $"{animal.Extra.GetValueOrDefault("vesicle.free")}+{animal.Extra.GetValueOrDefault("vesicle.golgi")}",
            animal.Extra.GetValueOrDefault("vesicle.free") == 30 && animal.Extra.GetValueOrDefault("vesicle.golgi") == 24);
        var ser = animal.Extra.GetValueOrDefault("ser.segments");
        r.Check("cell.animal.smooth-er", $"{ser}segments", ser > 40 && ser <= 160);
        r.Check("cell.animal.actin", $"{a.GetValueOrDefault("actin")}", a.GetValueOrDefault("actin") + animal.GaveUp.GetValueOrDefault("actin") == 220 && a.GetValueOrDefault("actin") >= 200);
        r.Check("cell.animal.gave-up", animal.GaveUp.Count == 0 ? "none" : string.Join(",", animal.GaveUp.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")),
            animal.GaveUp.GetValueOrDefault("mito") == 0 && animal.GaveUp.GetValueOrDefault("ribosome") == 0);

        // (the budget: fewer than 250k triangles in teaching scale, cut or whole)
        var whole = CEBuild.Build(new CEBuildOptions(CEKind.Animal, Cutaway: false));
        foreach (var (name, m) in new[] { ("animal", animal), ("animal-whole", whole), ("plant", plant), ("bacterium", bacterium) })
            r.Check($"cell.{name}.triangles", $"{m.Triangles}", m.Triangles < 250_000 && m.Triangles > 20_000);

        // (every free organelle inside the membrane, and the cutaway's wedge empty of organelles' centres)
        var freeParts = new HashSet<string> { "mito", "lysosome", "peroxisome", "vesicle", "ribosome", "golgi", "plasmid", "amyloplast" };
        bool FreeInside(CEModel m, Func<BioVec, double, bool> test) =>
            m.Instances.Where(i => !i.Shell && !i.Fixed && freeParts.Contains(i.Part)).All(i => test(CellFrame(m, i.Centre), i.Radius));
        var ell = BioShapes.EllipsoidRadius(11, 8, 9.5);
        var blob = new BioBlob(0xC311, 0.06);
        r.Check("cell.animal.inside", "yes", FreeInside(animal, (p, rad) => BioV.Length(p) + rad <= ell(BioV.Unit(p)) * blob.Factor(BioV.Unit(p)) - 0.08));
        r.Check("cell.plant.inside", "yes", FreeInside(plant, (p, rad) => CEGeo.RoundedBoxSdf(p, new BioVec(22, 13, 13), 3) <= -0.6 - rad));
        r.Check("cell.bacterium.inside", "yes", FreeInside(bacterium, (p, rad) => CEGeo.CapsuleXSdf(p, 0.5, 0.432) <= -rad + 1e-9));
        var spread = new HashSet<string> { "rer", "ser", "chromatin", "microtubule", "intermediate", "nucleus", "pore", "vacuole", "nucleoid" };
        var wedge = new[] { animal, plant, bacterium }.All(m =>
            m.Instances.Where(i => !i.Shell && !i.Fixed && !spread.Contains(i.Part)).All(i => !CEGeo.InWedge(CellFrame(m, i.Centre))));
        r.Check("cell.cutaway-wedge", wedge ? "empty" : "not-empty", wedge);

        // (no two mitochondria's bounding spheres overlap)
        var mitos = animal.Instances.Where(i => i.Part == "mito").Select(i => CellFrame(animal, i.Centre)).ToList();
        var closest = double.PositiveInfinity;
        for (var i = 0; i < mitos.Count; i++) for (var j = i + 1; j < mitos.Count; j++) closest = Math.Min(closest, BioV.Distance(mitos[i], mitos[j]));
        r.Check("cell.animal.mito-spacing", F2(closest), closest >= 2.2 * 1.05 - 1e-9 || mitos.Count < 2);

        // (the same twice: built again from the seed, the same meshes)
        var again = CEBuild.Build(new CEBuildOptions(CEKind.Animal));
        var same = again.Triangles == animal.Triangles && again.Instances.Count == animal.Instances.Count
            && again.Instances.Zip(animal.Instances).All(z => BioV.Distance(z.First.Centre, z.Second.Centre) < 1e-12);
        r.Check("cell.deterministic", $"{animal.Instances.Count}instances", same);

        // (explode: the nucleus moves least; an organelle at the cell's radius moves 0.6 of it; the shells fade to 15%)
        var nucleus = animal.Instances.First(i => i.Part == "nucleus");
        var farMito = animal.Instances.Where(i => i.Part == "mito").OrderByDescending(i => BioV.Length(i.Centre)).First();
        var dn = BioV.Length(CEScene.Displacement(nucleus, 1, animal.CellRadius));
        var dm = BioV.Length(CEScene.Displacement(farMito, 1, animal.CellRadius));
        var probe = new CEInstance { Part = "mito", Index = 0, Centre = new BioVec(animal.CellRadius, 0, 0), Radius = 1, ExplodeFrom = new BioVec(animal.CellRadius, 0, 0) };
        r.Near("cell.explode-rule", BioV.Length(CEScene.Displacement(probe, 1, animal.CellRadius)) / animal.CellRadius, 0.6, 1e-9, 3);
        r.Check("cell.explode-nucleus-least", $"{F2(dn)}<{F2(dm)}", dn < dm);
        r.Check("cell.explode-fade", $"{F2(CEScene.ShellFade(0))},{F2(CEScene.ShellFade(0.15))},{F2(CEScene.ShellFade(0.6))}",
            Math.Abs(CEScene.ShellFade(0) - 1) < 1e-12 && Math.Abs(CEScene.ShellFade(0.3) - 0.15) < 1e-12 && Math.Abs(CEScene.ShellFade(0.6) - 0.15) < 1e-12);

        // (the plant: 40 chloroplasts 5.2 µm apart and 6 µm from the nucleus; the vacuole dented round the nucleus)
        var chloro = plant.Instances.Where(i => i.Part == "chloroplast").Select(i => CellFrame(plant, i.Centre)).ToList();
        var placedChloro = plant.Placed.GetValueOrDefault("chloroplast");
        var chloroGap = double.PositiveInfinity;
        for (var i = 0; i < chloro.Count; i++) for (var j = i + 1; j < chloro.Count; j++) chloroGap = Math.Min(chloroGap, BioV.Distance(chloro[i], chloro[j]));
        var n = new BioVec(-9, 9.2, 0);
        r.Check("cell.plant.chloroplasts", $"{placedChloro},gap{F2(chloroGap)}", placedChloro == 40 && chloroGap >= 5.2 && chloro.All(c => BioV.Distance(c, n) >= 6.0));
        if (plant.Instances.FirstOrDefault(i => i.Part == "vacuole")?.Pieces.FirstOrDefault()?.Mesh is { } vac)
        {
            var nearest = vac.Positions.Min(p => BioV.Distance(CellFrame(plant, BioMesh.D(p)), n));
            r.Near("cell.plant.vacuole-dent", nearest, 3.4, 1e-3, 3);
        }
        r.Check("cell.plant.counts",
            $"mito={plant.Placed.GetValueOrDefault("mito")},golgi={plant.Placed.GetValueOrDefault("golgi")},perox={plant.Placed.GetValueOrDefault("peroxisome")},ribo={plant.Extra.GetValueOrDefault("ribosome.free")}+{plant.Extra.GetValueOrDefault("ribosome.er")}",
            plant.Placed.GetValueOrDefault("mito") == 18 && plant.Placed.GetValueOrDefault("golgi") == 6 && plant.Placed.GetValueOrDefault("peroxisome") == 6
                && plant.Extra.GetValueOrDefault("ribosome.free") == 900 && plant.Extra.GetValueOrDefault("ribosome.er") == 300
                && plant.Placed.GetValueOrDefault("plasmodesma") == 30 && plant.Placed.GetValueOrDefault("microtubule") == 30);

        // (the bacterium: flagella left-handed; 1,500 ribosomes outside the nucleoid; three plasmids)
        r.Check("cell.bacterium.counts",
            $"flagella={bacterium.Placed.GetValueOrDefault("flagellum")},fimbriae={bacterium.Placed.GetValueOrDefault("fimbria")},ribo={bacterium.Extra.GetValueOrDefault("ribosome.free")},plasmids={bacterium.Placed.GetValueOrDefault("plasmid")}",
            bacterium.Placed.GetValueOrDefault("flagellum") == 6 && bacterium.Placed.GetValueOrDefault("fimbria") == 120 && bacterium.Extra.GetValueOrDefault("ribosome.free") == 1500
                && bacterium.Placed.GetValueOrDefault("plasmid") == 3);
        var d = BioV.Unit(new BioVec(-0.4, 0.3, 0.2));
        var helix = CEBuilder.FlagellumHelix(BioVec.Zero, d);
        // (left-handed: going along d, the filament turns about −d)
        var e1 = CEGeo.Perpendicular(d);
        var turning = 0.0;
        for (var k = 30; k < helix.Count - 1; k++)
        {
            var p0 = helix[k] - d * BioV.Dot(helix[k], d) + e1 * 0.2;
            var p1 = helix[k + 1] - d * BioV.Dot(helix[k + 1], d) + e1 * 0.2;
            turning += BioV.Dot(BioV.Cross(p0, p1), d);
        }
        var length = BioV.Dot(helix[^1], d);
        r.Check("cell.bacterium.flagellum", $"{(turning < 0 ? "left-handed" : "right-handed")},{F2(length)}µm", turning < 0 && Math.Abs(length - 6) < 0.05);

        // (true scale: 20,000 ribosomes at their true sizes, the rest of the cell as it was)
        var trueScale = CEBuild.Build(new CEBuildOptions(CEKind.Bacterium, TrueScale: true));
        var total = trueScale.Extra.GetValueOrDefault("ribosome.free") + trueScale.Extra.GetValueOrDefault("ribosome.er");
        var plantTrue = CEBuild.Build(new CEBuildOptions(CEKind.Plant, TrueScale: true));
        static List<BioVec> Centres(CEModel m) => m.Instances.Where(i => i.Part != "ribosome").Select(i => i.Centre).ToList();
        var sameRest = trueScale.Placed.GetValueOrDefault("flagellum") == bacterium.Placed.GetValueOrDefault("flagellum")
            && trueScale.Placed.GetValueOrDefault("plasmid") == bacterium.Placed.GetValueOrDefault("plasmid")
            && Centres(plantTrue).Count == Centres(plant).Count && Centres(plantTrue).Zip(Centres(plant)).All(z => BioV.Distance(z.First, z.Second) < 1e-9);
        r.Check("cell.true-scale", $"{total}ribosomes", total >= 19_000 && total <= 20_000 && sameRest
            && Math.Abs((trueScale.Instances.FirstOrDefault(i => i.Part == "ribosome")?.Radius ?? 0) - 0.010) < 1e-12);

        // (every part's card says what the model has of it)
        var lines = new[] { animal, plant, bacterium }.SelectMany(m => CEParts.All(m.Options.Kind).Select(m.ModelLine)).ToList();
        r.Check("cell.card-model-lines", $"{lines.Count(l => l.Length > 0)}/{lines.Count}", lines.All(l => l.Length > 0));
        if (CEParts.Part(CEKind.Animal, "mito") is { } mito && CEParts.Part(CEKind.Animal, "ribosome") is { } ribosome)
        {
            r.Same("cell.card-mito", animal.ModelLine(mito), "24 mitochondria, each 2.2 µm long and 0.9 µm wide. 2 of them are in the part cut away.");
            r.Check("cell.card-ribosome", animal.ModelLine(ribosome),
                animal.ModelLine(ribosome).StartsWith($"{CEModel.Count(1500)} free and {CEModel.Count(2500)} on the rough ER, each 0.12 µm across — drawn 4× their true size", StringComparison.Ordinal));
        }
        var plantWhole = CEBuild.Build(new CEBuildOptions(CEKind.Plant, Cutaway: false));
        r.Check("cell.plant-whole.triangles", $"{plantWhole.Triangles}", plantWhole.Triangles < 250_000);

        // (labels: every part drawn has somewhere to point)
        var missing = new[] { animal, plant, bacterium }.SelectMany(m => m.PartsShown.Where(p => !m.Anchors.ContainsKey(p)).Select(p => $"{m.Options.Kind.Id()}.{p}")).ToList();
        r.Check("cell.label-anchors", missing.Count == 0 ? "all" : string.Join(",", missing), missing.Count == 0);

        // (SA:V: the bacterium's far higher than the animal cell's — why cells are small)
        r.Check("cell.sa-v", $"{F2(animal.Area / animal.Volume)}/{F2(plant.Area / plant.Volume)}/{F2(bacterium.Area / bacterium.Volume)}",
            bacterium.Area / bacterium.Volume > 4 && animal.Area / animal.Volume < 0.5 && plant.Area / plant.Volume < animal.Area / animal.Volume);

        // (a scene: isolate shows only its part; look inside builds a cut mitochondrion with labels)
        var iso = CEScene.Content(animal, new CEView(Isolate: "mito"));
        var looked = CEScene.Content(animal, new CEView(Isolate: "mito", LookInside: true));
        r.Check("cell.isolate", $"{iso.Items.Count},{looked.Items.Count}items,{looked.Labels.Count}labels",
            iso.Items.All(i => i.Id?.StartsWith("organelle:mito:", StringComparison.Ordinal) == true) && looked.Items.Count > 0 && looked.Labels.Count == 4);
        var compare = CEBuild.CompareScene(false);
        r.Check("cell.compare-scene", $"{compare.Items.Count}items", compare.Items.Select(i => i.Id).ToHashSet().SetEquals(new[] { "cell:animal", "cell:plant", "cell:bacterium" })
            && compare.Labels.Count == 3);

        // (Windows: the true-scale cells, with their 20,000 ribosomes, still within what Viewport3D draws smoothly)
        r.Check("cell.true-scale.triangles", $"{plantTrue.Triangles}", plantTrue.Triangles < 400_000);
    }

    private static void CellMembraneChecks(BioCheckReport r)
    {
        var m = CEMembrane.Build(false);
        var slots = (double)(m.Lipids + m.Cholesterol);
        r.Check("cell.membrane.lipids", $"{m.Lipids}+{m.Cholesterol}chol", m.Lipids > 9000 && Math.Abs(m.Cholesterol / slots - 0.12) < 0.015);
        // (Windows: within what Viewport3D draws smoothly; the Mac allows 600k)
        r.Check("cell.membrane.triangles", $"{m.Triangles}", m.Triangles < 400_000);
        r.Check("cell.membrane.labels", $"{m.Content.Labels.Count}", m.Content.Labels.Count == 12 && CEMembrane.Parts.Count == 12);
        var moving = CEMembrane.Moving();
        var sameCarrier = moving.Carrier.Zip(m.Carrier).All(z => BioV.Distance(z.First.Centre, z.Second.Centre) < 1e-9
            && Math.Abs(z.First.Inside.AxisAngle.Degrees - z.Second.Inside.AxisAngle.Degrees) < 1e-9);
        r.Check("cell.membrane.carrier", $"{m.Carrier.Count}helices", m.Carrier.Count == 7 && moving.Carrier.Count == 7 && sameCarrier);
        // (the bilayer's thickness: head centres 3.9 nm apart, about 4.8 nm across the heads)
        r.Near("cell.membrane.thickness", 2 * CEMembrane.HeadY + 0.9, 4.8, 1e-9, 2);
        // (the quaternion of a frame turns as the frame does)
        var q = CEQuat.Of(CEMembrane.Display);
        var v = new BioVec(0.3, -0.5, 0.8);
        r.Near("cell.membrane.turn", BioV.Distance(q.Rotate(v), CEMembrane.Display.ApplyVector(v)), 0, 1e-12, 3);
    }
}
