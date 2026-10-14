# The biology toolkit on Windows

Ten tools under a **Biology** heading on the Tools page (spec §2): `cell` Cell Explorer, `dna` DNA & RNA, `gene` Gene
Expression, `biomol` Biomolecule Builder, `cross` Genetics Crosses, `divide` Cell Division, `popgen` Hardy–Weinberg,
`biolab` Bio Lab Calculator, `enzyme` Enzyme Lab, `energy` Photosynthesis & Respiration. Each is pinnable like any
tool. All ten are built. This page is for the people working on them: what the shared foundation gives, which files
are each tool's, and what each tool's pages, launch words and hand-offs are (from "Cell Explorer" on). It is the
Windows twin of the Mac's `docs/mac-biology.md`: the same design, the same data, the same algorithms (the self-check's
shared lines come out identical on both, value for value), C# names in PascalCase.

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
  `pages` (`BioPage[]`, two or more) show in its header (`BioPages`: PTKit.Choice buttons; when tight the page shown
  keeps its name and the others are symbols, then all are symbols, each named in its tooltip; the pin then a symbol
  alone); `quick(c, state)` makes the pin's flyout (null: `BioQuickOpen` — its tile, its line, Open); every flyout's
  way to its whole tool is `BioUI.OpenButton(text, open, tip)`, one accent button across it. Flyout widths (spec §3.2): gene 420, cross 400, biolab 380, popgen 360, the rest 330
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
- **Hand-off:** `BioTools.Open(c, "gene", new() { ["sequence"] = "ATG…" })` turns this tool's popup into Gene Expression
  in place (`ToolsCenter.PopUp`) — or, from a pin's flyout, closes it and opens Gene Expression's popup over the window;
  it reads it once with `c.TakeInfo("sequence")` when it is made.

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
  who rule each out). A typed `A^Y a × A^Y a` is read as the yellow mouse's lethal allele (born 2 yellow : 1
  non-yellow, as the Yellow mice example; on Windows only for now — the Mac's parser reads it as complete dominance).
  `CrossesModel` keeps the pages' state. Hand-offs it takes: `["cross"] = "AaBb × aaBb"`, and
  `["chi"] = "round:3:5474;wrinkled:1:1850"` (name:ratio:observed) for the Chi-square page.
