using System.Globalization;

namespace Simpl.Tools;

// The molecule blocks of the biology toolkit's data (spec §3.3), the Mac's BioMolecule.swift: a residue's or a
// molecule's atoms (name, element, position in Å), its bonds with their orders, its aromatic rings, the rotamer and χ
// angles of an amino acid, and its pH 7 form — read from the text both apps share (BioDataText.cs), and turned into
// what the 3D views draw (BioMoleculeModel: atoms and bonds, as the chemistry builder's MoleculeModel has them, with no
// VSEPR — the coordinates are already right).

/// <summary>Where an atom of a template belongs: always, or only at a chain's end, or only in the pH 7 form.</summary>
public enum BioAtomRole
{
    Always,
    /// <summary>"# N-terminus only" (and Pro's "free Pro / N-terminus only"): the first residue's extra amine H.</summary>
    NTerminus,
    /// <summary>"# C-terminus only": OXT and HXT, the last residue's carboxyl.</summary>
    CTerminus,
    /// <summary>"# pH 7 form only": the extra H on N of the zwitterion.</summary>
    Ph7Only,
}

/// <summary>An atom of a molecule block: its PDB/CCD name ("CA", "O5'", "H5''"), its element ("C", "Fe"), where it is (Å),
/// where it belongs, and the comment after # if any.</summary>
public sealed record BioAtomRecord(string Name, string Element, BioVec Pos, BioAtomRole Role = BioAtomRole.Always, string Comment = "");

/// <summary>A bond between two atoms of a block, by name; order 1, 2 or 3.</summary>
public sealed record BioBondRecord(string A, string B, int Order);

/// <summary>A χ angle of an amino acid: its index (1…4), its four atoms and its value in the template (degrees).</summary>
public sealed record BioChi(int Index, string[] Atoms, double Degrees);

/// <summary>A rotamer of an amino acid: its name (Lovell 2000) and χ angles in degrees.</summary>
public sealed record BioRotamer(string Name, double[] Chis);

/// <summary>A block's pH 7 form, as its "pH 7:" line says: atoms to add (those marked "pH 7 form only"), atoms to remove,
/// and the charges that result ("N becomes +1", "OXT becomes −1", "NZ is +1").</summary>
public sealed class BioPH7
{
    public string Text { get; init; } = "";
    public List<string> Remove { get; } = new();
    public List<string> Add { get; } = new();
    public Dictionary<string, int> Charges { get; } = new();
}

/// <summary>A molecule block (§3.3): a residue template (§6.3) or a molecule (§6.6, §6.8, §6.9).</summary>
public sealed class BioMolecule
{
    public enum BlockKind { Residue, Molecule }

    public BlockKind Kind { get; init; }
    /// <summary>"ALA", "DA", "GLC", "POV".</summary>
    public string Code { get; init; } = "";
    /// <summary>"Alanine", "dAMP (deoxyadenosine monophosphate)", "maltose: glucose α(1→4) glucose".</summary>
    public string Title { get; init; } = "";
    /// <summary>"C10 H14 N5 O6 P" (molecules only).</summary>
    public string? Formula { get; init; }
    /// <summary>Molar mass in g/mol (molecules only).</summary>
    public double? Mass { get; init; }
    public List<BioAtomRecord> Atoms { get; set; } = new();
    public List<BioBondRecord> Bonds { get; set; } = new();
    public List<string> Aromatic { get; set; } = new();
    /// <summary>The default rotamer's name ("mtt180"); its χ angles as the template has them are <see cref="Chis"/>.</summary>
    public string? Rotamer { get; set; }
    public List<BioChi> Chis { get; set; } = new();
    /// <summary>Asp and Asn: the rotamer used in a β-strand.</summary>
    public BioRotamer? BetaRotamer { get; set; }
    /// <summary>Every other rotamer, most common first (the peptide builder's clash relief tries them in turn).</summary>
    public List<BioRotamer> Alternatives { get; set; } = new();
    /// <summary>Pro: its ring, which is never rotated.</summary>
    public string? Ring { get; set; }
    public BioPH7? Ph7 { get; set; }
    public List<string> Notes { get; set; } = new();

    public BioAtomRecord? Atom(string name) => Atoms.FirstOrDefault(a => a.Name == name);
    public int? Index(string name) { var i = Atoms.FindIndex(a => a.Name == name); return i < 0 ? null : i; }
    public BioVec? Pos(string name) => Atom(name)?.Pos;

