namespace Simpl.Tools;

// The Bio Lab Calculator's and Photosynthesis & Respiration's text, copied out of the biology toolkit's specification by
// a script (gen_lab_text_cs.py) — never edited by hand: §5.5's metric prefix table and §5.8's overall equations and
// stage table, verbatim (its cells joined by tabs; **so** marks the specification's bold). BioLabNumbers and
// EnergyPathwaysModel parse them.

internal static class LabSpecText
{
    /// <summary>§5.5: the metric prefix table shown on the Solutions page (from the AP sheet), comma-separated.</summary>
    public const string Prefixes = "giga G 10⁹, mega M 10⁶, kilo k 10³, deci d 10⁻¹, centi c 10⁻², milli m 10⁻³, micro µ 10⁻⁶, nano n 10⁻⁹, pico p 10⁻¹²";

    /// <summary>§5.8: photosynthesis overall.</summary>
    public const string Photosynthesis = "6 CO₂ + 6 H₂O + light → C₆H₁₂O₆ + 6 O₂";

    /// <summary>§5.8: cellular respiration overall.</summary>
    public const string Respiration = "C₆H₁₂O₆ + 6 O₂ → 6 CO₂ + 6 H₂O + energy (ATP and heat)";

    /// <summary>§5.8: a row per stage — Stage, Where, In (per glucose unless stated), Out, Notes — its cells joined by tabs.</summary>
    public static readonly string[] Stages =
    {
        "Light reactions\tThylakoid membranes (chloroplast)\tH₂O, light, NADP⁺, ADP + Pᵢ\tO₂ (from **water**), NADPH, ATP\tPhotosystem II, then I; an H⁺ gradient drives ATP synthase",
        "Calvin cycle\tStroma\t3 CO₂, 9 ATP, 6 NADPH per G3P (6 CO₂, 18 ATP, 12 NADPH per glucose)\t1 G3P (2 per glucose), ADP, NADP⁺\tRubisco fixes CO₂",
        "Glycolysis\tCytosol\tglucose, 2 NAD⁺, 2 ATP invested\t2 pyruvate, 2 NADH, 4 ATP (net **2**)\tNo O₂ needed",
        "Pyruvate oxidation\tMitochondrial matrix\t2 pyruvate, 2 NAD⁺, 2 CoA\t2 acetyl-CoA, 2 CO₂, 2 NADH\t",
        "Citric acid (Krebs) cycle\tMatrix\t2 acetyl-CoA (2 turns)\t4 CO₂, 6 NADH, 2 FADH₂, 2 ATP (or GTP)\t",
        "Oxidative phosphorylation\tInner mitochondrial membrane\t10 NADH, 2 FADH₂, O₂ (final e⁻ acceptor → H₂O)\tabout 26–28 ATP\tChemiosmosis via ATP synthase",
        "Total\t\t\tabout 30–32 ATP per glucose (older books: 36–38)\t",
        "Fermentation\tCytosol\tpyruvate, NADH\tlactate (animals, some bacteria) or ethanol + CO₂ (yeast); NAD⁺ regenerated\tTotal yield: only glycolysis's 2 ATP",
    };
}
