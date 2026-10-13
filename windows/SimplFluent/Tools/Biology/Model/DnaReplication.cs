namespace Simpl.Tools;

// The Replication page's model (spec §4.2 Replication): a replication fork drawn left to right, stepped through in nine
// steps that each name the enzyme at work — the origin; helicase unwinding (topoisomerase ahead, single-strand binding
// proteins holding the strands apart); primase laying an RNA primer on each template; DNA polymerase III adding to each
// primer's 3′ end, reading the template 3′→5′ and building 5′→3′; the continuous leading strand; the lagging strand's
// three Okazaki fragments, each from its own primer; DNA polymerase I replacing the primers; ligase sealing the nicks;
// and two semiconservative daughter molecules. Each step's keyframe is numbers (how far the fork has opened, how far each
// new strand reaches, which enzymes are there), so a step's change is drawn by mixing two keyframes.

/// <summary>A keyframe of the fork (positions in base pairs from the origin at the left; strand extents as positions).</summary>
/// <param name="Fork">How many base pairs are open from the origin (0 shut, 32 all; more: the fork has passed the end).</param>
/// <param name="Apart">0…1: the two finished daughter molecules drawn apart (the last step).</param>
/// <param name="LeadPrimer">0…1: the leading strand's primer laid (at the origin, positions 0–3).</param>
/// <param name="LeadDna">The leading strand's DNA reaches up to this position (from 4, after its primer).</param>
/// <param name="LagPrimer">Each Okazaki fragment's primer laid, 0…1.</param>
/// <param name="LagDna">Each fragment's 3′ end has reached down to this position (from its primer's start, leftward).</param>
/// <param name="Replaced">0…1: the RNA primers replaced by DNA (DNA polymerase I).</param>
/// <param name="Nicks">0…1: the nicks between the new pieces shown.</param>
/// <param name="Sealed">0…1: the nicks sealed (DNA ligase).</param>
/// <param name="Incoming">0…1: an incoming nucleotide shown pairing at each polymerase.</param>
public sealed record DnaForkFrame(double Fork, double Apart, double LeadPrimer, double LeadDna, double[] LagPrimer, double[] LagDna,
    double Replaced, double Nicks, double Sealed, double Incoming,
    double Helicase, double Topo, double Ssb, double Primase, double Pol3, double Pol1, double Ligase)
{
    public static DnaForkFrame Mix(DnaForkFrame a, DnaForkFrame b, double t)
    {
        double M(double x, double y) => x + (y - x) * t;
        double[] A(double[] x, double[] y) => x.Zip(y, M).ToArray();
        return new DnaForkFrame(M(a.Fork, b.Fork), M(a.Apart, b.Apart), M(a.LeadPrimer, b.LeadPrimer), M(a.LeadDna, b.LeadDna),
            A(a.LagPrimer, b.LagPrimer), A(a.LagDna, b.LagDna), M(a.Replaced, b.Replaced), M(a.Nicks, b.Nicks), M(a.Sealed, b.Sealed),
            M(a.Incoming, b.Incoming), M(a.Helicase, b.Helicase), M(a.Topo, b.Topo), M(a.Ssb, b.Ssb), M(a.Primase, b.Primase),
            M(a.Pol3, b.Pol3), M(a.Pol1, b.Pol1), M(a.Ligase, b.Ligase));
    }
}

/// <summary>What sits at a position of a new strand: RNA primer or DNA, and how much of it is drawn (0…1).</summary>
public enum DnaPieceKind { Rna, Dna }

public static class DnaReplication
{
    /// <summary>Base pairs drawn.</summary>
    public const int Length = 32;
    /// <summary>A primer as drawn (about 10 nt in the cell; 4 drawn so the fragments have room).</summary>
    public const int PrimerLength = 4;

    /// <summary>The bottom parental strand, written 5′→3′ left to right (the lagging strand's template).</summary>
    public const string Bottom = "ATGCGTACGTTAGCCATGCATCGAGCTTAGCC";

    /// <summary>The top parental strand, written left to right — so 3′→5′ (the leading strand's template).</summary>
    public static string Top => BioSeq.Complement(Bottom);

