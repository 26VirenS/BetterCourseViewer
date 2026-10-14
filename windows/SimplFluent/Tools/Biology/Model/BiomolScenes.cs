using System.Globalization;

namespace Simpl.Tools;

// The Biomolecule Builder's scenes as values (spec §4.4, §3.4, §9), the Mac's BiomolScenes.swift: each page's molecule
// drawn as the chemistry builder draws one (CPK colours and radii from the chemistry tables, handed in as BiomolChem;
// bonds by order), coloured by element, part or class, with the bonds a page is about highlighted (peptide, glycosidic,
// phosphodiester, ester, the cis double bond), backbone H-bonds as dashes, cartoons over or instead of the atoms, and
// labels with leader lines; and the short motions a page plays (a residue sliding in as water lifts off, a sugar split
// by water) as groups of items with where they go and how they fade. Ids name what a click picks: a part
// ("part:amino"), a residue ("ss:2:res:7"), an atom ("…:atom:31") — so a label pressed selects its whole part. Made off
// the UI thread (BioSceneView.Show), so it is plain arithmetic.

/// <summary>The chemistry tables atoms are drawn with: CPK colours as "#rrggbb" and the covalent and van der Waals radii
/// (Å). The view hands in the chemistry builder's (MBChem); the self-check uses <see cref="Plain"/>.</summary>
public sealed record BiomolChem(Func<string, string> Cpk, Func<string, double> Covalent, Func<string, double> Vdw)
{
    public static readonly BiomolChem Plain = new(
        s => s switch { "H" => "#f2f2f2", "C" => "#909090", "N" => "#3050f8", "O" => "#ff0d0d", "P" => "#ff8000", "S" => "#ffff30", _ => "#c8c8c8" },
        s => s switch { "H" => 0.31, "C" => 0.76, "N" => 0.71, "O" => 0.66, "P" => 1.07, "S" => 1.05, _ => 0.75 },
        s => s switch { "H" => 1.2, "C" => 1.7, "N" => 1.55, "O" => 1.52, "P" => 1.8, "S" => 1.8, _ => 1.7 });
}

/// <summary>What colours the atoms (the shared "Colour by" control).</summary>
public enum BiomolColourBy { Element, Part, Class }

public static class BiomolColourBys
{
    public static readonly BiomolColourBy[] All = { BiomolColourBy.Element, BiomolColourBy.Part, BiomolColourBy.Class };

    public static string Label(this BiomolColourBy c) => c == BiomolColourBy.Element ? "Element" : (c == BiomolColourBy.Part ? "Part" : "Class");

    /// <summary>"element", "part", "class" (the Mac's raw values; a launch's state names them).</summary>
    public static string Key(this BiomolColourBy c) => c == BiomolColourBy.Element ? "element" : (c == BiomolColourBy.Part ? "part" : "class");