    /// <summary>The atoms of a form: the pH 7 form (zwitterion, ionised groups) or the neutral one; with or without H; with
    /// or without the chain-end atoms (a free amino acid has both ends).</summary>
    public List<BioAtomRecord> AtomsOf(bool ph7 = true, bool hydrogens = true, bool nTerminus = true, bool cTerminus = true)
    {
        var removed = new HashSet<string>(ph7 ? Ph7?.Remove ?? new List<string>() : new List<string>());
        return Atoms.Where(a =>
        {
            if (!hydrogens && a.Element == "H") return false;
            switch (a.Role)
            {
                case BioAtomRole.Ph7Only when !ph7: return false;
                case BioAtomRole.NTerminus when !nTerminus: return false;
                case BioAtomRole.CTerminus when !cTerminus: return false;
            }
            return !removed.Contains(a.Name);
        }).ToList();
    }

    /// <summary>The value of χ<paramref name="index"/> as the atoms now lie (degrees), null if the residue has no such χ.</summary>
    public double? ChiValue(int index)
    {
        if (Chis.FirstOrDefault(c => c.Index == index) is not { } chi) return null;
        var p = chi.Atoms.Select(Pos).Where(v => v != null).Select(v => v!.Value).ToList();
        if (p.Count != 4) return null;
        return BioV.Dihedral(p[0], p[1], p[2], p[3]);
    }

    /// <summary>
    /// The block with χ<paramref name="index"/> turned to <paramref name="degrees"/> (§6.3): every atom on the far side of
    /// the χ bond — found by walking the bonds from its far atom without crossing it — turned about the bond. Set χ1
    /// first, then χ2 and so on.
    /// </summary>
    public BioMolecule SettingChi(int index, double degrees)
    {
        if (Chis.FirstOrDefault(c => c.Index == index) is not { } chi || ChiValue(index) is not { } now
            || Pos(chi.Atoms[1]) is not { } b || Pos(chi.Atoms[2]) is not { } cc) return this;
        var adjacent = new Dictionary<string, List<string>>();
        foreach (var bond in Bonds)
        {
            (adjacent.TryGetValue(bond.A, out var la) ? la : adjacent[bond.A] = new List<string>()).Add(bond.B);
            (adjacent.TryGetValue(bond.B, out var lb) ? lb : adjacent[bond.B] = new List<string>()).Add(bond.A);
        }
        var far = new HashSet<string> { chi.Atoms[2] };
        var stack = new Stack<string>();
        stack.Push(chi.Atoms[2]);
        while (stack.Count > 0)
        {
            var x = stack.Pop();
            foreach (var y in adjacent.GetValueOrDefault(x) ?? new List<string>())
                if (y != chi.Atoms[1] && far.Add(y)) stack.Push(y);
        }
        far.Remove(chi.Atoms[2]);
        var axis = cc - b;
        var turn = degrees - now;
        var copy = Clone();
        copy.Atoms = Atoms.Select(a => far.Contains(a.Name) ? a with { Pos = cc + BioV.Rotate(a.Pos - cc, axis, turn) } : a).ToList();
        return copy;
    }

    /// <summary>A copy whose lists can be changed without changing this one.</summary>
    public BioMolecule Clone() => new()
    {
        Kind = Kind, Code = Code, Title = Title, Formula = Formula, Mass = Mass, Atoms = Atoms.ToList(), Bonds = Bonds.ToList(),
        Aromatic = Aromatic.ToList(), Rotamer = Rotamer, Chis = Chis.ToList(), BetaRotamer = BetaRotamer, Alternatives = Alternatives.ToList(),
        Ring = Ring, Ph7 = Ph7, Notes = Notes.ToList(),
    };

    /// <summary>What a 3D view draws: the atoms of the form asked for, and the bonds between them; each atom tagged with
    /// its name (and <paramref name="tag"/>, e.g. a residue "Lys 12").</summary>
    public BioMoleculeModel Model(bool ph7 = true, bool hydrogens = true, bool nTerminus = true, bool cTerminus = true, string tag = "")
    {
        var chosen = AtomsOf(ph7, hydrogens, nTerminus, cTerminus);
        var m = new BioMoleculeModel();
        var ids = new Dictionary<string, int>();
        foreach (var a in chosen) ids[a.Name] = m.Add(a.Element, a.Pos, a.Name, tag);
        foreach (var bd in Bonds)
            if (ids.TryGetValue(bd.A, out var x) && ids.TryGetValue(bd.B, out var y)) m.AddBond(x, y, bd.Order);
        return m;
    }
}

