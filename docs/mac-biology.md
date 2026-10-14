# The biology toolkit on the Mac

Ten tools under a **Biology** heading on the Tools page (spec §2): `cell` Cell Explorer, `dna` DNA & RNA, `gene` Gene
Expression, `biomol` Biomolecule Builder, `cross` Genetics Crosses, `divide` Cell Division, `popgen` Hardy–Weinberg,
`biolab` Bio Lab Calculator, `enzyme` Enzyme Lab, `energy` Photosynthesis & Respiration. Each is pinnable like any
tool. This page is for the people working on them: what the shared foundation gives, then each tool's files, launch
states and hand-offs, and the pictures CI takes of them. The Windows app has the same ten (on the Windows branch,
`windows/SimplFluent/Tools/Biology/README.md`); where the two must agree — the hand-offs' keys, the rounding, the
seeds — it says so here.

## Files

```
mac/Simpl/Tools/Biology/
  BioTools.swift          the registry (names, lines, symbols, colours, pages, launch state, prefs, the Tools section)
  BioColors.swift         §3.8's colours as SwiftUI and AppKit colours
  BioUI.swift             cards, rows, headings, legend, sequence text, the pin's Open, the placeholder
  BioStepThrough.swift    the step-through controller, its controls and keys (§3.6)
  BioSceneView.swift      the 3D view: camera, picking, hover, selection, keys, labels with leader lines (§3.4, §3.5)
  BioSceneKit.swift       meshes → SceneKit, materials, batching; molecules with the chemistry colours and radii
  <Tool>Tool.swift        each tool's view (a placeholder until built) — yours
  Model/                  Foundation only: compiled and checked by CI's Models job (scripts/dev/swift-checks)
    BioDataText.swift     §5.3.4 and §6 copied out of the specification by a script — never edited by hand
    BioData.swift         those datasets parsed (once, lazily, thread-safe)
    BioMolecule.swift     the molecule-block parser (§3.3), BioMolecule, BioMoleculeModel
    BioGeneticCode.swift  BioCode (§6.1), BioAminoAcids (§6.2)
    BioSequence.swift     BioSequenceInput (cleaning), BioSeq (operations), BioORF (§3.7)
    BioDNA.swift          BioBDNA: B-DNA atom by atom (§6.7)
    BioGenetics.swift     BioCross, BioGene, gametes, ratios, product rule, typed crosses (§5.3, §5.6.2)
    BioMath.swift         BioV (vectors, dihedral, NeRF), BioRandom (SplitMix64), BioFrac, BioRatio, BioChiSquare
    BioMesh.swift         BioMesh and BioShapes / BioPath primitives, the cutaway, BioMeshBatch
    BioMoleculeMeshes.swift  atoms and bonds as scene items
    BioSceneContent.swift BioSceneContent / BioSceneItem / BioMaterial / BioLabel3D (a scene as values)
    BioLabelLayout.swift  §3.5's label layout
    BioSteps.swift        BioStep, BioStepPhase, BioStepTiming (§3.6)
    BioPalette.swift      §3.8's hex pairs
    BioSelfCheck.swift    §10.1's self-check
    <Tool>Model.swift     each tool's own model and its `<key>Checks` — yours
```

A tool's own files are `<Tool>Tool.swift` (and its other view files) and `Model/<Tool>Model.swift` (and its other
Foundation-only model files in `Model/`; CI compiles the whole folder). `BioTools.swift`, `Tools.swift` and
`PinnedTools.swift` are shared by all ten. Keep `Model/` free of AppKit, SwiftUI, SceneKit and `simd` (use `BioV`).
The Xcode project is a synchronized folder: a new file is in the app without a project edit (and do not put a `.md`
or other resource in `mac/Simpl/`).

## Registration (done)

- `ToolKind` has the ten cases; `ToolKind.isBiology`, `BioTools.kinds`, `BioTools.info(k)` (name, short name, line, SF
  Symbol with a fallback checked at run time, tile colour), `BioTools.popoverWidth(k)`.
- Your view: `struct <Tool>Tool: View { var state: BioLaunchState? = nil; static let pages: [BioPage] … }`. The type
  names are fixed: `CellExplorerTool`, `DNATool`, `GeneExpressionTool`, `BiomoleculeTool`, `CrossesTool`,
  `CellDivisionTool`, `HardyWeinbergTool`, `BioLabTool`, `EnzymeLabTool`, `EnergyPathwaysTool`.
- Pin quick versions (§3.2 item 3): `GeneExpressionCompact`, `CrossesCompact`, `HardyWeinbergCompact`, `BioLabCompact`,
  each `init(openFull: ([String: String]) -> Void)` — call `openFull(["sequence": "AUG…"])` to open the whole tool with
  something. Width: gene 420, cross 400, biolab 380, popgen 360. The other six show `BioQuickOpen`.
- **Pages:** list them in `static let pages` (`BioPage(id:title:symbol:help:)`); the header shows them
  (`BioHeaderControls`, glass buttons). The heading (`ToolHeader`) takes the first of four that fits beside the
  tool's whole name — the pages' names and "Pin to Toolbar"; the pages as symbols with the pin's words; symbols alone;
  and in a narrow window the pages' symbols under the name, in place of its line, so the name is never cut short ("Cell
  Explo…", "Bio La…") — and it is never wider than the window (a wider one widened the whole tool under it). Read the page with
  `@ObservedObject private var nav = BioNav.shared` and `nav.page(.cell)`; change it with `nav.set(.cell, "plant")`. It
  is remembered (`SimplBio.cell.page`).