- **Hardy–Weinberg** (`HardyWeinbergTool.cs`, `HardyWeinbergPages.cs`; `Model/HardyWeinbergModel.cs`):
  `HardyWeinberg.Calculate(given, value)` / `Calculate(counts)` (each step as text), `.Test(counts, df)` (df 1 by
  default), `HWSimulation.Step/Run` in §5.4.3's order — replicate r, from 0, draws from SplitMix64 seed S + r, 2N
  `Uniform() < p₃` draws a generation — so a seed gives the Mac's populations; run by `HWRunner`: an infinite
  population at once, as its settings change (well under a millisecond), finite ones off the UI thread; each run's
  start, end or cancelling in the log, and the Simulate page asks for a run whenever it comes on screen without one.
  With several populations its genotype bars carry their frequencies in their tooltips and a colour key goes under
  them (under the last bar they read as every population's).
  `HWFormat.Fixed` writes numbers as printf's `%.Nf` does, rounded half-up as every bio tool rounds (Numbers, below), so
  the working reads as on the Mac. Hand-offs:
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
  [new BioLabel3D(id, text, anchor, normal:, area:, Always:)], Frame = sphere? }`. Ids are what a press picks (`atom:12`,
  `residue:7`, `organelle:mito:3`); selecting `organelle:mito` selects every `organelle:mito:…`.
  `new BioMaterial(hex, Alpha:, Roughness:, Emission:, Unlit:, DoubleSided:)` or `new BioMaterial(pair, dark)` —
  alpha < 1 is drawn after the opaque, from both sides (`Order` stacks shells: outer last; Viewport3D sorts nothing).
  Molecules: `BioMoleculeScene.Items(model, style, hydrogens:, color:)` (MBChem CPK colours and the molecule builder's
  radii; UV spheres up to 300 atoms, icospheres above); `BioMoleculeScene.Symbols(model, style)` the atom letters as
  billboards (on the UI thread).
- **The view:** `var scene = new BioSceneView("3D model of …")`, then `scene.Show(key, () => MakeScene(copies…),
  frameKey:, billboards:, marks:)`. The build runs off the UI thread — capture copies, never a view — and again only when `key`
  changes (put the page, the options and light or dark in it); asked again for the scene shown while another is being
  made, it lets the other go. The camera frames the content on the first scene and when `frameKey` is new: 40°, as far
  off as makes the model's own boxes (not its ball) fill 85% of its room each way — the view, less a column for the
  labels at each side while they are shown — so a long helix or a wide cell fills its view; it keeps that share as the
  view is resized (a zoom of the student's own kept as the same share of it); `ResetView()` frames it again;
  `ZoomTo(id)` frames an object. Set
  `Selection` (the selection glows, the rest dims to 35%: `DimOthers`), `ShowLabels`, `Turning` (auto-turn, 30 s a turn;
  never with animations off), `PickSelects`, `Accessibility`; listen to `Picked`, `DoubleClicked`, `HoverChanged`, and
  `MorePressed` ("+N more" is a link to the parts list while it has a handler; Cell Explorer brings its list into view).
  Click picks (the nearest opaque object, through translucent membranes); hover outlines in the accent with a hand —
  only on the pointer's own movement (`ScreenPointer`: a view opening, or a scene or a label drawn, under a still
  pointer points at nothing; in 0.3.0 the part under it showed as pointed at from the first frame), and never with a
  see-through object's hull, which tinted everything inside a membrane in the accent;
  drag turns, right-drag pans, the wheel zooms; ← → ↑ ↓ turn 15°, + − zoom, Escape deselects; double-click zooms to an
  object (or resets on empty space). Labels with leader lines (§3.5: two columns 22 apart, 24 out from the model's box
  on the view — beside the model, not at the view's edges — kept even, a column holding as many as the view's height
  has room for, one with more giving its labels nearest the model's middle to the other while that has room, before
  "+N more", 120 ms fades, pressable, hover outlines their object) come from `Labels`; give a
  convex shell's label a `Normal`, else a ray
  decides what hides it; `Always` shows it whatever covers it (a strand's ends: both ends of both strands are always
  named). Cell Explorer's nucleus, cut open, is anchored on its envelope's ring at the cut face that turns most to the
  camera, with that face's normal (at the back of the envelope the nucleolus and the chromatin hid it). A label too wide for the room beside its column is moved in on its own (the Mac moves the whole
  column in for its widest, which in this app's narrower view put the short labels over the model), and an anchor past
  a label's far edge is led to that edge. Given the model's box on the view (its items' boxes, projected), a label
  that would still hide its own anchor, or reach more than 40 into a model narrower than 70% of the view (over half its
  width, for a short label), goes to a row just above the model (an anchor in its upper half) or below it; a row's
  labels are packed side by side in their anchors' order, their leaders straight up or down and never through a capsule
  in a row nearer the model; a label that fits no row stays in its column (and a row's label packed over it goes back to
  its own). It draws frame by frame only while something moves. Keep a scene under 300k triangles (§3.4: Viewport3D
  draws on one thread; icospheres for the many small spheres). `BioSceneCard.Make(bar, scene, hint)` and `BioSceneCard.Buttons(scene)` (Turn / Reset view) give the
  chemistry builder's card.
- WPF side (`BioScene.cs`): `BioWpf.Mesh(mesh)` (frozen `MeshGeometry3D`), `BioWpf.Material(material, fade)`,
  `BioWpf.Batches(items)` (one mesh per material and order, with `BioMeshBatch.ObjectAtVertex/ObjectAtTriangle`),
  `BioWpf.P/V/B` conversions, `BioBillboards.Text(s, size, colour, glow)`, `BioBillboard(model, anchor, lift, id)`
  (drawn in the scene, so the model can hide it), and `BioMark(text, anchor, size, atom, atomRadius, colour, charge)`:
  a few letters in 2D over the whole model, 11–17 px, an atom's number set just outside its atom's disc (DNA & RNA's
  "5′" and "3′" and the carbons' numbers: `DnaLetter(…, OnTop: true)`, `DnaWpf.Marks`; the Biomolecule Builder's ring
  and sugar carbons' numbers: `BiomolMark(…, OnTop: true)`, which `BiomolStage` hands the view as marks), or a charge's
  sign in a disc of the ground 16–22 across, edged in the sign's colour (`BioChargeBadge`: a bare blue + on the blue
  amino group could not be seen), on its atom's edge at the upper right — or the first other corner clear of the
  labels' capsules — over the labels (a label too wide for its room is moved in over the model, and hid the carboxyl's
  −) (a free amino acid's: drawn in the
  scene, lysine's side-chain NH₃⁺ sign was hidden behind the carbon next to it; a peptide's many stay in the scene,
  so the chain hides those at its back).

## Step-through (§3.6)

Keep the controller in your model: `var steps = new BioStepController(new[] { new BioStep(0, "Initiation", "…", Group:
1), … })`. Put `new BioStepControls(steps, new Dictionary<int, string> { [1] = "Interphase", … })` under the scene (the
explanation above a capsule of ⏮ ◀ ▶/⏸ ▶ ⏭, the dots, "Step 4 of 12 · Elongation", the speed menu — under a narrow
scene the dots give way to a shorter bar of progress, then to nothing, then the caption goes onto two lines, broken
after a " · ", so it reads whole; it takes ← →
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
chemistry builders' layout, `ChemLayout`, decided on the width it is given — a tool's popup's: the three columns side
by side from 1180 wide; from 840 — the popup on a 1280 or 1366 window — the drawing beside one column, the controls
over the facts, each scrolling in its own share of it (`ChemSide`: as tall as each needs while they fit, else 55% and
45%); narrower — the popup on a 1024 window, or a narrow one — the drawing across the top, as tall as leaves 100 of the
page for the two columns' headings and first controls under it (240 to 560: room beside an animal cell for its sixteen
labels). The controls and the facts are always in
sight: 0.3.0 gave the drawing the whole page under 1180, and they sat out of sight below it. A scene's
`LabelKeepClear` keeps its labels off a corner a page lays something over — Cell Explorer's scale bar),
`BioUI.Heading`, `BioUI.Caption`, `BioUI.Card(title, swatch, subtitle, rows…)`,
`BioUI.FactRow(label, text, tint)`, `BioUI.Facts(rows)`, `BioUI.Legend(items)`, `BioUI.SequenceText`, `BioUI.OpenButton`,
`BioQuickOpen`, `BioToolPlaceholder`.

## Self-check (§10.1)

`BioSelfCheck.Run()` covers the shared data and algorithms (the genetic code, the templates' frame, chirality, bonds
and χ angles, every molecule's formula, the B-DNA test vectors, SplitMix64, chi-square, every §5.3.2 cross, the genes,
proteins and tRNA, the geometry, the label layout and the step-through): the foundation's 111 lines, the Mac's 110
among them line for line, and each tool's own (`cell.…`, `dna.…`, `gx.…`, `biomol.…`, `cross.…`, `hw.…`, `divide.…`,
`biolab.…`, `enzyme.…`, `energy.…`): 450 lines, all OK. Add yours in `Model/<Tool>Model.cs`, in the partial method already there:
`static partial void CellChecks(BioCheckReport r) { r.Near("cell.count", …); }` with `r.Check(name, value, ok)`,
`r.Near(name, value, expected, tolerance, digits)`, `r.Same(name, value, expected)`. Names have no spaces. The unit
test `BiologyTests.TheSelfCheckIsAllOK` (CI's Unit tests job) fails on any FAIL; `--bio-check` writes the lines to the
log.

## Cell Explorer (`cell`)

The Mac's model ported draw for draw, so a seeded cell is the same picture on both. `Model/`: `CellExplorerText.cs`
(§5.1.1–5.1.4 copied out of the specification by a script), `CellExplorerModel.cs` (the parts and their cards, the
Compare rows, About, the quiz, the tool's state), `CellExplorerGeometry.cs` (§5.1.5's recipes), `CellExplorerBuild.cs`
and `CellExplorerCells.cs` (each cell placed from its seed; `CEBuildCache`), `CellExplorerScene.cs` (a built cell as a
`BioSceneContent`: hidden parts, isolate, look inside, explode; a part of more than 40 instances — ribosomes — drawn as
one item a material, since the view draws each selected item as a model of its own), `CellMembraneModel.cs` (§4.1.1) and
`CellExplorerChecks.cs` (its `bio-check cell.…` lines). Views: `CellExplorerTool.cs` (pages, launch state, the shared
pieces), `CellExplorerCellPage.cs`, `CellExplorerMembranePage.cs`, `CellExplorerComparePage.cs`. Windows draws a little
lighter than the Mac to stay near 250k triangles: tetrahedra for true scale's 20,000 ribosomes, four-sided lipid tails.
It reads two things of `BioSceneView` through its children (`BioSceneReach`): the camera, for the scale bar, and the
turning world, to add a layer for the membrane's water and carrier. Launch states, in the Mac's words: `animal`,
`plant`, `bacterium`, `membrane`, `compare`, then `nocut`, `true`, `nolabels`, `explode=0.6`, `gram+`, `show=…`,
`hide=…`, `select=mito`, `isolate=mito`, `inside=chloroplast`, `quiz=find|name`; on the membrane `osmosis`, `still`,
`nolabels`, `popc`, `select=integral:channel`. Each cell's hidden parts are kept (`SimplBio.cell.hidden.animal`).
"Why are cells small?" hands the Bio Lab Calculator the cell as both apps word it (`savShape` sphere, box or cylinder,
`savSize` its sizes across, `savUnit` µm, `from`; Between the tools, below), and "See a real phospholipid" hands the
Biomolecule Builder `molecule` = POV. A part chosen from the parts list while another is isolated is isolated in its
place, as on the Mac. Two differences from the Mac: it draws the thousands of ribosomes, pores, actin and fimbriae in
spatial clusters, Windows as one item a material (above) with the part's labelled instance on its own for Zoom to; and
a wrong answer in Find shakes the question here, where the Mac wobbles the part pressed in 3D.

## Gene Expression (`gene`)

Pages `express` (transcribe & translate), `codons`, `frames`, `mutations`. `Model/GeneExpressionModel.cs` (the tool's
state, the codon table and wheel), `GeneExpressionProcess.cs` (§4.3's process one nucleotide a press: transcription,
cap, tail, splicing — the codon split across the exon junction — export, then one codon a press: the E/P/A sites,
anticodons 3′→5′, a release factor), `GeneExpressionMutations.cs` (a typed `c.20A>T` or a pressed base, the seven
kinds, the original and the mutant lined up base for base). Views: `GeneExpressionTool.cs` (pages, launch, the pin's
codon lookup `GeneExpressionCompact`), `GeneExpressionExpressPage.cs`, `GeneExpressionScene.cs` and
`GeneExpressionDraw.cs` (the four lanes), `GeneExpressionCodonsPage.cs`, `GeneExpressionFramesPage.cs` (only the visible
part of 3,000 nt is drawn), `GeneExpressionMutationsPage.cs`. Launch: `express:step=40`, `express:prokaryote:step=45`,
`codons:find=Lys`, `codons:wheel:find=Leu`, `frames:seq=…`, `mutations:sickle`, `mutations:mut=c.6_7insA`; the pin
`--pin-open gene --tool-state codon=UGG`. Takes `sequence`, `codon`, `mutation` (= `sickle`) and `page`; sends a finished
protein (`protein`, page `proteins`) or an amino acid (`molecule`, page `amino`) to the Biomolecule Builder.

## Biomolecule Builder (`biomol`)

Pages `amino`, `proteins`, `nucleic`, `carbs`, `lipids`. `Model/BiomoleculeBuilderModel.cs` (state, input, its
`biomol.…` checks), `BiomolPeptide.cs` (a typed sequence built as a helix, strand, coil or the rarer shapes, side
chains turned apart), `BiomolProteins.cs` (ubiquitin's and haemoglobin's four levels, denature and refold),
`BiomolMolecules.cs` and `BiomolScenes.cs` (nucleotides joined into a strand, sugars and their glycosidic bonds,
cellulose and starch, fats, phospholipids, cholesterol). A sugar's or a nucleotide's carbons are numbered over the
whole model, each just outside its atom (in the scene the H or O in front hid them). A charge's sign sits on its atom,
at the atom's upper right on the view as a charge is written (N⁺, O⁻), whichever way the model is turned (set out from
the molecule's centre in the scene, a sign sat beside a neighbouring H or out in the open): a free amino acid's over
the whole model, just outside the atom's disc (in the scene, lysine's side-chain NH₃⁺ was hidden behind the carbon next
to it), a peptide's in front of its atom in the scene (`BioBillboard`'s shift), so the chain hides those at its back.
Haemoglobin's chains are each
named at the residue nearest a third of the way along that the first view (down z) shows 12 Å clear of every heme, on
the chain's near side, so no chain's leader ends on a heme. Views: `BiomoleculeTool.cs`, `BiomolStage.cs` (an animation
layer added to the shared 3D view), `BiomolDrawings.cs`, `BiomolAminoPage.cs`, `BiomolProteinPage.cs`,
`BiomolMoleculePages.cs`. Every page opens its molecule in the Molecule Builder (`MoleculeModel.Shared`, atom ids from
1,000,000). The built peptide is shown laid out as the Mac lays it out (`BiomoleculeBuilderModel.ShownPeptide`): its CA
trace's longest axis along x, N-terminus at the left, the next axis up, chosen again only when the camera frames it
afresh (it grew out of the ball last framed, or shrank well inside it), so a helix is seen from its side. Launch:
`amino:aa=W:colour=part`, `proteins:hbonds:ribbon=over`, `proteins:ss=helix`, `proteins:sheet`,
`proteins:level=tertiary:core`, `proteins:level=quaternary`, `nucleic:strand:seq=ATGCGT:complement`, `carbs:anomers`,
`carbs:di=SUC:split`, `carbs:poly=cellulose`, `lipids:lipid=compare` (the Mac's words, a few aliases beside them).
Takes `sequence`, `protein` and `molecule`; sends `mutation` = `sickle` (page `mutations`) to Gene Expression, `sequence`
(page `sequence`) or a `nucleotide` (page `nucleotides`) to DNA & RNA, and page `membrane` to Cell Explorer.

## DNA & RNA (`dna`)

Pages `helix`, `nucleotides`, `sequence`, `replication`, `rna`. `Model/DNAToolModel.cs` (state, styles),
`DnaHelix.cs` (right-handed B-DNA, 10.5 bp a turn, cartoon, atoms or space-filling; ends, grooves, measures,
H-bonds, unzip), `DnaNucleotides.cs` (the nine nucleotides, the A·T/A·U and G·C pairs drawn flat), `DnaReplication.cs`
(the fork's nine steps, each naming its enzyme), `DnaRna.cs` (the mRNA strip, the tRNA cloverleaf and its L-shape in
3D, ribosomes), `DnaChecks.cs`. Views: `DNATool.cs` (pages, launch, hand-off, `SimplBio.dna.<field>`), one file a
page (`DNAHelixPage.cs` … `DNARnaPage.cs`). Launch, in the Mac's words with this app's first ones beside them:
`helix:style=atoms|space|cartoon:grooves:measure:ends|noends:nohbonds:unzip=0.5:pair=10:seq=…`;
`nucleotides:mol=DT` (or `nt=`), `pairs` (or `bases`), `model`, `au`, `numbers`, `compare`, `parts`/`noparts` (or
`cpk`), `part=sugar`; `replication:step=6`; `rna:view=cloverleaf|lshape|mrna|ribosomes` (or `trna`, `trna3d`,
`mrna`, `ribosome`), `modified`, `base=34`, `arm=anticodon`. Takes `sequence` (shown on the Sequence page, RNA letters
written as DNA), `nucleotide` (the Nucleotides page; `molecule` read as the same) and `page` (Cell Division's "What
happens in S phase" opens
`replication`). Base letters in 3D are always shown (the shared view does not say how near the camera is). The
Replication page (`DnaTwoColumns`) puts the fork beside its cards from 1000 wide (beside a column on a 1366 window its
letters came out 7 px tall); narrower, the fork and its controls are as tall as leaves 90 of the page for the step's
card's title under them (240 to 640), so its words stay readable and the card is in sight. The fork is fitted by what
it draws (y 36 to 436 of its units: `DnaDrawing.DesignTop`), not the 470 it was drawn in, whose empty bands above and
below made it a sixth smaller in a tool's popup.

## Cell Division (`divide`)

Pages `mitosis`, `meiosis`, `compare`, `cycle`. `Model/CellDivisionModel.cs` (the stages and their counts, §5.2.1–2;
the Compare table, §5.2.3; the cards), `CellDivisionGeometry.cs` and `CellDivisionKeyframes.cs` (each step's cell,
chromosomes, spindle and labels), `CellDivisionPainter.cs` (a frame as primitives). Views: `CellDivisionTool.cs`
(pages, the counts panel, options, the step-through across the page's foot), `CellDivisionCanvas.cs` (the 2D drawing,
600 ms a step or a crossfade), `CellCycleView.cs` (the wheel to scale — G1 11 h, S 8 h, G2 4 h, M 1 h — G0, the three
checkpoints, cyclins, a cancer view). The Mac's model line for line: its frames, counts, labels' anchors and badges
compare equal for eight option sets (its plate spacing, prophase I's pairing, telophase's angles and interphase's
G1/S/G2 split among them); after nondisjunction, telophase II's label and its first note say which cells are n, n+1
and n−1 rather than "four haploid cells" (the Mac's still say that). Launch: `mitosis:step=6`, `mitosis:plant:step=8`, `meiosis:step=4`, `meiosis:step=11:nd=1`,
`meiosis:step=5:pick=0`, `compare:step=3`, `cycle:gate=g1s:cancer` (`--bio-cell plant`, `--bio-step <n>` too).
"What happens in S phase" opens DNA & RNA on page `replication`.

## Bio Lab Calculator, Enzyme Lab, Photosynthesis & Respiration (`biolab`, `enzyme`, `energy`)

- **Bio Lab** (`BioLabTool.cs`, the pin's `BioLabCompact`; `Model/BioLabModel.cs`, `BioLabCalculators.cs` — the 26
  calculators, the Mac's names — `BioLabNumbers.cs`, `LabSpecText.cs`): pages `solutions`, `microscopy`, `cells`,
  `stats`, `ecology`, `molecules`. Answers follow §5.5's significant-figure rule, as the Mac's do: the least precise
  measured input decides (a centrifuge's 10 cm at 3,000 rpm is "1,000 × g", its value 1,006.2 shown in the working),
  and an answer from exact inputs says so. Launch: `<page>:calc=<id>:mode=<id>:steps:sf=<n>:digits=<n>:<field>=<text>`
  (underscores for spaces); `steps` without `calc=` opens every card's steps. Takes Cell Explorer's cell for surface
  area to volume (`BioLabModel.TakeCells`: `savShape`, `savSize`, `savUnit`, `from`; the earlier `calc` = sav with
  `mode` and `r`/`l`/`w`/`h` too) and the pin's `calc`.
- **Enzyme Lab** (`EnzymeLabTool.cs`, `Model/EnzymeLabModel.cs`, `LabPlot.cs`): launch `none`, `competitive`,
  `noncompetitive`, `denatured`, `enzyme=`, `phpreset=`, `temp=`, `ph=`, `s=` (and `preset=`, `t=`, `i=`). "Fresh
  enzyme" while the sample is still too hot also brings the temperature to that enzyme's best, on both apps.
- **Photosynthesis & Respiration** (`EnergyPathwaysTool.cs`, `Model/EnergyPathwaysModel.cs`): pages `map`,
  `respiration`, `photosynthesis`, `atoms`; launch `map:stage=<id>:respire:night`,
  `respiration:step=<n>:ferment=lactate|ethanol`, `photosynthesis:step=<n>`, `atoms:trace=carbon|oxygen:step=<n>`.

## Between the tools (hand-offs)

The keys are the Mac's, so a hand-off reads the same on both; a tool also reads its own earlier words.

| From → to | Keys (page) |
|---|---|
| Cell Explorer → Bio Lab ("Why are cells small?") | `savShape` sphere, box or cylinder; `savSize` (a sphere's diameter, a box's `l,w,h`, a cylinder's `diameter,length`); `savUnit`; `from` (page `cells`) |
| Cell Explorer → Biomolecule Builder | `molecule` = POV (shown on its Lipids page) |
| Gene Expression → Biomolecule Builder | `protein` (page `proteins`), `molecule` (page `amino`) |
| Biomolecule Builder → Gene Expression | `mutation` = sickle (page `mutations`) |
| DNA & RNA ⇄ Gene Expression, Biomolecule Builder → DNA & RNA | `sequence` |
| Biomolecule Builder → DNA & RNA ("See it in DNA & RNA") | `nucleotide` = DT … (page `nucleotides`; `molecule` read as the same) |
| Cell Division → DNA & RNA | `page` = replication |
| Cell Explorer → Flashcards | `deck` |
| Genetics Crosses → Cell Division ("See it in Cell Division") | `focus` = assortment (Cell Division opens on Meiosis at Metaphase I, where each pair lines up either way round) |
| a pin's quick version → its tool | Crosses `cross`, `chi`; Hardy–Weinberg `given`, `value`, `counts`; Gene Expression `codon`; Bio Lab `calc` |

## Numbers (rounding)

Every number a bio tool shows is rounded one way on both apps: decimal half-up (uifix/conventions.md), the number as
it reads in decimal, so 0.125 is "0.13", 3.425 is "3.43" and 1.25c is "1.3c". `BioRound.Fixed`, `.Upto` and `.Value`
(`Model/BioMath.cs`) do it; .NET's custom formats (`"0.00"`, `{x:0}`) round this way too, but its standard ones
(`"F2"`, `"N2"`) and `Math.Round(x, 2)` do not, so a tool's numbers go through one of the first. The Bio Lab's
significant figures round their decimal digits half-up likewise. No specification test vector is a tie, so every one
still passes.

## Pictures

`.github/workflows/windows-fluent.yml` pictures each tool at 1366×778 (`--place tools --tool <key> --tool-state …`):
`tools-biology` (the section, with `--bio-check`: CI fails unless the log says `bio-check summary n/n OK`), `bio-tool`,
`pin-bio`, `bio-foundation(-dark)`; `bio-cell-animal`, `-card`, `-inside`, `-bacterium`, `-explode`, `-membrane`,
`-compare`; `bio-gene-transcribe`, `-translate`, `-splice`, `-prokaryote`, `-codons`, `-wheel`, `-frames`, `-sickle`,
`-frameshift`, `pin-gene`; `bio-cross-dihybrid`, `-xlinked`, `-forked`, `-why`, `-probability`, `-chi`, `-pedigree`,
`bio-popgen-calculate`, `-test`, `-simulate`, `-lethal`, `pin-cross`, `pin-popgen`; `bio-biomol-amino(-dark)`,
`-peptide`, `-helix-dark`, `-sheet`, `-tertiary`, `-quaternary`, `-strand`, `-anomers`, `-sucrose`, `-cellulose`,
`-lipids`; `bio-dna-helix(-dark)`, `-grooves`, `-atoms`, `-unzip`, `-nucleotide(-dark)`, `-pairs`, `-sequence`,
`-replication(-dark)`, `-okazaki`, `-trna(-dark)`, `-trna3d`, `-mrna`, `-ribosome`; `bio-divide-mitosis`, `-meiosis`,
`-gametes`, `-plant`, `-pick`, `-compare`, `-cycle`, each with `-dark`; `bio-lab-dilution`, `-stats-dark`,
`-cells-dark`, `pin-biolab`, `bio-enzyme-competitive`, `-denatured-dark`, `bio-energy-map`, `-respiration-dark`,
`-photosynthesis`, `-atoms`. Cell Explorer, DNA & RNA and the Biomolecule Builder are also pictured opened plainly —
no state, nothing selected — in dark, at 1024×681 beside the navigation pane and at 780×683 with it folded
(`bio-open-cell-1024`, `-780`, `bio-open-dna-…`, `bio-open-biomol-…`), and Cell Explorer under a still pointer put over
the cell before Simpl starts, alone on the runner (`bio-cell-pointer`: the workflow fails it if the view's log names
anything "pointed at" before the picture). An argument with a space in it (a cross typed `AaBb x AaBb`) is put in quotes by the
workflow's `Shot`. A pin's flyout opened by `--pin-open` stays open (`ToolHost.cs`): three copies of Simpl start at once
in CI, and another's window coming forward closed it before its picture. A picture waits for Hardy–Weinberg's
simulation from the moment it is asked for, not only once it runs (`HWRunner`), and the workflow fails
`bio-popgen-lethal` and `-simulate` unless the Simulate page's last "popgen simulate: shows …" line before the picture
is the finished run.