/// <summary>Atoms and bonds for a 3D view (spec §3.1: the chemistry builder's MoleculeModel's shape, plus a name and a tag
/// for each atom — residue and part — and no VSEPR). Ids are the atoms' indices.</summary>
public sealed class BioMoleculeModel
{
    /// <summary>An atom: its id, its element ("C": its colour and radius come from the chemistry tables, MBChem), where it
    /// is, its name in its block ("CA", "O5'") and what it belongs to ("Lys 12", "phosphate", "strand 1").</summary>
    public sealed record Atom(int Id, string Element, BioVec Pos, string Name = "", string Tag = "");

    public sealed record Bond(int A, int B, int Order = 1);

    public List<Atom> Atoms { get; private set; } = new();
    public List<Bond> Bonds { get; private set; } = new();

    public int Add(string element, BioVec pos, string name = "", string tag = "")
    {
        Atoms.Add(new Atom(Atoms.Count, element, pos, name, tag));
        return Atoms.Count - 1;
    }

    public void AddBond(int a, int b, int order = 1) => Bonds.Add(new Bond(a, b, order));

    /// <summary>Another model's atoms and bonds added to this one (its ids shifted); returns the first new id.</summary>
    public int Append(BioMoleculeModel other)
    {
        var start = Atoms.Count;
        foreach (var a in other.Atoms) Atoms.Add(a with { Id = start + a.Id });
        foreach (var b in other.Bonds) Bonds.Add(new Bond(start + b.A, start + b.B, b.Order));
        return start;
    }

    /// <summary>The bounding sphere of the atoms' centres (Å), for framing.</summary>
    public (BioVec Centre, double Radius) Bounds => BioV.BoundingSphere(Atoms.Select(a => a.Pos).ToList());

    /// <summary>Every atom moved by <paramref name="offset"/>.</summary>
    public BioMoleculeModel Moved(BioVec offset) => new() { Atoms = Atoms.Select(a => a with { Pos = a.Pos + offset }).ToList(), Bonds = Bonds.ToList() };

    /// <summary>The model mirrored through the xy-plane (z → −z): an L-amino acid's D image (§4.4 Chirality).</summary>
    public BioMoleculeModel Mirrored() => new() { Atoms = Atoms.Select(a => a with { Pos = new BioVec(a.Pos.X, a.Pos.Y, -a.Pos.Z) }).ToList(), Bonds = Bonds.ToList() };

    /// <summary>The bonds' lengths outside [lo, hi] Å, as "CA–CB 2.10".</summary>
    public List<string> OddBonds(double lo = 0.9, double hi = 1.9) => Bonds.Select(b =>
    {
        var d = BioV.Distance(Atoms[b.A].Pos, Atoms[b.B].Pos);
        return d < lo || d > hi ? $"{Atoms[b.A].Name}–{Atoms[b.B].Name} {d.ToString("0.00", CultureInfo.InvariantCulture)}" : null;
    }).Where(s => s != null).Select(s => s!).ToList();
}

// ---- the block parser (§3.3) ----

public static class BioBlockParser
{
    /// <summary>The keys a keyed line starts with (the rest of a line is an atom).</summary>
    private static readonly string[] Keys = { "bonds", "aromatic", "rotamer", "chi1", "chi2", "chi3", "chi4", "beta-strand rotamer", "alternatives", "ring", "pH 7", "note" };