    /// <summary>The lagging strand's three Okazaki fragments: [Start, End) each; its primer is its last four (its 5′ end,
    /// nearest the fork), its DNA grows leftward from there.</summary>
    public static readonly (int Start, int End)[] Fragments = { (0, 12), (12, 22), (22, 32) };

    public static int PrimerStart(int fragment) => Fragments[fragment].End - PrimerLength;

    /// <summary>Where nicks are left once the primers are replaced: the leading strand's after its primer, and the lagging
    /// strand's after each fragment's replaced primer.</summary>
    public static readonly int[] LeadingNicks = { PrimerLength };
    public static readonly int[] LaggingNicks = { 8, 18, 28 };

    /// <summary>The nine steps (§4.2), each naming the enzyme that acts; number keys jump to their groups.</summary>
    public static readonly BioStep[] Steps =
    {
        new(0, "Origin", "Replication starts at an origin. The parent double helix is two antiparallel strands, drawn in two shades: the top runs 3′→5′ left to right, the bottom 5′→3′.", Group: 1),
        new(1, "Helicase unwinds the helix", "Helicase unwinds the double helix and separates the strands, breaking the hydrogen bonds between base pairs. Topoisomerase relieves the twisting ahead of the fork; single-strand binding proteins hold the strands apart.", Group: 1),
        new(2, "Primase lays RNA primers", "Primase lays down a short RNA primer on each template (about 10 nucleotides in the cell; 4 drawn here). Primers are RNA, so they carry U, not T.", Group: 2),
        new(3, "DNA polymerase III adds nucleotides", "DNA polymerase III adds nucleotides to the 3′ end of each primer. It reads the template 3′→5′ and builds the new strand 5′→3′, pairing A with T and G with C.", Group: 3),
        new(4, "Leading strand: continuous", "On the leading strand, DNA polymerase III follows the fork, building one continuous new strand 5′→3′ toward it.", Group: 3),
        new(5, "Lagging strand: Okazaki fragments", "The lagging strand’s template runs the other way, so its new strand is built away from the fork in short Okazaki fragments, each from its own RNA primer laid by primase.", Group: 3),
        new(6, "DNA polymerase I replaces primers", "DNA polymerase I removes the RNA primers and fills the gaps with DNA. A nick, a missing link in the backbone, is left where each new piece meets the next.", Group: 4),
        new(7, "DNA ligase seals the nicks", "DNA ligase seals each nick with a phosphodiester bond, joining the pieces into one continuous strand.", Group: 4),
        new(8, "Semiconservative result", "Two DNA molecules, each with one old (parental) strand and one new strand: replication is semiconservative.", Group: 5),
    };

    /// <summary>The number keys' groups.</summary>
    public static readonly IReadOnlyDictionary<int, string> Groups = new Dictionary<int, string>
    {
        [1] = "Unwinding", [2] = "Priming", [3] = "Elongation", [4] = "Finishing", [5] = "Result",
    };

    /// <summary>The enzyme (or enzymes) at work in each step.</summary>
    public static readonly string[] Enzymes =
    {
        "None yet: the origin is where the proteins gather",
        "Helicase · topoisomerase · single-strand binding proteins",
        "Primase",
        "DNA polymerase III",
        "DNA polymerase III",
        "Primase and DNA polymerase III",
        "DNA polymerase I",
        "DNA ligase",
        "None: replication is finished",
    };

    private static double[] Three(double a, double b, double c) => new[] { a, b, c };

