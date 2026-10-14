namespace Simpl.Tools;

// The Nucleotides page's model (spec §4.2 Nucleotides, §6.5, §6.6): the eight nucleotides and ATP from their CCD
// coordinates, each atom sorted into its part — the phosphate group, the sugar (deoxyribose or ribose), the nitrogenous
// base — so the 3D view can colour by part and label each with a leader line; the sugar's carbons numbered 1′–5′ and the
// base's ring atoms 1–9 (where "5′ and 3′" come from); DNA against RNA marked at the 2′ position and the 5-methyl; and
// the Watson–Crick pairs in 2D from the B-DNA model (§6.7), their H-bonds between the exact atoms (§6.5).

/// <summary>The three parts of a nucleotide.</summary>
public enum DnaPart { Phosphate, Sugar, Base }

/// <summary>What the nucleotide scene is drawn with.</summary>
public sealed record DnaNucleotideOptions(string Code, bool Parts = true, bool Numbers = false, bool Differences = true, bool Hydrogens = true,
    bool Ph7 = true, bool Dark = false)
{
    public string Key => $"{Code}|{Parts}|{Numbers}|{Differences}|{Hydrogens}|{Ph7}|{Dark}";
}

/// <summary>A base pair drawn flat (§6.7's 2D pairs): its atoms (base, name, element, x, y in Å; +y toward the major
/// groove), bonds, rings and Watson–Crick H-bonds (donor, its H, acceptor; heavy-atom distance).</summary>
public sealed record DnaPairDrawing(string Name, List<(char Base, string Name, string Element, double X, double Y)> Atoms, List<(int A, int B, int Order)> Bonds,
    List<(int Donor, int H, int Acceptor, double Distance)> HBonds, List<(char Base, List<int> Ring)> Rings);

public static class DnaNucleotides
{
    /// <summary>§6.6's codes in the chooser's order: the four DNA nucleotides, the four RNA ones, ATP.</summary>
    public static readonly string[] Codes = { "DA", "DC", "DG", "DT", "A", "C", "G", "U", "ATP" };
    public static readonly string[] DnaCodes = { "DA", "DC", "DG", "DT" };
    public static readonly string[] RnaCodes = { "A", "C", "G", "U" };

    /// <summary>"dAMP" … "UMP", "ATP".</summary>
    public static string Short(string code) => code switch
    {
        "DA" => "dAMP", "DC" => "dCMP", "DG" => "dGMP", "DT" => "dTMP",
        "A" => "AMP", "C" => "CMP", "G" => "GMP", "U" => "UMP", _ => code,
    };

    public static string Name(string code) => code switch
    {
        "DA" => "Deoxyadenosine monophosphate", "DC" => "Deoxycytidine monophosphate", "DG" => "Deoxyguanosine monophosphate",
        "DT" => "Thymidine monophosphate", "A" => "Adenosine monophosphate", "C" => "Cytidine monophosphate",
        "G" => "Guanosine monophosphate", "U" => "Uridine monophosphate", "ATP" => "Adenosine triphosphate", _ => code,
    };

    /// <summary>The base's letter.</summary>
    public static char Base(string code) => code switch { "DA" or "A" or "ATP" => 'A', "DC" or "C" => 'C', "DG" or "G" => 'G', "DT" => 'T', _ => 'U' };

    /// <summary>Ribose (RNA and ATP) rather than deoxyribose (DNA).</summary>
    public static bool IsRibose(string code) => !code.StartsWith('D');

    public static string BaseName(char b) => b switch { 'A' => "adenine", 'C' => "cytosine", 'G' => "guanine", 'T' => "thymine", _ => "uracil" };

    public static bool IsPurine(char b) => b is 'A' or 'G';

    /// <summary>The base's N bonded to C1′ (the glycosidic bond): N9 in a purine, N1 in a pyrimidine.</summary>
    public static string GlycosidicN(char b) => IsPurine(b) ? "N9" : "N1";