    /// <summary>Every block in a text of blocks, in order.</summary>
    public static List<BioMolecule> Parse(string text)
    {
        var out_ = new List<BioMolecule>();
        BioMolecule? current = null;
        // (each keyed line gathered whole first: a line starting with a space or a tab carries on the one before)
        var keyed = new List<(string Key, string Value)>();

        void Finish()
        {
            if (current is not { } m) return;
            foreach (var (k, v) in keyed) Apply(k, v, m);
            out_.Add(m);
            current = null;
            keyed = new List<(string, string)>();
        }

        foreach (var raw in BioText.Lines(text, keepEmpty: true))
        {
            if (raw.Trim(' ', '\t').Length == 0) continue;
            if (raw.StartsWith("@molecule ", StringComparison.Ordinal) || raw.StartsWith("@residue ", StringComparison.Ordinal))
            {
                Finish();
                current = Header(raw);
                continue;
            }
            if (current == null) continue;
            if (raw.StartsWith(' ') || raw.StartsWith('\t'))
            {
                if (keyed.Count > 0) keyed[^1] = (keyed[^1].Key, keyed[^1].Value + " " + raw.Trim(' ', '\t'));
                continue;
            }
            if (KeyedLine(raw) is { } kv)
            {
                keyed.Add(kv);
                continue;
            }
            if (AtomLine(raw) is { } a) current.Atoms.Add(a);
        }
        Finish();
        return out_;
    }

    /// <summary>"@molecule DA dAMP (deoxyadenosine monophosphate)   (C10 H14 N5 O6 P, M = 331.222 g/mol, CCD DA)".</summary>
    private static BioMolecule Header(string line)
    {
        var isResidue = line.StartsWith("@residue ", StringComparison.Ordinal);
        var rest = line[line.IndexOf(' ')..].Trim(' ', '\t');
        var sp = rest.IndexOf(' ');
        var code = sp < 0 ? rest : rest[..sp];
        rest = rest[code.Length..].Trim(' ', '\t');
        string? formula = null;
        double? mass = null;
        // (the facts in the last parenthesis, after three spaces: "(C10 H14 N5 O6 P, M = 331.222 g/mol, CCD DA)")
        var r = rest.LastIndexOf("   (", StringComparison.Ordinal);
        if (r >= 0 && rest.EndsWith(')'))
        {
            var facts = rest[(r + 4)..^1];
            if (facts.Contains("M = "))
            {
                var parts = facts.Split(", ");
                formula = parts[0];
                if (parts.FirstOrDefault(p => p.StartsWith("M = ", StringComparison.Ordinal)) is { } m)
                    mass = BioText.Number(m[4..].Replace(" g/mol", ""));
                rest = rest[..r].Trim(' ', '\t');
            }
        }
        return new BioMolecule { Kind = isResidue ? BioMolecule.BlockKind.Residue : BioMolecule.BlockKind.Molecule, Code = code, Title = rest, Formula = formula, Mass = mass };
    }

    /// <summary>A keyed line's key and value: "bonds: …", "alternatives (most common first): …".</summary>
    private static (string Key, string Value)? KeyedLine(string line)
    {
        foreach (var k in Keys)
        {
            if (!line.StartsWith(k, StringComparison.Ordinal)) continue;
            var rest = line[k.Length..];
            if (rest.StartsWith(" (", StringComparison.Ordinal))
            {
                var close = rest.IndexOf(')');
                rest = close < 0 ? "" : rest[(close + 1)..];
            }
            if (!rest.StartsWith(':')) continue;
            return (k, rest[1..].Trim(' ', '\t'));
        }
        return null;
    }