    public static BiomolColourBy? Parse(string? s) => All.Cast<BiomolColourBy?>().FirstOrDefault(c => string.Equals(c!.Value.Key(), s?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>The shared controls' choices, as a scene needs them.</summary>
public readonly record struct BiomolLook(BioAtomStyle Style = BioAtomStyle.BallStick, bool Hydrogens = true, bool Ph7 = true,
    BiomolColourBy ColourBy = BiomolColourBy.Element, bool Labels = true, bool Dark = false)
{
    public BiomolLook() : this(BioAtomStyle.BallStick) { }

    public string Key => $"{Style}/{Hydrogens}/{Ph7}/{ColourBy.Key()}/{Labels}/{Dark}";
}

/// <summary>A short text drawn at a point in a scene (a ring carbon's number, a charge), facing the camera.</summary>
/// <param name="Hex">"#rrggbb", or null for the atom-symbol white.</param>
/// <param name="Size">Its size (Å).</param>
/// <param name="Lift">How far toward the viewer it sits (an atom's radius, so it shows on the atom's near side).</param>
public sealed record BiomolMark(BioVec Pos, string Text, string? Hex = null, double Size = 0.42, double Lift = 0);

/// <summary>A short motion (§4.4 Animate dehydration, the sugar's hydrolysis): groups of items, each moved from one offset
/// to another and faded, after a delay; a <c>Pulse</c> flashes in and out (a new bond).</summary>
public sealed class BiomolMotion
{
    public sealed record Group(List<BioSceneItem> Items, BioVec From = default, BioVec To = default, double FromAlpha = 1, double ToAlpha = 1,
        double Delay = 0, double Duration = 0.6, bool Pulse = false);

    public List<Group> Groups { get; init; } = new();

    /// <summary>How long it runs (s).</summary>
    public double Length => Groups.Count == 0 ? 0 : Groups.Max(g => g.Delay + g.Duration);

    /// <summary>A group's offset and opacity at a time (s) after the motion began, eased with the app's strong ease-out.</summary>
    public static (BioVec Offset, double Alpha) At(Group g, double seconds)
    {
        var u = g.Duration <= 0 ? 1 : Math.Clamp((seconds - g.Delay) / g.Duration, 0, 1);
        if (g.Pulse)
        {
            // (in and out once: brightest half way)
            var a = Math.Sin(Math.PI * u);
            return (g.From + (g.To - g.From) * u, seconds < g.Delay ? 0 : g.FromAlpha + (g.ToAlpha - g.FromAlpha) * a);
        }
        var e = BiomolEase.StrongOut(u);
        return (g.From + (g.To - g.From) * e, g.FromAlpha + (g.ToAlpha - g.FromAlpha) * e);
    }
}

/// <summary>A cartoon over the atoms, instead of them, or none.</summary>
public enum BiomolRibbon { Off, Over, Only }

public static class BiomolRibbons
{
    public static readonly BiomolRibbon[] All = { BiomolRibbon.Off, BiomolRibbon.Over, BiomolRibbon.Only };

    public static string Label(this BiomolRibbon r) => r == BiomolRibbon.Off ? "Atoms" : (r == BiomolRibbon.Over ? "Both" : "Ribbon");

    public static string Key(this BiomolRibbon r) => r == BiomolRibbon.Off ? "off" : (r == BiomolRibbon.Over ? "over" : "only");
}

/// <summary>A scene with the marks to draw on it (BioSceneContent's items and labels, and the billboards' text).</summary>
public sealed class BiomolScene
{
    public BioSceneContent Content { get; init; } = new();
    public List<BiomolMark> Marks { get; init; } = new();
}

public static class BiomolScenes
{
    // ---- atoms ----

    /// <summary>A model's atoms and bonds as items (the chemistry builder's look), each atom's id given by <paramref name="id"/>,
    /// its colour by <paramref name="colour"/> (null: CPK); H left out when the look hides them, and whatever
    /// <paramref name="skip"/> says.</summary>
    public static List<BioSceneItem> AtomItems(BioMoleculeModel m, BiomolLook look, BiomolChem chem, Func<BioMoleculeModel.Atom, string?> colour,
        Func<BioMoleculeModel.Atom, string> id, Func<BioMoleculeModel.Atom, bool>? skip = null)
    {
        var cpk = new Dictionary<string, string>();
        var radii = new Dictionary<string, double>();
        foreach (var a in m.Atoms)
        {
            if (cpk.ContainsKey(a.Element)) continue;
            cpk[a.Element] = chem.Cpk(a.Element);
            radii[a.Element] = BioMoleculeMeshes.Radius(look.Style, chem.Covalent(a.Element), chem.Vdw(a.Element));
        }
        var items = BioMoleculeMeshes.Items(m, look.Style, a => radii.GetValueOrDefault(a.Element, 0.3), a => colour(a) ?? cpk.GetValueOrDefault(a.Element, "#c8c8c8"),
            skip: a => (!look.Hydrogens && a.Element == "H") || (skip?.Invoke(a) ?? false));
        return items.Select(it =>
        {
            if (it.Id is not { } s || !s.StartsWith("atom:", StringComparison.Ordinal) || !int.TryParse(s[5..], out var k) || k >= m.Atoms.Count) return it;
            return it with { Id = id(m.Atoms[k]) };
        }).ToList();
    }

    /// <summary>The drawn radius of an atom (Å) in a look.</summary>
    public static double Radius(string element, BiomolLook look, BiomolChem chem) => BioMoleculeMeshes.Radius(look.Style, chem.Covalent(element), chem.Vdw(element));

    /// <summary>A glowing sleeve round a bond (or a path of bonds): the highlighted peptide, glycosidic, ester or
    /// phosphodiester bond, translucent so the bond shows through.</summary>
    public static BioSceneItem Sleeve(IReadOnlyList<BioVec> pts, string id, string hex, double radius = 0.26)
    {
        var mesh = pts.Count == 2 ? BioShapes.Cylinder(pts[0], pts[1], radius, 16) : BioPath.Tube(pts, radius, sides: 12, smooth: false);
        return new BioSceneItem(id, mesh, new BioMaterial(hex, Alpha: 0.5, Roughness: 0.3, Emission: 0.45), 3);
    }

    /// <summary>An H-bond's dashes (§3.8: 0.15 Å thick).</summary>
    public static BioSceneItem HBondItem(BioVec a, BioVec b, string id, bool dark) =>
        new(id, BioMoleculeMeshes.Dashes(a, b, 0.075, 0.28, 0.2), new BioMaterial(BioPalette.HBond.HexFor(dark), Roughness: 0.5));

    /// <summary>The atom of a group nearest the group's centre (a label's anchor sits on an atom, so the ray test finds it).</summary>
    public static BioVec? Anchor(IReadOnlyList<BioVec> pts)
    {
        if (pts.Count == 0) return null;
        var c = BiomolMath.Centroid(pts);
        return pts.OrderBy(p => BioV.Distance(p, c)).First();
    }

    /// <summary>The charge marks of a form ("+" blue, "−" red), each just outside its atom, away from the molecule's centre.</summary>
    public static List<BiomolMark> ChargeMarks(BioMoleculeModel m, IReadOnlyDictionary<string, int> charges, BiomolLook look, BiomolChem chem)
    {
        if (charges.Count == 0) return new List<BiomolMark>();
        var centre = m.Bounds.Centre;
        var out_ = new List<BiomolMark>();
        foreach (var a in m.Atoms)
        {
            if (!charges.TryGetValue(a.Name, out var q) || q == 0) continue;
            var dir = BioV.Unit(a.Pos - centre);
            var r = Radius(a.Element, look, chem);
            out_.Add(new BiomolMark(a.Pos + (BioV.Length(dir) > 0 ? dir : new BioVec(0, 1, 0)) * (r + 0.42), q > 0 ? "+" : "−",
                (q > 0 ? BioPalette.Basic : BioPalette.Acidic).HexFor(look.Dark), 0.62));
        }
        return out_;
    }

    // ---- amino acids (§4.4 Amino acids) ----

    /// <summary>A free amino acid: each atom "part:&lt;amino|alpha|carboxyl|r&gt;:atom:&lt;id&gt;", coloured by element, part, or
    /// class (the R group in its class's colour, the rest grey); its parts labelled. Mirrored, it is the D-form.</summary>
    public static BioSceneContent AminoAcid(char one, BiomolLook look, bool mirrored, BiomolChem chem)
    {
        if (BiomolAminoAcid.Template(one) is not { } t) return new BioSceneContent();
        var m = BiomolAminoAcid.Model(one, look.Ph7, mirrored);
        var cls = BioAminoAcids.ByOne.GetValueOrDefault(char.ToUpperInvariant(one))?.Group ?? BioAAClass.Nonpolar;
        var parts = m.Atoms.Select(a => BiomolAminoAcid.Part(a.Name, t)).ToList();
        var items = AtomItems(m, look, chem, a => look.ColourBy switch
        {
            BiomolColourBy.Element => null,
            BiomolColourBy.Part => parts[a.Id].Colour.HexFor(look.Dark),
            _ => parts[a.Id] == BiomolParts.RGroup ? BioPalette.AminoAcid(cls).HexFor(look.Dark) : BiomolParts.Grey.HexFor(look.Dark),
        }, a => $"part:{parts[a.Id].Id}:atom:{a.Id}");
        var labels = new List<BioLabel3D>();
        if (look.Labels)
            foreach (var p in new[] { BiomolParts.Amino, BiomolParts.Alpha, BiomolParts.Carboxyl, BiomolParts.RGroup })
            {
                var pts = m.Atoms.Where(a => parts[a.Id] == p && (look.Hydrogens || a.Element != "H")).Select(a => a.Pos).ToList();
                if (Anchor(pts) is not { } at) continue;
                var text = p.Name;
                if (p == BiomolParts.Amino) text = look.Ph7 ? "Amino group (–NH₃⁺)" : "Amino group (–NH₂)";
                else if (p == BiomolParts.Carboxyl) text = look.Ph7 ? "Carboxyl group (–COO⁻)" : "Carboxyl group (–COOH)";
                else if (p == BiomolParts.RGroup) text = t.Code == "GLY" ? "R group (just H)" : (t.Code == "PRO" ? "R group (ring back to N)" : "R group (side chain)");
                labels.Add(new BioLabel3D($"part:{p.Id}", text, at, Area: pts.Count));
            }
        return new BioSceneContent { Items = items, Labels = labels };
    }

    // ---- peptides (§4.4 Proteins) ----

    /// <summary>What a peptide scene shows besides its atoms.</summary>
    /// <param name="Hidden">Residues left out (one sliding in or out).</param>
    /// <param name="HideTerminalOH">The C-terminal OXT and HXT left out (water arriving to make them).</param>
    public sealed record PeptideOptions(bool HBonds = false, BiomolRibbon Ribbon = BiomolRibbon.Off, HashSet<int>? Hidden = null, bool HideTerminalOH = false, bool LabelSegments = true)
    {
        public HashSet<int> HiddenSet => Hidden ?? new HashSet<int>();
    }

    /// <summary>The segment (§ cartoon) each residue is in.</summary>
    public static (List<BiomolCartoon.Segment> Segments, int[] Of) SegmentOf(BiomolPeptide p)
    {
        var segs = BiomolCartoon.Segments(p.Built.Select(s => s.Cartoon()).ToList());
        var of = new int[p.Count];
        for (var k = 0; k < segs.Count; k++)
            for (var i = segs[k].First; i <= segs[k].Last; i++) of[i] = k;
        return (segs, of);
    }

    public static readonly HashSet<string> BackboneNames = new() { "N", "CA", "C", "O", "OXT", "H", "H2", "H3", "HXT", "HA", "HA2", "HA3" };

    /// <summary>A peptide: atoms "&lt;prefix&gt;ss:&lt;k&gt;:res:&lt;i&gt;:atom:&lt;n&gt;" (n its index in the peptide), coloured by
    /// element, part (backbone slate, R groups in their class colours, peptide bonds glowing) or class (each residue in its
    /// class colour); its backbone H-bonds ("&lt;prefix&gt;hbond:&lt;k&gt;"); its cartoon ("&lt;prefix&gt;ss:&lt;k&gt;:cartoon"); its labels.</summary>
    public static BioSceneContent Peptide(BiomolPeptide p, BiomolLook look, PeptideOptions? options, BiomolChem chem, string prefix = "", string labelPrefix = "")
    {
        var o = options ?? new PeptideOptions();
        var hidden = o.HiddenSet;
        if (p.Count == 0) return new BioSceneContent();
        var (segs, of) = SegmentOf(p);
        var (m, index) = p.Model(hydrogens: true);
        var items = new List<BioSceneItem>();
        var last = p.Count - 1;
        bool HiddenAtom(int k)
        {
            var a = p.Atoms[k];
            if (hidden.Contains(a.Residue)) return true;
            return o.HideTerminalOH && a.Residue == last && (a.Name == "OXT" || a.Name == "HXT");
        }
        if (o.Ribbon != BiomolRibbon.Only)
        {
            items.AddRange(AtomItems(m, look, chem, PeptideColour(p, index, look), a =>
            {
                var k = index[a.Id];
                var r = p.Atoms[k].Residue;
                return $"{prefix}ss:{of[r]}:res:{r}:atom:{k}";
            }, a => HiddenAtom(index[a.Id])));
            if (look.ColourBy == BiomolColourBy.Part)
                foreach (var b in p.Bonds)
                    if (b.Peptide && !HiddenAtom(b.A) && !HiddenAtom(b.B))
                        items.Add(Sleeve(new[] { p.Atoms[b.A].Pos, p.Atoms[b.B].Pos }, $"{prefix}pbond:{p.Atoms[b.B].Residue}", BiomolParts.BondHighlight.HexFor(look.Dark), 0.22));
        }
        if (o.HBonds)
            for (var k = 0; k < p.HBonds.Count; k++)
            {
                var h = p.HBonds[k];
                if (hidden.Contains(h.Donor) || hidden.Contains(h.Acceptor)) continue;
                var from = look.Hydrogens && o.Ribbon != BiomolRibbon.Only ? p.Atoms[h.H].Pos : p.Atoms[h.N].Pos;
                items.Add(HBondItem(from, p.Atoms[h.O].Pos, $"{prefix}hbond:{k}", look.Dark));
            }
        if (o.Ribbon != BiomolRibbon.Off)
        {
            var trace = p.CaTrace;
            var shown = hidden.Count == 0 ? trace : trace.Take(p.Count - hidden.Count).ToList();
            var segsShown = BiomolCartoon.Segments(p.Built.Select(s => s.Cartoon()).Take(shown.Count).ToList());
            var meshes = BiomolCartoon.Meshes(shown, segsShown);
            for (var k = 0; k < segsShown.Count; k++)
                items.Add(new BioSceneItem($"{prefix}ss:{k}:cartoon", meshes[k],
                    new BioMaterial(BiomolCartoon.Hex(segsShown[k].Kind, look.Dark), Alpha: o.Ribbon == BiomolRibbon.Over ? 0.72 : 1, Roughness: 0.45), o.Ribbon == BiomolRibbon.Over ? 1 : 0));
        }
        var labels = new List<BioLabel3D>();
        if (look.Labels)
        {
            var visible = Enumerable.Range(0, p.Count).Where(i => !hidden.Contains(i)).ToList();
            if (visible.Count > 0)
            {
                var first = visible[0];
                labels.Add(new BioLabel3D($"{prefix}ss:{of[first]}:res:{first}", $"{labelPrefix}N-terminus ({p.ResidueName(first)})", p.Atoms[p.Backbones[first].N].Pos, Area: 3));
                var lastShown = visible[^1];
                if (lastShown != first)
                {
                    var c = p.AtomIndex(lastShown, "OXT") ?? p.Backbones[lastShown].C;
                    labels.Add(new BioLabel3D($"{prefix}ss:{of[lastShown]}:res:{lastShown}", $"{labelPrefix}C-terminus ({p.ResidueName(lastShown)})", p.Atoms[c].Pos, Area: 3));
                }
            }
            if (o.LabelSegments)
                for (var k = 0; k < segs.Count; k++)
                {
                    var s = segs[k];
                    if (s.Kind == 'C' || s.Count < 3 || hidden.Contains(s.First)) continue;
                    var mid = (s.First + s.Last) / 2;
                    labels.Add(new BioLabel3D($"{prefix}ss:{k}", $"{labelPrefix}{BiomolCartoon.Name(s.Kind)} ({s.First + 1}–{s.Last + 1})", p.Atoms[p.Backbones[mid].CA].Pos, Area: s.Count));
                }
            if (o.HBonds && p.HBonds.FirstOrDefault(h => !hidden.Contains(h.Donor)) is { } hb)
                labels.Add(new BioLabel3D($"{prefix}hbond", "Backbone H-bond (N–H···O=C)", (p.Atoms[hb.H].Pos + p.Atoms[hb.O].Pos) / 2, Area: 2));
            if (look.ColourBy == BiomolColourBy.Part && o.Ribbon != BiomolRibbon.Only && p.Bonds.FirstOrDefault(b => b.Peptide && !HiddenAtom(b.B)) is { } pb)
                labels.Add(new BioLabel3D($"{prefix}pbond", "Peptide bond (C–N)", (p.Atoms[pb.A].Pos + p.Atoms[pb.B].Pos) / 2, Area: 2));
            foreach (var pro in p.ProlineBreaks)
                if (!hidden.Contains(pro))
                    labels.Add(new BioLabel3D($"{prefix}ss:{of[pro]}:res:{pro}", $"Pro {pro + 1} breaks the helix", p.Atoms[p.Backbones[pro].CA].Pos, Area: 2));
        }
        return new BioSceneContent { Items = items, Labels = labels };
    }

    /// <summary>A rotation (rows) taking <paramref name="along"/> to +y and <paramref name="across"/> (made perpendicular) to
    /// +x, right-handed, so a helix or a sheet stands up facing the camera without being mirrored.</summary>
    public static double[][] UprightRows(BioVec along, BioVec across)
    {
        var y = BioV.Unit(along);
        var x = across - BioV.Dot(across, y) * y;
        if (BioV.Length(x) < 1e-6) x = BioV.Cross(y, Math.Abs(y.Z) < 0.9 ? new BioVec(0, 0, 1) : new BioVec(1, 0, 0));
        x = BioV.Unit(x);
        var z = BioV.Cross(x, y);
        return new[] { new[] { x.X, x.Y, x.Z }, new[] { y.X, y.Y, y.Z }, new[] { z.X, z.Y, z.Z } };
    }

    /// <summary>A peptide stood upright (its long axis +y) and centred at <paramref name="at"/>.</summary>
    public static BiomolPeptide Upright(BiomolPeptide p, BioVec along, BioVec across, BioVec centre, BioVec at) =>
        p.Moved(-centre).Transformed(UprightRows(along, across), at);

    /// <summary>The secondary level (§4.4 item 6): an α-helix (Ala₁₂) and the β-sheet demo (Ala₇ × 3) side by side, upright,
    /// with their backbone H-bonds.</summary>
    public static BioSceneContent Secondary(BiomolLook look, BiomolChem chem)
    {
        var (helix, sheet) = SecondaryPeptides(look.Ph7);
        var c = Peptide(helix, look, new PeptideOptions(HBonds: true, LabelSegments: false), chem, "helix:", "α-helix ");
        c.Labels.RemoveAll(l => l.Text.Contains("terminus"));
        if (look.Labels) c.Labels.Add(new BioLabel3D("helix:ss", "α-helix (3.6 residues a turn)", helix.CaTrace[6], Area: 12));
        var sheetContent = SheetScene(sheet, look, chem);
        sheetContent.Labels.RemoveAll(l => l.Text.Contains("terminus"));
        c.Items.AddRange(sheetContent.Items);
        c.Labels.AddRange(sheetContent.Labels);
        return c;
    }

    /// <summary>The secondary level's two models: Ala₁₂ as an α-helix stood upright at x = −10 Å, and the β-sheet demo
    /// stood upright at x = +9 Å.</summary>
    public static (BiomolPeptide Helix, BiomolSheet Sheet) SecondaryPeptides(bool ph7)
    {
        var helix0 = BiomolPeptideBuilder.Build(new string('A', 12), Enumerable.Repeat(BiomolSS.Helix, 12).ToArray(), ph7);
        var ca = helix0.CaTrace;
        var axis = BiomolCartoon.Axes(ca).Aggregate(BioVec.Zero, (s, v) => s + (BioV.Dot(v, ca[^1] - ca[0]) < 0 ? -v : v));
        var hc = BiomolMath.Centroid(ca);
        return (Upright(helix0, axis, ca[0] - hc, hc, new BioVec(-10, 0, 0)), SheetUpright(new BiomolLook() with { Ph7 = ph7 }, new BioVec(9, 0, 0)));
    }

    /// <summary>The β-sheet demo stood upright: strands along +y, side by side across x, the sheet facing the camera.</summary>
    public static BiomolSheet SheetUpright(BiomolLook look, BioVec at = default)
    {
        var s = BiomolSheet.Build("AAAAAAA", look.Ph7);
        var s1 = s.Strands[0].CaTrace;
        var s3 = s.Strands[2].CaTrace;
        BioVec c1 = BiomolMath.Centroid(s1), c3 = BiomolMath.Centroid(s3);
        var centre = BiomolMath.Centroid(s.Strands.SelectMany(x => x.CaTrace).ToList());
        var r = UprightRows(s1[6] - s1[0], c3 - c1);
        return new BiomolSheet { Strands = s.Strands.Select(x => x.Moved(-centre).Transformed(r, at)).ToList(), HBonds = s.HBonds };
    }

    /// <summary>The β-sheet demo's scene: its three strands ("sheet:&lt;s&gt;:…") and the H-bonds between them ("sheet:hbond:&lt;k&gt;").</summary>
    public static BioSceneContent SheetScene(BiomolSheet s, BiomolLook look, BiomolChem chem)
    {
        var c = new BioSceneContent();
        for (var k = 0; k < s.Strands.Count; k++)
        {
            var part = Peptide(s.Strands[k], look, new PeptideOptions(LabelSegments: false), chem, $"sheet:{k}:", $"Strand {k + 1} ");
            c.Items.AddRange(part.Items);
            if (k == 1) c.Labels.AddRange(part.Labels.Where(l => l.Text.Contains("N-terminus") || l.Text.Contains("C-terminus")));
        }
        for (var k = 0; k < s.HBonds.Count; k++)
        {
            var h = s.HBonds[k];
            BiomolPeptide d = s.Strands[h.Donor], a = s.Strands[h.Acceptor];
            var from = look.Hydrogens ? d.Atoms[h.Bond.H].Pos : d.Atoms[h.Bond.N].Pos;
            c.Items.Add(HBondItem(from, a.Atoms[h.Bond.O].Pos, $"sheet:hbond:{k}", look.Dark));
        }
        if (look.Labels)
        {
            if (s.HBonds.Count > 0)
            {
                var h = s.HBonds[0];
                BiomolPeptide d = s.Strands[h.Donor], a = s.Strands[h.Acceptor];
                c.Labels.Add(new BioLabel3D("sheet:hbond", "H-bond between strands (backbone)", (d.Atoms[h.Bond.H].Pos + a.Atoms[h.Bond.O].Pos) / 2, Area: 2));
            }
            c.Labels.Add(new BioLabel3D("sheet:0", "β-pleated sheet (antiparallel)", s.Strands[0].CaTrace[3], Area: 14));
        }
        return c;
    }

    /// <summary>Where the primary level's beads sit: a string of beads 3.8 Å apart (a CA–CA step), ten to a row, each row
    /// running back the way the one above came (a necklace laid in rows), rows 7 Å apart.</summary>
    public static List<BioVec> BeadPlaces(int count, int perRow = 10)
    {
        var out_ = new List<BioVec>();
        for (var i = 0; i < count; i++)
        {
            int row = i / perRow, col = i % perRow;
            var x = (row % 2 == 0 ? col : perRow - 1 - col) * 3.8;
            out_.Add(new BioVec(x, -row * 7.0, 0));
        }
        return out_;
    }

    /// <summary>The primary level (§4.4 item 6): the sequence as beads ("bead:&lt;i&gt;", in its class's colour, N-terminus
    /// first) joined by rods for the peptide bonds ("pbond:&lt;i&gt;"), the ends labelled, and each bead's one-letter code as
    /// a mark.</summary>
    public static BiomolScene Primary(string sequence, BiomolLook look)
    {
        var c = new BioSceneContent();
        var marks = new List<BiomolMark>();
        var n = sequence.Length;
        if (n == 0) return new BiomolScene { Content = c };
        var at = BeadPlaces(n);
        var ball = BioShapes.Icosphere(1.45, 2);
        for (var i = 0; i < n; i++)
        {
            var cls = BioAminoAcids.ByOne.GetValueOrDefault(sequence[i])?.Group ?? BioAAClass.Nonpolar;
            c.Items.Add(new BioSceneItem($"bead:{i}", ball.Translated(at[i]), new BioMaterial(BioPalette.AminoAcid(cls).HexFor(look.Dark), Roughness: 0.4)));
            marks.Add(new BiomolMark(at[i], sequence[i].ToString(), null, 1.3, 1.5));
            if (i > 0)
                c.Items.Add(new BioSceneItem($"pbond:{i}", BioShapes.Cylinder(at[i - 1], at[i], 0.3, 12), new BioMaterial(BiomolParts.BondHighlight.HexFor(look.Dark), Roughness: 0.4)));
        }
        if (look.Labels)
        {
            string Name(int i) => $"{BioAminoAcids.ByOne.GetValueOrDefault(sequence[i])?.Three ?? sequence[i].ToString()} {i + 1}";
            c.Labels.Add(new BioLabel3D("bead:0", $"N-terminus (H₃N⁺–, {Name(0)})", at[0], Area: 3));
            if (n > 1)
            {
                c.Labels.Add(new BioLabel3D($"bead:{n - 1}", $"C-terminus (–COO⁻, {Name(n - 1)})", at[^1], Area: 3));
                c.Labels.Add(new BioLabel3D("pbond", "Peptide bonds (covalent)", (at[0] + at[1]) / 2, Area: 2));
            }
        }
        return new BiomolScene { Content = c, Marks = marks };
    }

    /// <summary>A peptide's charges at pH 7 as marks (§4.4 Net charge): the N-terminus +, the C-terminus −, each Lys and Arg
    /// +, each Asp and Glu − (His counted as 0); none in the neutral form.</summary>
    public static List<BiomolMark> PeptideChargeMarks(BiomolPeptide p, BiomolLook look, BiomolChem chem, HashSet<int>? hidden = null)
    {
        if (!look.Ph7 || p.Count == 0) return new List<BiomolMark>();
        var charges = new List<(int Atom, int Q)>();
        if (!(hidden?.Contains(0) ?? false)) charges.Add((p.Backbones[0].N, 1));
        var last = p.Count - 1;
        if (!(hidden?.Contains(last) ?? false) && p.AtomIndex(last, "OXT") is { } oxt) charges.Add((oxt, -1));
        for (var i = 0; i < p.Count; i++)
        {
            if (hidden?.Contains(i) ?? false) continue;
            var (name, q) = p.Sequence[i] switch { 'K' => ("NZ", 1), 'R' => ("NH2", 1), 'D' => ("OD2", -1), 'E' => ("OE2", -1), _ => ("", 0) };
            if (q != 0 && p.AtomIndex(i, name) is { } k) charges.Add((k, q));
        }
        var centre = BiomolMath.Centroid(p.Atoms.Select(a => a.Pos).ToList());
        return charges.Select(x =>
        {
            var a = p.Atoms[x.Atom];
            var dir = BioV.Unit(a.Pos - centre);
            var r = Radius(a.Element, look, chem);
            return new BiomolMark(a.Pos + (BioV.Length(dir) > 0 ? dir : new BioVec(0, 1, 0)) * (r + 0.5), x.Q > 0 ? "+" : "−",
                (x.Q > 0 ? BioPalette.Basic : BioPalette.Acidic).HexFor(look.Dark), 0.75);
        }).ToList();
    }

    // ---- nucleotides and a strand (§4.4 Nucleic acids) ----

    /// <summary>A nucleotide: atoms "part:&lt;phosphate|sugar|base&gt;:atom:&lt;id&gt;", coloured by element, part (phosphate
    /// violet, sugar teal, base in its letter's colour) or class (the base in its colour, the rest grey); its parts labelled.</summary>
    public static BioSceneContent Nucleotide(string code, BiomolLook look, BiomolChem chem)
    {
        if (BioData.Molecule(code) is not { } mol) return new BioSceneContent();
        var m = mol.Model(look.Ph7);
        var letter = BiomolNucleotide.BaseLetter(code);
        var parts = m.Atoms.Select(a => BiomolNucleotide.Part(a.Name)).ToList();
        var baseHex = BioPalette.Base(letter).HexFor(look.Dark);
        var items = AtomItems(m, look, chem, a => look.ColourBy switch
        {
            BiomolColourBy.Element => null,
            BiomolColourBy.Part => parts[a.Id] == BiomolParts.Base ? baseHex : parts[a.Id].Colour.HexFor(look.Dark),
            _ => parts[a.Id] == BiomolParts.Base ? baseHex : BiomolParts.Grey.HexFor(look.Dark),
        }, a => $"part:{parts[a.Id].Id}:atom:{a.Id}");
        var labels = new List<BioLabel3D>();
        if (look.Labels)
        {
            var deoxy = BiomolNucleotide.IsDeoxy(code);
            var texts = new (BiomolPart, string)[]
            {
                (BiomolParts.Phosphate, code == "ATP" ? "Three phosphates (α, β, γ)" : "Phosphate (on the 5′ carbon)"),
                (BiomolParts.Sugar, deoxy ? "Deoxyribose (H at 2′)" : "Ribose (OH at 2′)"),
                (BiomolParts.Base, $"{BiomolNucleotide.BaseName(letter)} ({(BiomolNucleotide.IsPurine(letter) ? "purine: 2 rings" : "pyrimidine: 1 ring")})"),
            };
            foreach (var (p, text) in texts)
            {
                var pts = m.Atoms.Where(a => parts[a.Id] == p && a.Element != "H").Select(a => a.Pos).ToList();
                if (Anchor(pts) is { } at) labels.Add(new BioLabel3D($"part:{p.Id}", text, at, Area: pts.Count));
            }
            if (m.Atoms.FirstOrDefault(a => a.Name == "O3'") is { } o3)
                labels.Add(new BioLabel3D($"part:sugar:atom:{o3.Id}", "3′-OH (the next nucleotide joins here)", o3.Pos, Area: 1));
        }
        return new BioSceneContent { Items = items, Labels = labels };
    }

    /// <summary>The sugar carbons' numbers 1′–5′ on a nucleotide (<paramref name="primes"/>), or a sugar's 1–6, as marks on
    /// the atoms' near sides.</summary>
    public static List<BiomolMark> CarbonMarks(BioMoleculeModel m, bool primes, BiomolLook look, BiomolChem chem)
    {
        var out_ = new List<BiomolMark>();
        if (look.Style == BioAtomStyle.Sticks) return out_;
        var lift = Radius("C", look, chem) * 1.03;
        foreach (var a in m.Atoms)
        {
            if (a.Element != "C") continue;
            if (primes)
            {
                if (!a.Name.EndsWith('\'') || a.Name.Length < 2 || !char.IsDigit(a.Name[1])) continue;
                out_.Add(new BiomolMark(a.Pos, $"{a.Name[1]}′", Lift: lift));
                continue;
            }
            var digits = new string(a.Name.Where(char.IsDigit).ToArray());
            if (digits.Length > 0) out_.Add(new BiomolMark(a.Pos, digits, Lift: lift));
        }
        return out_;
    }

    /// <summary>What a strand scene shows besides its atoms: strand 1's pair left out (a nucleotide sliding in or out), with
    /// its partner.</summary>
    public sealed record StrandOptions(int? HiddenPair = null);

    /// <summary>The strand (and its complement): atoms "nt:&lt;strand&gt;:&lt;pair&gt;:atom:&lt;n&gt;", coloured by element, part
    /// (phosphate, sugar, base) or class (each strand's backbone in its colour, bases by letter); the phosphodiester bonds
    /// glowing ("pdiester:&lt;strand&gt;:&lt;k&gt;"); the base pairs' H-bonds ("bp:&lt;k&gt;"); labels for the 5′ phosphate, the
    /// 3′-OH and the phosphodiester bond.</summary>
    public static BioSceneContent Strand(BioDuplex d, BiomolLook look, StrandOptions? options, BiomolChem chem)
    {
        var o = options ?? new StrandOptions();
        var m = d.Model;
        var n = d.Sequence.Length;
        if (n == 0) return new BioSceneContent();
        var parts = d.Atoms.Select(a => BiomolNucleotide.Part(a.Name)).ToList();
        bool Hidden(int k) => o.HiddenPair is { } hp && d.Atoms[k].Pair == hp;
        var items = AtomItems(m, look, chem, StrandColour(d, look), a => $"nt:{d.Atoms[a.Id].Strand}:{d.Atoms[a.Id].Pair}:atom:{a.Id}", a => Hidden(a.Id));
        foreach (var s in d.HasStrand2 ? new[] { 1, 2 } : new[] { 1 })
        {
            var links = BiomolStrand.Linkages(d, s);
            for (var k = 0; k < links.Count; k++)
                if (!links[k].Any(Hidden))
                    items.Add(Sleeve(links[k].Select(i => d.Atoms[i].Pos).ToList(), $"pdiester:{s}:{k}", BiomolParts.BondHighlight.HexFor(look.Dark), 0.3));
        }
        for (var k = 0; k < d.HBonds.Count; k++)
            if (!Hidden(d.HBonds[k].A))
                items.Add(HBondItem(d.Atoms[d.HBonds[k].A].Pos, d.Atoms[d.HBonds[k].B].Pos, $"bp:{k}", look.Dark));
        var labels = new List<BioLabel3D>();
        if (look.Labels)
        {
            if (d.Index(1, 0, "P") is { } p) labels.Add(new BioLabel3D("nt:1:0", "5′ end (phosphate)", d.Atoms[p].Pos, Area: 2));
            var lastPair = o.HiddenPair is { } h ? h - 1 : n - 1;
            if (lastPair >= 0 && d.Index(1, lastPair, "O3'") is { } o3) labels.Add(new BioLabel3D($"nt:1:{lastPair}", "3′ end (–OH)", d.Atoms[o3].Pos, Area: 2));
            if (BiomolStrand.Linkages(d, 1).FirstOrDefault() is { } link && !link.Any(Hidden))
                labels.Add(new BioLabel3D("pdiester", "Phosphodiester bond", d.Atoms[link[2]].Pos, Area: 3));
            if (d.HasStrand2)
            {
                var mid = n / 2;
                if ((d.Index(2, mid, "P") ?? d.Index(2, mid, "C4'")) is { } p2)
                    labels.Add(new BioLabel3D("nt:2", "Complementary strand (runs 3′→5′ alongside)", d.Atoms[p2].Pos, Area: 6));
                if (d.HBonds.Count > 0)
                    labels.Add(new BioLabel3D("bp", "Base pairs: A·T 2 H-bonds, G·C 3", (d.Atoms[d.HBonds[0].A].Pos + d.Atoms[d.HBonds[0].B].Pos) / 2, Area: 3));
            }
            // (each base named by its letter, on strand 1)
            if (n <= 6)
                for (var k = 0; k < n; k++)
                {
                    if (k == o.HiddenPair) continue;
                    var names = Enumerable.Range(0, d.Atoms.Count).Where(i => d.Atoms[i].Strand == 1 && d.Atoms[i].Pair == k && parts[i] == BiomolParts.Base).ToList();
                    if (names.Count > 0 && Anchor(names.Select(i => d.Atoms[i].Pos).ToList()) is { } a)
                        labels.Add(new BioLabel3D($"nt:1:{k}", $"{BiomolNucleotide.BaseName(d.Atoms[names[0]].Base)} {k + 1}", a, Area: 1));
                }
        }
        return new BioSceneContent { Items = items, Labels = labels };
    }

    // ---- sugars (§4.4 Carbohydrates) ----

    /// <summary>A monosaccharide's parts, for "Colour by Part": its ring, the anomeric –OH, the CH₂OH it is judged against,
    /// and its other –OH groups.</summary>
    public static readonly BiomolPart RingPart = new("ring", "Ring", new("#5b6b8c", "#a3b1cf"));
    public static readonly BiomolPart AnomericPart = new("anomeric", "Anomeric –OH", new("#e8508a", "#ff7aa8"));
    public static readonly BiomolPart ReferencePart = new("ch2oh", "CH₂OH", new("#3aa3b8", "#6fd3e6"));
    public static readonly BiomolPart HydroxylPart = new("oh", "–OH groups", new("#d63a34", "#ff6961"));

    public static BiomolPart SugarPart(string name, BioMolecule mol, BiomolSugar.Ring? ring)
    {
        if (ring is not { } r) return RingPart;
        var heavy = BiomolGraph.HeavyNeighbour(mol).GetValueOrDefault(name) ?? name;
        if (heavy == r.AnomericO) return AnomericPart;
        if (heavy == r.Reference) return ReferencePart;
        if (r.Atoms.Contains(heavy)) return RingPart;
        // (the reference carbon's own O goes with it; any other carbon off the ring — fructose's C1 — is drawn with the
        // ring, its O with the –OH groups)
        if (mol.Bonds.Any(b => (b.A == r.Reference && b.B == heavy) || (b.B == r.Reference && b.A == heavy))) return ReferencePart;
        if (mol.Atom(heavy)?.Element == "C") return RingPart;
        return HydroxylPart;
    }

    /// <summary>A monosaccharide: atoms "part:&lt;ring|anomeric|ch2oh|oh&gt;:atom:&lt;id&gt;", its ring carbons numbered (marks),
    /// the anomeric –OH labelled with which face of the ring it is on.</summary>
    public static BiomolScene Monosaccharide(string code, BiomolLook look, BiomolChem chem)
    {
        if (BioData.Molecule(code) is not { } mol) return new BiomolScene();
        var m = mol.Model(look.Ph7);
        var ring = BiomolSugar.RingOf(mol);
        var parts = m.Atoms.Select(a => SugarPart(a.Name, mol, ring)).ToList();
        var items = AtomItems(m, look, chem, a => look.ColourBy == BiomolColourBy.Element ? null : parts[a.Id].Colour.HexFor(look.Dark), a => $"part:{parts[a.Id].Id}:atom:{a.Id}");
        var labels = new List<BioLabel3D>();
        if (look.Labels && ring is { } r)
        {
            var alpha = BiomolSugar.IsAlpha(mol, r);
            var cNum = new string(r.Anomeric.Where(char.IsDigit).ToArray());
            var refNum = new string(r.Reference.Where(char.IsDigit).ToArray());
            if (m.Atoms.FirstOrDefault(a => a.Name == r.AnomericO) is { } o)
                labels.Add(new BioLabel3D("part:anomeric", $"C{cNum} –OH: {(alpha ? "opposite face to" : "same face as")} C{refNum} ({(alpha ? "α" : "β")})", o.Pos, Area: 3));
            if (m.Atoms.FirstOrDefault(a => a.Name == r.Reference) is { } c)
                labels.Add(new BioLabel3D("part:ch2oh", $"C{refNum} (CH₂OH)", c.Pos, Area: 2));
            if (m.Atoms.FirstOrDefault(a => a.Name == r.RingO) is { } ro)
                labels.Add(new BioLabel3D("part:ring", $"Ring O ({r.Atoms.Count}-membered ring)", ro.Pos, Area: 4));
        }
        return new BiomolScene { Content = new BioSceneContent { Items = items, Labels = labels }, Marks = CarbonMarks(m, false, look, chem) };
    }

    /// <summary>α- and β-glucose side by side ("alpha:…", "beta:…"), each centred 4.6 Å either side of the middle, so the one
    /// difference — which face of the ring C1's –OH is on — can be seen; their carbons numbered.</summary>
    public static BiomolScene AnomerPair(BiomolLook look, BiomolChem chem)
    {
        var c = new BioSceneContent();
        var marks = new List<BiomolMark>();
        foreach (var (code, prefix) in new[] { ("GLC", "alpha"), ("BGC", "beta") })
        {
            if (BioData.Molecule(code) is null) continue;
            var shift = AnomerShift(code);
            var one = Monosaccharide(code, look, chem);
            c.Items.AddRange(one.Content.Items.Select(it => it with { Mesh = it.Mesh.Translated(shift), Id = it.Id is { } id ? $"{prefix}:{id}" : null }));
            c.Labels.AddRange(one.Content.Labels.Where(l => l.Id == "part:anomeric").Select(l =>
                new BioLabel3D($"{prefix}:{l.Id}", code == "GLC" ? "α-glucose: C1 –OH opposite C6" : "β-glucose: C1 –OH beside C6", l.Anchor + shift, Area: l.Area)));
            marks.AddRange(one.Marks.Select(m => m with { Pos = m.Pos + shift }));
        }
        return new BiomolScene { Content = c, Marks = marks };
    }

    /// <summary>The offset <see cref="AnomerPair"/> gives a sugar (its atoms' centre to 4.6 Å either side of the middle).</summary>
    public static BioVec AnomerShift(string code)
    {
        if (BioData.Molecule(code) is not { } mol) return BioVec.Zero;
        return new BioVec(code == "GLC" ? -4.6 : 4.6, 0, 0) - BiomolMath.Centroid(mol.Atoms.Select(a => a.Pos).ToList());
    }

    /// <summary>A disaccharide, joined or split by water: atoms "res:&lt;0|1&gt;:atom:&lt;id&gt;" coloured by element or by
    /// residue; the glycosidic bond glowing ("glyco:&lt;k&gt;") and labelled with its linkage; each sugar named. Split, the
    /// water's atoms are labelled where they went.</summary>
    public static BiomolScene Disaccharide(string code, bool split, BiomolLook look, BiomolChem chem)
    {
        if (BioData.Molecule(code) is not { } mol || BiomolSugar.GlycosidicOf(mol) is not { } g) return new BiomolScene();
        (string First, string Second) names = BiomolSugar.Residues.TryGetValue(code, out var nm) ? nm : ("Sugar 1", "Sugar 2");
        BioMoleculeModel m;
        List<int> res;
        var added = new List<int>();
        if (split && BiomolSugar.Split(mol) is { } s)
        {
            m = s.Model;
            res = s.Residue;
            added = s.NewAtoms;
        }
        else
        {
            m = mol.Model(look.Ph7);
            res = m.Atoms.Select(a => BiomolSugar.ResidueOf(a.Name, mol, g.O)).ToList();
        }
        var items = AtomItems(m, look, chem, a =>
        {
            if (look.ColourBy == BiomolColourBy.Element) return null;
            if (added.Contains(a.Id)) return BiomolParts.BondHighlight.HexFor(look.Dark);
            return BiomolSugar.ResidueColours[res[a.Id]].HexFor(look.Dark);
        }, a => $"res:{res[a.Id]}:atom:{a.Id}");
        if (!split && m.Atoms.FirstOrDefault(a => a.Name == g.O) is { } go && m.Atoms.FirstOrDefault(a => a.Name == g.Anomeric) is { } c1
            && m.Atoms.FirstOrDefault(a => a.Name == g.Other) is { } c2)
            items.Add(Sleeve(new[] { c1.Pos, go.Pos, c2.Pos }, "glyco:0", BiomolParts.BondHighlight.HexFor(look.Dark), 0.3));
        var labels = new List<BioLabel3D>();
        if (look.Labels)
        {
            foreach (var (r, name) in new[] { (0, names.First), (1, names.Second) })
            {
                var ring = BiomolSugar.RingOf(mol, primed: r == 1);
                var pts = m.Atoms.Where(a => res[a.Id] == r && (ring?.Atoms.Contains(a.Name) ?? a.Element != "H")).Select(a => a.Pos).ToList();
                if (Anchor(pts) is { } at) labels.Add(new BioLabel3D($"res:{r}", name, at, Area: 6));
            }
            if (split)
            {
                foreach (var k in added)
                    if (m.Atoms[k].Element == "O") labels.Add(new BioLabel3D($"res:{res[k]}:atom:{k}", "New –OH (from water)", m.Atoms[k].Pos, Area: 1));
                if (added.Count > 0) labels.Add(new BioLabel3D($"res:1:atom:{added[0]}", "H (from water)", m.Atoms[added[0]].Pos, Area: 1));
            }
            else if (m.Atoms.FirstOrDefault(a => a.Name == g.O) is { } o)
                labels.Add(new BioLabel3D("glyco", $"{BiomolSugar.Linkage.GetValueOrDefault(code)} glycosidic bond", o.Pos, Area: 3));
        }
        return new BiomolScene
        {
            Content = new BioSceneContent { Items = items, Labels = labels, Frame = DisaccharideFrame(code) },
            Marks = CarbonMarks(m, false, look, chem),
        };
    }

    // ---- lipids (§4.4 Lipids) ----

    /// <summary>A lipid (or stearic and oleic acids side by side, "STE+OLA"): atoms "part:&lt;slug&gt;:atom:&lt;id&gt;"
    /// ("mol:&lt;k&gt;:…" when two are shown), coloured by element or by part (hydrophilic head blue, hydrophobic tails tan);
    /// its ester bonds and cis double bond glowing; its parts labelled.</summary>
    public static BioSceneContent Lipid(string code, BiomolLook look, BiomolChem chem)
    {
        var codes = code == "STE+OLA" ? new[] { "STE", "OLA" } : new[] { code };
        var items = new List<BioSceneItem>();
        var labels = new List<BioLabel3D>();
        for (var k = 0; k < codes.Length; k++)
        {
            var cc = codes[k];
            if (BioData.Molecule(cc) is not { } mol) continue;
            var shift = codes.Length == 2 ? new BioVec(k == 0 ? -4.5 : 4.5, 0, 0) : BioVec.Zero;
            var m = mol.Model(look.Ph7).Moved(shift);
            var detail = m.Atoms.Select(a => BiomolLipid.Detail(a.Name, mol)).ToList();
            var prefix = codes.Length == 2 ? $"mol:{k}:" : "";
            static string Slug(string s) => s.ToLowerInvariant().Replace(' ', '-');
            items.AddRange(AtomItems(m, look, chem, a =>
            {
                if (look.ColourBy == BiomolColourBy.Element) return null;
                return (BiomolLipid.IsHydrophilic(detail[a.Id]) ? BiomolParts.Hydrophilic : BiomolParts.Hydrophobic).Colour.HexFor(look.Dark);
            }, a => $"{prefix}part:{Slug(detail[a.Id])}:atom:{a.Id}"));
            BioVec? Pos(string n) => m.Atoms.FirstOrDefault(a => a.Name == n)?.Pos;
            var esters = BiomolLipid.EsterBonds(cc);
            for (var j = 0; j < esters.Length; j++)
                if (Pos(esters[j].Item1) is { } x && Pos(esters[j].Item2) is { } y) items.Add(Sleeve(new[] { x, y }, $"{prefix}ester:{j}", BiomolParts.BondHighlight.HexFor(look.Dark)));
            var cis = BiomolLipid.CisBonds(cc);
            for (var j = 0; j < cis.Length; j++)
                if (Pos(cis[j].Item1) is { } x && Pos(cis[j].Item2) is { } y) items.Add(Sleeve(new[] { x, y }, $"{prefix}cis:{j}", BioPalette.Strand.HexFor(look.Dark), 0.3));
            if (!look.Labels) continue;
            if (codes.Length == 2)
            {
                var heavy = m.Atoms.Where(a => a.Element != "H").Select(a => a.Pos).ToList();
                if (heavy.Count > 0)
                {
                    var top = heavy.OrderByDescending(p => p.Y).First();
                    labels.Add(new BioLabel3D($"{prefix}part", cc == "STE" ? "Stearic acid: saturated, straight" : "Oleic acid: cis double bond, kinked", top, Area: 10));
                }
                if (cc == "OLA" && Pos("C9") is { } c9) labels.Add(new BioLabel3D($"{prefix}cis", "cis C9=C10 (the kink)", c9, Area: 3));
                continue;
            }
            var seen = new HashSet<string>();
            foreach (var d in detail)
            {
                if (d.Length == 0 || !seen.Add(d)) continue;
                var pts = m.Atoms.Where(a => detail[a.Id] == d && a.Element != "H").Select(a => a.Pos).ToList();
                if (Anchor(pts) is not { } at) continue;
                var text = d;
                if (d == "Ester bond") text = cc == "TGL" ? "Ester bonds (3)" : "Ester bonds (2)";
                if (d == "Stearate tail") text = "Stearate tails (3, saturated)";
                if (d == "Oleate tail") text = "Oleate tail (cis kink)";
                if (d == "Hydrocarbon tail" && (cc is "PLM" or "STE")) text = "Hydrocarbon tail (saturated)";
                if (d == "Hydrocarbon tail" && cc == "OLA") text = "Hydrocarbon tail (unsaturated)";
                labels.Add(new BioLabel3D($"{prefix}part:{Slug(d)}", text, at, Area: pts.Count));
            }
            if (cis.Length > 0 && Pos(cis[0].Item1) is { } cx) labels.Add(new BioLabel3D($"{prefix}cis", "cis double bond (kink)", cx, Area: 2));
        }
        return new BioSceneContent { Items = items, Labels = labels };
    }

    // ---- motions (§4.4 Animate dehydration, Dehydration/hydrolysis) ----

    /// <summary>A water molecule (O + two H at 0.96 Å and 104.5°, its H atoms below the O), as items.</summary>
    public static List<BioSceneItem> Water(BioVec o, BiomolLook look, BiomolChem chem)
    {
        var m = new BioMoleculeModel();
        var half = 104.5 / 2 * Math.PI / 180;
        var oi = m.Add("O", o, "O");
        var h1 = m.Add("H", o + new BioVec(Math.Sin(half), -Math.Cos(half), 0) * 0.96, "H");
        var h2 = m.Add("H", o + new BioVec(-Math.Sin(half), -Math.Cos(half), 0) * 0.96, "H");
        m.AddBond(oi, h1);
        m.AddBond(oi, h2);
        return AtomItems(m, look with { Hydrogens = true }, chem, _ => null, a => $"water:{a.Id}");
    }

    /// <summary>Some atoms of a model and the bonds among them, as items coloured as the page colours them (what moves).</summary>
    public static List<BioSceneItem> ItemsFor(BioMoleculeModel m, Func<BioMoleculeModel.Atom, bool> keep, BiomolLook look, BiomolChem chem, Func<BioMoleculeModel.Atom, string?> colour)
    {
        var sub = new BioMoleculeModel();
        var map = new Dictionary<int, int>();
        var back = new List<int>();
        foreach (var a in m.Atoms)
        {
            if (!keep(a)) continue;
            map[a.Id] = sub.Add(a.Element, a.Pos, a.Name, a.Tag);
            back.Add(a.Id);
        }
        foreach (var b in m.Bonds)
            if (map.TryGetValue(b.A, out var x) && map.TryGetValue(b.B, out var y)) sub.AddBond(x, y, b.Order);
        return AtomItems(sub, look, chem, a => colour(m.Atoms[back[a.Id]]), a => $"motion:{back[a.Id]}");
    }

    /// <summary>A single bond drawn as the look draws bonds (a grey rod; sticks: each half in its atom's colour; none in
    /// space filling), for a bond that moves with one of its atoms.</summary>
    public static List<BioSceneItem> BondItems(BioVec a, BioVec b, string hexA, string hexB, BiomolLook look)
    {
        switch (look.Style)
        {
            case BioAtomStyle.SpaceFill: return new List<BioSceneItem>();
            case BioAtomStyle.Sticks:
                var mid = (a + b) / 2;
                return new List<BioSceneItem>
                {
                    new(null, BioShapes.Cylinder(a, mid, 0.13, 16, caps: false), new BioMaterial(hexA)),
                    new(null, BioShapes.Cylinder(mid, b, 0.13, 16, caps: false), new BioMaterial(hexB)),
                };
            default:
                return new List<BioSceneItem> { new(null, BioShapes.Cylinder(a, b, 0.09, 16, caps: false), new BioMaterial("#9e9e9e")) };
        }
    }

    /// <summary>A peptide's atom colours in a look (by the model's atom id, <paramref name="index"/> mapping it to the
    /// peptide's atom).</summary>
    public static Func<BioMoleculeModel.Atom, string?> PeptideColour(BiomolPeptide p, IReadOnlyList<int> index, BiomolLook look) => a =>
    {
        var atom = p.Atoms[index[a.Id]];
        var cls = BioAminoAcids.ByOne.GetValueOrDefault(p.Sequence[atom.Residue])?.Group ?? BioAAClass.Nonpolar;
        return look.ColourBy switch
        {
            BiomolColourBy.Element => null,
            BiomolColourBy.Part => BackboneNames.Contains(atom.Name) ? BiomolParts.Backbone.Colour.HexFor(look.Dark) : BioPalette.AminoAcid(cls).HexFor(look.Dark),
            _ => BioPalette.AminoAcid(cls).HexFor(look.Dark),
        };
    };

    /// <summary>A strand's atom colours in a look (by atom index in the duplex).</summary>
    public static Func<BioMoleculeModel.Atom, string?> StrandColour(BioDuplex d, BiomolLook look) => a =>
    {
        var atom = d.Atoms[a.Id];
        var part = BiomolNucleotide.Part(atom.Name);
        switch (look.ColourBy)
        {
            case BiomolColourBy.Element: return null;
            case BiomolColourBy.Part: return part == BiomolParts.Base ? BioPalette.Base(atom.Base).HexFor(look.Dark) : part.Colour.HexFor(look.Dark);
            default:
                if (part == BiomolParts.Base) return BioPalette.Base(atom.Base).HexFor(look.Dark);
                return (atom.Strand == 1 ? BioPalette.Strand1 : BioPalette.Strand2).HexFor(look.Dark);
        }
    };

    /// <summary>A residue added at the C-terminus (dehydration synthesis): it slides in from the right as the water made
    /// from the last residue's OH and the new residue's N's H lifts off, and the new peptide bond flashes. The scene
    /// meanwhile leaves the new residue out (<see cref="PeptideOptions.Hidden"/>).</summary>
    public static BiomolMotion PeptideAdded(BiomolPeptide p, BiomolLook look, BiomolChem chem)
    {
        if (p.Count < 2) return new BiomolMotion();
        var n = p.Count - 1;
        var (m, index) = p.Model(hydrogens: true);
        var residueItems = ItemsFor(m, a => p.Atoms[index[a.Id]].Residue == n, look, chem, PeptideColour(p, index, look));
        var bb = p.Backbones;
        var oxt = BioV.Place(p.Atoms[bb[n - 1].N].Pos, p.Atoms[bb[n - 1].CA].Pos, p.Atoms[bb[n - 1].C].Pos, 1.25, 117.0, p.Psi[n - 1]);
        var bond = Sleeve(new[] { p.Atoms[bb[n - 1].C].Pos, p.Atoms[bb[n].N].Pos }, "flash", BiomolParts.BondHighlight.HexFor(look.Dark), 0.3);
        return new BiomolMotion
        {
            Groups =
            {
                new(residueItems, new BioVec(7, 0, 0), BioVec.Zero, 0, 1, 0, 0.42),
                new(Water(oxt, look, chem), BioVec.Zero, new BioVec(0, 3, 0), 1, 0, 0.3, 0.6),
                new(new List<BioSceneItem> { bond }, default, default, 0, 1, 0.32, 0.6, Pulse: true),
            },
        };
    }

    /// <summary>The last residue taken off (hydrolysis): water comes down to the peptide bond, which flashes, and the residue
    /// slides away to the right, leaving the chain its C-terminal OH. The scene meanwhile is the shorter peptide without its
    /// C-terminal OH (<see cref="PeptideOptions.HideTerminalOH"/>).</summary>
    public static BiomolMotion PeptideRemoved(BiomolPeptide old, BiomolLook look, BiomolChem chem)
    {
        if (old.Count < 2) return new BiomolMotion();
        var n = old.Count - 1;
        var (m, index) = old.Model(hydrogens: true);
        var leaving = ItemsFor(m, a => old.Atoms[index[a.Id]].Residue == n, look, chem, PeptideColour(old, index, look));
        var bb = old.Backbones;
        var oxt = BioV.Place(old.Atoms[bb[n - 1].N].Pos, old.Atoms[bb[n - 1].CA].Pos, old.Atoms[bb[n - 1].C].Pos, 1.25, 117.0, old.Psi[n - 1]);
        var bond = Sleeve(new[] { old.Atoms[bb[n - 1].C].Pos, old.Atoms[bb[n].N].Pos }, "flash", BiomolParts.BondHighlight.HexFor(look.Dark), 0.3);
        return new BiomolMotion
        {
            Groups =
            {
                new(Water(oxt, look, chem), new BioVec(0, 3, 0), BioVec.Zero, 0, 1, 0, 0.42),
                new(new List<BioSceneItem> { bond }, default, default, 0, 1, 0.25, 0.5, Pulse: true),
                new(leaving, BioVec.Zero, new BioVec(7, 0, 0), 1, 0, 0.4, 0.45),
            },
        };
    }

    /// <summary>The items of a strand's last nucleotide (and its partner), as the page colours them.</summary>
    private static List<BioSceneItem> LastNucleotide(BioDuplex d, BiomolLook look, BiomolChem chem)
    {
        var n = d.Sequence.Length;
        return ItemsFor(d.Model, a => d.Atoms[a.Id].Pair == n - 1, look, chem, StrandColour(d, look));
    }

    /// <summary>A nucleotide added at the 3′ end: it slides down onto the strand as its phosphate bonds to the 3′-OH, the
    /// phosphodiester bond flashes, and water drifts away. The scene meanwhile leaves it out (<see cref="StrandOptions.HiddenPair"/>).</summary>
    public static BiomolMotion StrandAdded(BioDuplex d, BiomolLook look, BiomolChem chem)
    {
        if (d.Sequence.Length < 2 || BiomolStrand.Linkages(d, 1).LastOrDefault() is not { } link) return new BiomolMotion();
        var site = BiomolStrand.WaterSite(d, link);
        var outward = BioV.Unit(new BioVec(site.X, 0, site.Z));
        return new BiomolMotion
        {
            Groups =
            {
                new(LastNucleotide(d, look, chem), new BioVec(0, 7, 0), BioVec.Zero, 0, 1, 0, 0.42),
                new(new List<BioSceneItem> { Sleeve(link.Select(i => d.Atoms[i].Pos).ToList(), "flash", BiomolParts.BondHighlight.HexFor(look.Dark), 0.34) }, default, default, 0, 1, 0.32, 0.6, Pulse: true),
                new(Water(site, look, chem), BioVec.Zero, outward * 3 + new BioVec(0, 1, 0), 1, 0, 0.3, 0.6),
            },
        };
    }

    /// <summary>The 3′ nucleotide taken off (hydrolysis): water comes in, the bond flashes, and the nucleotide lifts away.</summary>
    public static BiomolMotion StrandRemoved(BioDuplex d, BiomolLook look, BiomolChem chem)
    {
        if (d.Sequence.Length < 2 || BiomolStrand.Linkages(d, 1).LastOrDefault() is not { } link) return new BiomolMotion();
        var site = BiomolStrand.WaterSite(d, link);
        var outward = BioV.Unit(new BioVec(site.X, 0, site.Z));
        return new BiomolMotion
        {
            Groups =
            {
                new(Water(site, look, chem), outward * 3, BioVec.Zero, 0, 1, 0, 0.4),
                new(new List<BioSceneItem> { Sleeve(link.Select(i => d.Atoms[i].Pos).ToList(), "flash", BiomolParts.BondHighlight.HexFor(look.Dark), 0.34) }, default, default, 0, 1, 0.25, 0.5, Pulse: true),
                new(LastNucleotide(d, look, chem), BioVec.Zero, new BioVec(0, 7, 0), 1, 0, 0.4, 0.45),
            },
        };
    }

    /// <summary>A disaccharide split by water (hydrolysis) or joined with water released (dehydration synthesis): the two
    /// sugars move 4 Å apart (or together) along the glycosidic bond, the water arrives and is used up (or forms and drifts
    /// away), the new –OH and H fade in (or out) with their sugars, and the bond flashes. The scene meanwhile shows nothing
    /// but its frame (everything moves).</summary>
    public static BiomolMotion DisaccharideMotion(string code, bool toSplit, BiomolLook look, BiomolChem chem)
    {
        if (BioData.Molecule(code) is not { } mol || BiomolSugar.GlycosidicOf(mol) is not { } g || mol.Pos(g.Anomeric) is not { } c1 || mol.Pos(g.O) is not { } og
            || mol.Pos(g.Other) is not { } c2 || BiomolSugar.Split(mol) is not { } s) return new BiomolMotion();
        var d = BioV.Unit(og - c1);
        var joined = mol.Model(look.Ph7);
        var res = joined.Atoms.Select(a => BiomolSugar.ResidueOf(a.Name, mol, g.O)).ToList();
        string? Colour(int r) => look.ColourBy == BiomolColourBy.Element ? null : BiomolSugar.ResidueColours[r].HexFor(look.Dark);
        var r0 = ItemsFor(joined, a => res[a.Id] == 0, look, chem, _ => Colour(0));
        var r1 = ItemsFor(joined, a => res[a.Id] == 1, look, chem, _ => Colour(1));
        // (the water's atoms where they end up, each with its bond to the sugar it joins)
        var newSet = s.NewAtoms.ToHashSet();
        var highlight = look.ColourBy == BiomolColourBy.Element ? null : BiomolParts.BondHighlight.HexFor(look.Dark);
        List<BioSceneItem> NewItems(int r)
        {
            var items = ItemsFor(s.Model, a => newSet.Contains(a.Id) && s.Residue[a.Id] == r, look, chem, _ => highlight);
            foreach (var b in s.Model.Bonds)
            {
                var (x, y) = newSet.Contains(b.A) ? (b.A, b.B) : (b.B, b.A);
                if (!newSet.Contains(x) || newSet.Contains(y) || s.Residue[x] != r) continue;
                BioMoleculeModel.Atom ax = s.Model.Atoms[x], ay = s.Model.Atoms[y];
                // (no bond to an H that is not drawn)
                if (!look.Hydrogens && ax.Element == "H") continue;
                items.AddRange(BondItems(ay.Pos, ax.Pos, Colour(r) ?? chem.Cpk(ay.Element), highlight ?? chem.Cpk(ax.Element), look));
            }
            return items;
        }
        List<BioSceneItem> new0 = NewItems(0), new1 = NewItems(1);
        var glyco = Sleeve(new[] { c1, og, c2 }, "flash", BiomolParts.BondHighlight.HexFor(look.Dark), 0.3);
        var side = BioV.Cross(d, new BioVec(0, 0, 1));
        if (BioV.Length(side) < 0.2) side = BioV.Cross(d, new BioVec(0, 1, 0));
        side = BioV.Unit(side);
        var site = (c1 + og) / 2 + side * 1.6;
        if (toSplit)
            return new BiomolMotion
            {
                Groups =
                {
                    new(Water(site, look, chem), side * 4, BioVec.Zero, 0, 1, 0, 0.4),
                    new(new List<BioSceneItem> { glyco }, default, default, 0, 1, 0.2, 0.4, Pulse: true),
                    new(Water(site, look, chem), BioVec.Zero, BioVec.Zero, 1, 0, 0.4, 0.2),
                    new(r0, BioVec.Zero, d * -2, 1, 1, 0.4, 0.5),
                    new(r1, BioVec.Zero, d * 2, 1, 1, 0.4, 0.5),
                    new(new0, d * 2, BioVec.Zero, 0, 1, 0.4, 0.5),
                    new(new1, d * -2, BioVec.Zero, 0, 1, 0.4, 0.5),
                },
            };
        return new BiomolMotion
        {
            Groups =
            {
                new(r0, d * -2, BioVec.Zero, 1, 1, 0, 0.5),
                new(r1, d * 2, BioVec.Zero, 1, 1, 0, 0.5),
                new(new0, BioVec.Zero, d * 2, 1, 0, 0, 0.5),
                new(new1, BioVec.Zero, d * -2, 1, 0, 0, 0.5),
                new(Water(site, look, chem), BioVec.Zero, side * 4, 1, 0, 0.45, 0.6),
                new(new List<BioSceneItem> { glyco }, default, default, 0, 1, 0.45, 0.5, Pulse: true),
            },
        };
    }

    /// <summary>The ball a disaccharide's scenes are framed in, joined or split (so the camera holds still as it splits).</summary>
    public static BioSphere? DisaccharideFrame(string code)
    {
        if (BioData.Molecule(code) is not { } mol || BiomolSugar.Split(mol) is not { } s) return null;
        var b = BioV.BoundingSphere(mol.Atoms.Select(a => a.Pos).Concat(s.Model.Atoms.Select(a => a.Pos)).ToList());
        return new BioSphere(b.Centre, b.Radius + 1.2);
    }
}

// ---- open in the molecule builder ----

/// <summary>A molecule handed to the chemistry builder (§4.4 shared controls: "Open in Molecule Builder", its VSEPR relax
/// not run on it): its title, then a line an atom ("atom C x y z") and a line a bond ("bond i j order"), atoms numbered
/// from 0 — the Mac's text, and what the Windows view reads back into the builder's model (BiomolHandOff.Parse).</summary>
public static class BiomolHandOff
{
    public static string Text(BioMoleculeModel m, string title, bool hydrogens = true)
    {
        var lines = new List<string> { $"title {title}" };
        var map = new Dictionary<int, int>();
        foreach (var a in m.Atoms)
        {
            if (!hydrogens && a.Element == "H") continue;
            map[a.Id] = map.Count;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"atom {a.Element} {a.Pos.X:0.000} {a.Pos.Y:0.000} {a.Pos.Z:0.000}"));
        }
        foreach (var b in m.Bonds)
            if (map.TryGetValue(b.A, out var x) && map.TryGetValue(b.B, out var y)) lines.Add($"bond {x} {y} {b.Order}");
        return string.Join("\n", lines);
    }

    /// <summary>The text read back: its title, atoms (element, place) and bonds (atom numbers from 0, order).</summary>
    public static (string Title, List<(string Element, BioVec Pos)> Atoms, List<(int A, int B, int Order)> Bonds) Parse(string text)
    {
        var title = "";
        var atoms = new List<(string, BioVec)>();
        var bonds = new List<(int, int, int)>();
        foreach (var line in BioText.Lines(text))
        {
            if (line.StartsWith("title ", StringComparison.Ordinal)) { title = line[6..]; continue; }
            var f = BioText.Words(line);
            if (f.Length == 5 && f[0] == "atom" && BioText.Number(f[2]) is { } x && BioText.Number(f[3]) is { } y && BioText.Number(f[4]) is { } z) atoms.Add((f[1], new BioVec(x, y, z)));
            else if (f.Length == 4 && f[0] == "bond" && BioText.Int(f[1]) is { } a && BioText.Int(f[2]) is { } b && BioText.Int(f[3]) is { } o && a < atoms.Count && b < atoms.Count) bonds.Add((a, b, o));
        }
        return (title, atoms, bonds);
    }
}
