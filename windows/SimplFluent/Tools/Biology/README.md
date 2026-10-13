# The biology toolkit on Windows: the foundation

Ten tools under a **Biology** heading on the Tools page (spec §2): `cell` Cell Explorer, `dna` DNA & RNA, `gene` Gene
Expression, `biomol` Biomolecule Builder, `cross` Genetics Crosses, `divide` Cell Division, `popgen` Hardy–Weinberg,
`biolab` Bio Lab Calculator, `enzyme` Enzyme Lab, `energy` Photosynthesis & Respiration. Each is pinnable like any
tool. This page is for the people building them: what the foundation gives you, and which files are yours. It is the
Windows twin of the Mac's `docs/mac-biology.md`: the same design, the same data, the same algorithms (the self-check's
110 shared lines come out identical on both, value for value), C# names in PascalCase.

## Files

```
windows/SimplFluent/Tools/Biology/
  BioTools.cs           the registry (the ten Kinds, Register, pages in the header, launch state, prefs, hand-off, --bio-check)
  BioColors.cs          §3.8's colours as WPF colours and brushes
  BioUI.cs              the layout, headings, cards, rows, facts, legend, sequence text, the pin's Open, the placeholder
  BioStepThrough.cs     the step-through's controls, keys, frame-by-frame drawing and Narrator (§3.6)
  BioScene.cs           meshes → frozen MeshGeometry3D, materials, batching; molecules with the chemistry colours and radii
  BioSceneView.cs       the 3D view: camera, picking, hover, selection, keys, labels with leader lines (§3.4, §3.5); its card
  BioDemo.cs            the foundation seen working (--place tools --bio-demo, a development run's): not a tool
  <Tool>.cs             each tool's view and its registration (a placeholder until built) — yours
  Model/                plain .NET, no WPF: the unit tests compile and run it anywhere
    BioDataText.cs      §5.3.4 and §6 copied out of the specification by a script — never edited by hand
    BioData.cs          those datasets parsed (once, lazily, thread-safe): BioData, BioGenes
    BioMolecule.cs      the molecule-block parser (§3.3), BioMolecule, BioMoleculeModel
    BioGeneticCode.cs   BioCode (§6.1), BioAminoAcids (§6.2)
    BioSequence.cs      BioSequenceInput (cleaning), BioSeq (operations), BioORF (§3.7)
    BioDNA.cs           BioBDNA: B-DNA atom by atom (§6.7)
    BioGenetics.cs      BioCross, BioGene, gametes, ratios, product rule, typed crosses (§5.3, §5.6.2)
    BioMath.cs          BioVec/BioV (vectors, dihedral, NeRF), BioRandom (SplitMix64), BioFrac, BioRatio, BioChiSquare, BioText
    BioMesh.cs          BioMesh, BioFrame, BioShapes / BioPath primitives, the cutaway, BioMeshBatch
    BioMoleculeMeshes.cs  atoms and bonds as scene items
    BioSceneContent.cs  BioSceneContent / BioSceneItem / BioMaterial / BioLabel3D / BioSphere (a scene as values)
    BioLabelLayout.cs   §3.5's label layout
    BioSteps.cs         BioStep, BioStepPhase, BioStepTiming (§3.6)
    BioStepModel.cs     BioStepController (the step-through's state, its clock handed in)
    BioPalette.cs       §3.8's hex pairs
    BioSelfCheck.cs     §10.1's self-check
    <Tool>Model.cs      each tool's own static model and its <Key>Checks — yours
```