- **Prefs:** `BioPrefs.string/bool/set(.cell, "field", …)` → UserDefaults `SimplBio.<key>.<field>` (§3.2).
- **Hand-off:** `BioTools.open(.gene, info: ["sequence": "ATG…"])` (1.3.79: in the same popup over the window, the
  tool switched in place); the receiving tool reads it once with `ToolsCenter.shared.takeInfo("sequence")` on appear.

## Launch flags (pictures)

`-SimplTool <key> -SimplToolState <state>` opens the tool; `state` reaches your view as `BioLaunchState`:
parts split at `:` (`plant:cutaway`, `helix:style=atoms:seq=ATGC`). A first part that is one of your page ids opens
that page by itself. Read the rest with `state?.has("cutaway")` and `state?.value("style")`. The spec's flags read the
same way: `-SimplBioSeq ATGC` is `value("Seq")`, `-SimplBioStep 4` is `value("Step")`, `-SimplBioPage <page>` picks
the page. Make it part of the view's first state, not a change made on appear: in a store's `init` (DNA & RNA's
`DNAStore`, Gene Expression's `GXStore`), in a page's `init` as `@State`/`@StateObject` initial values (the Cell
Explorer's pages, Photosynthesis & Respiration's map and step-throughs), so every page made for the launch shows it
from its first frame — SwiftUI makes a tool's page more than once as the window settles at its size, and state applied
once on appear, or taken once, never reached the pictures (the grooves, the unzip, a cell at true scale, a step's
number, the codon grid's lookup, the map's stage).
In the screenshot suite those tools also say on the console what the page shows of its launch
(`[Simpl] bio launch: dna helix:grooves: style cartoon, grooves on, …`), and every scene says when it is made
(`[Simpl] bio scene: <key> made in <ms> ms, <n> objects`). `-SimplBioCheck YES` prints the self-check on the console
(`bio-check <name> <value> <OK|FAIL>`, each group's lines as it ends — the whole check takes about a minute in a debug
build — then `bio-check summary n/m OK`). `-SimplToolsScroll biology` scrolls the Tools page to the Biology section.
The pictures are in `scripts/dev/mac-shots.sh` (see **Pictures** below): `bio both 57c-cell-plant 60 cell
plant:select=chloroplast` opens a tool at 1440×920 (the runner's screen holds it to 1024×683) in light and dark. A
picture is taken once the app has settled, and a scene being made (`BioSceneView`) or a cell being built counts as not
settled (`Shot.began()`/`ended()`), so the wait is only the longest a picture may take; it is taken with the app in
front, asked again when another copy of the app has the front (`Shot.settle`). A picture's launch keeps the
tools' settings in a suite of its own that starts empty (`BioPrefs`), so one picture's choices never show in the next.

A scene is made once for its key at a time (`BioSceneMaking`): a page made again while its scene is on its way waits
for that one, rather than making it a second time beside it (the cells' models are kept the same way,
`CEBuildCache`). A debug build makes the cells slowly — the plant cell took 39 s on the runner with four copies of the
app at work — so the cell builder's hot paths are written for it: a rounded box's or a lens's radius along a direction
is found by false position (`BioShapes.byDistance`) or worked out (`lensRadius`) rather than by 48 halvings, a mesh's
normals are moved with their matrix made once (`BioMesh.transformed`), a nuclear envelope's pores are looked up by
height (`CERecipes.envelope`), and a chloroplast is made once and placed sixty times (`CEShapeShelf`). Each gives the
same meshes, to a float's last digit.

## Data (Model, any thread)

- `BioData.residues` (20), `.nucleotides` (DA DC DG DT A C G U ATP), `.sugars` (GLC BGC GAL FRU BDR MAL CBI SUC LAT),
  `.lipids` (GOL PLM STE OLA TGL POV CLR); `BioData.molecule("POV")`, `BioData.residue("K")`/`("Lys")`.
  `BioMolecule`: `code`, `title`, `formula`, `mass`, `atoms` (`BioAtomRecord`: name, element, pos Å, role
  `.always/.nTerminus/.cTerminus/.ph7Only`), `bonds` (`a`, `b`, `order`), `aromatic`, `rotamer`, `chis` (`BioChi`),
  `betaRotamer`, `alternatives` (`BioRotamer`), `ring`, `ph7` (`remove`, `add`, `charges`), `notes`;
  `atoms(ph7:hydrogens:nTerminus:cTerminus:)`, `chiValue(i)`, `settingChi(i, to:)`, `model(…) -> BioMoleculeModel`.
- `BioMoleculeModel`: atoms (`id` = index, `element`, `pos`, `name`, `tag`) and bonds; `add`, `bond`, `append`,
  `moved(by:)`, `mirrored()` (D-form), `bounds`, `oddBonds()`.
- `BioData.bdna` (helix table), `.bdnaTests`, `.basePairs2D["AT"|"GC"]` (`PairAtom`), `.ubiquitin`
  (`TraceResidue`: chain, i, aa, ss, ca, centroid), `.hemoglobin` (`residues`, `hemes`: fe + 4 N),
  `.trna` (`nucleotides` with P and modification code, `pairs` (21), `elements`, `modified`), `BioData.trnaSequence`,
  `.peptideTestText`, `.sheetTransformText`.
- `BioGenes.teaching` (`coding`, `parts`, `tata`, `startSite`, `startCodon`, `stopCodon`, `polyASignal`, `template`,
  `preMRNA`, `matureMRNA`, `cds`, `protein`, `unsplicedProtein`, `mutations`), `BioGenes.hbbCDS`, `.hbbProtein`,
  `.sickle`.
- `BioCode.entries`, `aminoAcid("AUG"/"ATG")` (X for N), `codons(for: "L")`, `isStart`, `isStop`, `stopNames`,
  `anticodon3to5`, `anticodon5to3`, `bases` (U C A G). `BioAminoAcids.all` (§6.2 order), `.byOne`, `.find("lys")`,
  `peptideMass`, `netCharge`, `gravy`; `BioAminoAcid` (`group: BioAAClass`, `pKa`, `charge`, `chargeText`,
  `hydropathy`, `mass`, `residueMass`, `formula`, `codonCount`, `note`, `codons`, `isChiral`).

## Sequences (§3.7)

`BioSequenceInput(text, as: .dna|.rna|.protein, cap: 3000)`: `letters`, `sequence` (nil unless valid), `invalid`
(letter, 1-based positions), `suggestion` ("This looks like RNA. Switch?"), `truncated`, `message`.
`BioSeq`: `complement`, `reverseComplement`, `reverse`, `transcribe(codingStrand:)`, `templateStrand`, `toDNA`,
`codons(_, frame:)`, `translate(_, frame: 0…2, reverse:)`, `proteinFromFirstStart`, `orfs(_, minCodons:, reverseStrand:)`
→ `BioORF` (frame ±1…3, 1-based forward `start`/`end`, start > end on the reverse strand, `codons`, `protein`,
`hasStop`), `counts`, `gcContent`, `mass(_, rna:)`, `meltingTemperature`, `chargaff`, `lines(_, size: 10, perLine: 6)`.
`BioSequenceText` draws a strand in groups of ten with numbers and 5′/3′.

## B-DNA (§6.7)

`BioBDNA.build("ATGC", bothStrands: true) -> BioDuplex`: `atoms` (strand, pair, base, name, element, pos — app frame
X = r·sin θ, Y = x, Z = r·cos θ; strand 1 5′→3′ up), `bonds`, `hBonds` (pair, a, b), `phosphates(strand:)` (5′→3′,
for the cartoon's spline), `pos(strand:pair:name:)`, `model` (tagged "strand 1 · A 3"). `BioBDNA.rise`, `.twist`,
`.place`, `.hBondAtoms`.

## Crosses (§5.3, §5.6.2)

`BioCross.parse("AaBb × aaBb")` → (`cross`, `error`); patterns guessed (X^ X-linked recessive, I^A ABO, ^ incomplete,
else complete) and changeable (`cross.genes[k].pattern`, `.traitNames`). `BioGene(letter, pattern, alleles:,
traitNames:)`; `BioInheritance` (complete, incomplete, codominant, abo, xRecessive, xDominant,
lethalDominantHomozygote, lethalRecessive). `BioCross.gametes(genotype)`, `gametes1/2`, `square`, `offspring`,
`result(sex: nil|.female|.male)` → `BioCrossResult` (`genotypes` rows with count in square, `p`, phenotype, `dies`,
`carrier`; `phenotypes` (born); `genotypicRatio(Text)`, `bornGenotypicRatio`, `phenotypicRatio(Text)`), and
`probability(of: [BioTarget])` → factors and product (the product rule). Exact `BioFrac` throughout (`text` "3/32",
`percentText` "9.4%"); `BioRatio.reduce/whole/text`.

### The Genetics Crosses and Hardy–Weinberg tools

- **Crosses** (`CrossesTool.swift` and the page, part and model files beside it): `CrossSetup` is the Cross page's
  cross (one to three genes, one X-linked at most; `genotypes(gene:parent:)` lists only what a parent can be — never a
  lethal homozygote; `setPattern`, `setLetter`, `applyExample` (§5.3.1's `CrossExamples`), `testCross`, `parse`, and
  `encoded` for `SimplBio.cross.setup`). `CrossPunnett`, `CrossForkedLine` (by phenotype or genotype),
  `CrossProbability` (chips per gene, the product and sum rules, at least one / exactly k of n), `ChiSquareWork` (the
  null hypothesis, the table, AP's conclusion), and `PedigreeModel.swift` (`Pedigree`, `PedigreeChecker`: four modes,
  the people who rule each out). Hand-offs it takes: `["cross": "AaBb × aaBb"]`, and
  `["chi": "round:3:5474;wrinkled:1:1850"]` (name:ratio:observed per category) for the Chi-square page.
- **Hardy–Weinberg** (`HardyWeinbergTool.swift`, `HardyWeinbergPages.swift`, `HardyWeinbergModel.swift`):
  `HardyWeinberg.calculate(_:_:)` / `calculate(counts:)` (each step as text), `.test(_:df:)` (df 1 by default),
  `HWSimulation.step/run` in §5.4.3's order — replicate r, counting from 0, draws from SplitMix64 seed S + r, 2N
  `uniform() < p₃` draws a generation even once an allele is fixed, so Windows gets the same k's. Hand-offs:
  `["given": "q2", "value": "0.16"]`, `["counts": "50,30,20"]`.
- **Launch states:** `-SimplTool cross -SimplToolState cross:forked` (`square`, `genotypes`, `why`, `testcross`,
  `example=yellow` (a `CrossExamples` id), `cross=…` or `-SimplBioCross "AaBb x AaBb"`);
  `probability:prob=AaBbCc x AaBbCc:target=aa,B_,Cc:children=3`;
  `chi:fromcross` or `chi:obs=315,108,101,32`; `pedigree:example=xr:mode=xd:select=2`. `-SimplTool popgen
  -SimplToolState calculate:q2=0.16` (any `HWGiven` raw value, or `counts=50,30,20`); `test:counts=50,30,20:df=2`;
  `simulate:n=10:p0=0.5:seed=7:reps=5:gens=100:preset=recessiveLethal:F=0.25:u=…:v=…:m=…:pm=…:hover=40`
  (`infinite` for no drift).

## Chi-square and randomness (§5.3.4, §5.6.3–4)

`BioChiSquare.test(observed:expected:df:)` → `chi2`, `terms`, `df`, `critical` (p = 0.05), `p`, `reject`,
`conclusion` (AP wording); `expected(ratio:total:)`, `pValue(_, df:)`, `critical(df:p:)`, `table`.
`BioRandom(seed:)` (SplitMix64): `next()`, `uniform()`, `uniform(lo, hi)`, `int(below:)`, `unitVector()` (2 draws:
z, then φ), `inBall()`, `rotation()` — each helper's draw order is documented so Windows can match bit for bit.

## 3D (§3.4, §3.5)

- **Meshes** (`BioMesh`: Float positions/normals, UInt32 indices, CCW outward): `BioShapes.sphere`, `icosphere`
  (1: 80 triangles), `cylinder(from:to:)`, `capsule`, `spheroid`, `superellipsoid`, `roundedBox`, `lens`, `torus`,
  `shellPatch` (RER sheets), and `radial(rings:segments:cut:inner:radius:)` for any star-shaped solid or shell —
  `cut: .octant` (x, y, z > 0 removed) or `.half` (z > 0) with capped cut faces showing the wall; radius functions
  `BioShapes.sphere(r)`, `ellipsoid`, `capsuleRadius`, `roundedBoxRadius`, `lensRadius`, `superellipsoidRadius`,
  `byDistance(sdf)`, `blob(base, BioBlob(seed:amplitude:terms:))`. `BioPath.catmullRom`, `frames` (rotation-minimising),
  `tube(points, radius:, radii:, sides:, closed:)`, `ribbon(points, guides:, width:, thickness:, arrow:, elliptical:)`.
  `BioFrame` places a mesh (`along(dir, at:)`, `rotation(axis:degrees:)`); `mesh.transformed/translated/scaled/
  flipped`. `BioMoleculeMeshes.dashes` for H-bonds.
- **A scene** is values: `BioSceneContent(items: [BioSceneItem(id:mesh:material:order:)], labels: [BioLabel3D],
  frame: BioSphere?)`. Ids are what a click picks (`atom:12`, `residue:7`, `organelle:mito:3`); selecting
  `organelle:mito` selects every `organelle:mito:…`. `BioMaterial(hex, alpha:, roughness:, emission:, unlit:)` —
  alpha < 1 is drawn after the opaque, without depth writes, both sides (`order` stacks shells: outer last).
  `BioMoleculeScene.items(model, style:, hydrogens:, color:)` draws atoms (CPK, chemistry radii, UV spheres ≤ 300
  atoms, icospheres above) and bonds; `BioMoleculeScene.symbolNodes` the atom letters (as `extras`).
- **The view:** `BioSceneView(key:, build: BioSceneBuild { … }, extras:, selection: $sel, showLabels:, turning:,
  frameKey:, reset:, zoom: BioZoomRequest?, dimOthers:, accessibility:, onPick:, onDoubleClick:, onHover:, onScale:,
  labelBottom:)`
  (`onHover` tells the id under the pointer, for a tooltip; `onScale` the points one scene unit spans at the framed
  ball's centre whenever the zoom changes it, for a scale bar; `labelBottom` the room the label columns leave clear
  at the bottom, for that scale bar — `CEScaleBar.room`). The build closure
  runs off the main thread — capture copies, not your ObservableObject; it runs again only when `key` changes (put
  page, options and light/dark in it). The camera frames the content on the first scene, when `frameKey` changes and
  — until the model is turned or zoomed — when the view changes size; `reset += 1` puts it back; `zoom` frames an
  object. Framing (1.3.79) fills 84% of the room the model has (the view's height, or the width beside the label
  columns: each the widest label and 40 points), worked out from the content's outline (`BioSceneContent.outline()`,
  26 extreme points, made off the main thread with the scene by `measured()`), and no nearer than a `frame` ball the
  scene gives. Translucent things write no depth and are drawn after the opaque, by `order` (a cell's shells last,
  from both sides); (1.3.80) a translucent shell's faces each take 1 − ⁴√(1 − α) (`CEBuilder.faceAlpha`), so the four
  over a point inside the cell — outer and inner, near and far side — make the part's own α: the plasma membrane's
  0.30 over the organelles, not the gold veil of 0.51 or 0.76 that hid their colours until a click dimmed it. Click
  picks (opaque first, through translucent membranes), hover outlines the opaque pieces and shows a hand, the
  selection glows and the rest dims to 35%, ← → ↑ ↓ turn 15°, + − zoom, Escape deselects, double-click zooms (or
  resets on empty space). Labels with leader lines (§3.5: two columns, 22 pt apart, as many as the columns hold (24
  at most) with "+N more" past that, the columns kept within two labels of each other and 24 pt out from the model's
  outline, 120 ms fades, pressable) come from `labels`; the anchors are projected by `BioProjector` (the camera's own
  projection, in the overlay's points — not SceneKit's `projectPoint`), laid out again for a second after anything
  moves. Give a
  convex shell's label a `normal`, else a ray decides what hides it. Each label's capsule is its words measured at
  12 pt semibold plus its padding (`BioLabelWidth.of`; `BioLabelLayout.estimatedWidth` is only the model's guess), so
  none is cut short; two labels may name one object (a β-strand and the C-terminus in it) — the overlay tells them
  apart by id and words. Frame a model on itself: a frame shared with a much larger state (ubiquitin with its unfolded
  coil) left it small in the card. `BioSceneCard { bar } scene: { … }` and
  `BioSceneButtons(turning:reset:)` give the chemistry builder's card. Auto-turn is off by default and never under
  Reduce Motion.

## Step-through (§3.6)

`@StateObject var steps = BioStepController(steps: [BioStep(0, "Initiation", "…", group: 1)])`;
`BioStepControls(controller: steps, groups: [1: "Interphase", …])` under the scene; `.bioStepKeys(steps)` on the scene
for ← → Space Home End 1–9. Draw with
`TimelineView(.animation(paused: !steps.moving)) { ctx in let ph = steps.phase(at: ctx.date) … }`: `ph.from`, `ph.to`,
`ph.t` (eased; 600 ms at 1×, back plays in reverse) and `ph.crossfade` (Reduce Motion: fade both keyframes, 200 ms).
`steps.go(to:)`, `next`, `back`, `play`, `pause`, `speed`, `setSteps`; `BioStepController(steps:start:)` starts at a
launch's step. VoiceOver hears each step.

## Colours and pieces (§3.8, §9)

`BioPalette.base("A")`, `.aminoAcid(.acidic)`, `.aminoAcid(one: "E")`, `.secondary("H")`, `.strand1`, `.rna`,
`.maternal`, `.hBond` … are `BioHexPair`s: `.color` (SwiftUI, follows the appearance), `.nsColor(dark:)`,
`.hex(dark:)` (for `BioMaterial`). `BioColor.base`, `BioColor.aminoAcid`. Never colour alone: show the letter or a
`BioLegend`. Pieces: `BioHeading`, `BioCard(title:swatch:subtitle:) { BioFactRow(label:text:) … }`, `BioValueRow`,
`BioLegend`, `BioSequenceText`, `PTWorkLayout { left } centre: { } right: { }` (the chemistry layout, 1.3.79: 1,100
points of room or more, the three side by side; 700 or more, the drawing and one column beside it, the controls over
the facts, each in its own scroller and the top of each in sight (`PTSideColumn`); narrower, the drawing across the
top — the room less 170 points, so the columns' tops show under it, at most 560 and at least 300 for a 3D view's card
(`BioSceneCard` says so through `PTDrawingMinKey`) or 450 for a 2D drawing — and the left and right columns under it,
side by side from 520 points, else one over the other. So a 2D centre must fit 450 points: give its cards flexible
heights, not fixed minimums that add up to more).

## Numbers shown

Every number a tool shows is rounded **half up on its decimal digits**, as everywhere in the apps and on Windows:
3.425 → 3.43 (though it is held as 3.42499…), 1/16 → 6.3%, 0.125 → 0.13, −2.5 → −3. Write it with
`String(halfUp: "%.2f g/mol", mass)` — `String(format:)` with every fixed-point number rounded so first — or
`x.bioHalfUp(2)` (`BioMath.swift`); `String(format:)` alone rounds the binary value and a true tie to even. A
`NumberFormatter` gets `roundingMode = .halfUp`. The Bio Lab Calculator's `BioLabFormat` rounds the same way with its
significant figures (§5.5). The self-check's `format.half-up` line covers ties, signs and a `%d` beside a `%.1f`.

## Hand-offs between the tools

`BioTools.open(<tool>, info: [...])`; the tool takes each key once (`ToolsCenter.shared.takeInfo`). The keys are the
same in both apps; where a tool also reads the Windows app's older names, they are listed:

| From → to | Keys |
|---|---|
| Cell Explorer → Bio Lab Calculator ("Why are cells small?") | `savShape` (`sphere`, `box`, `cylinder`), `savSize` (a sphere's diameter; a box's `l,w,h`; a cylinder's `d,l`), `savUnit`, `from` = `cell`; the calculator also reads Windows' `calc=sav`, `mode`, `r`, `s`, `l`, `w`, `h` (`BioLabSAVHandOff`) |
| Cell Explorer → Biomolecule Builder ("See a real phospholipid" → Open) | `molecule` = `POV` |
| Gene Expression → Biomolecule Builder | `protein` (one-letter codes) with `page` = `proteins`; `molecule` (an amino acid's three letters) with `page` = `amino` |
| DNA & RNA ⇄ Gene Expression | `sequence` (and `page`); Gene Expression also takes `codon`, `mutation` |
| Biomolecule Builder → Gene Expression (haemoglobin's sickle-cell marks) | `mutation` = `sickle` |
| Biomolecule Builder → DNA & RNA (a strand) | `sequence` |
| Cell Division → DNA & RNA (S phase) | `page` = `replication` |
| any → Genetics Crosses / Hardy–Weinberg | `cross`, `chi`; `given` + `value`, `counts` |
| any → Biomolecule Builder | `sequence` (≤ 12 ACGT letters: the strand builder; else the peptide builder), `molecule` (an amino acid, nucleotide, sugar or lipid code), `page` |
| any → Flashcards | `deck` |

## Self-check (§10.1)

`BioSelfCheck.run()` covers the shared data and algorithms (the genetic code, the templates' frame, chirality, bonds
and χ angles, every molecule's formula, the B-DNA test vectors, SplitMix64, chi-square, every §5.3.2 cross, the genes,
proteins and tRNA, the geometry and label layout). Add yours in `Model/<Tool>Model.swift`:
`extension BioSelfCheck { static func cellChecks(_ r: inout BioCheckReport) { r.near("cell.count", …) } }` with
`r.check(name, value, ok)`, `r.near(name, value, expected, tolerance)`, `r.same(name, value, expected)`. Names have no
spaces. CI's Models job fails on any FAIL.

## Cell Explorer (`cell`)

`CellExplorerTool.swift` (the pages; a cell's page `CECellPage`), `CellExplorerViews.swift` (card rows, the scale bar
and chip, the quiz), `CellExplorerPages.swift` (Compare, Membrane, the POPC sheet, the membrane's moving nodes). Model
(Foundation only): `CellExplorerText.swift` (§5.1.1–5.1.4 copied by a script — never edited by hand), `CellExplorerModel.swift`
(`CEParts`: each part's card from those words; `CECompare`; `CEAbout`; `CEQuiz`; `CEModel.modelLine`),
`CellExplorerGeometry.swift` (`CERecipes`: §5.1.5's organelles in their own frames; `CEGeo`), `CellExplorerBuild.swift` and
`CellExplorerCells.swift` (`CEBuild.build(CEBuildOptions)`: the placement, each cell's recipe in its draw order, the
labels' anchors, Look inside; `CEBuildCache`), `CellExplorerScene.swift` (`CEScene.content(model, CEView)`: explode,
fade, isolate, labels; the Compare page's three cells), `CellMembraneModel.swift` (`CEMembrane`: the 60 × 60 nm patch)
and `CellExplorerChecks.swift` (the `cell.…` self-check lines: counts, the 250k-triangle budget, inside the membrane,
the empty cutaway wedge, the same twice, explode and fade, chloroplasts, the dented vacuole, left-handed flagella, true
scale, labels, SA:V, the membrane, (1.3.80) the plasma membrane's veil: its four faces over a point of the cell together
its α 0.30, `CEBuilder.faceAlpha`).

Scene ids: `organelle:<part>:<n>` (a click selects the part: every `organelle:<part>:…` glows; ribosomes, pores, actin and
fimbriae are drawn in clusters, `organelle:<part>:g<x>_<y>_<z>`, so a selection is dozens of objects, not thousands), `inside:<part>:…` (Look
inside's labels), `cell:<kind>` (Compare), `part:<id>…` and `side:outside|inside` (Membrane). Remembered:
`SimplBio.cell.cutaway|labels|trueScale|gramPositive|hidden.<kind>|membrane.*`. The cells are turned 32° and tilted
22° in the scene (a three-quarter view into the cut); the membrane 30° and 26°.

Launch states: `animal|plant|bacterium[:nocut][:nolabels][:true][:gram+][:explode=0.6][:show=flagellum,amyloplast]`
`[:select=<part>|:isolate=<part>|:inside=mito|chloroplast|nucleus][:quiz=find|name]`; `membrane[:select=<id>][:osmosis]`
`[:still][:nolabels][:popc]`; `compare`; `-SimplBioCell animal|plant|bacterium` picks the cell. "Why are cells small?"
opens the Bio Lab Calculator's `cells` page with `["from": "cell", "savShape": "sphere"|"box"|"cylinder",
"savSize": "18.9" | "44,26,26" | "1,2", "savUnit": "µm"]` (the calculator's surface area and volume card takes the
cell, with a line saying so); "Make flashcards" adds a Flashcards set and opens it (`["deck": id]`).

## DNA & RNA (`dna`)

The `dna` tool (spec §4.2). Five pages, chosen in the tool's header: **Double helix**, **Nucleotides**, **Sequence**, **Replication** and **RNA**.

### Files

```
mac/Simpl/Tools/Biology/
  DNATool.swift            the tool: its pages, the store they share (DNAStore: the sequence, each page's choices and
                           selection, replication's step-through; remembered as SimplBio.dna.<field>), the launch state,
                           the hand-off, and 3D text (DNASceneText; DNAMarkShelf carries a scene's words to the main thread)
  DNAHelixPage.swift       the double helix: controls, the 3D view, the pair/backbone/groove cards, the pairs as buttons
  DNANucleotidesPage.swift the nucleotides in 3D, their cards, DNA against RNA, and the bases in 2D (DNABasesPanel)
  DNASequencePage.swift    a strand, its complement and reverse complement, and its facts
  DNAReplicationPage.swift the replication fork: the drawing (DNAForkDrawing), the enzymes, the step card
  DNARNAPage.swift         the tRNA cloverleaf, its 3D L-shape, the mRNA strip (DNAMRNAStrip) and the ribosomes
  Model/                   Foundation only, checked by the Models job
    DNAToolModel.swift     what the pages share (atom look, 3D text marks, cones, tube pieces) and dnaChecks
    DNAHelixModel.swift    the helix's scene: cartoon, atoms, ends, grooves, measurements, unzip; picking; pair facts
    DNANucleotideModel.swift  the nucleotides' parts, labels, numbers and differences; the bases and pairs in 2D
    DNASequenceModel.swift the Sequence page's facts
    DNAReplicationModel.swift  the nine steps and their keyframes
    DNARNAModel.swift      the mRNA strip's parts, the cloverleaf's layout, the L-shape and the ribosomes
```

### What the models promise (and `dnaChecks` checks)

- **Helix**: §6.7's atoms (BioBDNA), right-handed on both strands, 10.5 bp and 35.7 Å a turn. Cartoon: a tube (1.1 Å)
  through each strand's P atoms, a piece per nucleotide; each base a rounded box from its C1′ toward its partner's, the
  purine's 58% and the pyrimidine's 42% of C1′–C1′ less the 1.0 Å gap (so they fill it exactly); 2 white dashes for A·T,
  3 for G·C. Atoms: every heavy atom with the chemistry radii, H-bonds dashed (0.08 Å) between §6.5's atoms. Strand 1's
  5′ end at the bottom, strand 2's at the top; arrowheads point up strand 1 and down strand 2. Grooves: 148.45° (minor)
  and 211.55° of arc; widths 6.6 and 11.6 Å (P–P less 5.8 Å). Unzip opens pair k once unzip > k/n, 8 Å apart.
- **Ids** (what a press picks): `pair:<k>:<strand>:base`, `pair:<k>:<strand>:bb` (`:a<i>` per atom in the atom
  views, `:arrow` for an arrowhead), `pair:<k>:hb`, `groove:major|minor`; selecting `pair:<k>` lights both
  nucleotides. Nucleotides: `part:phosphate|sugar|base:a<i>`, `part:glyco`, `diff:2prime|methyl`. tRNA:
  `arm:<arm>:<i>`, `arm:<arm>:rung<i>`.
- **Nucleotides**: every atom of the nine in a part (phosphate with O5′, sugar, base), the parts' heavy atoms counted;
  the masses equal the Sequence page's; numbers 1′–5′ and 1–9; A·T, G·C and A·U in 2D with H-bond lengths from the 3D
  model (2.84/2.91; 2.87/2.79/3.00 Å).
- **Sequence**: counts, GC, exact masses (5 of each base: 6196.87 g/mol), Tm (Wallace to 14 nt), Chargaff (50%),
  complement against reverse complement, RNA's own masses.
- **Replication**: nine steps, each naming its enzyme; the fork only moves on; the leading strand grows toward it; three
  Okazaki fragments, each begun at its 5′ end (nearer the fork) from its own primer; primers replaced, nicks sealed,
  semiconservative at the end.
- **RNA**: the mRNA's coding sequence from its AUG at 14 reads MAFSDWQKG; the cloverleaf obeys §4.2's layout (stems 1.0
  and 1.6, loop radius (m + 1)/(2π) × 1.05, arms up/left/down/right, the variable loop lower right, the codon UUC
  under the anticodon GAA, antiparallel); the L-shape is turned (a rotation) so the elbow is top left, the acceptor arm
  runs right and the anticodon arm down; anticodon to 3′ end 72 Å.

### Launch state (pictures)

`-SimplTool dna -SimplToolState <page>[:options]`, and the specification's flags (`-SimplBioDNAStyle`, `-SimplBioSeq`,
`-SimplBioStep`, `-SimplBioPage`):

- `helix:style=atoms|space|cartoon:grooves:measure:noends:nohbonds:nolabels:unzip=0.5:pair=3:seq=ATGC…`
- `nucleotides:mol=DT:numbers:compare:noparts:part=sugar`, `nucleotides:pairs[:au]` (the 2D bases)
- `sequence:seq=ATGC…`
- `replication:step=4`
- `rna:view=cloverleaf|lshape|mrna|ribosomes:modified:base=34:arm=anticodon`

### Hand-off

Gene Expression (or any tool) can open DNA & RNA with `BioTools.open(.dna, info: ["sequence": "ATG…"])` (the Sequence
page, or `"page": "helix"`), or `["nucleotide": "DT"]`. The Sequence page's "Transcribe in Gene Expression" hands
`["sequence": <coding strand>]` to `gene`.

## Gene Expression (`gene`)

`GeneExpressionTool.swift` (the tool, `GXStore` — what it remembers, `SimplBio.gene.*` — the launch, the hand-off and
the pin's quick codon lookup `GeneExpressionCompact`), `GeneExpressionExpressPage.swift` and
`GeneExpressionScene.swift` (transcription one nucleotide a step, processing — cap, poly-A tail, splicing at GU…AG,
export — and translation one codon a step, on the shared step-through; a prokaryote's version without the intron, with
the Shine–Dalgarno region), `GeneExpressionCodonsPage.swift` (§6.1's grid and the wheel, lookup both ways, an amino
acid's card), `GeneExpressionFramesPage.swift` (six frames, the ORF finder), `GeneExpressionMutationsPage.swift` (every
class of §4.3, the sickle-cell preset). Model: `GeneExpressionModel.swift`, `GeneExpressionProcess.swift` (the steps:
the teaching gene 99, a prokaryote 68), `GeneExpressionMutations.swift`; checks `gene.*`.

Launch states: `express:eukaryote|prokaryote:gene=teaching|hbb|own:seq=…:step=<n>`; `codons[:wheel][:dna][:find=Leu]`;
`frames:example=<n>[:min=<codons>]`; `mutations:sickle` or `mutations:mutgene=teaching:mut=c.6_7insA`.
`-SimplPinCodon UGG` fills the pin's codon field. Takes `sequence`, `codon`, `mutation` (`sickle` or a `c.…` change)
and `page`; the last step's "Open in Biomolecule Builder" hands over the protein.

## Biomolecule Builder (`biomol`)

`BiomoleculeTool.swift` (pages, launch, hand-offs), `BiomolState.swift` (one state for the five pages, remembered),
`BiomolNodes.swift`, `BiomolUI.swift`, and a page file each: `BiomolAminoPage`, `BiomolProteinPage`,
`BiomolNucleicPage`, `BiomolCarbPage`, `BiomolLipidPage`. Model: `BiomoleculeBuilderModel.swift` (input, easing,
`biomolChecks`), `BiomolPeptide.swift` (§6.4: NeRF backbone, side chains, clash relief, proline's rule),
`BiomolProteins.swift` (ubiquitin 1UBQ, haemoglobin 2HHB), `BiomolMolecules.swift`, `BiomolScenes.swift`.
"Open in Molecule Builder" hands the atoms to the Molecule Builder (1.3.80: a chemistry tool of its own, `molecule`,
no longer a page of the Periodic Table's), which keeps their shape (`MoleculeModel.load(handOff:)`, `fixedShape`).

Launch states (the page first): any page `style=ball|space|sticks`, `colour=element|part|class`, `neutral`,
`nolabels`, `h=on|off`, `select=<id>`; `amino:aa=W[:dform]`; `proteins[:build][:level=primary|secondary|tertiary|
quaternary][:seq=…][:ss=helix|strand|coil|<letters>][:nohbonds][:ribbon=off|over|only][:sheet][:core|:surface]
[:denatured]`; `nucleic[:nt=ATP][:strand:seq=ATGCGT][:complement]`; `carbs[:mono=GLC][:anomers][:di=MAL][:split]
[:poly=cellulose]`; `lipids:lipid=POV|compare`.

## Cell Division (`divide`)

`CellDivisionTool.swift` (four pages: Mitosis, Meiosis, Compare, Cell cycle), `CellDivisionCanvas.swift` (the cell in
2D, its labels and badges), `CellDivisionPanels.swift` (the counts panel, always shown, with human values),
`CellCycleView.swift` (the wheel, three checkpoints, cyclins, the cancer view; S phase opens DNA & RNA's Replication
page). Model: `CellDivisionModel.swift`, `CellDivisionGeometry.swift`, `CellDivisionKeyframes.swift` (9 and 11 steps;
meiosis' interphase split into G1, S and G2), `CellDivisionPainter.swift` (the drawing as a list of shapes, so the
counts are counted from the keyframe itself); checks `divide.*`. Where it departs from §5.2.4 (both apps): plate
spacing at least 0.22 / 0.16 and 0.08 between chromosomes where there is room; in prophase I the facing non-sister
chromatids 0.05 apart; telophase arms at ±25°; nondisjunction acts on the short pair.

Launch states: a page, then `plant`, `2n=6`, `nocross`, `chiasma=0.3`, `flip`, `nd=1|2`, `nolabels`, `step=<n>`,
`pick=<chromatid>`, `part=<label>`; on Cell cycle `gate=g1s|g2m|spindle` or `phase=g1|s|g2|m|g0`, `cancer`.
`-SimplBioCell plant` and `-SimplBioStep <n>` read the same.

## Bio Lab Calculator (`biolab`), Enzyme Lab (`enzyme`), Photosynthesis & Respiration (`energy`)

- **Bio Lab Calculator** (`BioLabTool.swift`; `Model/BioLabModel.swift`, `Model/BioLabCalculators.swift`): 26
  calculators on six pages (`solutions`, `microscopy`, `cells`, `stats`, `ecology`, `molecules`), each a card with the
  formula, fields with unit menus, "solve for", the answer to §5.5's significant figures (3 by default, 2–6 in the
  page's menu, never more than one past the least precise input), the numbers substituted and "Show steps". Inputs
  are remembered per calculator (`SimplBio.biolab.calc.<id>`); the pin's C₁V₁ and molarity share them. Launch:
  `<page>[:steps][:calc=<id>][:digits=<n>]`. Takes `calc`, and Cell Explorer's cell (above).
- **Enzyme Lab** (`EnzymeLabTool.swift`; `Model/EnzymeLabModel.swift`): rate against [S], temperature and pH, linked;
  inhibitors; denaturing is for good (the cooled curve dashed at a tenth), and "Fresh enzyme" while the sample is still
  too hot puts the temperature at that enzyme's optimum (both apps). Launch:
  `[competitive|noncompetitive][:denatured][:enzyme=human|taq|fish][:phpreset=pepsin|amylase|catalase|trypsin]
  [:temp=<°C>][:ph=<pH>][:s=<mM>]`.
- **Photosynthesis & Respiration** (`EnergyPathwaysTool.swift`; `Model/EnergyPathwaysModel.swift`): Map, Respiration,
  Photosynthesis, Follow the atoms. Launch: `map[:stage=<id>][:respire]`, `respiration[:step=<n>][:ferment=lactate|
  ethanol]`, `photosynthesis[:step=<n>]`, `atoms[:trace=oxygen|carbon][:step=<n>]`.

## Pictures

`scripts/dev/mac-shots.sh`'s `bio <light|dark|both> <name> <wait> <tool> <state>`. A push takes each tool once, in light
(`bio_quick`): 57 the animal cell, 58a the helix measured, 59a a gene being transcribed, 61b the peptide builder, 62a a
dihybrid cross, 63a metaphase, 64a Hardy–Weinberg worked out, 65a the lab's solutions, 66a a competitive inhibitor,
67a the energy map at the Calvin cycle, beside 35b (the Tools page's Biology section); and (1.3.79, `bio_plain_all`,
in both sets) the Cell Explorer, DNA & RNA and the Biomolecule Builder opened plainly — no launch state, so nothing
selected — in dark at 1,024 × 683 and 720 × 520, the sidebar out and folded (`dark-57z-cell-plain-1024`, `-1024-folded`,
`-720`, `-720-folded`; `58z-dna-plain`, `61z-biomol-plain` alike): the launch states' pictures never showed what a
plain open did in the 1.3.78 beta. The full set (`bio_full`, a
commit saying `[full shots]` or Run workflow → full) takes every tool's pages, in light and dark where the builders
asked: 57–57l Cell Explorer, 58a–58l DNA & RNA, 59a–59k Gene Expression, 61a–61r Biomolecule Builder, 62a–62h Genetics
Crosses, 63a–63j Cell Division, 64a–64d Hardy–Weinberg, 65a–65d Bio Lab Calculator, 66a–66b Enzyme Lab, 67a–67e
Photosynthesis & Respiration, and the pins' quick versions 54b–54e. CI's runner shows a window about 1,024 × 683
whatever is asked, so the pictures show each tool at the narrow end of a Mac's sizes. A pin's picture opens its popover
with `-SimplPinOpen <tool>`; the suite runs several copies of the app at once, and a popover closes when its app is put
behind another, so a pin opened that way opens its popover again whenever its app comes back to the front
(`PinnedToolButton`, Tools/PinnedTools.swift) — without that, 54d and 54e were pictured with the popover closed. The
four biology pins are pictured at the end of the full set, each alone (`bio_pins`): four copies picturing their pins
at once took the front from each other, and three were pictured closed.