    /// <summary>The part colours (§4.2): phosphate orange, deoxyribose violet, ribose orange-red, the base its own colour.</summary>
    public static readonly BioHexPair Phosphate = new("#f0a01e", "#ffbd45");
    public static readonly BioHexPair Ribose = new("#e0502a", "#ff7a57");

    public static BioHexPair PartColour(DnaPart part, string code) => part switch
    {
        DnaPart.Phosphate => Phosphate,
        DnaPart.Sugar => IsRibose(code) ? Ribose : BioPalette.Strand1,
        _ => BioPalette.Base(Base(code)),
    };

    public static string PartKey(DnaPart p) => p switch { DnaPart.Phosphate => "phosphate", DnaPart.Sugar => "sugar", _ => "base" };

    /// <summary>
    /// Each atom's part, found from the bonds: every P with the O atoms bonded to it (O5′ and the bridging O of ATP's
    /// chain among them) and their H is the phosphate; the rest of the primed atoms (C1′–C5′, O4′, O3′, O2′ and their H)
    /// are the sugar; everything else is the base.
    /// </summary>
    public static Dictionary<string, DnaPart> Parts(BioMolecule m)
    {
        var neighbours = new Dictionary<string, List<string>>();
        foreach (var b in m.Bonds)
        {
            (neighbours.TryGetValue(b.A, out var la) ? la : neighbours[b.A] = new List<string>()).Add(b.B);
            (neighbours.TryGetValue(b.B, out var lb) ? lb : neighbours[b.B] = new List<string>()).Add(b.A);
        }
        var element = m.Atoms.ToDictionary(a => a.Name, a => a.Element);
        var out_ = new Dictionary<string, DnaPart>();
        foreach (var a in m.Atoms.Where(a => a.Element == "P"))
        {
            out_[a.Name] = DnaPart.Phosphate;
            foreach (var o in neighbours.GetValueOrDefault(a.Name) ?? new List<string>())
                if (element.GetValueOrDefault(o) == "O") out_[o] = DnaPart.Phosphate;
        }
        foreach (var a in m.Atoms)
        {
            if (out_.ContainsKey(a.Name)) continue;
            if (a.Element == "H")
            {
                var host = (neighbours.GetValueOrDefault(a.Name) ?? new List<string>()).FirstOrDefault();
                if (host != null && out_.TryGetValue(host, out var hp)) { out_[a.Name] = hp; continue; }
                if (host != null && host.Contains('\'')) { out_[a.Name] = DnaPart.Sugar; continue; }
                out_[a.Name] = a.Name.Contains('\'') ? DnaPart.Sugar : DnaPart.Base;
                continue;
            }
            out_[a.Name] = a.Name.Contains('\'') ? DnaPart.Sugar : DnaPart.Base;
        }
        // (an H on a phosphate O, met before its O was sorted above)
        foreach (var a in m.Atoms.Where(a => a.Element == "H"))
            if ((neighbours.GetValueOrDefault(a.Name) ?? new List<string>()).FirstOrDefault() is { } host && out_.TryGetValue(host, out var hp) && hp == DnaPart.Phosphate)
                out_[a.Name] = DnaPart.Phosphate;
        return out_;
    }

    /// <summary>The number a ring atom or sugar carbon carries (§4.2 Number the carbons): "1′"–"5′" on the sugar's carbons,
    /// "1"–"9" on a purine's ring atoms and "1"–"6" on a pyrimidine's; null for anything else.</summary>
    public static string? Number(string name, char b)
    {
        if (name.Length == 3 && name[0] == 'C' && name[2] == '\'' && name[1] is >= '1' and <= '5') return $"{name[1]}′";
        if (name.Length != 2 || name[0] is not ('C' or 'N') || name[1] is < '1' or > '9') return null;
        var k = name[1] - '0';
        var ring = IsPurine(b)
            ? new[] { "N1", "C2", "N3", "C4", "C5", "C6", "N7", "C8", "N9" }
            : new[] { "N1", "C2", "N3", "C4", "C5", "C6" };
        return ring.Contains(name) ? $"{k}" : null;
    }

