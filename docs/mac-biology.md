# The biology toolkit on the Mac: the foundation

Ten tools under a **Biology** heading on the Tools page (spec §2): `cell` Cell Explorer, `dna` DNA & RNA, `gene` Gene
Expression, `biomol` Biomolecule Builder, `cross` Genetics Crosses, `divide` Cell Division, `popgen` Hardy–Weinberg,
`biolab` Bio Lab Calculator, `enzyme` Enzyme Lab, `energy` Photosynthesis & Respiration. Each is pinnable like any
tool. This page is for the people building them: what the foundation gives you, and which files are yours.

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

**Yours:** `<Tool>Tool.swift` (and any new view files) and `Model/<Tool>Model.swift` (and any new Foundation-only model
files in `Model/`; CI compiles the whole folder). Never edit `BioTools.swift`, `Tools.swift` or `PinnedTools.swift`.
Keep `Model/` free of AppKit, SwiftUI, SceneKit and `simd` (use `BioV`). The Xcode project is a synchronized folder: a
new file is in the app without a project edit (and do not put a `.md` or other resource in `mac/Simpl/`).

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
  (`BioHeaderControls`, glass buttons, squished to symbols when tight). Read the page with
  `@ObservedObject private var nav = BioNav.shared` and `nav.page(.cell)`; change it with `nav.set(.cell, "plant")`. It
  is remembered (`SimplBio.cell.page`).
- **Prefs:** `BioPrefs.string/bool/set(.cell, "field", …)` → UserDefaults `SimplBio.<key>.<field>` (§3.2).
- **Hand-off:** `BioTools.open(.gene, info: ["sequence": "ATG…"])` (in the pin's sheet if that is where you are, else
  on the Tools page); the receiving tool reads it once with `ToolsCenter.shared.takeInfo("sequence")` on appear.

## Launch flags (pictures)

`-SimplTool <key> -SimplToolState <state>` opens the tool; `state` reaches your view as `BioLaunchState`:
parts split at `:` (`plant:cutaway`, `helix:style=atoms:seq=ATGC`). A first part that is one of your page ids opens
that page by itself. Read the rest with `state?.has("cutaway")` and `state?.value("style")`. The spec's flags read the
same way: `-SimplBioSeq ATGC` is `value("Seq")`, `-SimplBioStep 4` is `value("Step")`, `-SimplBioPage <page>` picks
the page. Apply it once, on first appear. `-SimplBioCheck YES` prints the self-check on the console
(`bio-check <name> <value> <OK|FAIL>`, then `bio-check summary n/m OK`). `-SimplToolsScroll biology` scrolls the Tools
page to the Biology section. Add your pictures to `scripts/dev/mac-shots.sh`, e.g.
`SIZE=1440x920 shoot light-56c-cell-plant 16 -SimplPlace tools -SimplTool cell -SimplToolState plant:cutaway`.

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
  frameKey:, reset:, zoom: BioZoomRequest?, dimOthers:, accessibility:, onPick:, onDoubleClick:, onHover:, onScale:)`
  (`onHover` tells the id under the pointer, for a tooltip; `onScale` the points one scene unit spans at the framed
  ball's centre whenever the zoom changes it, for a scale bar). The build closure
  runs off the main thread — capture copies, not your ObservableObject; it runs again only when `key` changes (put
  page, options and light/dark in it). The camera frames the content (40°, 1.15·R/sin 20°) on the first scene and when
  `frameKey` changes; `reset += 1` puts it back; `zoom` frames an object. Click picks (opaque first, through
  translucent membranes), hover outlines and shows a hand, the selection glows and the rest dims to 35%,
  ← → ↑ ↓ turn 15°, + − zoom, Escape deselects, double-click zooms (or resets on empty space). Labels with leader
  lines (§3.5: two columns, 22 pt apart, ≤ 14 with "+N more", 120 ms fades, pressable) come from `labels`; give a
  convex shell's label a `normal`, else a ray decides what hides it. `BioSceneCard { bar } scene: { … }` and
  `BioSceneButtons(turning:reset:)` give the chemistry builder's card. Auto-turn is off by default and never under
  Reduce Motion.

## Step-through (§3.6)

`@StateObject var steps = BioStepController(steps: [BioStep(0, "Initiation", "…", group: 1)])`;
`BioStepControls(controller: steps, groups: [1: "Interphase", …])` under the scene; `.bioStepKeys(steps)` on the scene
for ← → Space Home End 1–9. Draw with
`TimelineView(.animation(paused: !steps.moving)) { ctx in let ph = steps.phase(at: ctx.date) … }`: `ph.from`, `ph.to`,
`ph.t` (eased; 600 ms at 1×, back plays in reverse) and `ph.crossfade` (Reduce Motion: fade both keyframes, 200 ms).
`steps.go(to:)`, `next`, `back`, `play`, `pause`, `speed`, `setSteps`. VoiceOver hears each step.

## Colours and pieces (§3.8, §9)

`BioPalette.base("A")`, `.aminoAcid(.acidic)`, `.aminoAcid(one: "E")`, `.secondary("H")`, `.strand1`, `.rna`,
`.maternal`, `.hBond` … are `BioHexPair`s: `.color` (SwiftUI, follows the appearance), `.nsColor(dark:)`,
`.hex(dark:)` (for `BioMaterial`). `BioColor.base`, `BioColor.aminoAcid`. Never colour alone: show the letter or a
`BioLegend`. Pieces: `BioHeading`, `BioCard(title:swatch:subtitle:) { BioFactRow(label:text:) … }`, `BioValueRow`,
`BioLegend`, `BioSequenceText`, `PTWorkLayout { left } centre: { } right: { }` (the chemistry layout).

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
scale, labels, SA:V, the membrane).

Scene ids: `organelle:<part>:<n>` (a click selects the part: every `organelle:<part>:…` glows), `inside:<part>:…` (Look
inside's labels), `cell:<kind>` (Compare), `part:<id>…` and `side:outside|inside` (Membrane). Remembered:
`SimplBio.cell.cutaway|labels|trueScale|gramPositive|hidden.<kind>|membrane.*`. The cells are turned 32° and tilted
22° in the scene (a three-quarter view into the cut); the membrane 30° and 26°.

Launch states: `animal|plant|bacterium[:nocut][:nolabels][:true][:gram+][:explode=0.6][:show=flagellum,amyloplast]`
`[:select=<part>|:isolate=<part>|:inside=mito|chloroplast|nucleus][:quiz=find|name]`; `membrane[:select=<id>][:osmosis]`
`[:still][:nolabels][:popc]`; `compare`; `-SimplBioCell animal|plant|bacterium` picks the cell. "Why are cells small?"
opens the Bio Lab Calculator's `cells` page with `["from": "cell", "savShape": "sphere"|"rectangular"|"cylinder",
"savSize": "18.9" | "44,26,26" | "1,2", "savUnit": "µm"]`; "Make flashcards" adds a Flashcards set and opens it
(`["deck": id]`).