    /// <summary>The keyframes, one a step.</summary>
    public static readonly DnaForkFrame[] Frames =
    {
        //  fork apart leadP leadDna  lagPrimer         lagDna               repl nicks seal inc   heli topo ssb  prim pol3 pol1 lig
        new(0, 0, 0, 4, Three(0, 0, 0), Three(8, 18, 28), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new(14, 0, 0, 4, Three(0, 0, 0), Three(8, 18, 28), 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0),
        new(14, 0, 1, 4, Three(1, 0, 0), Three(8, 18, 28), 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0),
        new(14, 0, 1, 11, Three(1, 0, 0), Three(3, 18, 28), 0, 0, 0, 1, 1, 1, 1, 0, 1, 0, 0),
        new(23, 0, 1, 21, Three(1, 0, 0), Three(0, 18, 28), 0, 0, 0, 0, 1, 1, 1, 0, 1, 0, 0),
        new(34, 0, 1, 32, Three(1, 1, 1), Three(0, 12, 24), 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0),
        new(34, 0, 1, 32, Three(1, 1, 1), Three(0, 12, 22), 1, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0),
        new(34, 0, 1, 32, Three(1, 1, 1), Three(0, 12, 22), 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1),
        new(36, 1, 1, 32, Three(1, 1, 1), Three(0, 12, 22), 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0),
    };

    /// <summary>How far the strands have parted at a place along the DNA (in base pairs), 0 shut … 1 fully apart: the
    /// fork's opening takes <see cref="Ramp"/> base pairs, eased.</summary>
    public static double Open(double x, double fork) => BioStepTiming.Ease(Math.Clamp((fork - x) / Ramp, 0, 1));

    /// <summary>The base pairs the fork's opening takes.</summary>
    public const double Ramp = 2.0;

    /// <summary>The fork as a phase of the step-through draws it (a keyframe, or two mixed).</summary>
    public static DnaForkFrame At(BioStepPhase p)
    {
        var a = Frames[Math.Clamp(p.From, 0, Frames.Length - 1)];
        var b = Frames[Math.Clamp(p.To, 0, Frames.Length - 1)];
        return p.Crossfade || p.From == p.To ? (p.T >= 0.5 ? b : a) : DnaForkFrame.Mix(a, b, p.T);
    }

    /// <summary>The leading strand at a position: its primer (positions 0–3, RNA until replaced) or its DNA, and how much
    /// of it is there; null where nothing is yet.</summary>
    public static (DnaPieceKind Kind, double Amount)? Leading(int i, DnaForkFrame f)
    {
        // (nothing pairs with a template until the fork has fully parted it there)
        if (Open(i + 0.5, f.Fork) < 0.999) return null;
        if (i < PrimerLength)
            return f.LeadPrimer <= 0.01 ? null : (f.Replaced >= 0.5 ? DnaPieceKind.Dna : DnaPieceKind.Rna, Math.Clamp(f.LeadPrimer, 0, 1));
        var amount = Math.Clamp(f.LeadDna - i, 0, 1);
        return amount <= 0.01 ? null : (DnaPieceKind.Dna, amount);
    }

    /// <summary>The lagging strand at a position: which fragment, its primer or DNA, and how much is there; null where
    /// nothing is yet.</summary>
    public static (DnaPieceKind Kind, double Amount, int Fragment)? Lagging(int i, DnaForkFrame f)
    {
        if (Open(i + 0.5, f.Fork) < 0.999) return null;
        for (var j = 0; j < Fragments.Length; j++)
        {
            var (start, end) = Fragments[j];
            if (i < start || i >= end) continue;
            if (i >= PrimerStart(j))
                return f.LagPrimer[j] <= 0.01 ? null : (f.Replaced >= 0.5 ? DnaPieceKind.Dna : DnaPieceKind.Rna, Math.Clamp(f.LagPrimer[j], 0, 1), j);
            var amount = Math.Clamp(i + 1 - f.LagDna[j], 0, 1);
            return amount <= 0.01 ? null : (DnaPieceKind.Dna, amount, j);
        }
        return null;
    }

    /// <summary>The letter of a new strand at a position: the leading strand pairs with the top template (so it reads as
    /// the bottom strand does), the lagging strand with the bottom one; RNA has U where DNA has T.</summary>
    public static char NewLetter(bool leading, int i, bool rna)
    {
        var c = leading ? BioSeq.Pair(Top[i], false) : BioSeq.Pair(Bottom[i], false);
        return rna && c == 'T' ? 'U' : c;
    }

    /// <summary>Where the lagging strand's polymerase works: the 3′ end of the newest fragment that has its primer, while
    /// that fragment is still growing (none once it is finished and the next has no primer yet).</summary>
    public static int? LaggingEnd(DnaForkFrame f)
    {
        for (var j = Fragments.Length - 1; j >= 0; j--)
            if (f.LagPrimer[j] > 0.5)
                return f.LagDna[j] > Fragments[j].Start + 0.01 ? (int)Math.Round(f.LagDna[j]) : null;
        return null;
    }
}