    /// <summary>The nucleotide's 3D scene (ball and stick, the chemistry builder's radii, CPK or by part), its labels with
    /// leader lines, the DNA-vs-RNA marks, and the numbers for its carbons (as words over the scene).</summary>
    public static (BioSceneContent Content, List<DnaLetter> Numbers) Build(DnaNucleotideOptions o, DnaAtomPaint? paint = null)
    {
        paint ??= DnaAtomPaint.Plain;
        var mol = BioData.Molecule(o.Code) ?? BioData.Nucleotides[0];
        var model = mol.Model(ph7: o.Ph7, hydrogens: o.Hydrogens);
        var parts = Parts(mol);
        var b = Base(o.Code);
        var dark = o.Dark;
        DnaPart PartOf(BioMoleculeModel.Atom a) => parts.GetValueOrDefault(a.Name, DnaPart.Base);
        string Hex(BioMoleculeModel.Atom a) => o.Parts ? PartColour(PartOf(a), o.Code).HexFor(dark) : paint.Hex(a.Element);
        var unit = BioShapes.Sphere(1, 14, 28);
        var items = new List<BioSceneItem>();
        foreach (var a in model.Atoms)
        {
            var r = paint.Radius(a.Element, DnaStyle.Atoms);
            var mesh = unit.Transformed(new BioFrame(a.Pos, new BioVec(r, 0, 0), new BioVec(0, r, 0), new BioVec(0, 0, r)));
            items.Add(new BioSceneItem($"part:{PartKey(PartOf(a))}:{a.Id}", mesh, new BioMaterial(Hex(a), Roughness: 0.35)));
        }
        var bondGrey = new BioMaterial("#9e9e9e", Roughness: 0.35);
        var glycosidic = GlycosidicN(b);
        foreach (var bd in model.Bonds)
        {
            BioMoleculeModel.Atom x = model.Atoms[bd.A], y = model.Atoms[bd.B];
            var isGly = (x.Name == "C1'" && y.Name == glycosidic) || (y.Name == "C1'" && x.Name == glycosidic);
            string? id = isGly ? "part:glycosidic" : PartOf(x) == PartOf(y) ? $"part:{PartKey(PartOf(x))}:b{bd.A}-{bd.B}" : null;
            var mat = isGly ? new BioMaterial(dark ? "#f2f2f7" : "#3a3a3c", Roughness: 0.35) : bondGrey;
            var len = BioV.Distance(x.Pos, y.Pos);
            if (len < 1e-6) continue;
            var dir = (y.Pos - x.Pos) / len;
            var side = BioV.Cross(dir, new BioVec(0, 0, 1));
            if (BioV.Length(side) < 0.1) side = BioV.Cross(dir, new BioVec(0, 1, 0));
            side = BioV.Unit(side);
            if (isGly) items.Add(new BioSceneItem(id, BioShapes.Cylinder(x.Pos, y.Pos, 0.16, 16, caps: false), mat));
            else if (bd.Order >= 2)
                foreach (var off in new[] { -0.11, 0.11 })
                    items.Add(new BioSceneItem(id, BioShapes.Cylinder(x.Pos + side * off, y.Pos + side * off, 0.06, 12, caps: false), mat));
            else items.Add(new BioSceneItem(id, BioShapes.Cylinder(x.Pos, y.Pos, 0.09, 12, caps: false), mat));
        }

        // (the labels: each part, and the bond that joins the base to the sugar)
        BioVec Centre(IEnumerable<string> names)
        {
            var ps = names.Select(mol.Pos).Where(p => p != null).Select(p => p!.Value).ToList();
            return ps.Count == 0 ? BioVec.Zero : ps.Aggregate(BioVec.Zero, (s, p) => s + p) / ps.Count;
        }
        var labels = new List<BioLabel3D>();
        var phosphates = mol.Atoms.Where(a => a.Element == "P").Select(a => a.Name).ToList();
        labels.Add(new BioLabel3D("part:phosphate", phosphates.Count > 1 ? "Three phosphate groups (α, β, γ)" : "Phosphate group", Centre(phosphates), Area: 3));
        labels.Add(new BioLabel3D("part:sugar", IsRibose(o.Code) ? "Ribose (2′–OH)" : "Deoxyribose (no O on 2′)", Centre(new[] { "C1'", "C2'", "C3'", "C4'", "O4'" }), Area: 3));
        var ringNames = IsPurine(b) ? new[] { "N1", "C2", "N3", "C4", "C5", "C6", "N7", "C8", "N9" } : new[] { "N1", "C2", "N3", "C4", "C5", "C6" };
        labels.Add(new BioLabel3D("part:base", $"Nitrogenous base ({BaseName(b)})", Centre(ringNames), Area: 3));
        labels.Add(new BioLabel3D("part:glycosidic", $"Glycosidic bond (C1′–{glycosidic})", Centre(new[] { "C1'", glycosidic }), Area: 2));

        // (DNA against RNA: the 2′ position, and thymine's 5-methyl or uracil's lack of one)
        if (o.Differences)
        {
            var halo = new BioMaterial(dark ? "#ff6482" : "#ff2d55", Alpha: 0.22, Roughness: 0.2, DoubleSided: true);
            if (mol.Pos("C2'") is { } c2)
            {
                var o2 = mol.Pos("O2'");
                var centre = o2 is { } ox ? (c2 + ox) / 2 : c2;
                items.Add(new BioSceneItem("diff:2", BioShapes.Sphere(o2 != null ? 1.75 : 1.5, 16, 32).Translated(centre), halo, Order: 1));
                labels.Add(new BioLabel3D("diff:2", IsRibose(o.Code) ? "2′ carbon: OH (ribose)" : "2′ carbon: H only (deoxyribose)", centre, Area: 1));
            }
            if (b == 'T' && mol.Pos("C7") is { } c7)
            {
                items.Add(new BioSceneItem("diff:5", BioShapes.Sphere(1.6, 16, 32).Translated(c7), halo, Order: 1));
                labels.Add(new BioLabel3D("diff:5", "5-methyl group (T has it; U doesn’t)", c7, Area: 1));
            }
            else if (b == 'U' && mol.Pos("C5") is { } c5)
            {
                var h5 = mol.Pos("H5") ?? c5;
                var centre = (c5 + h5) / 2;
                items.Add(new BioSceneItem("diff:5", BioShapes.Sphere(1.3, 16, 32).Translated(centre), halo, Order: 1));
                labels.Add(new BioLabel3D("diff:5", "No methyl on C5 (uracil)", centre, Area: 1));
            }
        }

        var numbers = new List<DnaLetter>();
        if (o.Numbers)
        {
            var mid = model.Atoms.Count == 0 ? BioVec.Zero : model.Atoms.Aggregate(BioVec.Zero, (s, a) => s + a.Pos) / model.Atoms.Count;
            foreach (var a in model.Atoms)
            {
                if (Number(a.Name, b) is not { } text) continue;
                var outward = BioV.Unit(a.Pos - mid);
                var r = paint.Radius(a.Element, DnaStyle.Atoms);
                numbers.Add(new DnaLetter(text, a.Pos + outward * (r + 0.42), 0.62, 0.6, $"part:{PartKey(PartOf(a))}:{a.Id}", null));
            }
        }
        return (new BioSceneContent { Items = items, Labels = labels }, numbers);
    }

