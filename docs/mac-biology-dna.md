# DNA & RNA on the Mac

The biology toolkit's `dna` tool (spec §4.2), built on the foundation described in `mac-biology.md`. Five pages, chosen
in the tool's header: **Double helix**, **Nucleotides**, **Sequence**, **Replication** and **RNA**.

## Files

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

## What the models promise (and `dnaChecks` checks)

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

## Launch state (pictures)

`-SimplTool dna -SimplToolState <page>[:options]`, and the specification's flags (`-SimplBioDNAStyle`, `-SimplBioSeq`,
`-SimplBioStep`, `-SimplBioPage`):

- `helix:style=atoms|space|cartoon:grooves:measure:noends:nohbonds:nolabels:unzip=0.5:pair=3:seq=ATGC…`
- `nucleotides:mol=DT:numbers:compare:noparts:part=sugar`, `nucleotides:pairs[:au]` (the 2D bases)
- `sequence:seq=ATGC…`
- `replication:step=4`
- `rna:view=cloverleaf|lshape|mrna|ribosomes:modified:base=34:arm=anticodon`

## Hand-off

Gene Expression (or any tool) can open DNA & RNA with `BioTools.open(.dna, info: ["sequence": "ATG…"])` (the Sequence
page, or `"page": "helix"`), or `["nucleotide": "DT"]`. The Sequence page's "Transcribe in Gene Expression" hands
`["sequence": <coding strand>]` to `gene`.