**Yours:** `<Tool>.cs` (and any new view files here) and `Model/<Tool>Model.cs` (and any new plain .NET files in
`Model/`). The files are `CellExplorerTool`, `DNATool`, `GeneExpressionTool`, `BiomoleculeTool`, `CrossesTool`,
`CellDivisionTool`, `HardyWeinbergTool`, `BioLabTool`, `EnzymeLabTool`, `EnergyPathwaysTool`, and the models
`CellExplorerModel`, `DNAToolModel`, `GeneExpressionModel`, `BiomoleculeBuilderModel`, `CrossesModel`,
`CellDivisionModel`, `HardyWeinbergModel`, `BioLabModel`, `EnzymeLabModel`, `EnergyPathwaysModel`. Never edit
`BioTools.cs`, `ToolsScreen.cs` or `ToolHost.cs`. Keep `Model/` free of WPF (`System.Windows`), `Simpl.Kit` and
`Simpl.Support` (use `BioVec`, not `Vector3D`): it is what the unit tests reach. Add your tests as
`windows/SimplFluent.Tests/<Tool>Tests.cs` (`BiologyTests.cs` is the foundation's). Check a new `SymbolRegular`: WPF-UI
4.3 does not draw one whose code is above U+FFFF (which is why the Cell Explorer's tile is `GridCircles24`, not
§2.1's `ShapeOrganic24`; the windows README, Icons).

## Registration (done; one line in your file)

Each tool's file registers itself from a module initializer — the placeholder already does:

```csharp
BioTools.Register("gene", (c, state) => new GeneExpressionTool(c, state), GeneExpressionTool.Pages,
    (c, state) => new GeneExpressionCompact(c, state));
```

- `BioTools.Register(key, full, pages, quick)`: `full(ToolContext c, BioLaunchState? state)` makes the whole tool;
  `pages` (`BioPage[]`, two or more) show in its header (`BioPages`: PTKit.Choice buttons, squished to symbols when
  tight, the pin then a symbol alone); `quick(c, state)` makes the pin's flyout (null: `BioQuickOpen` — its tile, its
  line, Open). Flyout widths (spec §3.2): gene 420, cross 400, biolab 380, popgen 360, the rest 330
  (`ToolsCenter.FlyoutWidth`); short names Cells, DNA, Genes, Biomolecules, Crosses, Division, Hardy–Weinberg, Lab maths,
  Enzymes, Energy (`ToolsCenter.ShortName`).
- `BioTools.Kinds` (the ten `ToolsScreen.Kind`s: key, name, line, `SymbolRegular`, hex), `BioTools.IsBiology(key)`,
  `BioTools.Kind(key)`, `BioTools.Pages(key)`.
- The quick versions of §3.2 item 3 are `GeneExpressionCompact` (codon lookup), `CrossesCompact` (monohybrid Punnett),
  `HardyWeinbergCompact` (HW from one value), `BioLabCompact` (C₁V₁ and molarity), each `(ToolContext c,
  BioLaunchState? state)`; `c.OpenFull(info, null)` opens the whole tool with what was begun (`["sequence"] = "AUG…"`).
- **Pages:** `BioPage(Id, Title, SymbolRegular Symbol, Help)`. Read the page with `BioNav.Page("cell")`; change it with
  `BioNav.Set("cell", "plant")` (remembered in Prefs `SimplBio.cell.page`); listen to `BioNav.Changed` (the key) on
  `Loaded`, let go on `Unloaded`. Page ids are the Mac's, so a launch's state names the same page on both.
- **Prefs:** `BioPrefs.String/Bool/Double(key, field)` and `BioPrefs.Set(key, field, value)` → Prefs
  `SimplBio.<key>.<field>` (§3.2).
- **State:** keep it in your static model class (`CellExplorerModel` …: it outlives the view being built again; it has
  a `Changed` event and `Tell()` to start from). Views subscribe on `Loaded` and unsubscribe on `Unloaded`.
- **Hand-off:** `BioTools.Open(c, "gene", new() { ["sequence"] = "ATG…" })` closes the sheet or flyout `c` is in and
  opens Gene Expression over the window; it reads it once with `c.TakeInfo("sequence")` when it is made.

## Launch flags (pictures)

`--place tools --tool <key> --tool-state <state>` opens the tool; `state` reaches your view (and only the first time
it is made) as `BioLaunchState`: parts split at `:` (`plant:cutaway`, `helix:style=atoms:seq=ATGC`). A first part
that is one of your page ids shows that page by itself. Read the rest with `state?.Has("cutaway")` and
`state?.Value("style")`. The spec's flags read the same way: `--bio-seq ATGC` is `Value("seq")`, `--bio-step 4` is
`Value("step")`, `--bio-cell plant` is `Value("cell")`, `--bio-cross "AaBb x AaBb"` is `Value("cross")` (any
`--bio-<name> <value>`); `--bio-page <page>` picks the page. `--pin <key> --pin-open <key> --tool-state …` hands the
state to your quick version instead. `--place tools --tools-scroll biology` scrolls the Tools page to the Biology
section; `--bio-check` writes the self-check to simpl.log (`bio-check <name> <value> <OK|FAIL>`, then
`bio-check summary n/m OK`); `--place tools --bio-demo` (a development run) opens the foundation's demo. The picture
waits while a 3D scene is being made (`Shot.Began/Ended` in `BioSceneView.Show`) and after each step
(`Shot.Moved`). Add your pictures to `.github/workflows/windows-fluent.yml` (in a commit of its own), e.g.
`Shot 'bio-cell-plant' '1366x778' 50 ($mock + @('--appearance', 'light', '--place', 'tools', '--tool', 'cell', '--tool-state', 'plant:cutaway'))`.

## Data (Model, any thread)

- `BioData.Residues` (20), `.Nucleotides` (DA DC DG DT A C G U ATP), `.Sugars` (GLC BGC GAL FRU BDR MAL CBI SUC LAT),
  `.Lipids` (GOL PLM STE OLA TGL POV CLR); `BioData.Molecule("POV")`, `BioData.Residue("K")`/`("Lys")`.
  `BioMolecule`: `Code`, `Title`, `Formula`, `Mass`, `Atoms` (`BioAtomRecord`: Name, Element, Pos Å, Role
  `Always/NTerminus/CTerminus/Ph7Only`, Comment), `Bonds` (A, B, Order), `Aromatic`, `Rotamer`, `Chis` (`BioChi`:
  Index, Atoms, Degrees), `BetaRotamer`, `Alternatives` (`BioRotamer`: Name, Chis), `Ring`, `Ph7` (Remove, Add,
  Charges), `Notes`; `AtomsOf(ph7:, hydrogens:, nTerminus:, cTerminus:)`, `ChiValue(i)`, `SettingChi(i, degrees)`,
  `Model(…) → BioMoleculeModel`.
- `BioMoleculeModel`: `Atoms` (`Atom`: Id = index, Element, Pos, Name, Tag) and `Bonds` (A, B, Order); `Add`,
  `AddBond`, `Append`, `Moved(offset)`, `Mirrored()` (D-form), `Bounds`, `OddBonds()`.
- `BioData.Bdna` (Backbone, Bases by letter: `HelixAtom` Name X R Theta), `.BdnaTests`, `.BasePairs2D["AT"|"GC"]`
  (`PairAtom`), `.Ubiquitin` (`TraceResidue`: Chain, I, Aa, Ss, Ca, Centroid), `.Hemoglobin` (Residues, Hemes: Fe + 4 N),
  `.Trna` (Nucleotides with P and Modified, Pairs (21), Elements, Modified labels), `BioData.TrnaSequence`,
  `.PeptideTestText`, `.SheetTransformText`.
- `BioGenes.Teaching` (`Coding`, `Parts`, `Tata`, `StartSite`, `StartCodon`, `StopCodon`, `PolyASignal`, `Template`,
  `PreMRNA`, `MatureMRNA`, `Cds`, `Protein`, `UnsplicedProtein`, `Mutations` (Name, Cds, Protein, Kind)),
  `BioGenes.HbbCDS`, `.HbbProtein`, `.Sickle` (Position 20, From 'A', To 'T', ProteinStart).
- `BioCode.Entries`, `AminoAcid("AUG"/"ATG")` (`char?`; X for N), `Codons('L')`, `IsStart`, `IsStop`, `StopNames`,
  `Anticodon3To5`, `Anticodon5To3`, `Bases` (U C A G). `BioAminoAcids.All` (§6.2 order), `.ByOne`, `.Find("lys")`,
  `PeptideMass`, `NetCharge`, `Gravy`; `BioAminoAcid` (Name, Three, One, Group `BioAAClass`, Lehninger, PKa, Charge,
  ChargeText, Hydropathy, Mass, ResidueMass, Formula, CodonCount, Note, Codons, IsChiral); `BioAAClass.Label()`.

## Sequences (§3.7)

`new BioSequenceInput(text, BioSeqKind.Dna|Rna|Protein, cap: 3000)`: `Letters`, `Sequence` (null unless valid),
`Invalid` (letter, 1-based positions), `Suggestion` ("This looks like RNA. Switch?"), `Truncated`, `Message`.
`BioSeq`: `Pair`, `Complement`, `ReverseComplement`, `Reverse`, `Transcribe(codingStrand)`, `TemplateStrand`, `ToDna`,
`Codons(s, frame)`, `Translate(s, frame: 0…2, reverse:)`, `ProteinFromFirstStart`, `Orfs(s, minCodons:, reverseStrand:)`
→ `BioORF` (Frame ±1…3, 1-based forward Start/End, Start > End on the reverse strand, Codons, Protein, HasStop,
FrameLabel), `Counts`, `GcContent`, `Mass(s, rna:)`, `MeltingTemperature`, `Chargaff`, `Lines(s, size: 10, perLine: 6)`.
`BioUI.SequenceText(sequence, fivePrimeFirst:, coloured:, groupsPerLine:)` draws a strand in groups of ten with numbers
and 5′/3′. (One fix over the Mac: a pasted `5′-…-3′` has its ends taken off before the digits go, so it reads clean.)

## B-DNA (§6.7)

`BioBDNA.Build("ATGC", bothStrands: true) → BioDuplex`: `Atoms` (Strand, Pair, Base, Name, Element, Pos — app frame
X = r·sin θ, Y = x, Z = r·cos θ; strand 1 5′→3′ up), `Bonds`, `HBonds` (Pair, A, B), `Phosphates(strand)` (5′→3′,
for a cartoon's spline), `Index`/`Pos(strand, pair, name)`, `Model` (tagged "strand 1 · A 3"). `BioBDNA.Rise`,
`.Twist`, `.Place`, `.AppFrame`, `.HBondAtoms`.

## Crosses (§5.3, §5.6.2)

`BioCross.Parse("AaBb × aaBb")` → (`Cross`, `Error`); patterns guessed (X^ X-linked recessive, I^A ABO, ^ incomplete,
else complete) and changeable (`cross.Genes[k].Pattern`, `.TraitNames`). `new BioGene('A', pattern, alleles:,
traitNames:)`; `BioInheritance` (Complete, Incomplete, Codominant, Abo, XRecessive, XDominant,
LethalDominantHomozygote, LethalRecessive; `.Label()`, `.IsXLinked()`). `BioCross.Gametes(genotype)`, `Gametes1/2`,
`Square`, `Offspring`, `Result(BioSex.Female|Male|null)` → `BioCrossResult` (`Genotypes` rows with Count in the square,
P, Phenotype, Dies, Carrier; `Phenotypes` (born); `GenotypicRatio(Text)`, `BornGenotypicRatio`, `PhenotypicRatio(Text)`,
`SquareSize`, `BornFraction`), and `Probability(targets)` → (Factors, Product) — the product rule, with `BioTarget`
(HomDominant "AA", Heterozygous "Aa", HomRecessive "aa", Dominant "A_", Any). Exact `BioFrac` throughout (`Text` "3/32",
`PercentText` "9.4%"); `BioRatio.Reduce/Whole/Text`.

### The Genetics Crosses and Hardy–Weinberg tools (built)

- **Crosses** (`CrossesTool.cs`, `CrossesPages.cs`, `CrossesParts.cs`; `Model/CrossesModel.cs`, `Model/PedigreeModel.cs`,
  the Mac's models line for line): `CrossSetup` is the Cross page's cross (one to three genes, one X-linked at most;
  `Genotypes(k, parent)` lists only what a parent can be — never a lethal homozygote; `SetPattern`, `SetLetter`,
  `ApplyExample` (§5.3.1's `CrossExamples`), `TestCross`, `Parse`, `Encoded` for `SimplBio.cross.setup`).
  `CrossPunnett`, `CrossForkedLine`, `CrossProbability` (chips, the product and sum rules, at least one / exactly k of
  n), `ChiSquareWork` (the null hypothesis, AP's conclusion), `Pedigree` and `PedigreeChecker` (four modes, the people
  who rule each out). `CrossesModel` keeps the pages' state. Hand-offs it takes: `["cross"] = "AaBb × aaBb"`, and
  `["chi"] = "round:3:5474;wrinkled:1:1850"` (name:ratio:observed) for the Chi-square page.
- **Hardy–Weinberg** (`HardyWeinbergTool.cs`, `HardyWeinbergPages.cs`; `Model/HardyWeinbergModel.cs`):
  `HardyWeinberg.Calculate(given, value)` / `Calculate(counts)` (each step as text), `.Test(counts, df)` (df 1 by
  default), `HWSimulation.Step/Run` in §5.4.3's order — replicate r, from 0, draws from SplitMix64 seed S + r, 2N
  `Uniform() < p₃` draws a generation — so a seed gives the Mac's populations; run off the UI thread by `HWRunner`.
  `HWFormat.Fixed` writes numbers as printf does (a tie rounded to even), so the working reads as on the Mac. Hand-offs:
  `["given"] = "q2", ["value"] = "0.16"`, `["counts"] = "50,30,20"`.
- **Launch states:** `--tool cross --tool-state cross:forked` (`square`, `genotypes`, `why`, `testcross`, `cross=…` or
  `--bio-cross "AaBb x AaBb"`); `probability:prob=AaBbCc x AaBbCc:target=aa,B_,Cc:children=3`; `chi:fromcross` or
  `chi:obs=315,108,101,32`; `pedigree:example=xr:mode=xd:select=2`. `--tool popgen --tool-state calculate:q2=0.16`
  (any `HWGiven` key, or `counts=50,30,20`); `test:counts=50,30,20:df=2`;
  `simulate:n=10:p0=0.5:seed=7:reps=5:gens=100:preset=recessiveLethal:F=0.25:u=…:v=…:m=…:pm=…:hover=40` (`infinite`
  for no drift). The pins: `--pin-open cross --tool-state "cross=Aa x aa"`, `--pin-open popgen --tool-state q2=0.16`.

## Chi-square and randomness (§5.3.4, §5.6.3–4)

`BioChiSquare.Test(observed, expected, df:)` → `Chi2`, `Terms`, `Df`, `Critical` (p = 0.05), `P`, `Reject`,
`Conclusion` (AP wording: "reject" or "fail to reject", never "accept"); `Expected(ratio, total)`, `PValue(chi2, df)`,
`Critical(df, p)`, `CriticalComputed`, `Table`, `TableP`. ln Γ is Lanczos g = 7, n = 9, as on the Mac: p-values agree to
12 decimals. `new BioRandom(seed)` (SplitMix64): `Next()`, `Uniform()`, `Uniform(lo, hi)`, `Int(below)`,
`UnitVector()` (z = 2u−1, then φ = 2πu), `InBall()` (x, y, z each 2u−1 until inside), `Rotation()` (UnitVector, then
360u) — the Mac's draw orders, so a seeded cell is the same picture on both. `BioBlob(seed, amplitude, terms: 4)`:
per term d (UnitVector), f = U(1.5, 3.5), φ = U(0, 2π), weight U(0.5, 1), weights scaled to the amplitude.

## 3D (§3.4, §3.5)

- **Meshes** (`BioMesh`: float `Positions`/`Normals` as `System.Numerics.Vector3`, int `Indices`, CCW outward):
  `BioShapes.Sphere`, `Icosphere` (1: 80 triangles), `Cylinder(from, to, radius)`, `Capsule`, `Spheroid`,
  `Superellipsoid`, `RoundedBox`, `Lens`, `Torus`, `ShellPatch` (RER sheets), and `Radial(radius, rings, segments, cut:,
  inner:)` for any star-shaped solid or shell — `cut: BioCut.Octant` (x, y, z > 0 removed) or `.Half` (z > 0), capped
  where cut; radius functions (`BioRadius`) `BioShapes.SphereRadius`, `EllipsoidRadius`, `CapsuleRadius`,
  `RoundedBoxRadius`, `LensRadius`, `SuperellipsoidRadius`, `ByDistance(sdf)`, `Blob(base, BioBlob)`.
  `BioPath.CatmullRom`, `Frames` (rotation-minimising), `Tube(points, radius, radii:, sides:, closed:)`,
  `Ribbon(points, width, thickness, guides:, arrow:, elliptical:)`. `BioFrame` places a mesh (`Along(dir, origin)`,
  `Rotation(axis, degrees)`); `mesh.Transformed/Translated/Scaled/Flipped/Copy`. `BioMoleculeMeshes.Dashes` for H-bonds.
- **A scene** is values: `new BioSceneContent { Items = [new BioSceneItem(id, mesh, material, order)], Labels =
  [new BioLabel3D(id, text, anchor, normal:, area:)], Frame = sphere? }`. Ids are what a press picks (`atom:12`,
  `residue:7`, `organelle:mito:3`); selecting `organelle:mito` selects every `organelle:mito:…`.
  `new BioMaterial(hex, Alpha:, Roughness:, Emission:, Unlit:, DoubleSided:)` or `new BioMaterial(pair, dark)` —
  alpha < 1 is drawn after the opaque, from both sides (`Order` stacks shells: outer last; Viewport3D sorts nothing).
  Molecules: `BioMoleculeScene.Items(model, style, hydrogens:, color:)` (MBChem CPK colours and the molecule builder's
  radii; UV spheres up to 300 atoms, icospheres above); `BioMoleculeScene.Symbols(model, style)` the atom letters as
  billboards (on the UI thread).
- **The view:** `var scene = new BioSceneView("3D model of …")`, then `scene.Show(key, () => MakeScene(copies…),
  frameKey:, billboards:)`. The build runs off the UI thread — capture copies, never a view — and again only when `key`
  changes (put the page, the options and light or dark in it). The camera frames the content (40°, 1.15·R/sin 20°) on
  the first scene and when `frameKey` is new; `ResetView()` puts it back; `ZoomTo(id)` frames an object. Set
  `Selection` (the selection glows, the rest dims to 35%: `DimOthers`), `ShowLabels`, `Turning` (auto-turn, 30 s a turn;
  never with animations off), `PickSelects`, `Accessibility`; listen to `Picked`, `DoubleClicked`, `HoverChanged`.
  Click picks (the nearest opaque object, through translucent membranes); hover outlines in the accent with a hand;
  drag turns, right-drag pans, the wheel zooms; ← → ↑ ↓ turn 15°, + − zoom, Escape deselects; double-click zooms to an
  object (or resets on empty space). Labels with leader lines (§3.5: two columns 22 apart, ≤ 14 with "+N more", 120 ms
  fades, pressable, hover outlines their object) come from `Labels`; give a convex shell's label a `Normal`, else a ray
  decides what hides it. It draws frame by frame only while something moves. Keep a scene under 300k triangles
  (§3.4: Viewport3D draws on one thread; icospheres for the many small spheres). `BioSceneCard.Make(bar, scene, hint)` and
  `BioSceneCard.Buttons(scene)` (Turn / Reset view) give the chemistry builder's card.
- WPF side (`BioScene.cs`): `BioWpf.Mesh(mesh)` (frozen `MeshGeometry3D`), `BioWpf.Material(material, fade)`,
  `BioWpf.Batches(items)` (one mesh per material and order, with `BioMeshBatch.ObjectAtVertex/ObjectAtTriangle`),
  `BioWpf.P/V/B` conversions, `BioBillboards.Text(s, size, colour, glow)`, `BioBillboard(model, anchor, lift, id)`.

## Step-through (§3.6)

Keep the controller in your model: `var steps = new BioStepController(new[] { new BioStep(0, "Initiation", "…", Group:
1), … })`. Put `new BioStepControls(steps, new Dictionary<int, string> { [1] = "Interphase", … })` under the scene (the
explanation above a capsule of ⏮ ◀ ▶/⏸ ▶ ⏭, the dots, "Step 4 of 12 · Elongation", the speed menu; it takes ← →
Space Home End 1–9 when it has the focus) and `BioStepKeys.Attach(scene, steps)` on the scene (Space Home End 1–9 there:
a 3D view keeps its own ← → ↑ ↓ for turning; on a 2D drawing ← → step too). Draw with
`var anim = new BioStepAnimator(steps, phase => Draw(phase))` (`Loaded += (_, _) => anim.Start(); Unloaded += (_, _) =>
anim.Stop();`): `phase.From`, `phase.To`, `phase.T` (eased; 600 ms at 1×; back plays in reverse), `phase.Crossfade`
(animations off: both keyframes faded over 200 ms), `phase.Mix(a, b)`. `steps.Go(i)`, `Next`, `Back`, `First`, `Last`,
`JumpToGroup`, `Play`, `Pause`, `Toggle`, `Speed`, `SetSteps(steps, keepIndex:)`, `Changed`; Narrator hears each step
(`Announce`). The controller takes a clock (`clock: () => seconds`), so your tests can drive it.

## Colours and pieces (§3.8, §9)

`BioPalette.Base('A')`, `.AminoAcid(BioAAClass.Acidic)`, `.AminoAcid('E')`, `.Secondary('H')`, `.Strand1`, `.Rna`,
`.Maternal`, `.HBond` … are `BioHexPair`s (`Light`, `Dark`, `HexFor(dark)`): `.ToColor()` (as the app is drawn now),
`.ToColor(dark)`, `.ToBrush()`; `BioColors.Base(char)`, `.AminoAcid(char)`, `.Of(BioAAClass)`, `.Tool(key)`,
`.Hex(color)`. Never colour alone: show the letter or a `BioUI.Legend`. Pieces: `BioUI.Work(left, centre, right)` (the
chemistry builders' three columns), `BioUI.Heading`, `BioUI.Caption`, `BioUI.Card(title, swatch, subtitle, rows…)`,
`BioUI.FactRow(label, text, tint)`, `BioUI.Facts(rows)`, `BioUI.Legend(items)`, `BioUI.SequenceText`, `BioQuickOpen`,
`BioToolPlaceholder`.

## Self-check (§10.1)

`BioSelfCheck.Run()` covers the shared data and algorithms (the genetic code, the templates' frame, chirality, bonds
and χ angles, every molecule's formula, the B-DNA test vectors, SplitMix64, chi-square, every §5.3.2 cross, the genes,
proteins and tRNA, the geometry, the label layout and the step-through): 111 lines, all OK, the Mac's 110 among them
line for line. Add yours in `Model/<Tool>Model.cs`, in the partial method already there:
`static partial void CellChecks(BioCheckReport r) { r.Near("cell.count", …); }` with `r.Check(name, value, ok)`,
`r.Near(name, value, expected, tolerance, digits)`, `r.Same(name, value, expected)`. Names have no spaces. The unit
test `BiologyTests.TheSelfCheckIsAllOK` (CI's Unit tests job) fails on any FAIL; `--bio-check` writes the lines to the
log.