    // ---- the 2D pairs (§6.5, §6.7) ----

    /// <summary>"AT", "GC" or "AU" drawn flat: §6.7's projected pair (A·U is A·T without T's C7 and its H atoms, with an
    /// H5 on C5 1.08 Å along the C7 direction), bonds from §6.6's lists, rings, and the Watson–Crick H-bonds.</summary>
    public static DnaPairDrawing Pair2D(string key)
    {
        var uracil = key == "AU";
        var source = BioData.BasePairs2D.GetValueOrDefault(uracil ? "AT" : key) ?? new List<BioData.PairAtom>();
        var atoms = new List<(char Base, string Name, string Element, double X, double Y)>();
        foreach (var a in source)
        {
            var b = uracil && a.Base == 'T' ? 'U' : a.Base;
            if (b == 'U' && a.Name is "C7" or "H71" or "H72" or "H73") continue;
            atoms.Add((b, a.Name, a.Name[..1], a.X, a.Y));
        }
        if (uracil && source.FirstOrDefault(a => a.Base == 'T' && a.Name == "C5") is { } c5 && source.FirstOrDefault(a => a.Base == 'T' && a.Name == "C7") is { } c7)
        {
            double dx = c7.X - c5.X, dy = c7.Y - c5.Y, l = Math.Sqrt(dx * dx + dy * dy);
            atoms.Add(('U', "H5", "H", c5.X + dx / l * 1.08, c5.Y + dy / l * 1.08));
        }
        int Index(char b, string name) => atoms.FindIndex(a => a.Base == b && a.Name == name);
        var bonds = new List<(int, int, int)>();
        foreach (var b in atoms.Select(a => a.Base).Distinct())
        {
            var code = b switch { 'A' => "DA", 'C' => "DC", 'G' => "DG", _ => "DT" };
            var list = BioData.Molecule(code)?.Bonds ?? new List<BioBondRecord>();
            foreach (var bd in list)
            {
                int x = Index(b, bd.A), y = Index(b, bd.B);
                if (x >= 0 && y >= 0) bonds.Add((x, y, bd.Order));
            }
            if (b == 'U' && Index('U', "C5") is >= 0 and var u5 && Index('U', "H5") is >= 0 and var h5) bonds.Add((u5, h5, 1));
        }
        var rings = new List<(char, List<int>)>();
        foreach (var b in atoms.Select(a => a.Base).Distinct())
        {
            var names = IsPurine(b)
                ? new[] { new[] { "N9", "C8", "N7", "C5", "C4" }, new[] { "C4", "C5", "C6", "N1", "C2", "N3" } }
                : new[] { new[] { "N1", "C2", "N3", "C4", "C5", "C6" } };
            foreach (var ring in names) rings.Add((b, ring.Select(n => Index(b, n)).ToList()));
        }
        var purine = key[0];
        var pyrimidine = uracil ? 'U' : key[1];
        var hb = new List<(int, int, int, double)>();
        var spec = purine == 'A'
            ? new[] { ('A', "N6", "H61", pyrimidine, "O4"), (pyrimidine, "N3", "H3", 'A', "N1") }
            : new[] { ('G', "N1", "H1", 'C', "N3"), ('C', "N4", "H41", 'G', "O6"), ('G', "N2", "H22", 'C', "O2") };
        // (each H-bond's length as the 3D model has it — §6.5's numbers; the flat projection shortens them a hair)
        var lengths = DnaHelix.PairBonds(BioBDNA.Build($"{purine}"), 0).Select(x => x.Distance).ToList();
        for (var k = 0; k < spec.Length; k++)
        {
            var (db, dn, hn, ab, an) = spec[k];
            int d = Index(db, dn), h = Index(db, hn), acc = Index(ab, an);
            if (d < 0 || h < 0 || acc < 0) continue;
            var flat = Math.Sqrt(Math.Pow(atoms[d].X - atoms[acc].X, 2) + Math.Pow(atoms[d].Y - atoms[acc].Y, 2));
            hb.Add((d, h, acc, k < lengths.Count ? lengths[k] : flat));
        }
        var name = uracil ? "A·U" : $"{key[0]}·{key[1]}";
        return new DnaPairDrawing(name, atoms, bonds, hb, rings);
    }
}