    /// <summary>"CA   C     0.000    0.000    0.000   # comment".</summary>
    private static BioAtomRecord? AtomLine(string line)
    {
        var body = line;
        var comment = "";
        var h = line.IndexOf('#');
        if (h >= 0)
        {
            comment = line[(h + 1)..].Trim(' ', '\t');
            body = line[..h];
        }
        var f = body.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 5 || BioText.Number(f[2]) is not { } x || BioText.Number(f[3]) is not { } y || BioText.Number(f[4]) is not { } z) return null;
        var role = BioAtomRole.Always;
        if (comment.Contains("pH 7 form only")) role = BioAtomRole.Ph7Only;
        else if (comment.Contains("N-terminus only")) role = BioAtomRole.NTerminus;
        else if (comment.Contains("C-terminus only")) role = BioAtomRole.CTerminus;
        return new BioAtomRecord(f[0], f[1], new BioVec(x, y, z), role, comment);
    }

    /// <summary>A bond token split at the first -, = or # that is not its first character.</summary>
    public static BioBondRecord? Bond(string token)
    {
        if (token.Length < 3) return null;
        for (var i = 1; i < token.Length - 1; i++)
        {
            var c = token[i];
            if (c is '-' or '=' or '#') return new BioBondRecord(token[..i], token[(i + 1)..], c == '-' ? 1 : (c == '=' ? 2 : 3));
        }
        return null;
    }

    private static double[] Numbers(string s) =>
        s.Replace('−', '-').Split(new[] { ' ', ',', '[', ']' }, StringSplitOptions.RemoveEmptyEntries).Select(w => BioText.Number(w)).Where(d => d != null).Select(d => d!.Value).ToArray();

    private static void Apply(string key, string value, BioMolecule m)
    {
        switch (key)
        {
            case "bonds":
                m.Bonds.AddRange(BioText.Words(value).Select(Bond).Where(b => b != null).Select(b => b!));
                break;
            case "aromatic":
                m.Aromatic = BioText.Words(value).ToList();
                break;
            case "rotamer":
                m.Rotamer = BioText.Words(value).FirstOrDefault();
                break;
            case "chi1" or "chi2" or "chi3" or "chi4":
            {
                // "N-CA-CB-CG = -67"
                var sides = value.Split('=');
                if (sides.Length != 2 || BioText.Number(sides[1].Trim(' ', '\t').Replace('−', '-')) is not { } deg) return;
                var atoms = sides[0].Trim(' ', '\t').Split('-');
                if (atoms.Length != 4 || BioText.Int(key[3..]) is not { } i) return;
                m.Chis.Add(new BioChi(i, atoms, deg));
                m.Chis = m.Chis.OrderBy(c => c.Index).ToList();
                break;
            }
            case "beta-strand rotamer":
            {
                // "m-20 (chi2 at -75, in its range) chis [-70, -75]"
                var name = BioText.Words(value).FirstOrDefault() ?? "";
                var r = value.IndexOf("chis [", StringComparison.Ordinal);
                if (r >= 0) m.BetaRotamer = new BioRotamer(name, Numbers(value[(r + 6)..]));
                break;
            }
            case "alternatives":
                m.Alternatives = value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(entry =>
                {
                    var f = BioText.Words(entry);
                    if (f.Length < 2) return null;
                    return new BioRotamer(f[0], f.Skip(1).Select(w => BioText.Number(w.Replace('−', '-'))).Where(d => d != null).Select(d => d!.Value).ToArray());
                }).Where(r => r != null).Select(r => r!).ToList();
                break;
            case "ring":
                m.Ring = value;
                break;
            case "pH 7":
                m.Ph7 = Ph7(value, m.Atoms);
                break;
            case "note":
                m.Notes.Add(value);
                break;
        }
    }

    /// <summary>"add H3 on N (N becomes +1); remove HXT (OXT becomes −1); remove HOP2 and HOP3 (phosphate −2)".</summary>
    private static BioPH7 Ph7(string text, List<BioAtomRecord> atoms)
    {
        var p = new BioPH7 { Text = text };
        p.Add.AddRange(atoms.Where(a => a.Role == BioAtomRole.Ph7Only).Select(a => a.Name));
        var names = atoms.Select(a => a.Name).ToHashSet();
        string? Name(string w)
        {
            var n = w.Trim('(');
            return names.Contains(n) ? n : null;
        }
        var words = BioText.Words(text.Replace(";", " ; "));
        var i = 0;
        while (i < words.Length)
        {
            if (words[i] == "remove")
            {
                var j = i + 1;
                while (j < words.Length && !words[j].StartsWith('(') && words[j] != ";")
                {
                    if (words[j] != "and") p.Remove.Add(words[j]);
                    j++;
                }
                i = j;
                continue;
            }
            // ("OXT becomes −1", "NZ is +1", "NH2 carries the +1")
            if (i + 2 < words.Length && (words[i + 1] == "becomes" || words[i + 1] == "is") && Name(words[i]) is { } n && Charge(words[i + 2]) is { } c) p.Charges[n] = c;
            if (i + 3 < words.Length && words[i + 1] == "carries" && words[i + 2] == "the" && Name(words[i]) is { } n2 && Charge(words[i + 3]) is { } c2) p.Charges[n2] = c2;
            i++;
        }
        return p;
    }

    private static int? Charge(string w)
    {
        var s = w.Trim('(', ')', ';', ',').Replace('−', '-');
        if (!(s.StartsWith('+') || s.StartsWith('-'))) return null;
        return BioText.Int(s);
    }
}
