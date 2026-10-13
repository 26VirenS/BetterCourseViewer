namespace Simpl.Tools;

// The biology toolkit's datasets as its specification writes them (§5.3, §6): copied out of the specification by a
// script, unchanged — molecule blocks, tables and sequences, the same text the Mac app carries (BioDataText.swift) —
// and parsed on first use (BioData.cs). Not edited by hand: BioSelfCheck checks what they hold (the codon counts, the
// templates' chirality and χ angles, the B-DNA test vectors, the genes' translations). A Windows checkout may end each
// line with a carriage return; the parsers drop it (BioText.Lines).

/// <summary>The specification's datasets, as text (generated; see the comment above).</summary>
public static class BioDataText
{
    /// <summary>§6.1: the 64 codons, "codon=amino acid" (* a stop), in U C A G order.</summary>
    public const string CodeText = """
UUU=F UUC=F UUA=L UUG=L UCU=S UCC=S UCA=S UCG=S UAU=Y UAC=Y UAA=* UAG=* UGU=C UGC=C UGA=* UGG=W
CUU=L CUC=L CUA=L CUG=L CCU=P CCC=P CCA=P CCG=P CAU=H CAC=H CAA=Q CAG=Q CGU=R CGC=R CGA=R CGG=R
AUU=I AUC=I AUA=I AUG=M ACU=T ACC=T ACA=T ACG=T AAU=N AAC=N AAA=K AAG=K AGU=S AGC=S AGA=R AGG=R
GUU=V GUC=V GUA=V GUG=V GCU=A GCC=A GCA=A GCG=A GAU=D GAC=D GAA=E GAG=E GGU=G GGC=G GGA=G GGG=G
""";

    /// <summary>§6.1: the same table as NCBI writes it (index 16·b1 + 4·b2 + b3, T C A G).</summary>
    public const string NcbiText = """
AAs   = FFLLSSSSYY**CC*WLLLLPPPPHHQQRRRRIIIMTTTTNNKKSSRRVVVVAAAADDEEGGGG
Base1 = TTTTTTTTTTTTTTTTCCCCCCCCCCCCCCCCAAAAAAAAAAAAAAAAGGGGGGGGGGGGGGGG
Base2 = TTTTCCCCAAAAGGGGTTTTCCCCAAAAGGGGTTTTCCCCAAAAGGGGTTTTCCCCAAAAGGGG
Base3 = TCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAGTCAG
""";

    /// <summary>§6.2: the 20 amino acids, a row each, the table's 13 cells joined by tabs.</summary>
    public const string AminoAcidText = """
Glycine	Gly	G	Nonpolar	Nonpolar, aliphatic	—	0	-0.4	75.07	57.05	C2H5NO2	4	Side chain is just H: the only amino acid that is not chiral; very flexible, common in turns.
Alanine	Ala	A	Nonpolar	Nonpolar, aliphatic	—	0	+1.8	89.09	71.08	C3H7NO2	4	Side chain CH₃; strong helix former.
Valine	Val	V	Nonpolar	Nonpolar, aliphatic	—	0	+4.2	117.15	99.13	C5H11NO2	4	Branched at Cβ; favours β-strands.
Leucine	Leu	L	Nonpolar	Nonpolar, aliphatic	—	0	+3.8	131.17	113.16	C6H13NO2	6	Six codons; very common in protein cores and helices.
Isoleucine	Ile	I	Nonpolar	Nonpolar, aliphatic	—	0	+4.5	131.17	113.16	C6H13NO2	3	Two chiral centres (Cα and Cβ); branched at Cβ.
Methionine	Met	M	Nonpolar	Nonpolar, aliphatic	—	0	+1.9	149.21	131.20	C5H11NO2S	1	Contains S (thioether). AUG is the start codon, so new proteins begin with Met.
Proline	Pro	P	Nonpolar	Nonpolar, aliphatic	—	0	-1.6	115.13	97.11	C5H9NO2	4	Side chain loops back to the backbone N (a ring); no N–H to H-bond; breaks α-helices; common in turns.
Phenylalanine	Phe	F	Nonpolar	Aromatic	—	0	+2.8	165.19	147.17	C9H11NO2	2	Benzene ring; hydrophobic core residue.
Tryptophan	Trp	W	Nonpolar	Aromatic	—	0	-0.9	204.22	186.21	C11H12N2O2	1	Largest side chain (indole); one codon (UGG).
Tyrosine	Tyr	Y	Polar	Aromatic	10.07	0	-1.3	181.19	163.17	C9H11NO3	2	Phenol OH can be phosphorylated (signalling).
Serine	Ser	S	Polar	Polar, uncharged	—	0	-0.8	105.09	87.08	C3H7NO3	6	OH group; can be phosphorylated.
Threonine	Thr	T	Polar	Polar, uncharged	—	0	-0.7	119.12	101.10	C4H9NO3	4	OH group; two chiral centres; can be phosphorylated.
Cysteine	Cys	C	Polar	Polar, uncharged	8.18	0	+2.5	121.16	103.14	C3H7NO2S	2	Thiol (–SH); two Cys can form a covalent disulfide bond (–S–S–) that holds tertiary/quaternary structure.
Asparagine	Asn	N	Polar	Polar, uncharged	—	0	-3.5	132.12	114.10	C4H8N2O3	2	Amide side chain; site of N-linked glycosylation.
Glutamine	Gln	Q	Polar	Polar, uncharged	—	0	-3.5	146.14	128.13	C5H10N2O3	2	Amide side chain.
Lysine	Lys	K	Basic (+)	Positively charged	10.53	+1	-3.9	146.19	128.17	C6H14N2O2	2	Amino group at the end of a long chain.
Arginine	Arg	R	Basic (+)	Positively charged	12.48	+1	-4.5	174.20	156.19	C6H14N4O2	6	Guanidinium group; always charged in cells.
Histidine	His	H	Basic (+)	Positively charged	6.00	≈0 (about 4% +1 at pH 7.4, 9% at 7.0)	-3.2	155.15	137.14	C6H9N3O2	2	Imidazole ring; pKa near cell pH, so it gains and loses H⁺ easily (enzyme active sites). Textbooks list it as basic.
Aspartic acid	Asp	D	Acidic (−)	Negatively charged	3.65	−1	-3.5	133.10	115.09	C4H7NO4	2	Carboxylate (aspartate) at cell pH.
Glutamic acid	Glu	E	Acidic (−)	Negatively charged	4.25	−1	-3.5	147.13	129.11	C5H9NO4	2	Carboxylate (glutamate) at cell pH.
""";

    /// <summary>§6.3: the amino acids' 3D templates (molecule blocks, §3.3).</summary>
    public const string ResidueText = """
@residue ALA Alanine
N    N    -0.491    1.383   -0.000
CA   C     0.000    0.000    0.000
C    C     1.505    0.000    0.000
O    O     2.110    0.906   -0.521
CB   C    -0.509   -0.721   -1.249
OXT  O     2.177   -1.006    0.582   # C-terminus only
H    H    -0.132    1.819   -0.836
H2   H    -1.493    1.337   -0.105   # N-terminus only
HA   H    -0.364   -0.514    0.889
HB1  H    -1.600   -0.722   -1.249
HB2  H    -0.146   -0.207   -2.138
HB3  H    -0.146   -1.749   -1.249
HXT  H     3.144   -1.006    0.583   # C-terminus only
H3   H    -0.219    1.882    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-HB1 CB-HB2 CB-HB3 OXT-HXT N-H3
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue ARG Arginine
N    N    -0.455    1.389    0.000
CA   C     0.000    0.000    0.000
C    C     1.518    0.000   -0.000
O    O     2.229    0.994   -0.068
CB   C    -0.575   -0.782   -1.191
CG   C    -2.097   -0.993   -1.141
CD   C    -2.616   -1.774   -2.346
NE   N    -4.046   -1.954   -2.269
CZ   C    -4.786   -2.640   -3.248
NH1  N    -6.163   -2.789   -3.132
NH2  N    -4.136   -3.180   -4.354
OXT  O     2.026   -1.259    0.088   # C-terminus only
H    H    -1.427    1.562    0.134
H2   H     0.173    2.087   -0.332   # N-terminus only
HA   H    -0.337   -0.458    0.937
HB2  H    -0.079   -1.760   -1.247
HB3  H    -0.319   -0.254   -2.119
HG2  H    -2.601   -0.019   -1.125
HG3  H    -2.374   -1.515   -0.218
HD2  H    -2.383   -1.226   -3.265
HD3  H    -2.126   -2.751   -2.413
HE   H    -4.577   -1.574   -1.476
HH11 H    -6.697   -3.286   -3.842
HH12 H    -6.681   -2.409   -2.344
HH21 H    -3.130   -3.089   -4.469
HH22 H    -4.651   -3.681   -5.074
HXT  H     3.005   -1.316    0.095   # C-terminus only
H3   H    -0.299    1.746    0.932   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-CD CG-HG2 CG-HG3 CD-NE CD-HD2 CD-HD3 NE-CZ
       NE-HE CZ-NH1 CZ=NH2 NH1-HH11 NH1-HH12 NH2-HH21 NH2-HH22 OXT-HXT N-H3
rotamer: mtt180 (Lovell 2000)
chi1: N-CA-CB-CG = -67
chi2: CA-CB-CG-CD = 180
chi3: CB-CG-CD-NE = 180
chi4: CG-CD-NE-CZ = 180
alternatives (most common first): mtt-85 -67 180 180 -85; mtm-85 -67 -167 -65 -85; mtp180 -67 180 65 -175;
  mtm180 -67 180 -65 175; ttp85 -177 180 65 85; ttt180 -177 180 180 180; mtt85 -67 180 180 85;
  ttp180 -177 180 65 -175; ttt-85 -177 180 180 -85; ttm-85 -177 180 -65 -85; ptt85 62 180 180 85;
  ptt180 62 180 180 180; ptt-85 62 180 180 -85; tpt85 -177 65 180 85; tpt180 -177 65 180 180;
  ttt85 -177 180 180 85; mtp85 -67 180 65 85; mtm105 -67 180 -65 105; mmt180 -62 -68 180 180;
  mmt-85 -62 -68 180 -85; mmm-85 -62 -68 -65 -85; ptp180 62 180 65 -175; ptm180 62 180 -65 175;
  ptm-85 62 180 -65 -85; tpp85 -177 65 65 85; tpp180 -177 65 65 -175; ttp-105 -177 180 65 -105;
  ttm105 -177 180 -65 105; ttm180 -177 180 -65 175; mtp-105 -67 180 65 -105; mmt85 -62 -68 180 85;
  mmm180 -62 -68 -65 175
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1); keep all (NH2 carries the +1 of the guanidinium)

@residue ASN Asparagine
N    N    -0.490    1.384   -0.000
CA   C     0.000    0.000    0.000
C    C     1.507    0.000   -0.000
O    O     2.111    0.983   -0.357
CB   C    -0.510   -0.722   -1.250
CG   C    -2.011   -0.831   -1.190
OD1  O    -2.585   -0.722   -0.127
ND2  N    -2.717   -1.051   -2.317
OXT  O     2.178   -1.092    0.398   # C-terminus only
H    H    -0.221    1.863    0.846
H2   H    -0.167    1.882   -0.815   # N-terminus only
HA   H    -0.364   -0.515    0.889
HB2  H    -0.222   -0.157   -2.137
HB3  H    -0.074   -1.719   -1.297
HD21 H    -2.258   -1.139   -3.167
HD22 H    -3.683   -1.122   -2.279
HXT  H     3.144   -1.045    0.380   # C-terminus only
H3   H    -1.499    1.355   -0.034   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG=OD1 CG-ND2 ND2-HD21 ND2-HD22 OXT-HXT N-H3
rotamer: m-20 (Lovell 2000)
chi1: N-CA-CB-CG = -65
chi2: CA-CB-CG-OD1 = -20
beta-strand rotamer: m-80 chis [-65, -75]
alternatives (most common first): t30 -177 30; t-20 -174 -20; p30 62 30; m-80 -65 -75; p-10 62 -10;
  m120 -65 120
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue ASP Aspartic acid
N    N    -0.490    1.385    0.000
CA   C     0.000    0.000    0.000
C    C     1.507   -0.000   -0.000
O    O     2.111    0.984   -0.359
CB   C    -0.510   -0.722   -1.249
CG   C    -1.997   -0.939   -1.131
OD1  O    -2.543   -0.811   -0.061
OD2  O    -2.713   -1.276   -2.214
OXT  O     2.179   -1.092    0.397   # C-terminus only
H    H    -0.220    1.863    0.847
H2   H    -0.167    1.883   -0.815   # N-terminus only
HA   H    -0.362   -0.514    0.890
HB2  H    -0.301   -0.115   -2.130
HB3  H    -0.008   -1.685   -1.343
HD2  H    -3.663   -1.405   -2.090
HXT  H     3.145   -1.044    0.379   # C-terminus only
H3   H    -1.499    1.357   -0.033   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG=OD1 CG-OD2 OD2-HD2 OXT-HXT N-H3
rotamer: m-20 (Lovell 2000)
chi1: N-CA-CB-CG = -70
chi2: CA-CB-CG-OD1 = -15
beta-strand rotamer: m-20 (chi2 at -75, in its range) chis [-70, -75]
alternatives (most common first): t0 -177 0; p-10 62 -10; p30 62 30; t70 -177 65
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1); remove HD2 (OD2 becomes −1)

@residue CYS Cysteine
N    N    -0.488    1.386    0.000
CA   C     0.000    0.000    0.000
C    C     1.506   -0.000    0.000
O    O     2.111    0.904   -0.523
CB   C    -0.511   -0.720   -1.248
SG   S    -2.319   -0.849   -1.175
OXT  O     2.177   -1.007    0.582   # C-terminus only
H    H    -0.129    1.819   -0.837
H2   H    -1.491    1.339   -0.105   # N-terminus only
HA   H    -0.364   -0.512    0.890
HB2  H    -0.222   -0.158   -2.136
HB3  H    -0.077   -1.719   -1.293
HG   H    -2.554   -1.495   -2.331
HXT  H     3.144   -1.008    0.582   # C-terminus only
H3   H    -0.214    1.885    0.834   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-SG CB-HB2 CB-HB3 SG-HG OXT-HXT N-H3
rotamer: m (Lovell 2000)
chi1: N-CA-CB-SG = -65
alternatives (most common first): t -177; p 62
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue GLN Glutamine
N    N    -0.489    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.506   -0.000   -0.000
O    O     2.111    0.905   -0.523
CB   C    -0.511   -0.721   -1.248
CG   C    -2.030   -0.875   -1.163
CD   C    -2.534   -1.585   -2.392
OE1  O    -1.897   -1.537   -3.422
NE2  N    -3.692   -2.272   -2.348
OXT  O     2.177   -1.008    0.581   # C-terminus only
H    H    -0.130    1.819   -0.836
H2   H    -1.491    1.339   -0.105   # N-terminus only
HA   H    -0.364   -0.513    0.890
HB2  H    -0.254   -0.139   -2.133
HB3  H    -0.048   -1.705   -1.313
HG2  H    -2.287   -1.457   -0.277
HG3  H    -2.492    0.110   -1.096
HE21 H    -4.016   -2.729   -3.139
HE22 H    -4.202   -2.310   -1.523
HXT  H     3.144   -1.008    0.581   # C-terminus only
H3   H    -0.217    1.883    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-CD CG-HG2 CG-HG3 CD=OE1 CD-NE2 NE2-HE21
       NE2-HE22 OXT-HXT N-H3
rotamer: mt-30 (Lovell 2000)
chi1: N-CA-CB-CG = -67
chi2: CA-CB-CG-CD = 180
chi3: CB-CG-CD-OE1 = -25
alternatives (most common first): tt0 -177 180 0; mm-40 -65 -65 -40; tp60 -177 65 60; pt20 62 180 20;
  mp0 -65 85 0; mm100 -65 -65 100; pm0 70 -75 0; tp-100 -177 65 -100
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue GLU Glutamic acid
N    N    -0.490    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.508   -0.000   -0.000
O    O     2.112    0.984   -0.358
CB   C    -0.509   -0.721   -1.250
CG   C    -2.029   -0.875   -1.165
CD   C    -2.532   -1.585   -2.396
OE1  O    -1.789   -1.772   -3.331
OE2  O    -3.804   -2.010   -2.454
OXT  O     2.179   -1.092    0.397   # C-terminus only
H    H    -0.166    1.883   -0.816
H2   H    -1.496    1.412    0.065   # N-terminus only
HA   H    -0.363   -0.514    0.890
HB2  H    -0.252   -0.140   -2.136
HB3  H    -0.048   -1.706   -1.315
HG2  H    -2.287   -1.456   -0.279
HG3  H    -2.490    0.110   -1.101
HE2  H    -4.080   -2.460   -3.263
HXT  H     3.144   -1.045    0.380   # C-terminus only
H3   H    -0.113    1.846    0.815   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-CD CG-HG2 CG-HG3 CD=OE1 CD-OE2 OE2-HE2
       OXT-HXT N-H3
rotamer: mt-10 (Lovell 2000)
chi1: N-CA-CB-CG = -67
chi2: CA-CB-CG-CD = -180
chi3: CB-CG-CD-OE1 = -10
alternatives (most common first): tt0 -177 180 0; mm-40 -65 -65 -40; tp10 -177 65 10; mp0 -65 85 0;
  pt-20 62 180 -20; pm0 70 -80 0; tm-20 -177 -80 -25
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1); remove HE2 (OE2 becomes −1)

@residue GLY Glycine
N    N    -0.489    1.386    0.000
CA   C     0.000    0.000    0.000
C    C     1.507    0.000    0.000
O    O     2.112    1.046    0.000
OXT  O     2.177   -1.163   -0.001   # C-terminus only
H    H    -0.114    1.902   -0.781
H2   H    -1.497    1.412    0.001   # N-terminus only
HA2  H    -0.363   -0.514    0.890
HA3  H    -0.363   -0.513   -0.891
HXT  H     3.143   -1.114   -0.001   # C-terminus only
H3   H    -0.166    1.830    0.848   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-HA2 CA-HA3 C=O C-OXT OXT-HXT N-H3
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue HIS Histidine
N    N    -0.520    1.344    0.000
CA   C     0.000    0.000    0.000
C    C     1.522    0.000    0.000
O    O     2.190    0.849   -0.582
CB   C    -0.523   -0.795   -1.203
CG   C    -0.061   -2.233   -1.205
ND1  N    -0.379   -3.053   -0.180
CD2  C     0.662   -2.901   -2.110
CE1  C     0.148   -4.254   -0.436
NE2  N     0.785   -4.174   -1.610
OXT  O     2.091   -1.008    0.704   # C-terminus only
H    H    -0.737    1.783    0.879
H2   H    -0.845    1.742   -0.865   # N-terminus only
HA   H    -0.318   -0.452    0.946
HB2  H    -1.622   -0.790   -1.216
HB3  H    -0.221   -0.308   -2.140
HD1  H    -0.921   -2.818    0.647
HD2  H     1.127   -2.692   -3.054
HE1  H     0.074   -5.129    0.189
HE2  H     1.280   -4.939   -2.058
HXT  H     3.072   -1.004    0.709   # C-terminus only
H3   H     0.358    1.839   -0.067   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-ND1 CG=CD2 ND1=CE1 ND1-HD1 CD2-NE2 CD2-HD2
       CE1-NE2 CE1-HE1 NE2-HE2 OXT-HXT N-H3
aromatic: CG ND1 CD2 CE1 NE2
rotamer: t60 (Lovell 2000)
chi1: N-CA-CB-CG = -177
chi2: CA-CB-CG-ND1 = 60
alternatives (most common first): m-70 -65 -70; m80 -65 80; t-80 -177 -80; p-80 62 -75; m170 -65 165;
  t-160 -177 -165; p80 62 80
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1); remove HD1 (ND1 neutral; neutral Nε2–H tautomer); the +1 form keeps HD1

@residue ILE Isoleucine
N    N    -0.490    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.506    0.000   -0.000
O    O     2.110    0.905   -0.524
CB   C    -0.511   -0.720   -1.248
CG1  C    -2.036   -0.831   -1.186
CG2  C     0.098   -2.123   -1.313
CD1  C    -2.568   -1.321   -2.533
OXT  O     2.178   -1.007    0.580   # C-terminus only
H    H    -0.132    1.819   -0.836
H2   H    -1.492    1.338   -0.103   # N-terminus only
HA   H    -0.363   -0.515    0.891
HB   H    -0.223   -0.159   -2.135
HG12 H    -2.461    0.147   -0.963
HG13 H    -2.317   -1.537   -0.406
HG21 H    -0.189   -2.685   -0.425
HG22 H    -0.266   -2.636   -2.203
HG23 H     1.184   -2.045   -1.358
HD11 H    -3.653   -1.400   -2.490
HD12 H    -2.285   -0.616   -3.314
HD13 H    -2.142   -2.299   -2.757
HXT  H     3.145   -1.007    0.579   # C-terminus only
H3   H    -0.217    1.885    0.834   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG1 CB-CG2 CB-HB CG1-CD1 CG1-HG12 CG1-HG13 CG2-HG21 CG2-HG22
       CG2-HG23 CD1-HD11 CD1-HD12 CD1-HD13 OXT-HXT N-H3
rotamer: mt (Lovell 2000)
chi1: N-CA-CB-CG1 = -65
chi2: CA-CB-CG1-CD1 = 170
alternatives (most common first): mm -57 -60; pt 62 170; tt -177 165; tp -177 66; pp 62 100; mp -65 100
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue LEU Leucine
N    N    -0.489    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.506   -0.000    0.000
O    O     2.112    0.905   -0.522
CB   C    -0.510   -0.719   -1.249
CG   C    -2.035   -0.827   -1.189
CD1  C    -2.556   -1.434   -2.493
CD2  C    -2.437   -1.720   -0.014
OXT  O     2.177   -1.008    0.581   # C-terminus only
H    H    -0.130    1.820   -0.835
H2   H    -1.491    1.339   -0.104   # N-terminus only
HA   H    -0.364   -0.514    0.889
HB2  H    -0.077   -1.718   -1.295
HB3  H    -0.220   -0.157   -2.137
HG   H    -2.464    0.165   -1.055
HD11 H    -3.642   -1.512   -2.450
HD12 H    -2.269   -0.798   -3.331
HD13 H    -2.127   -2.427   -2.628
HD21 H    -2.008   -2.712   -0.150
HD22 H    -2.066   -1.287    0.915
HD23 H    -3.524   -1.796    0.029
HXT  H     3.143   -1.009    0.581   # C-terminus only
H3   H    -0.216    1.884    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-CD1 CG-CD2 CG-HG CD1-HD11 CD1-HD12 CD1-HD13
       CD2-HD21 CD2-HD22 CD2-HD23 OXT-HXT N-H3
rotamer: mt (Lovell 2000)
chi1: N-CA-CB-CG = -65
chi2: CA-CB-CG-CD1 = 175
alternatives (most common first): tp -177 65; tt -172 145; mp -85 65; pp 62 80
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue LYS Lysine
N    N    -0.490    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.507    0.000   -0.000
O    O     2.111    0.983   -0.358
CB   C    -0.510   -0.721   -1.250
CG   C    -2.030   -0.874   -1.164
CD   C    -2.541   -1.595   -2.415
CE   C    -4.060   -1.748   -2.330
NZ   N    -4.551   -2.440   -3.530
OXT  O     2.178   -1.093    0.397   # C-terminus only
H    H    -0.131    1.819   -0.837
H2   H    -1.491    1.339   -0.105   # N-terminus only
HA   H    -0.363   -0.513    0.890
HB2  H    -0.252   -0.140   -2.135
HB3  H    -0.049   -1.706   -1.315
HG2  H    -2.287   -1.455   -0.280
HG3  H    -2.492    0.112   -1.100
HD2  H    -2.283   -1.014   -3.300
HD3  H    -2.079   -2.580   -2.479
HE2  H    -4.319   -2.330   -1.445
HE3  H    -4.522   -0.763   -2.264
HZ1  H    -4.311   -1.901   -4.349
HZ2  H    -4.123   -3.351   -3.591
HZ3  H    -5.553   -2.541   -3.475
HXT  H     3.145   -1.093    0.398   # C-terminus only
H3   H    -0.218    1.885    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-CD CG-HG2 CG-HG3 CD-CE CD-HD2 CD-HD3 CE-NZ
       CE-HE2 CE-HE3 NZ-HZ1 NZ-HZ2 NZ-HZ3 OXT-HXT N-H3
rotamer: mttt (Lovell 2000)
chi1: N-CA-CB-CG = -67
chi2: CA-CB-CG-CD = 180
chi3: CB-CG-CD-CE = 180
chi4: CG-CD-CE-NZ = 180
alternatives (most common first): tttt -177 180 180 180; mmtt -62 -68 180 180; mttm -67 180 180 -65;
  tttp -177 180 180 65; tptt -177 68 180 180; tttm -177 180 180 -65; mtpt -67 180 68 180; mttp -67 180 180 65;
  mtmt -67 180 -68 180; pttt 62 180 180 180; ttpt -177 180 68 180; ttmt -177 180 -68 180; ptpt 62 180 68 180;
  pttp 62 180 180 65; pttm 62 180 180 -65; tptp -177 68 180 65; tptm -177 68 180 -65; ttpp -177 180 68 65;
  mtpp -67 180 68 65; mtmm -67 180 -68 -65; mmtp -62 -68 180 65; mmtm -62 -68 180 -65; mmmt -62 -68 -68 180;
  ptmt 62 180 -68 180; ttmm -177 180 -68 -65; mptt -90 68 180 180
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1); keep all (NZ is +1)

@residue MET Methionine
N    N    -0.489    1.385    0.000
CA   C     0.000    0.000    0.000
C    C     1.506    0.000    0.000
O    O     2.111    0.905   -0.523
CB   C    -0.512   -0.720   -1.249
CG   C    -2.035   -0.829   -1.188
SD   S    -2.511   -1.889    0.204
CE   C    -1.949   -3.498   -0.417
OXT  O     2.177   -1.007    0.581   # C-terminus only
H    H    -0.130    1.820   -0.837
H2   H    -1.492    1.339   -0.105   # N-terminus only
HA   H    -0.364   -0.513    0.890
HB2  H    -0.077   -1.719   -1.295
HB3  H    -0.222   -0.157   -2.137
HG2  H    -2.405   -1.262   -2.117
HG3  H    -2.465    0.163   -1.053
HE1  H    -2.166   -4.269    0.321
HE2  H    -2.467   -3.729   -1.346
HE3  H    -0.875   -3.461   -0.598
HXT  H     3.144   -1.007    0.581   # C-terminus only
H3   H    -0.216    1.884    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-SD CG-HG2 CG-HG3 SD-CE CE-HE1 CE-HE2 CE-HE3
       OXT-HXT N-H3
rotamer: mmm (Lovell 2000)
chi1: N-CA-CB-CG = -65
chi2: CA-CB-CG-SD = -65
chi3: CB-CG-SD-CE = -70
alternatives (most common first): mtp -67 180 75; mtm -67 180 -75; mtt -67 180 180; ttm -177 180 -75;
  tpp -177 65 75; ttp -177 180 75; ptm 62 180 -75; ttt -177 180 180; mmp -65 -65 103; ptp 62 180 75;
  tpt -177 65 180; mmt -65 -65 180
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue PHE Phenylalanine
N    N    -0.490    1.384    0.000
CA   C     0.000    0.000    0.000
C    C     1.507   -0.000    0.000
O    O     2.110    0.905   -0.524
CB   C    -0.511   -0.722   -1.247
CG   C    -0.071   -2.161   -1.211
CD1  C    -0.762   -3.077   -0.442
CD2  C     1.023   -2.568   -1.954
CE1  C    -0.357   -4.398   -0.409
CE2  C     1.429   -3.888   -1.918
CZ   C     0.737   -4.804   -1.147
OXT  O     2.178   -1.006    0.581   # C-terminus only
H    H    -0.131    1.818   -0.837
H2   H    -1.492    1.337   -0.104   # N-terminus only
HA   H    -0.363   -0.513    0.890
HB2  H    -1.600   -0.675   -1.273
HB3  H    -0.108   -0.240   -2.137
HD1  H    -1.619   -2.760    0.134
HD2  H     1.562   -1.852   -2.557
HE1  H    -0.897   -5.113    0.193
HE2  H     2.286   -4.205   -2.495
HZ   H     1.054   -5.836   -1.121
HXT  H     3.145   -1.006    0.581   # C-terminus only
H3   H    -0.218    1.885    0.834   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG=CD1 CG-CD2 CD1-CE1 CD1-HD1 CD2=CE2 CD2-HD2
       CE1=CZ CE1-HE1 CE2-CZ CE2-HE2 CZ-HZ OXT-HXT N-H3
aromatic: CG CD1 CD2 CE1 CE2 CZ
rotamer: t80 (Lovell 2000)
chi1: N-CA-CB-CG = -177
chi2: CA-CB-CG-CD1 = 80
alternatives (most common first): m-85 -65 -85; p90 62 90; m-30 -65 -30
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue PRO Proline
N    N    -0.518    1.393    0.000
CA   C     0.000    0.000    0.000
C    C     1.508    0.000   -0.000
O    O     2.112    1.001   -0.306
CB   C    -0.538   -0.618   -1.308
CG   C    -1.788    0.215   -1.661
CD   C    -1.898    1.267   -0.537
OXT  O     2.178   -1.112    0.338   # C-terminus only
H    H    -0.606    1.673    0.965   # free Pro / N-terminus only
HA   H    -0.379   -0.546    0.864
HB2  H     0.206   -0.537   -2.100
HB3  H    -0.812   -1.661   -1.149
HG2  H    -1.656    0.705   -2.625
HG3  H    -2.675   -0.418   -1.674
HD2  H    -2.238    2.222   -0.940
HD3  H    -2.576    0.921    0.243
HXT  H     3.145   -1.112    0.338   # C-terminus only
H2   H     0.062    2.039   -0.517   # pH 7 form only
bonds: N-CA N-CD N-H CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG-CD CG-HG2 CG-HG3 CD-HD2 CD-HD3 OXT-HXT
       N-H2
ring: N-CA-CB-CG-CD (chi1 N-CA-CB-CG = -24 as given; do not rotate)
pH 7: add H2 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue SER Serine
N    N    -0.489    1.385    0.000
CA   C     0.000    0.000    0.000
C    C     1.507   -0.000   -0.000
O    O     2.111    0.905   -0.522
CB   C    -0.510   -0.720   -1.248
OG   O    -1.934   -0.823   -1.191
OXT  O     2.177   -1.007    0.582   # C-terminus only
H    H    -0.130    1.819   -0.836
H2   H    -1.491    1.339   -0.105   # N-terminus only
HA   H    -0.363   -0.513    0.890
HB2  H    -0.222   -0.157   -2.136
HB3  H    -0.076   -1.719   -1.294
HG   H    -2.214   -1.282   -1.994
HXT  H     3.144   -1.008    0.582   # C-terminus only
H3   H    -0.217    1.885    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-OG CB-HB2 CB-HB3 OG-HG OXT-HXT N-H3
rotamer: m (Lovell 2000)
chi1: N-CA-CB-OG = -65
alternatives (most common first): p 62; t -177
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue THR Threonine
N    N    -0.488    1.386    0.000
CA   C     0.000    0.000    0.000
C    C     1.506   -0.000   -0.000
O    O     2.111    0.904   -0.522
CB   C    -0.511   -0.719   -1.249
OG1  O    -1.935   -0.820   -1.192
CG2  C     0.097   -2.122   -1.316
OXT  O     2.176   -1.008    0.581   # C-terminus only
H    H    -0.129    1.820   -0.836
H2   H    -1.491    1.341   -0.104   # N-terminus only
HA   H    -0.364   -0.512    0.890
HB   H    -0.222   -0.155   -2.136
HG1  H    -2.148   -1.324   -0.396
HG21 H    -0.267   -2.633   -2.207
HG22 H     1.183   -2.045   -1.360
HG23 H    -0.192   -2.685   -0.431
HXT  H     3.143   -1.009    0.581   # C-terminus only
H3   H    -0.215    1.885    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-OG1 CB-CG2 CB-HB OG1-HG1 CG2-HG21 CG2-HG22 CG2-HG23 OXT-HXT
       N-H3
rotamer: m (Lovell 2000)
chi1: N-CA-CB-OG1 = -65
alternatives (most common first): p 62; t -175
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue TRP Tryptophan
N    N    -0.489    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.507    0.000   -0.000
O    O     2.111    0.906   -0.523
CB   C    -0.510   -0.722   -1.247
CG   C    -0.069   -2.162   -1.209
CD1  C     0.912   -2.716   -1.938
CD2  C    -0.625   -3.220   -0.364
NE1  N     1.026   -4.046   -1.633
CE2  C     0.098   -4.387   -0.675
CE3  C    -1.650   -3.269    0.583
CZ2  C    -0.213   -5.575   -0.023
CZ3  C    -1.937   -4.447    1.212
CH2  C    -1.223   -5.599    0.913
OXT  O     2.178   -1.007    0.581   # C-terminus only
H    H    -0.130    1.819   -0.838
H2   H    -1.492    1.339   -0.104   # N-terminus only
HA   H    -0.363   -0.513    0.890
HB2  H    -1.599   -0.675   -1.274
HB3  H    -0.104   -0.241   -2.137
HD1  H     1.522   -2.193   -2.658
HE1  H     1.666   -4.656   -2.030
HE3  H    -2.210   -2.376    0.821
HZ2  H     0.336   -6.477   -0.250
HZ3  H    -2.726   -4.483    1.948
HH2  H    -1.463   -6.523    1.417
HXT  H     3.145   -1.007    0.582   # C-terminus only
H3   H    -0.215    1.885    0.834   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG=CD1 CG-CD2 CD1-NE1 CD1-HD1 CD2=CE2 CD2-CE3
       NE1-CE2 NE1-HE1 CE2-CZ2 CE3=CZ3 CE3-HE3 CZ2=CH2 CZ2-HZ2 CZ3-CH2 CZ3-HZ3 CH2-HH2 OXT-HXT N-H3
aromatic: CG CD1 CD2 NE1 CE2 CE3 CZ2 CZ3 CH2
rotamer: t-105 (Lovell 2000)
chi1: N-CA-CB-CG = -177
chi2: CA-CB-CG-CD1 = -105
alternatives (most common first): m95 -65 95; t90 -177 90; p-90 62 -90; m0 -65 -5; p90 62 90; m-90 -65 -90
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue TYR Tyrosine
N    N    -0.490    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.507    0.000    0.000
O    O     2.111    0.905   -0.522
CB   C    -0.510   -0.721   -1.248
CG   C    -0.070   -2.161   -1.212
CD1  C    -0.764   -3.075   -0.441
CD2  C     1.023   -2.567   -1.954
CE1  C    -0.362   -4.396   -0.406
CE2  C     1.431   -3.886   -1.919
CZ   C     0.737   -4.806   -1.146
OH   O     1.134   -6.105   -1.114
OXT  O     2.177   -1.007    0.582   # C-terminus only
H    H    -0.130    1.819   -0.837
H2   H    -1.492    1.337   -0.105   # N-terminus only
HA   H    -0.364   -0.513    0.891
HB2  H    -1.598   -0.673   -1.276
HB3  H    -0.104   -0.239   -2.138
HD1  H    -1.620   -2.756    0.133
HD2  H     1.563   -1.852   -2.556
HE1  H    -0.904   -5.110    0.196
HE2  H     2.288   -4.203   -2.494
HH   H     0.664   -6.560   -1.825
HXT  H     3.145   -1.006    0.583   # C-terminus only
H3   H    -0.218    1.885    0.834   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG CB-HB2 CB-HB3 CG=CD1 CG-CD2 CD1-CE1 CD1-HD1 CD2=CE2 CD2-HD2
       CE1=CZ CE1-HE1 CE2-CZ CE2-HE2 CZ-OH OH-HH OXT-HXT N-H3
aromatic: CG CD1 CD2 CE1 CE2 CZ
rotamer: t80 (Lovell 2000)
chi1: N-CA-CB-CG = -177
chi2: CA-CB-CG-CD1 = 80
alternatives (most common first): m-85 -65 -85; p90 62 90; m-30 -65 -30
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)

@residue VAL Valine
N    N    -0.489    1.385   -0.000
CA   C     0.000    0.000    0.000
C    C     1.506    0.000   -0.000
O    O     2.110    0.905   -0.523
CB   C    -0.510   -0.720   -1.248
CG1  C     0.099   -2.122   -1.314
CG2  C    -2.034   -0.829   -1.188
OXT  O     2.177   -1.007    0.582   # C-terminus only
H    H    -0.131    1.819   -0.837
H2   H    -1.492    1.338   -0.105   # N-terminus only
HA   H    -0.363   -0.514    0.890
HB   H    -0.221   -0.157   -2.136
HG11 H    -0.265   -2.636   -2.205
HG12 H     1.185   -2.044   -1.358
HG13 H    -0.190   -2.685   -0.428
HG21 H    -2.323   -1.392   -0.300
HG22 H    -2.469    0.169   -1.141
HG23 H    -2.399   -1.342   -2.078
HXT  H     3.144   -1.007    0.581   # C-terminus only
H3   H    -0.217    1.884    0.835   # pH 7 form only
bonds: N-CA N-H N-H2 CA-C CA-CB CA-HA C=O C-OXT CB-CG1 CB-CG2 CB-HB CG1-HG11 CG1-HG12 CG1-HG13 CG2-HG21 CG2-HG22
       CG2-HG23 OXT-HXT N-H3
rotamer: t (Lovell 2000)
chi1: N-CA-CB-CG1 = 175
alternatives (most common first): m -60; p 63
pH 7: add H3 on N (N becomes +1); remove HXT (OXT becomes −1)
""";

    /// <summary>§6.4: the peptide builder's test vectors (Ala₄ α-helix; Gly₇ β-strand S1 and S2).</summary>
    public const string PeptideTestText = """
Ala4, α-helix (φ −57°, ψ −47°, ω 180°): every heavy atom, residue 0′s template H (N-terminal), and the computed amide H of residues 1–3
0 ALA N       0.000    0.000    0.000
0 ALA CA      1.458    0.000    0.000
0 ALA C       2.009    1.422    0.000
0 ALA O       2.910    1.735    0.779
0 ALA CB      1.946   -0.736   -1.249
0 ALA H      -0.285    0.535   -0.836
1 ALA N       1.463    2.263   -0.872
1 ALA CA      1.899    3.650   -0.974
1 ALA C       1.768    4.370    0.364
1 ALA O       2.698    5.055    0.790
1 ALA CB      1.036    4.358   -2.020
1 ALA H       0.727    1.932   -1.480
2 ALA N       0.618    4.205    1.008
2 ALA CA      0.364    4.838    2.297
2 ALA C       1.421    4.443    3.323
2 ALA O       1.953    5.304    4.024
2 ALA CB     -1.015    4.400    2.793
2 ALA H      -0.100    3.626    0.595
3 ALA N       1.711    3.149    3.398
3 ALA CA      2.704    2.639    4.337
3 ALA C       4.057    3.309    4.126
3 ALA O       4.690    3.741    5.090
3 ALA OXT     4.473    3.391    2.950
3 ALA CB      2.844    1.131    4.128
3 ALA H       1.232    2.501    2.789

Gly7 β-strand S1 (φ −139°, ψ 135°): CA atoms
0 CA    1.458    0.000    0.000
1 CA    3.625    2.981    0.942
2 CA    7.314    3.696    0.352
3 CA    9.466    6.705    1.237
4 CA   13.169    7.393    0.704
5 CA   15.309   10.427    1.532
6 CA   19.024   11.090    1.057
S2 = R·S1 + t, CA atoms (antiparallel, its residue 6 opposite S1 residue 0)
0 CA   16.074   13.823   -2.443
1 CA   12.770   12.436   -1.165
2 CA   10.213   10.133   -2.787
3 CA    6.941    8.695   -1.485
4 CA    4.353    6.444   -3.129
5 CA    1.112    4.954   -1.807
6 CA   -1.508    2.754   -3.470
""";

    /// <summary>§6.4: the β-sheet demo's transforms (S2 = R·S1 + t, S3 = S1 + Δ).</summary>
    public const string SheetTransformText = """
R = [[-0.70504, -0.41370, -0.57600], [-0.41370, -0.41977, 0.80787], [-0.57600, 0.80787, 0.12481]]
t = [17.1016, 14.4261, -1.6037]
Δ = [-4.4865, 5.6017, -6.3208]   (S3 = S1 + Δ)
""";

    /// <summary>§6.6: the eight nucleotides and ATP (molecule blocks).</summary>
    public const string NucleotideText = """
@molecule DA dAMP (deoxyadenosine monophosphate)   (C10 H14 N5 O6 P, M = 331.222 g/mol, CCD DA)
OP3  O     5.672    2.298    1.319
P    P     5.036    1.359    0.176
OP1  O     4.592    2.198   -0.960
OP2  O     6.145    0.310   -0.333
O5'  O     3.776    0.560    0.778
C5'  C     3.257   -0.250   -0.278
C4'  C     2.050   -1.041    0.230
O4'  O     0.958   -0.153    0.561
C3'  C     1.467   -1.931   -0.897
O3'  O     2.144   -3.187   -0.958
C2'  C     0.002   -2.116   -0.437
C1'  C    -0.242   -0.941    0.529
N9   N    -1.360   -0.124    0.049
C8   C    -1.276    0.941   -0.795
N7   N    -2.461    1.433   -1.011
C5   C    -3.380    0.712   -0.325
C6   C    -4.775    0.778   -0.171
N6   N    -5.513    1.750   -0.825
N1   N    -5.370   -0.116    0.612
C2   C    -4.673   -1.044    1.239
N3   N    -3.366   -1.143    1.130
C4   C    -2.683   -0.292    0.369
HOP3 H     6.422    2.758    0.917
HOP2 H     6.407   -0.215    0.434
H5'  H     4.029   -0.942   -0.617
H5'' H     2.952    0.386   -1.108
H4'  H     2.326   -1.646    1.093
H3'  H     1.512   -1.419   -1.858
HO3' H     1.677   -3.726   -1.611
H2'  H    -0.120   -3.067    0.079
H2'' H    -0.676   -2.055   -1.290
H1'  H    -0.463   -1.321    1.527
H8   H    -0.360    1.322   -1.222
H61  H    -6.476    1.783   -0.714
H62  H    -5.067    2.399   -1.391
H2   H    -5.197   -1.751    1.866
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-C1' C2'-H2' C2'-H2'' C1'-N9 C1'-H1' N9-C8 N9-C4 C8=N7 C8-H8 N7-C5
       C5-C6 C5=C4 C6-N6 C6=N1 N6-H61 N6-H62 N1-C2 C2=N3 C2-H2 N3-C4
aromatic: N9 C8 N7 C5 C6 N1 C2 N3 C4
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule DC dCMP   (C9 H14 N3 O7 P, M = 307.197 g/mol, CCD DC)
OP3  O     4.830    2.490    1.192
P    P     4.180    1.471    0.128
OP1  O     3.604    2.238   -0.999
OP2  O     5.315    0.474   -0.424
O5'  O     3.017    0.618    0.845
C5'  C     2.482   -0.262   -0.144
C4'  C     1.367   -1.107    0.474
O4'  O     0.245   -0.278    0.853
C3'  C     0.765   -2.073   -0.577
O3'  O     1.518   -3.286   -0.644
C2'  C    -0.650   -2.332   -0.009
C1'  C    -0.902   -1.139    0.933
N1   N    -2.101   -0.413    0.507
C2   C    -2.154    0.127   -0.723
O2   O    -1.200    0.005   -1.474
N3   N    -3.232    0.792   -1.136
C4   C    -4.282    0.940   -0.341
N4   N    -5.390    1.630   -0.777
C5   C    -4.256    0.390    0.957
C6   C    -3.156   -0.287    1.360
HOP3 H     5.519    2.980    0.723
HOP2 H     5.663   -0.004    0.341
H5'  H     3.271   -0.917   -0.513
H5'' H     2.078    0.322   -0.971
H4'  H     1.740   -1.661    1.336
H3'  H     0.711   -1.597   -1.556
HO3' H     1.041   -3.877   -1.245
H2'  H    -0.672   -3.269    0.546
H2'' H    -1.387   -2.345   -0.812
H1'  H    -1.028   -1.495    1.956
H41  H    -5.403    2.010   -1.670
H42  H    -6.158    1.733   -0.195
H5   H    -5.104    0.502    1.617
H6   H    -3.114   -0.721    2.348
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-C1' C2'-H2' C2'-H2'' C1'-N1 C1'-H1' N1-C2 N1-C6 C2=O2 C2-N3 N3=C4
       C4-N4 C4-C5 N4-H41 N4-H42 C5=C6 C5-H5 C6-H6
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule DG dGMP   (C10 H14 N5 O7 P, M = 347.221 g/mol, CCD DG)
OP3  O    -5.993    1.720   -1.770
P    P    -5.361    0.872   -0.556
OP1  O    -5.109    1.771    0.592
OP2  O    -6.390   -0.283   -0.117
O5'  O    -3.975    0.206   -1.031
C5'  C    -3.467   -0.528    0.083
C4'  C    -2.143   -1.190   -0.303
O4'  O    -1.128   -0.194   -0.563
C3'  C    -1.566   -1.995    0.890
O3'  O    -2.109   -3.317    0.923
C2'  C    -0.055   -2.026    0.559
C1'  C     0.144   -0.846   -0.410
N9   N     1.122    0.094    0.141
C8   C     0.851    1.157    0.951
N7   N     1.956    1.776    1.252
C5   C     2.998    1.148    0.655
C6   C     4.396    1.367    0.627
O6   O     4.894    2.298    1.237
N1   N     5.164    0.516   -0.087
C2   C     4.596   -0.517   -0.769
N2   N     5.401   -1.361   -1.492
N3   N     3.299   -0.731   -0.752
C4   C     2.475    0.065   -0.061
HOP3 H    -6.820    2.102   -1.445
HOP2 H    -6.528   -0.845   -0.891
H5'  H    -4.186   -1.294    0.372
H5'' H    -3.302    0.150    0.921
H4'  H    -2.276   -1.835   -1.171
H3'  H    -1.748   -1.478    1.832
HO3' H    -1.644   -3.792    1.625
H2'  H     0.211   -2.966    0.076
H2'' H     0.536   -1.880    1.463
H1'  H     0.489   -1.215   -1.377
H8   H    -0.133    1.444    1.292
H1   H     6.125    0.647   -0.121
H21  H     5.011   -2.103   -1.979
H22  H     6.359   -1.213   -1.518
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-C1' C2'-H2' C2'-H2'' C1'-N9 C1'-H1' N9-C8 N9-C4 C8=N7 C8-H8 N7-C5
       C5-C6 C5=C4 C6=O6 C6-N1 N1-C2 N1-H1 C2-N2 C2=N3 N2-H21 N2-H22 N3-C4
aromatic: N9 C8 N7 C5 C4
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule DT dTMP (thymidine monophosphate)   (C10 H15 N2 O8 P, M = 322.208 g/mol, CCD DT)
OP3  O    -2.189   -2.620   -0.741
P    P    -3.224   -2.097    0.386
OP1  O    -3.458   -3.042    1.529
OP2  O    -4.543   -1.716   -0.467
O5'  O    -2.635   -0.649    0.807
C5'  C    -2.385    0.300   -0.216
C4'  C    -1.840    1.574    0.418
O4'  O    -0.590    1.249    1.057
C3'  C    -1.527    2.658   -0.604
O3'  O    -1.606    3.942    0.011
C2'  C    -0.095    2.363   -0.988
C1'  C     0.488    1.827    0.309
N1   N     1.501    0.835    0.094
C2   C     2.867    1.185    0.034
O2   O     3.286    2.337    0.160
N3   N     3.744    0.116   -0.181
C4   C     3.407   -1.220   -0.338
O4   O     4.231   -2.111   -0.524
C5   C     1.951   -1.510   -0.264
C7   C     1.507   -2.929   -0.425
C6   C     1.109   -0.489   -0.058
HOP3 H    -2.302   -3.524   -1.106
HOP2 H    -5.140   -2.436   -0.764
H5'  H    -1.658   -0.115   -0.917
H5'' H    -3.319    0.509   -0.743
H4'  H    -2.532    1.921    1.191
H3'  H    -2.210    2.662   -1.458
HO3' H    -2.195    4.489   -0.537
H2'  H    -0.073    1.593   -1.769
H2'' H     0.458    3.235   -1.350
H1'  H     0.928    2.628    0.913
H3   H     4.732    0.344   -0.227
H71  H     1.812   -3.300   -1.402
H72  H     0.422   -2.988   -0.338
H73  H     1.957   -3.545    0.353
H6   H     0.038   -0.657    0.003
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-C1' C2'-H2' C2'-H2'' C1'-N1 C1'-H1' N1-C2 N1-C6 C2=O2 C2-N3 N3-C4
       N3-H3 C4=O4 C4-C5 C5-C7 C5=C6 C7-H71 C7-H72 C7-H73 C6-H6
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule A AMP (adenosine monophosphate)   (C10 H14 N5 O7 P, M = 347.221 g/mol, CCD A)
OP3  O     5.570    2.685    1.067
P    P     5.075    1.507    0.088
OP1  O     4.783    2.067   -1.249
OP2  O     6.231    0.394   -0.037
O5'  O     3.743    0.832    0.690
C5'  C     3.355   -0.199   -0.217
C4'  C     2.088   -0.883    0.298
O4'  O     1.002    0.065    0.380
C3'  C     1.604   -1.953   -0.705
O3'  O     2.039   -3.252   -0.302
C2'  C     0.063   -1.854   -0.646
O2'  O    -0.502   -3.091   -0.206
C1'  C    -0.201   -0.734    0.384
N9   N    -1.351    0.074   -0.023
C8   C    -1.320    1.173   -0.829
N7   N    -2.523    1.645   -0.984
C5   C    -3.401    0.879   -0.294
C6   C    -4.790    0.909   -0.088
N6   N    -5.572    1.888   -0.677
N1   N    -5.338   -0.026    0.680
C2   C    -4.599   -0.963    1.246
N3   N    -3.295   -1.027    1.087
C4   C    -2.659   -0.135    0.335
HOP3 H     6.368    3.061    0.670
HOP2 H     6.390    0.054    0.854
H5'  H     4.157   -0.933   -0.298
H5'' H     3.160    0.235   -1.198
H4'  H     2.272   -1.335    1.273
H3'  H     1.964   -1.725   -1.708
HO3' H     1.709   -3.875   -0.964
H2'  H    -0.341   -1.576   -1.620
HO2' H    -0.253   -3.760   -0.859
H1'  H    -0.369   -1.161    1.373
H8   H    -0.429    1.589   -1.274
H61  H    -6.530    1.896   -0.531
H62  H    -5.160    2.568   -1.233
H2   H    -5.085   -1.704    1.864
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-O2' C2'-C1' C2'-H2' O2'-HO2' C1'-N9 C1'-H1' N9-C8 N9-C4 C8=N7 C8-H8
       N7-C5 C5-C6 C5=C4 C6-N6 C6=N1 N6-H61 N6-H62 N1-C2 C2=N3 C2-H2 N3-C4
aromatic: N9 C8 N7 C5 C6 N1 C2 N3 C4
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule C CMP   (C9 H14 N3 O8 P, M = 323.197 g/mol, CCD C)
OP3  O     4.728    2.870    0.964
P    P     4.247    1.640    0.043
OP1  O     3.823    2.149   -1.279
OP2  O     5.465    0.604   -0.144
O5'  O     3.011    0.890    0.752
C5'  C     2.632   -0.185   -0.108
C4'  C     1.455   -0.942    0.508
O4'  O     0.314   -0.067    0.652
C3'  C     0.976   -2.063   -0.439
O3'  O     1.527   -3.321   -0.045
C2'  C    -0.562   -2.067   -0.271
O2'  O    -1.007   -3.329    0.229
C1'  C    -0.827   -0.945    0.757
N1   N    -2.058   -0.225    0.418
C2   C    -2.172    0.377   -0.778
O2   O    -1.248    0.317   -1.572
N3   N    -3.279    1.038   -1.112
C4   C    -4.299    1.120   -0.268
N4   N    -5.437    1.806   -0.623
C5   C    -4.208    0.504    0.997
C6   C    -3.079   -0.170    1.318
HOP3 H     5.468    3.290    0.505
HOP2 H     5.710    0.297    0.737
H5'  H     3.475   -0.863   -0.233
H5'' H     2.338    0.214   -1.080
H4'  H     1.737   -1.358    1.475
H3'  H     1.248   -1.833   -1.469
HO3' H     1.195   -3.980   -0.670
H2'  H    -1.050   -1.839   -1.217
HO2' H    -0.758   -3.994   -0.428
H1'  H    -0.896   -1.360    1.762
H41  H    -5.496    2.232   -1.492
H42  H    -6.183    1.861   -0.006
H5   H    -5.031    0.561    1.694
H6   H    -2.987   -0.653    2.280
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-O2' C2'-C1' C2'-H2' O2'-HO2' C1'-N1 C1'-H1' N1-C2 N1-C6 C2=O2 C2-N3
       N3=C4 C4-N4 C4-C5 N4-H41 N4-H42 C5=C6 C5-H5 C6-H6
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule G GMP   (C10 H14 N5 O8 P, M = 363.221 g/mol, CCD G)
OP3  O    -5.959   -2.076    1.500
P    P    -5.415   -0.976    0.457
OP1  O    -5.300   -1.585   -0.886
OP2  O    -6.444    0.261    0.397
O5'  O    -3.971   -0.451    0.935
C5'  C    -3.544    0.515   -0.025
C4'  C    -2.170    1.057    0.371
O4'  O    -1.195   -0.009    0.388
C3'  C    -1.651    2.048   -0.692
O3'  O    -1.901    3.396   -0.287
C2'  C    -0.131    1.774   -0.754
O2'  O     0.604    2.944   -0.390
C1'  C     0.086    0.647    0.278
N9   N     1.102   -0.295   -0.200
C8   C     0.878   -1.395   -0.975
N7   N     2.004   -2.003   -1.213
C5   C     3.013   -1.331   -0.606
C6   C     4.412   -1.523   -0.519
O6   O     4.948   -2.468   -1.073
N1   N     5.139   -0.630    0.186
C2   C     4.528    0.418    0.806
N2   N     5.291    1.306    1.521
N3   N     3.230    0.607    0.733
C4   C     2.446   -0.232    0.048
HOP3 H    -6.824   -2.363    1.177
HOP2 H    -6.490    0.629    1.291
H5'  H    -4.262    1.336   -0.059
H5'' H    -3.481    0.049   -1.008
H4'  H    -2.220    1.541    1.347
H3'  H    -2.116    1.850   -1.658
HO3' H    -1.558    3.967   -0.987
H2'  H     0.157    1.438   -1.751
HO2' H     0.380    3.628   -1.037
H1'  H     0.383    1.065    1.240
H8   H    -0.087   -1.715   -1.338
H1   H     6.100   -0.740    0.259
H21  H     4.871    2.061    1.964
H22  H     6.250    1.178    1.586
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-O2' C2'-C1' C2'-H2' O2'-HO2' C1'-N9 C1'-H1' N9-C8 N9-C4 C8=N7 C8-H8
       N7-C5 C5-C6 C5=C4 C6=O6 C6-N1 N1-C2 N1-H1 C2-N2 C2=N3 N2-H21 N2-H22 N3-C4
aromatic: N9 C8 N7 C5 C4
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule U UMP   (C9 H13 N2 O9 P, M = 324.181 g/mol, CCD U)
OP3  O     4.950    2.451    0.987
P    P     4.370    1.264    0.066
OP1  O     4.003    1.804   -1.261
OP2  O     5.495    0.126   -0.109
O5'  O     3.068    0.628    0.766
C5'  C     2.604   -0.413   -0.094
C4'  C     1.361   -1.063    0.516
O4'  O     0.298   -0.091    0.647
C3'  C     0.790   -2.142   -0.429
O3'  O     1.221   -3.443   -0.023
C2'  C    -0.741   -2.008   -0.276
O2'  O    -1.303   -3.223    0.225
C1'  C    -0.917   -0.864    0.746
N1   N    -2.078   -0.041    0.395
C2   C    -2.110    0.592   -0.790
O2   O    -1.178    0.472   -1.560
N3   N    -3.166    1.351   -1.139
C4   C    -4.208    1.496   -0.296
O4   O    -5.161    2.186   -0.610
C5   C    -4.183    0.838    0.957
C6   C    -3.114    0.079    1.280
HOP3 H     5.727    2.804    0.532
HOP2 H     5.705   -0.198    0.776
H5'  H     3.386   -1.164   -0.209
H5'' H     2.354    0.005   -1.068
H4'  H     1.599   -1.499    1.487
H3'  H     1.090   -1.946   -1.458
HO3' H     0.811   -4.076   -0.629
H2'  H    -1.199   -1.743   -1.229
HO2' H    -1.108   -3.911   -0.425
H1'  H    -1.029   -1.267    1.752
H3   H    -3.175    1.799   -2.000
H5   H    -5.009    0.942    1.645
H6   H    -3.078   -0.430    2.231
bonds: OP3-P OP3-HOP3 P=OP1 P-OP2 P-O5' OP2-HOP2 O5'-C5' C5'-C4' C5'-H5' C5'-H5'' C4'-O4' C4'-C3' C4'-H4' O4'-C1'
       C3'-O3' C3'-C2' C3'-H3' O3'-HO3' C2'-O2' C2'-C1' C2'-H2' O2'-HO2' C1'-N1 C1'-H1' N1-C2 N1-C6 C2=O2 C2-N3
       N3-C4 N3-H3 C4=O4 C4-C5 C5=C6 C5-H5 C6-H6
pH 7: remove HOP2 and HOP3 (phosphate −2)

@molecule ATP ATP (adenosine triphosphate)   (C10 H16 N5 O13 P3, M = 507.181 g/mol, CCD ATP)
PG   P     7.631   -1.458   -0.123
O1G  O     7.415   -1.992    1.240
O2G  O     8.650   -2.418   -0.919
O3G  O     8.253    0.023   -0.027
PB   P     5.260   -0.429   -0.060
O1B  O     5.080   -0.979    1.302
O2B  O     5.922    1.034    0.035
O3B  O     6.226   -1.400   -0.906
PA   P     2.920    0.654    0.106
O1A  O     3.564    1.984    0.190
O2A  O     2.763    0.039    1.585
O3A  O     3.829   -0.326   -0.790
O5'  O     1.466    0.805   -0.569
C5'  C     0.711    1.682    0.267
C4'  C    -0.689    1.869   -0.321
O4'  O    -1.384    0.603   -0.377
C3'  C    -1.552    2.751    0.606
O3'  O    -1.567    4.103    0.140
C2'  C    -2.965    2.129    0.511
O2'  O    -3.896    3.078   -0.014
C1'  C    -2.784    0.941   -0.459
N9   N    -3.609   -0.193   -0.038
C8   C    -3.243   -1.177    0.831
N7   N    -4.218   -2.025    0.981
C5   C    -5.273   -1.636    0.224
C6   C    -6.558   -2.149   -0.017
N6   N    -6.986   -3.307    0.606
N1   N    -7.356   -1.492   -0.852
C2   C    -6.957   -0.386   -1.451
N3   N    -5.759    0.128   -1.261
C4   C    -4.893   -0.459   -0.442
HOG2 H     9.477   -2.424   -0.417
HOG3 H     8.378    0.333   -0.935
HOB2 H     6.021    1.355   -0.871
HOA2 H     2.340   -0.824    1.485
H5'1 H     1.211    2.649    0.326
H5'2 H     0.631    1.253    1.265
H4'  H    -0.626    2.311   -1.316
H3'  H    -1.181    2.706    1.630
HO3' H    -2.118    4.604    0.756
H2'  H    -3.292    1.775    1.489
HO2' H    -3.918    3.820    0.605
H1'  H    -3.044    1.240   -1.475
H8   H    -2.283   -1.243    1.323
HN61 H    -7.878   -3.650    0.436
HN62 H    -6.394   -3.780    1.212
H2   H    -7.637    0.117   -2.123
bonds: PG=O1G PG-O2G PG-O3G PG-O3B O2G-HOG2 O3G-HOG3 PB=O1B PB-O2B PB-O3B PB-O3A O2B-HOB2 PA=O1A PA-O2A PA-O3A
       PA-O5' O2A-HOA2 O5'-C5' C5'-C4' C5'-H5'1 C5'-H5'2 C4'-O4' C4'-C3' C4'-H4' O4'-C1' C3'-O3' C3'-C2' C3'-H3'
       O3'-HO3' C2'-O2' C2'-C1' C2'-H2' O2'-HO2' C1'-N9 C1'-H1' N9-C8 N9-C4 C8=N7 C8-H8 N7-C5 C5-C6 C5=C4 C6-N6
       C6=N1 N6-HN61 N6-HN62 N1-C2 C2=N3 C2-H2 N3-C4
aromatic: N9 C8 N7 C5 C6 N1 C2 N3 C4
pH 7: remove HOG2 HOG3 HOB2 HOA2 (ATP⁴⁻)
""";

    /// <summary>§6.7: one strand-I nucleotide of B-DNA in helical coordinates (name, x, r, θ°).</summary>
    public const string BdnaText = """
# B-DNA strand I nucleotide, heavy atoms: name  x  r  theta_deg   (base pair k adds k*rise to x and k*twist to theta)
# backbone + sugar (same for every base)
P       0.000    8.910      0.00
OP1     0.220   10.200      4.13
OP2     0.790    8.820     -8.08
O5'     0.250    7.730      7.22
C5'    -0.690    7.700     15.41
C4'     0.040    7.590     25.32
O4'     0.250    6.220     29.22
C3'     1.440    8.200     25.32
O3'     1.830    8.750     33.80
C2'     2.320    7.040     22.00
C1'     1.610    5.860     27.79
# base A
N9      1.656    4.669     18.40
C8      1.573    4.876      2.21
N7      1.656    3.919     -9.31
C5      1.803    2.720      1.47
C6      1.931    1.388    -11.15
N6      1.937    1.859    -58.93
N1      2.053    0.855     56.40
C2      2.048    2.151     66.79
N3      1.925    3.150     48.04
C4      1.798    3.384     25.03
# base C
N1      1.661    4.626     18.83
C2      1.806    3.426     27.32
O2      1.896    3.667     46.70
N3      1.855    2.340     11.72
C4      1.761    2.931    -14.38
N4      1.813    2.766    -42.11
C5      1.612    4.320    -11.29
C6      1.560    4.996      3.20
# base G
N9      1.657    4.653     18.24
C8      1.575    4.851      1.94
N7      1.659    3.887     -9.56
C5      1.800    2.696      1.63
C6      1.935    1.351    -10.96
O6      1.940    1.718    -55.80
N1      2.065    0.863     59.95
C2      2.052    2.212     67.52
N2      2.178    3.044     91.63
N3      1.926    3.173     48.24
C4      1.801    3.376     25.04
# base T
N1      1.660    4.647     18.93
C2      1.814    3.414     28.58
O2      1.906    3.631     48.36
N3      1.856    2.335     10.47
C4      1.763    2.953    -16.79
O4      1.806    2.853    -41.11
C5      1.604    4.399    -11.87
C7      1.497    5.441    -24.39
C6      1.563    4.987      2.89
""";

    /// <summary>§6.7: the duplex 5′-AC-3′'s test vectors, in the app's frame.</summary>
    public const string BdnaTestText = """
# test vectors: duplex 5'-AC-3' (strand I) with rise 3.40 Å, twist 360/10.5°; app frame X = r·sin θ, Y = x, Z = r·cos θ
strand 1 bp 0 P     X    0.000  Y    0.000  Z    8.910
strand 1 bp 0 C1'   X    2.732  Y    1.610  Z    5.184
strand 1 bp 0 N9    X    1.474  Y    1.656  Z    4.430
strand 1 bp 1 P     X    5.019  Y    3.400  Z    7.362
strand 1 bp 1 O3'   X    8.118  Y    5.230  Z    3.266
strand 2 bp 0 P     X   -1.609  Y    4.160  Z   -8.763
strand 2 bp 0 C1'   X    1.751  Y    2.550  Z   -5.592
strand 2 bp 0 N3    X    0.003  Y    2.304  Z   -2.335
strand 2 bp 1 P     X   -6.266  Y    7.560  Z   -6.334
strand 2 bp 1 N1    X    0.227  Y    5.495  Z   -0.833
""";

    /// <summary>§6.7: the A·T and G·C pairs in 2D (Å), for the Nucleotides page.</summary>
    public const string BasePairText = """
# A·T pair, 2D (Å): origin midway between the C1' atoms, +x toward the T C1', +y toward the major groove
# A (strand I)
N9    -4.565   1.184
C8    -4.895   2.504
N7    -3.816   3.232
C5    -2.728   2.427
C6    -1.340   2.641
N6    -0.821   3.923
N1    -0.536   1.584
C2    -1.023   0.359
N3    -2.315   0.109
C4    -3.196   1.103
H8    -5.903   2.890
H61    0.140   4.058
H62   -1.418   4.687
H2    -0.334  -0.472
C1'   -5.431   0.000
# T (strand II)
N1     4.534   1.148
C2     3.145   0.896
O2     2.654  -0.233
N3     2.336   2.036
C4     2.756   3.357
O4     1.987   4.314
C5     4.231   3.543
C7     4.764   4.938
C6     5.009   2.452
H3     1.332   1.879
H71    4.436   5.489
H72    5.853   4.917
H73    4.398   5.438
H6     6.091   2.544
C1'    5.431  -0.000

# G·C pair, 2D (Å): origin midway between the C1' atoms, +x toward the C C1', +y toward the major groove
# G (strand I)
N9    -4.552   1.201
C8    -4.869   2.527
N7    -3.781   3.241
C5    -2.705   2.419
C6    -1.306   2.627
O6    -0.842   3.754
N1    -0.497   1.546
C2    -1.026   0.291
N2    -0.181  -0.788
N3    -2.324   0.085
C4    -3.188   1.105
H8    -5.873   2.925
H1     0.466   1.665
H21   -0.543  -1.687
H22    0.780  -0.651
C1'   -5.431   0.000
# C (strand II)
N1     4.515   1.161
C2     3.186   0.960
O2     2.752  -0.179
N3     2.335   1.985
C4     2.779   3.233
N4     1.891   4.283
C5     4.167   3.477
C6     5.019   2.425
H41    0.937   4.112
H42    2.220   5.195
H5     4.546   4.488
H6     6.086   2.587
C1'    5.431  -0.000
""";

    /// <summary>§6.8: the sugars and disaccharides (molecule blocks).</summary>
    public const string SugarText = """
@molecule GLC α-D-glucose (α-D-glucopyranose)   (C6 H12 O6, M = 180.156 g/mol, CCD GLC)
C1   C    -0.754    1.495   -0.242
C2   C    -1.628    0.275   -0.542
C3   C    -1.083   -0.935    0.224
C4   C     0.381   -1.157   -0.169
C5   C     1.177    0.118    0.115
C6   C     2.627   -0.075   -0.332
O1   O    -0.821    1.798    1.152
O2   O    -2.971    0.536   -0.128
O3   O    -1.848   -2.095   -0.106
O4   O     0.921   -2.238    0.593
O5   O     0.599    1.210   -0.604
O6   O     3.398    1.070    0.039
H1   H    -1.112    2.348   -0.818
H2   H    -1.610    0.066   -1.612
H3   H    -1.148   -0.746    1.296
H4   H     0.440   -1.394   -1.233
H5   H     1.151    0.333    1.184
H61  H     3.041   -0.961    0.149
H62  H     2.661   -0.200   -1.414
HO1  H    -0.290    2.563    1.415
HO2  H    -3.379    1.293   -0.571
HO3  H    -2.788   -2.022    0.110
HO4  H     0.461   -3.079    0.464
HO6  H     4.329    1.017   -0.214
bonds: C1-C2 C1-O1 C1-O5 C1-H1 C2-C3 C2-O2 C2-H2 C3-C4 C3-O3 C3-H3 C4-C5 C4-O4 C4-H4 C5-C6 C5-O5 C5-H5 C6-O6
       C6-H61 C6-H62 O1-HO1 O2-HO2 O3-HO3 O4-HO4 O6-HO6

@molecule BGC β-D-glucose (β-D-glucopyranose)   (C6 H12 O6, M = 180.156 g/mol, CCD BGC)
C2   C    -1.613   -0.161    0.298
C3   C    -0.977    1.120   -0.250
C4   C     0.487    1.182    0.200
C5   C     1.213   -0.078   -0.278
C6   C     2.661   -0.053    0.218
C1   C    -0.806   -1.370   -0.185
O1   O    -1.361   -2.565    0.367
O2   O    -2.958   -0.266   -0.175
O3   O    -1.679    2.257    0.255
O4   O     1.111    2.336   -0.365
O5   O     0.552   -1.234    0.240
O6   O     3.369   -1.168   -0.325
H2   H    -1.611   -0.133    1.388
H3   H    -1.024    1.114   -1.338
H4   H     0.532    1.237    1.287
H5   H     1.202   -0.112   -1.368
H61  H     3.138    0.873   -0.103
H62  H     2.672   -0.109    1.306
H1   H    -0.843   -1.420   -1.273
HO1  H    -0.898   -3.372    0.103
HO2  H    -3.423   -1.056    0.133
HO3  H    -2.616    2.279    0.014
HO4  H     0.699    3.172   -0.107
HO6  H     4.296   -1.216   -0.053
bonds: C2-C3 C2-C1 C2-O2 C2-H2 C3-C4 C3-O3 C3-H3 C4-C5 C4-O4 C4-H4 C5-C6 C5-O5 C5-H5 C6-O6 C6-H61 C6-H62 C1-O1
       C1-O5 C1-H1 O1-HO1 O2-HO2 O3-HO3 O4-HO4 O6-HO6

@molecule GAL β-D-galactose   (C6 H12 O6, M = 180.156 g/mol, CCD GAL)
C1   C    -0.695    1.351    0.166
C2   C    -1.560    0.129   -0.153
C3   C    -0.989   -1.094    0.570
C4   C     0.470   -1.289    0.145
C5   C     1.258   -0.013    0.452
C6   C     2.702   -0.177   -0.024
O1   O    -1.190    2.485   -0.550
O2   O    -2.899    0.363    0.289
O3   O    -1.747   -2.255    0.220
O4   O     0.528   -1.560   -1.256
O5   O     0.655    1.092   -0.225
O6   O     3.465    0.967    0.367
H1   H    -0.730    1.553    1.236
H2   H    -1.557   -0.050   -1.229
H3   H    -1.037   -0.937    1.648
H4   H     0.902   -2.124    0.697
H5   H     1.248    0.171    1.527
H61  H     3.133   -1.071    0.426
H62  H     2.719   -0.271   -1.109
HO1  H    -0.688    3.296   -0.396
HO2  H    -3.322    1.128   -0.124
HO3  H    -2.683   -2.198    0.454
HO4  H     0.045   -2.354   -1.524
HO6  H     4.393    0.931    0.097
bonds: C1-C2 C1-O1 C1-O5 C1-H1 C2-C3 C2-O2 C2-H2 C3-C4 C3-O3 C3-H3 C4-C5 C4-O4 C4-H4 C5-C6 C5-O5 C5-H5 C6-O6
       C6-H61 C6-H62 O1-HO1 O2-HO2 O3-HO3 O4-HO4 O6-HO6

@molecule FRU β-D-fructose (fructofuranose)   (C6 H12 O6, M = 180.156 g/mol, CCD FRU)
C1   C    -1.976    0.658    0.761
C2   C    -1.069    0.048   -0.308
C3   C    -0.588   -1.345    0.140
C4   C     0.951   -1.200    0.248
C5   C     1.122    0.334    0.381
C6   C     2.510    0.764   -0.097
O1   O    -2.404    1.955    0.340
O2   O    -1.780   -0.059   -1.543
O3   O    -0.934   -2.333   -0.832
O4   O     1.447   -1.875    1.406
O5   O     0.098    0.866   -0.485
O6   O     2.623    2.187   -0.011
H11  H    -2.846    0.019    0.909
H12  H    -1.425    0.744    1.698
H3   H    -1.013   -1.601    1.111
H4   H     1.441   -1.567   -0.652
H5   H     0.956    0.651    1.410
H61  H     2.652    0.451   -1.130
H62  H     3.270    0.303    0.532
HO1  H    -2.971    2.303    1.041
HO2  H    -2.051    0.836   -1.788
HO3  H    -0.610   -3.179   -0.498
HO4  H     2.406   -1.748    1.414
HO6  H     3.510    2.416   -0.321
bonds: C1-C2 C1-O1 C1-H11 C1-H12 C2-C3 C2-O2 C2-O5 C3-C4 C3-O3 C3-H3 C4-C5 C4-O4 C4-H4 C5-C6 C5-O5 C5-H5 C6-O6
       C6-H61 C6-H62 O1-HO1 O2-HO2 O3-HO3 O4-HO4 O6-HO6

@molecule BDR β-D-ribose (ribofuranose)   (C5 H10 O5, M = 150.130 g/mol, CCD BDR)
C4   C    -0.973    0.096    0.416
O4   O    -0.331   -1.175    0.216
C1   C     1.090   -0.927    0.181
O1   O     1.770   -1.995   -0.482
C2   C     1.210    0.388   -0.628
O2   O     2.414    1.082   -0.298
C3   C    -0.034    1.180   -0.144
O3   O     0.332    2.111    0.876
C5   C    -2.308    0.126   -0.329
O5   O    -3.170   -0.886    0.191
H4   H    -1.139    0.263    1.480
H1   H     1.481   -0.794    1.190
HO1  H     1.602   -2.796    0.030
H2   H     1.155    0.193   -1.697
HO2  H     3.147    0.498   -0.539
H3   H    -0.504    1.697   -0.980
HO3  H     0.964    2.726    0.480
H51  H    -2.136   -0.054   -1.391
H52  H    -2.775    1.103   -0.198
HO5  H    -3.997   -0.832   -0.304
bonds: C4-O4 C4-C3 C4-C5 C4-H4 O4-C1 C1-O1 C1-C2 C1-H1 O1-HO1 C2-O2 C2-C3 C2-H2 O2-HO2 C3-O3 C3-H3 O3-HO3 C5-O5
       C5-H51 C5-H52 O5-HO5

@molecule MAL maltose: glucose α(1→4) glucose   (C12 H22 O11, M = 342.296 g/mol, CCD MAL)
C1'  C    -3.910   -0.708   -0.163
C2'  C    -3.514    0.386    0.831
C3'  C    -2.224    1.058    0.351
C4'  C    -1.135   -0.008    0.196
C5'  C    -1.623   -1.088   -0.774
C6'  C    -0.565   -2.187   -0.888
O1'  O    -4.175   -0.121   -1.438
O2'  O    -4.557    1.360    0.910
O3'  O    -1.808    2.032    1.310
O1   O     0.054    0.596   -0.320
O5'  O    -2.843   -1.649   -0.284
O6'  O    -0.985   -3.154   -1.853
C1   C     0.900    1.163    0.682
C2   C     1.961    2.043    0.015
C3   C     2.835    1.175   -0.894
C4   C     3.454    0.046   -0.063
C5   C     2.335   -0.756    0.606
C6   C     2.947   -1.849    1.486
O2   O     1.320    3.056   -0.763
O3   O     3.874    1.974   -1.463
O4   O     4.214   -0.813   -0.916
O5   O     1.541    0.116    1.413
O6   O     1.906   -2.671    2.018
H1'  H    -4.806   -1.217    0.195
H2'  H    -3.352   -0.055    1.814
H3'  H    -2.399    1.542   -0.608
H4'  H    -0.923   -0.458    1.166
H5'  H    -1.795   -0.645   -1.754
H6'1 H    -0.437   -2.671    0.080
H6'2 H     0.383   -1.748   -1.203
HO1' H    -4.433   -0.758   -2.119
HO2' H    -5.407    1.003    1.203
HO3' H    -2.454    2.736    1.462
HO6' H    -0.361   -3.882   -1.977
H1   H     0.301    1.768    1.363
H2   H     2.581    2.509    0.781
H3   H     2.224    0.750   -1.690
H4   H     4.106    0.471    0.701
H5   H     1.708   -1.215   -0.159
H61  H     3.500   -1.388    2.305
H62  H     3.623   -2.461    0.888
HO2  H     0.745    3.641   -0.251
HO3  H     3.553    2.713   -1.998
HO4  H     4.938   -0.369   -1.377
HO6  H     2.222   -3.386    2.587
bonds: O6-C6 O5-C1 O5-C5 C6-C5 C6'-O6' C6'-C5' O5'-C5' O5'-C1' C1-O1 C1-C2 C4'-C5' C4'-O1 C4'-C3' C2'-C1' C2'-C3'
       C2'-O2' C5-C4 O3'-C3' C1'-O1' C2-O2 C2-C3 C4-C3 C4-O4 C3-O3 C1-H1 C2-H2 C3-H3 C4-H4 C5-H5 C6-H62 C6-H61
       O2-HO2 O3-HO3 O4-HO4 O6-HO6 C1'-H1' C2'-H2' C3'-H3' C4'-H4' C5'-H5' C6'-H6'2 C6'-H6'1 O1'-HO1' O2'-HO2'
       O3'-HO3' O6'-HO6'

@molecule CBI cellobiose: glucose β(1→4) glucose (the repeat of cellulose)   (C12 H22 O11, M = 342.296 g/mol, CCD CBI)
C2'  C    -2.741    1.691   -0.012
C3'  C    -1.511    1.007   -0.616
C4'  C    -1.315   -0.355    0.059
C5'  C    -2.588   -1.188   -0.113
C6'  C    -2.422   -2.527    0.609
C1'  C    -3.958    0.778   -0.182
O1'  O    -5.099    1.385    0.428
O2'  O    -2.975    2.931   -0.685
O3'  O    -0.356    1.820   -0.398
O4'  O    -0.213   -1.033   -0.546
O5'  O    -3.698   -0.482    0.441
O6'  O    -3.561   -3.352    0.351
C2   C     2.129   -1.634   -0.571
C3   C     3.484   -1.297    0.055
C4   C     3.760    0.200   -0.122
C5   C     2.614    0.999    0.505
C6   C     2.851    2.493    0.278
C1   C     1.047   -0.765    0.073
O2   O     1.829   -3.014   -0.348
O3   O     4.512   -2.051   -0.591
O4   O     4.988    0.540    0.522
O5   O     1.378    0.614   -0.102
O6   O     1.845    3.243    0.963
H2'  H    -2.571    1.878    1.049
H3'  H    -1.660    0.865   -1.686
H4'  H    -1.117   -0.210    1.121
H5'  H    -2.764   -1.368   -1.175
H6'1 H    -2.336   -2.352    1.681
H6'2 H    -1.523   -3.027    0.248
H1'  H    -4.152    0.626   -1.244
HO1' H    -5.908    0.862    0.359
HO2' H    -3.744    3.416   -0.356
HO3' H    -0.415    2.700   -0.793
HO6' H    -3.524   -4.218    0.779
H2   H     2.162   -1.438   -1.643
H3   H     3.466   -1.543    1.117
H4   H     3.827    0.433   -1.185
H5   H     2.572    0.795    1.575
H61  H     2.804    2.711   -0.788
H62  H     3.834    2.767    0.663
H1   H     0.984   -0.992    1.138
HO2  H     0.980   -3.296   -0.715
HO3  H     4.397   -3.010   -0.525
HO4  H     5.758    0.069    0.174
HO6  H     1.933    4.201    0.864
bonds: O4-C4 C6-O6 C6-C5 C4-C5 C4-C3 C5-O5 C3-O3 C3-C2 O5-C1 C1-C2 C1-O4' C2-O2 O3'-C3' O6'-C6' O4'-C4' C4'-C3'
       C4'-C5' C3'-C2' C2'-O2' C2'-C1' C6'-C5' C5'-O5' O5'-C1' C1'-O1' C1-H1 C2-H2 C3-H3 C4-H4 C5-H5 C6-H62 C6-H61
       O2-HO2 O3-HO3 O4-HO4 O6-HO6 C1'-H1' C2'-H2' C3'-H3' C4'-H4' C5'-H5' C6'-H6'2 C6'-H6'1 O1'-HO1' O2'-HO2'
       O3'-HO3' O6'-HO6'

@molecule SUC sucrose: glucose α(1→2)β fructose   (C12 H22 O11, M = 342.296 g/mol, CCD SUC)
C1   C    -1.250    0.834   -0.871
C2   C    -2.690    1.095   -0.423
C3   C    -3.012    0.201    0.778
C4   C    -2.779   -1.263    0.388
C5   C    -1.335   -1.432   -0.091
C6   C    -1.112   -2.878   -0.539
O2   O    -2.834    2.466   -0.049
O3   O    -4.377    0.382    1.158
O4   O    -3.007   -2.102    1.523
O5   O    -1.094   -0.550   -1.190
O6   O     0.264   -3.065   -0.877
C1'  C     1.237    2.083   -1.411
C2'  C     1.026    1.174   -0.198
C3'  C     1.899    1.657    0.976
C4'  C     2.864    0.477    1.242
C5'  C     2.816   -0.292   -0.104
C6'  C     3.189   -1.761    0.100
O1'  O     0.585    1.515   -2.550
O1   O    -0.351    1.185    0.183
O3'  O     1.092    1.912    2.128
O4'  O     4.186    0.949    1.512
O2'  O     1.434   -0.168   -0.516
O6'  O     3.251   -2.417   -1.167
H1   H    -1.029    1.437   -1.753
H2   H    -3.374    0.866   -1.241
H3   H    -2.363    0.464    1.613
H4   H    -3.465   -1.540   -0.412
H5   H    -0.651   -1.194    0.723
H61  H    -1.384   -3.555    0.271
H62  H    -1.733   -3.088   -1.411
HO2  H    -2.640    3.091   -0.761
HO3  H    -4.598    1.289    1.411
HO4  H    -3.902   -2.042    1.884
HO6  H     0.478   -3.961   -1.171
H1'1 H     0.816    3.067   -1.206
H1'2 H     2.303    2.177   -1.613
H3'  H     2.455    2.551    0.695
H4'  H     2.500   -0.148    2.059
H5'  H     3.477    0.173   -0.835
H6'1 H     4.161   -1.825    0.590
H6'2 H     2.437   -2.244    0.723
HO1' H     0.678    2.040   -3.357
HO3' H     1.590    2.218    2.897
HO4' H     4.826    0.246    1.685
HO6' H     3.483   -3.355   -1.114
bonds: O6'-C6' C6'-C5' O2'-C5' O2'-C2' O1'-C1' C5'-C4' O6-C6 O5-C1 O5-C5 C1'-C2' C1-C2 C1-O1 C2'-O1 C2'-C3' C6-C5
       C2-O2 C2-C3 C4'-C3' C4'-O4' C5-C4 C3'-O3' C4-C3 C4-O4 C3-O3 C1-H1 C2-H2 C3-H3 C4-H4 C5-H5 C6-H61 C6-H62
       O2-HO2 O3-HO3 O4-HO4 O6-HO6 C1'-H1'1 C1'-H1'2 C3'-H3' C4'-H4' C5'-H5' C6'-H6'1 C6'-H6'2 O1'-HO1' O3'-HO3'
       O4'-HO4' O6'-HO6'

@molecule LAT lactose: galactose β(1→4) glucose   (C12 H22 O11, M = 342.296 g/mol, CCD LAT)
C2'  C    -2.696    1.687   -0.004
C3'  C    -1.446    1.007   -0.570
C4'  C    -1.269   -0.357    0.106
C5'  C    -2.533   -1.192   -0.109
C6'  C    -2.388   -2.533    0.615
C1'  C    -3.905    0.772   -0.215
O1'  O    -5.066    1.375    0.361
O2'  O    -2.912    2.927   -0.680
O3'  O    -0.300    1.820   -0.312
O1   O    -0.147   -1.032   -0.465
O5'  O    -3.663   -0.488    0.412
O6'  O    -3.516   -3.358    0.319
C1   C     1.092   -0.761    0.194
C2   C     2.196   -1.627   -0.419
C3   C     3.531   -1.289    0.252
C4   C     3.807    0.209    0.087
C5   C     2.641    1.003    0.681
C6   C     2.882    2.499    0.466
O2   O     1.892   -3.008   -0.209
O3   O     4.579   -2.039   -0.364
O4   O     3.940    0.520   -1.301
O5   O     1.426    0.619    0.034
O6   O     1.854    3.246    1.121
H2'  H    -2.560    1.870    1.062
H3'  H    -1.561    0.869   -1.645
H4'  H    -1.102   -0.214    1.174
H5'  H    -2.675   -1.369   -1.175
H6'1 H    -2.335   -2.361    1.691
H6'2 H    -1.477   -3.029    0.281
H1'  H    -4.065    0.621   -1.283
HO1' H    -5.871    0.849    0.264
HO2' H    -3.692    3.411   -0.375
HO3' H    -0.348    2.702   -0.706
HO6' H    -3.492   -4.226    0.746
H1   H     0.996   -0.991    1.255
H2   H     2.265   -1.427   -1.488
H3   H     3.480   -1.537    1.312
H4   H     4.729    0.469    0.608
H5   H     2.566    0.797    1.747
H61  H     2.868    2.721   -0.601
H62  H     3.851    2.775    0.882
HO2  H     1.056   -3.291   -0.603
HO3  H     4.465   -2.998   -0.305
HO4  H     4.659    0.047   -1.744
HO6  H     1.943    4.204    1.027
bonds: O1'-C1' C1'-O5' C1'-C2' O5'-C5' O2'-C2' C2'-C3' O6-C6 O6'-C6' C5'-C6' C5'-C4' C3'-O3' C3'-C4' C4'-O1 C6-C5
       O5-C5 O5-C1 C5-C4 O1-C1 C1-C2 C4-O4 C4-C3 C2-C3 C2-O2 C3-O3 C1-H1 C2-H2 C3-H3 C4-H4 C5-H5 C6-H62 C6-H61
       O2-HO2 O3-HO3 O4-HO4 O6-HO6 C1'-H1' C2'-H2' C3'-H3' C4'-H4' C5'-H5' C6'-H6'2 C6'-H6'1 O1'-HO1' O2'-HO2'
       O3'-HO3' O6'-HO6'
""";

    /// <summary>§6.9: the lipids (molecule blocks).</summary>
    public const string LipidText = """
@molecule GOL glycerol   (C3 H8 O3, M = 92.094 g/mol, CCD GOL)
C1   C    -1.249   -0.657    0.300
O1   O    -2.413   -0.072   -0.287
C2   C     0.000    0.049   -0.231
O2   O     0.000    1.409    0.205
C3   C     1.249   -0.657    0.300
O3   O     2.413   -0.072   -0.287
H11  H    -1.295   -0.549    1.384
H12  H    -1.206   -1.716    0.043
HO1  H    -3.244   -0.471    0.004
H2   H     0.000    0.016   -1.320
HO2  H     0.000    1.514    1.167
H31  H     1.295   -0.549    1.384
H32  H     1.206   -1.716    0.043
HO3  H     3.244   -0.471    0.004
bonds: C1-O1 C1-C2 C1-H11 C1-H12 O1-HO1 C2-O2 C2-C3 C2-H2 O2-HO2 C3-O3 C3-H31 C3-H32 O3-HO3

@molecule PLM palmitic acid (C16:0, saturated)   (C16 H32 O2, M = 256.424 g/mol, CCD PLM)
C1   C    -0.037    8.258   -0.000
O1   O    -0.655    9.449    0.001
O2   O     1.169    8.203    0.001
C2   C    -0.849    6.988    0.000
C3   C     0.090    5.781   -0.000
C4   C    -0.733    4.492    0.000
C5   C     0.206    3.286   -0.000
C6   C    -0.617    1.996    0.000
C7   C     0.322    0.790   -0.000
C8   C    -0.501   -0.498    0.000
C9   C     0.438   -1.705   -0.000
CA   C    -0.385   -2.994    0.000
CB   C     0.554   -4.201   -0.000
CC   C    -0.269   -5.490    0.000
CD   C     0.671   -6.696   -0.000
CE   C    -0.153   -7.986   -0.000
CF   C     0.786   -9.192   -0.001
CG   C    -0.037  -10.481   -0.000
H    H    -0.135   10.264    0.001
H21  H    -1.477    6.959   -0.890
H22  H    -1.478    6.958    0.889
H31  H     0.719    5.811    0.890
H32  H     0.719    5.811   -0.889
H41  H    -1.362    4.464   -0.889
H42  H    -1.361    4.463    0.890
H51  H     0.836    3.315    0.889
H52  H     0.835    3.315   -0.890
H61  H    -1.246    1.967   -0.890
H62  H    -1.246    1.968    0.889
H71  H     0.950    0.818    0.890
H72  H     0.952    0.819   -0.889
H81  H    -1.129   -0.527   -0.891
H82  H    -1.131   -0.528    0.889
H91  H     1.067   -1.676    0.890
H92  H     1.067   -1.677   -0.889
HA1  H    -1.014   -3.024   -0.889
HA2  H    -1.014   -3.024    0.890
HB1  H     1.183   -4.172    0.889
HB2  H     1.183   -4.171   -0.891
HC1  H    -0.897   -5.519   -0.890
HC2  H    -0.898   -5.518    0.889
HD1  H     1.298   -6.668    0.890
HD2  H     1.299   -6.667   -0.889
HE1  H    -0.782   -8.015   -0.891
HE2  H    -0.783   -8.015    0.888
HF1  H     1.415   -9.163    0.890
HF2  H     1.415   -9.164   -0.889
HG1  H     0.632  -11.341    0.000
HG2  H    -0.666  -10.511    0.890
HG3  H    -0.666  -10.511   -0.889
bonds: C1-O1 C1=O2 C1-C2 O1-H C2-C3 C2-H21 C2-H22 C3-C4 C3-H31 C3-H32 C4-C5 C4-H41 C4-H42 C5-C6 C5-H51 C5-H52
       C6-C7 C6-H61 C6-H62 C7-C8 C7-H71 C7-H72 C8-C9 C8-H81 C8-H82 C9-CA C9-H91 C9-H92 CA-CB CA-HA1 CA-HA2 CB-CC
       CB-HB1 CB-HB2 CC-CD CC-HC1 CC-HC2 CD-CE CD-HD1 CD-HD2 CE-CF CE-HE1 CE-HE2 CF-CG CF-HF1 CF-HF2 CG-HG1 CG-HG2
       CG-HG3
pH 7: remove H (on O1); the carboxylate is −1

@molecule STE stearic acid (C18:0, saturated)   (C18 H36 O2, M = 284.477 g/mol, CCD STE)
C1   C    -0.035    9.492   -0.000
O1   O     1.172    9.430    0.000
O2   O    -0.645   10.686    0.001
C2   C    -0.853    8.226    0.001
C3   C     0.080    7.014   -0.000
C4   C    -0.751    5.730    0.000
C5   C     0.183    4.518   -0.000
C6   C    -0.649    3.233    0.000
C7   C     0.285    2.022   -0.000
C8   C    -0.546    0.737    0.000
C9   C     0.387   -0.474   -0.000
C10  C    -0.444   -1.758    0.000
C11  C     0.489   -2.970   -0.000
C12  C    -0.342   -4.255    0.000
C13  C     0.592   -5.467   -0.001
C14  C    -0.239   -6.751    0.000
C15  C     0.694   -7.963   -0.001
C16  C    -0.137   -9.248   -0.000
C17  C     0.796  -10.459   -0.001
C18  C    -0.035  -11.744   -0.000
HO2  H    -0.121   11.498    0.001
H21  H    -1.482    8.201   -0.890
H22  H    -1.482    8.201    0.889
H31  H     0.709    7.040    0.890
H32  H     0.709    7.041   -0.889
H41  H    -1.379    5.705   -0.890
H42  H    -1.380    5.705    0.889
H51  H     0.811    4.544    0.889
H52  H     0.811    4.544   -0.890
H61  H    -1.277    3.208   -0.890
H62  H    -1.277    3.209    0.889
H71  H     0.913    2.048    0.890
H72  H     0.914    2.048   -0.889
H81  H    -1.175    0.712   -0.890
H82  H    -1.175    0.712    0.889
H91  H     1.015   -0.448    0.889
H92  H     1.016   -0.448   -0.890
H101 H    -1.072   -1.784   -0.890
H102 H    -1.073   -1.783    0.889
H111 H     1.117   -2.944    0.890
H112 H     1.118   -2.944   -0.889
H121 H    -0.970   -4.280   -0.890
H122 H    -0.971   -4.280    0.889
H131 H     1.220   -5.440    0.890
H132 H     1.219   -5.441   -0.889
H141 H    -0.868   -6.776   -0.890
H142 H    -0.868   -6.776    0.889
H151 H     1.321   -7.937    0.890
H152 H     1.323   -7.937   -0.889
H161 H    -0.766   -9.272   -0.891
H162 H    -0.766   -9.273    0.889
H171 H     1.425  -10.433    0.889
H172 H     1.424  -10.433   -0.890
H181 H     0.629  -12.607    0.000
H182 H    -0.664  -11.769    0.890
H183 H    -0.663  -11.769   -0.889
bonds: C1=O1 C1-O2 C1-C2 O2-HO2 C2-C3 C2-H21 C2-H22 C3-C4 C3-H31 C3-H32 C4-C5 C4-H41 C4-H42 C5-C6 C5-H51 C5-H52
       C6-C7 C6-H61 C6-H62 C7-C8 C7-H71 C7-H72 C8-C9 C8-H81 C8-H82 C9-C10 C9-H91 C9-H92 C10-C11 C10-H101 C10-H102
       C11-C12 C11-H111 C11-H112 C12-C13 C12-H121 C12-H122 C13-C14 C13-H131 C13-H132 C14-C15 C14-H141 C14-H142
       C15-C16 C15-H151 C15-H152 C16-C17 C16-H161 C16-H162 C17-C18 C17-H171 C17-H172 C18-H181 C18-H182 C18-H183
pH 7: remove HO2 (on O2); the carboxylate is −1

@molecule OLA oleic acid (C18:1 cis-9, unsaturated)   (C18 H34 O2, M = 282.461 g/mol, CCD OLA)
C1   C    -1.566    8.665   -0.000
O1   O    -0.428    9.068    0.002
O2   O    -2.586    9.538    0.001
C2   C    -1.842    7.183   -0.000
C3   C    -0.517    6.418   -0.000
C4   C    -0.796    4.914   -0.000
C5   C     0.527    4.148   -0.000
C6   C     0.248    2.645   -0.000
C7   C     1.572    1.880   -0.000
C8   C     1.293    0.375   -0.000
C9   C     2.597   -0.378   -0.000
C10  C     2.597   -1.688   -0.000
C11  C     1.292   -2.441   -0.000
C12  C     1.570   -3.944   -0.000
C13  C     0.246   -4.709   -0.000
C14  C     0.524   -6.214   -0.000
C15  C    -0.800   -6.978   -0.000
C16  C    -0.520   -8.483   -0.000
C17  C    -1.845   -9.248   -0.000
C18  C    -1.566  -10.752   -0.000
HO2  H    -2.410   10.488    0.000
H21  H    -2.412    6.921   -0.891
H22  H    -2.414    6.919    0.888
H31  H     0.054    6.681    0.890
H32  H     0.055    6.682   -0.889
H41  H    -1.368    4.652   -0.891
H42  H    -1.369    4.650    0.888
H51  H     1.099    4.412    0.890
H52  H     1.100    4.412   -0.890
H61  H    -0.324    2.382   -0.890
H62  H    -0.323    2.382    0.890
H71  H     2.144    2.142    0.889
H72  H     2.144    2.142   -0.890
H81  H     0.722    0.112   -0.890
H82  H     0.720    0.113    0.889
H9   H     3.533    0.161    0.001
H10  H     3.532   -2.228    0.001
H111 H     0.720   -2.177   -0.890
H112 H     0.720   -2.177    0.889
H121 H     2.142   -4.208    0.890
H122 H     2.142   -4.208   -0.889
H131 H    -0.325   -4.445   -0.890
H132 H    -0.326   -4.446    0.889
H141 H     1.097   -6.477    0.889
H142 H     1.096   -6.477   -0.890
H151 H    -1.372   -6.714   -0.888
H152 H    -1.370   -6.716    0.891
H161 H     0.053   -8.747    0.888
H162 H     0.049   -8.745   -0.891
H171 H    -2.418   -8.983   -0.888
H172 H    -2.416   -8.985    0.891
H181 H    -2.510  -11.296    0.000
H182 H    -0.996  -11.015   -0.892
H183 H    -0.994  -11.016    0.888
bonds: C1=O1 C1-O2 C1-C2 O2-HO2 C2-C3 C2-H21 C2-H22 C3-C4 C3-H31 C3-H32 C4-C5 C4-H41 C4-H42 C5-C6 C5-H51 C5-H52
       C6-C7 C6-H61 C6-H62 C7-C8 C7-H71 C7-H72 C8-C9 C8-H81 C8-H82 C9=C10 C9-H9 C10-C11 C10-H10 C11-C12 C11-H111
       C11-H112 C12-C13 C12-H121 C12-H122 C13-C14 C13-H131 C13-H132 C14-C15 C14-H141 C14-H142 C15-C16 C15-H151
       C15-H152 C16-C17 C16-H161 C16-H162 C17-C18 C17-H171 C17-H172 C18-H181 C18-H182 C18-H183
pH 7: remove HO2 (on O2); the carboxylate is −1

@molecule TGL tristearin (a triglyceride: glycerol + 3 stearic acids, 3 ester bonds)   (C57 H110 O6, M = 891.480 g/mol, CCD TGL)
CA1  C     2.322    8.348   -1.880
CA2  C     3.753    7.963   -2.150
CA3  C     3.888    6.439   -2.110
CA4  C     5.342    6.047   -2.384
CA5  C     5.476    4.524   -2.344
CA6  C     6.929    4.132   -2.618
CA7  C     7.063    2.609   -2.578
CA8  C     8.516    2.217   -2.851
CA9  C     8.651    0.694   -2.812
OA1  O     1.557    8.510   -2.800
CB1  C    -0.791    6.092   -1.475
CB2  C    -1.751    5.527   -2.488
CB3  C    -2.557    4.395   -1.852
CB4  C    -3.532    3.821   -2.881
CB5  C    -4.339    2.688   -2.244
CB6  C    -5.314    2.114   -3.272
CB7  C    -6.122    0.981   -2.636
CB8  C    -7.097    0.407   -3.665
CB9  C    -7.903   -0.725   -3.028
C10  C    -8.879   -1.300   -4.057
C11  C    -9.685   -2.432   -3.420
C12  C   -10.660   -3.006   -4.449
C13  C   -11.467   -4.139   -3.812
C14  C   -12.442   -4.713   -4.841
OB1  O     0.304    5.598   -1.339
OG1  O     1.897    8.509   -0.617
CG1  C     0.518    8.880   -0.357
CG2  C    -0.224    7.690    0.252
OG2  O    -1.150    7.144   -0.724
CG3  C    -0.999    8.152    1.489
OG3  O    -0.762    7.226    2.581
CC1  C    -1.788    6.805    3.339
CC2  C    -1.542    5.844    4.474
CC3  C    -1.597    4.409    3.948
CC4  C    -1.347    3.433    5.100
CC5  C    -1.402    1.998    4.574
CC6  C    -1.153    1.023    5.726
CC7  C    -1.208   -0.413    5.201
CC8  C    -0.958   -1.389    6.353
CC9  C    -1.013   -2.824    5.826
C15  C    -0.764   -3.799    6.978
C16  C    -0.819   -5.234    6.453
C17  C    -0.569   -6.210    7.605
C18  C    -0.624   -7.646    7.079
C19  C    -0.375   -8.622    8.230
OC1  O    -2.907    7.197    3.111
C20  C    10.104    0.302   -3.085
C21  C    10.238   -1.222   -3.046
C22  C    11.691   -1.613   -3.319
C23  C    11.826   -3.136   -3.280
C24  C    13.279   -3.528   -3.553
C25  C    13.413   -5.051   -3.514
C26  C    14.866   -5.444   -3.787
C27  C    15.001   -6.967   -3.748
C28  C    16.454   -7.358   -4.021
C29  C   -13.249   -5.846   -4.205
C30  C   -14.224   -6.420   -5.233
C31  C   -15.031   -7.553   -4.596
C32  C   -16.007   -8.127   -5.626
C33  C    -0.430  -10.057    7.705
C34  C    -0.180  -11.032    8.857
C35  C    -0.235  -12.467    8.331
C36  C     0.014  -13.443    9.483
HA21 H     4.399    8.405   -1.391
HA22 H     4.048    8.327   -3.134
HA31 H     3.243    5.997   -2.869
HA32 H     3.593    6.074   -1.127
HA41 H     5.986    6.489   -1.624
HA42 H     5.636    6.412   -3.367
HA51 H     4.831    4.082   -3.102
HA52 H     5.180    4.159   -1.360
HA61 H     7.574    4.575   -1.860
HA62 H     7.223    4.496   -3.602
HA71 H     6.418    2.166   -3.336
HA72 H     6.768    2.245   -1.593
HA81 H     9.161    2.659   -2.093
HA82 H     8.811    2.582   -3.835
HA91 H     8.006    0.253   -3.572
HA92 H     8.355    0.329   -1.829
HB21 H    -2.429    6.313   -2.823
HB22 H    -1.194    5.141   -3.342
HB31 H    -1.879    3.610   -1.516
HB32 H    -3.115    4.781   -0.998
HB41 H    -4.210    4.604   -3.217
HB42 H    -2.974    3.433   -3.733
HB51 H    -3.661    1.902   -1.908
HB52 H    -4.897    3.074   -1.391
HB61 H    -5.992    2.898   -3.608
HB62 H    -4.756    1.727   -4.126
HB71 H    -5.444    0.196   -2.300
HB72 H    -6.679    1.368   -1.783
HB81 H    -7.775    1.193   -4.001
HB82 H    -6.538    0.020   -4.517
HB91 H    -7.226   -1.510   -2.692
HB92 H    -8.462   -0.338   -2.176
H101 H    -9.556   -0.515   -4.393
H102 H    -8.319   -1.687   -4.909
H111 H    -9.007   -3.218   -3.084
H112 H   -10.243   -2.045   -2.568
H121 H   -11.338   -2.221   -4.784
H122 H   -10.103   -3.393   -5.302
H131 H   -10.789   -4.924   -3.477
H132 H   -12.026   -3.753   -2.959
H141 H   -13.121   -3.928   -5.176
H142 H   -11.885   -5.099   -5.694
HG11 H     0.493    9.720    0.337
HG12 H     0.038    9.168   -1.292
HG2  H     0.494    6.923    0.540
HG31 H    -0.666    9.148    1.777
HG32 H    -2.065    8.178    1.260
HC21 H    -0.560    6.036    4.906
HC22 H    -2.307    5.981    5.238
HC31 H    -2.579    4.217    3.516
HC32 H    -0.832    4.272    3.185
HC41 H    -0.367    3.626    5.533
HC42 H    -2.114    3.570    5.863
HC51 H    -2.383    1.806    4.140
HC52 H    -0.635    1.862    3.812
HC61 H    -0.172    1.215    6.160
HC62 H    -1.919    1.159    6.489
HC71 H    -2.189   -0.605    4.767
HC72 H    -0.442   -0.549    4.437
HC81 H     0.023   -1.197    6.785
HC82 H    -1.724   -1.252    7.115
HC91 H    -1.995   -3.016    5.394
HC92 H    -0.248   -2.961    5.064
H151 H     0.217   -3.607    7.412
H152 H    -1.530   -3.663    7.742
H161 H    -1.800   -5.426    6.020
H162 H    -0.053   -5.371    5.689
H171 H     0.413   -6.018    8.038
H172 H    -1.335   -6.074    8.368
H181 H    -1.606   -7.838    6.647
H182 H     0.142   -7.782    6.315
H191 H     0.606   -8.430    8.663
H192 H    -1.140   -8.484    8.995
H201 H    10.749    0.744   -2.326
H202 H    10.399    0.667   -4.069
H211 H     9.593   -1.664   -3.804
H212 H     9.943   -1.586   -2.061
H221 H    12.336   -1.172   -2.561
H222 H    11.987   -1.248   -4.303
H231 H    11.181   -3.578   -4.040
H232 H    11.530   -3.501   -2.297
H241 H    13.924   -3.086   -2.795
H242 H    13.574   -3.164   -4.538
H251 H    12.768   -5.493   -4.272
H252 H    13.118   -5.416   -2.529
H261 H    15.511   -5.002   -3.028
H262 H    15.162   -5.078   -4.770
H271 H    14.355   -7.409   -4.505
H272 H    14.706   -7.331   -2.763
H281 H    16.550   -8.443   -3.994
H282 H    16.748   -6.994   -5.006
H283 H    17.099   -6.916   -3.263
H291 H   -12.571   -6.631   -3.869
H292 H   -13.807   -5.460   -3.352
H301 H   -14.902   -5.635   -5.569
H302 H   -13.667   -6.807   -6.085
H311 H   -14.354   -8.337   -4.261
H312 H   -15.589   -7.165   -3.744
H321 H   -16.581   -8.933   -5.172
H322 H   -16.684   -7.342   -5.961
H323 H   -15.449   -8.514   -6.478
H331 H    -1.412  -10.248    7.272
H332 H     0.335  -10.194    6.941
H341 H     0.800  -10.840    9.290
H342 H    -0.947  -10.896    9.620
H351 H    -1.216  -12.659    7.898
H352 H     0.531  -12.604    7.568
H361 H    -0.025  -14.465    9.108
H362 H     0.995  -13.252    9.915
H363 H    -0.752  -13.307   10.245
bonds: CA1-CA2 CA1=OA1 CA1-OG1 CA2-CA3 CA2-HA21 CA2-HA22 CA3-CA4 CA3-HA31 CA3-HA32 CA4-CA5 CA4-HA41 CA4-HA42
       CA5-CA6 CA5-HA51 CA5-HA52 CA6-CA7 CA6-HA61 CA6-HA62 CA7-CA8 CA7-HA71 CA7-HA72 CA8-CA9 CA8-HA81 CA8-HA82
       CA9-C20 CA9-HA91 CA9-HA92 CB1-CB2 CB1=OB1 CB1-OG2 CB2-CB3 CB2-HB21 CB2-HB22 CB3-CB4 CB3-HB31 CB3-HB32
       CB4-CB5 CB4-HB41 CB4-HB42 CB5-CB6 CB5-HB51 CB5-HB52 CB6-CB7 CB6-HB61 CB6-HB62 CB7-CB8 CB7-HB71 CB7-HB72
       CB8-CB9 CB8-HB81 CB8-HB82 CB9-C10 CB9-HB91 CB9-HB92 C10-C11 C10-H101 C10-H102 C11-C12 C11-H111 C11-H112
       C12-C13 C12-H121 C12-H122 C13-C14 C13-H131 C13-H132 C14-C29 C14-H141 C14-H142 OG1-CG1 CG1-CG2 CG1-HG11
       CG1-HG12 CG2-OG2 CG2-CG3 CG2-HG2 CG3-OG3 CG3-HG31 CG3-HG32 OG3-CC1 CC1-CC2 CC1=OC1 CC2-CC3 CC2-HC21 CC2-HC22
       CC3-CC4 CC3-HC31 CC3-HC32 CC4-CC5 CC4-HC41 CC4-HC42 CC5-CC6 CC5-HC51 CC5-HC52 CC6-CC7 CC6-HC61 CC6-HC62
       CC7-CC8 CC7-HC71 CC7-HC72 CC8-CC9 CC8-HC81 CC8-HC82 CC9-C15 CC9-HC91 CC9-HC92 C15-C16 C15-H151 C15-H152
       C16-C17 C16-H161 C16-H162 C17-C18 C17-H171 C17-H172 C18-C19 C18-H181 C18-H182 C19-C33 C19-H191 C19-H192
       C20-C21 C20-H201 C20-H202 C21-C22 C21-H211 C21-H212 C22-C23 C22-H221 C22-H222 C23-C24 C23-H231 C23-H232
       C24-C25 C24-H241 C24-H242 C25-C26 C25-H251 C25-H252 C26-C27 C26-H261 C26-H262 C27-C28 C27-H271 C27-H272
       C28-H281 C28-H282 C28-H283 C29-C30 C29-H291 C29-H292 C30-C31 C30-H301 C30-H302 C31-C32 C31-H311 C31-H312
       C32-H321 C32-H322 C32-H323 C33-C34 C33-H331 C33-H332 C34-C35 C34-H341 C34-H342 C35-C36 C35-H351 C35-H352
       C36-H361 C36-H362 C36-H363
note: tails point toward −y, head toward +y

@molecule POV POPC (a phospholipid: glycerol + palmitate + oleate + phosphate + choline)   (C42 H82 N O8 P, M = 760.076 g/mol, CCD POV)
N    N     5.587   10.091   -3.445
P    P     1.037    9.525   -1.577
C1   C     1.243    7.191   -0.291
C2   C     0.404    5.915   -0.193
C3   C    -0.532    6.012    1.014
C210 C     2.470   -6.705    2.282
C310 C    -8.165   -4.637    0.006
C11  C     3.643    9.366   -2.144
O11  O     0.851    7.931   -1.449
C211 C     3.887   -6.920    1.817
C311 C    -7.915   -6.140   -0.132
C12  C     4.765   10.400   -2.268
O12  O     2.585    9.904   -1.349
C212 C     4.325   -8.346    2.157
C312 C    -9.244   -6.858   -0.372
C13  C     6.680   11.068   -3.549
O13  O     0.596    9.984   -2.985
C213 C     5.764   -8.564    1.685
C313 C    -8.995   -8.362   -0.509
C14  C     6.149    8.741   -3.310
O14  O     0.171   10.231   -0.510
C214 C     6.202   -9.990    2.025
C314 C   -10.325   -9.079   -0.750
C15  C     4.755   10.157   -4.655
C215 C     7.641  -10.207    1.553
C315 C   -10.075  -10.583   -0.887
C216 C     8.079  -11.634    1.893
C316 C   -11.404  -11.300   -1.127
C217 C     9.518  -11.851    1.421
C218 C     9.956  -13.277    1.761
C21  C     1.316    3.885   -1.044
O21  O     1.281    4.769   -0.036
C22  C     2.204    2.670   -0.951
O22  O     0.636    4.061   -2.027
C23  C     1.446    1.536   -0.258
C24  C     2.347    0.304   -0.164
C25  C     1.589   -0.831    0.529
C26  C     2.490   -2.064    0.624
C27  C     1.731   -3.197    1.316
C28  C     2.633   -4.431    1.411
C29  C     1.886   -5.548    2.093
C31  C    -2.536    4.955    1.753
O31  O    -1.782    5.336    0.710
C32  C    -3.846    4.248    1.516
O32  O    -2.165    5.177    2.881
C33  C    -3.596    2.745    1.379
C34  C    -4.926    2.027    1.139
C35  C    -4.676    0.523    1.002
C36  C    -6.006   -0.195    0.761
C37  C    -5.756   -1.697    0.624
C38  C    -7.085   -2.416    0.383
C39  C    -6.836   -3.919    0.246
H29  H     0.870   -5.394    2.426
H1   H     1.084    7.800    0.600
H1A  H     2.298    6.928   -0.367
H2   H    -0.187    5.798   -1.102
H3   H    -0.063    5.538    1.876
H3A  H    -0.730    7.059    1.236
H310 H    -8.623   -4.259   -0.908
H31A H    -8.833   -4.455    0.847
H210 H     1.935   -7.506    2.772
H11  H     3.264    9.122   -3.136
H11A H     4.031    8.464   -1.671
H211 H     3.942   -6.770    0.739
H21A H     4.545   -6.209    2.317
H311 H    -7.247   -6.321   -0.973
H31B H    -7.459   -6.518    0.784
H12  H     5.387   10.370   -1.373
H12A H     4.332   11.394   -2.377
H22  H     2.493    2.354   -1.953
H212 H     4.269   -8.496    3.236
H22A H     3.097    2.916   -0.375
H21B H     3.667   -9.057    1.657
H32  H    -4.514    4.429    2.359
H312 H    -9.913   -6.676    0.469
H32A H    -4.303    4.626    0.602
H31C H    -9.702   -6.481   -1.287
H13  H     7.297   11.019   -2.652
H13A H     7.289   10.840   -4.423
H13B H     6.262   12.070   -3.649
H23  H     1.156    1.852    0.745
H213 H     5.819   -8.413    0.607
H23A H     0.553    1.291   -0.833
H21C H     6.422   -7.853    2.185
H33  H    -2.928    2.563    0.538
H313 H    -8.327   -8.543   -1.351
H33A H    -3.140    2.366    2.295
H31D H    -8.539   -8.739    0.406
H14  H     5.339    8.016   -3.234
H14A H     6.760    8.512   -4.184
H14B H     6.766    8.692   -2.414
H24  H     2.635   -0.013   -1.166
H214 H     6.146  -10.139    3.103
H24A H     3.240    0.548    0.411
H21D H     5.544  -10.701    1.525
H34  H    -5.594    2.208    1.980
H314 H   -10.993   -8.897    0.092
H34A H    -5.382    2.405    0.224
H31E H   -10.781   -8.702   -1.664
H15  H     4.581   11.200   -4.920
H15A H     5.267    9.654   -5.475
H15B H     3.801    9.667   -4.466
H25  H     1.300   -0.514    1.532
H215 H     7.697  -10.057    0.475
H25A H     0.696   -1.076   -0.045
H21E H     8.299   -9.496    2.054
H35  H    -4.008    0.341    0.159
H315 H    -9.406  -10.764   -1.728
H35A H    -4.219    0.146    1.916
H31F H    -9.619  -10.960    0.028
H26  H     2.779   -2.379   -0.379
H216 H     8.023  -11.783    2.971
H26A H     3.382   -1.818    1.198
H21F H     7.421  -12.344    1.393
H36  H    -6.673   -0.013    1.604
H316 H   -11.226  -12.371   -1.225
H36A H    -6.463    0.183   -0.153
H31G H   -12.073  -11.119   -0.286
H31H H   -11.861  -10.922   -2.043
H27  H     1.442   -2.882    2.319
H217 H     9.573  -11.701    0.342
H27A H     0.840   -3.443    0.741
H21G H    10.175  -11.140    1.921
H37  H    -5.087   -1.879   -0.219
H37A H    -5.298   -2.075    1.538
H28  H     2.923   -4.746    0.408
H218 H     9.900  -13.426    2.839
H28A H     3.525   -4.185    1.988
H21H H     9.298  -13.988    1.261
H21J H    10.981  -13.432    1.425
H38  H    -7.754   -2.234    1.225
H38A H    -7.542   -2.038   -0.531
H39  H    -6.378   -4.297    1.160
H39A H    -6.167   -4.101   -0.596
bonds: N-C12 N-C13 N-C14 N-C15 P-O11 P-O12 P-O13 P=O14 C1-C2 C1-O11 C1-H1 C1-H1A C2-C3 C2-O21 C2-H2 C3-O31 C3-H3
       C3-H3A C210-C211 C210=C29 C210-H210 C310-C311 C310-C39 C310-H310 C310-H31A C11-C12 C11-O12 C11-H11 C11-H11A
       C211-C212 C211-H211 C211-H21A C311-C312 C311-H311 C311-H31B C12-H12 C12-H12A C212-C213 C212-H212 C212-H21B
       C312-C313 C312-H312 C312-H31C C13-H13 C13-H13A C13-H13B C213-C214 C213-H213 C213-H21C C313-C314 C313-H313
       C313-H31D C14-H14 C14-H14A C14-H14B C214-C215 C214-H214 C214-H21D C314-C315 C314-H314 C314-H31E C15-H15
       C15-H15A C15-H15B C215-C216 C215-H215 C215-H21E C315-C316 C315-H315 C315-H31F C216-C217 C216-H216 C216-H21F
       C316-H316 C316-H31G C316-H31H C217-C218 C217-H217 C217-H21G C218-H218 C218-H21H C218-H21J C21-O21 C21-C22
       C21=O22 C22-C23 C22-H22 C22-H22A C23-C24 C23-H23 C23-H23A C24-C25 C24-H24 C24-H24A C25-C26 C25-H25 C25-H25A
       C26-C27 C26-H26 C26-H26A C27-C28 C27-H27 C27-H27A C28-C29 C28-H28 C28-H28A C29-H29 C31-O31 C31-C32 C31=O32
       C32-C33 C32-H32 C32-H32A C33-C34 C33-H33 C33-H33A C34-C35 C34-H34 C34-H34A C35-C36 C35-H35 C35-H35A C36-C37
       C36-H36 C36-H36A C37-C38 C37-H37 C37-H37A C38-C39 C38-H38 C38-H38A C39-H39 C39-H39A
note: tails point toward −y, head toward +y

@molecule CLR cholesterol (a steroid)   (C27 H46 O, M = 386.654 g/mol, CCD CLR)
C1   C    -4.568    1.305   -0.761
C2   C    -5.868    1.568    0.002
C3   C    -6.559    0.239    0.312
C4   C    -5.675   -0.593    1.244
C5   C    -4.308   -0.745    0.599
C6   C    -3.806   -1.940    0.494
C7   C    -2.469   -2.214   -0.130
C8   C    -1.583   -0.972    0.006
C9   C    -2.323    0.215   -0.626
C10  C    -3.615    0.497    0.123
C11  C    -1.458    1.473   -0.652
C12  C    -0.103    1.220   -1.330
C13  C     0.572    0.093   -0.572
C14  C    -0.297   -1.177   -0.774
C15  C     0.633   -2.276   -0.247
C16  C     2.018   -1.864   -0.818
C17  C     1.946   -0.338   -1.077
C18  C     0.646    0.441    0.917
C19  C    -3.265    1.346    1.346
C20  C     3.048    0.384   -0.298
C21  C     2.988    1.883   -0.601
C22  C     4.412   -0.166   -0.718
C23  C     5.505    0.465    0.148
C24  C     6.869   -0.085   -0.271
C25  C     7.962    0.547    0.594
C26  C     7.778    0.107    2.048
C27  C     9.335    0.093    0.094
O1   O    -7.815    0.493    0.947
H11  H    -4.786    0.744   -1.669
H12  H    -4.103    2.255   -1.024
H21  H    -5.643    2.086    0.935
H22  H    -6.527    2.187   -0.605
H3   H    -6.726   -0.309   -0.616
H41  H    -5.573   -0.086    2.204
H42  H    -6.121   -1.576    1.393
H6   H    -4.376   -2.774    0.875
H71  H    -1.996   -3.056    0.376
H72  H    -2.602   -2.453   -1.186
H8   H    -1.375   -0.770    1.057
H9   H    -2.571   -0.042   -1.655
H111 H    -1.986    2.257   -1.197
H112 H    -1.286    1.807    0.371
H121 H    -0.257    0.927   -2.369
H122 H     0.511    2.119   -1.283
H14  H    -0.514   -1.325   -1.831
H151 H     0.650   -2.278    0.843
H152 H     0.333   -3.252   -0.630
H161 H     2.802   -2.084   -0.094
H162 H     2.210   -2.393   -1.752
H17  H     2.038   -0.131   -2.143
H181 H    -0.361    0.599    1.303
H182 H     1.120   -0.377    1.458
H183 H     1.233    1.351    1.048
H191 H    -4.176    1.584    1.896
H192 H    -2.586    0.791    1.993
H193 H    -2.785    2.269    1.023
H20  H     2.903    0.223    0.770
H211 H     3.067    2.040   -1.675
H212 H     2.040    2.287   -0.243
H213 H     3.811    2.388   -0.097
H221 H     4.424   -1.248   -0.586
H222 H     4.594    0.074   -1.766
H231 H     5.492    1.548    0.016
H232 H     5.323    0.226    1.196
H241 H     6.881   -1.166   -0.140
H242 H     7.052    0.155   -1.319
H25  H     7.892    1.633    0.531
H261 H     6.800    0.431    2.404
H262 H     7.846   -0.978    2.111
H263 H     8.556    0.558    2.665
H271 H     9.465    0.406   -0.942
H272 H    10.113    0.543    0.711
H273 H     9.403   -0.993    0.157
H1   H    -8.310   -0.306    1.173
bonds: C1-C2 C1-C10 C1-H11 C1-H12 C2-C3 C2-H21 C2-H22 C3-C4 C3-O1 C3-H3 C4-C5 C4-H41 C4-H42 C5=C6 C5-C10 C6-C7
       C6-H6 C7-C8 C7-H71 C7-H72 C8-C9 C8-C14 C8-H8 C9-C10 C9-C11 C9-H9 C10-C19 C11-C12 C11-H111 C11-H112 C12-C13
       C12-H121 C12-H122 C13-C14 C13-C17 C13-C18 C14-C15 C14-H14 C15-C16 C15-H151 C15-H152 C16-C17 C16-H161 C16-H162
       C17-C20 C17-H17 C18-H181 C18-H182 C18-H183 C19-H191 C19-H192 C19-H193 C20-C21 C20-C22 C20-H20 C21-H211
       C21-H212 C21-H213 C22-C23 C22-H221 C22-H222 C23-C24 C23-H231 C23-H232 C24-C25 C24-H241 C24-H242 C25-C26
       C25-C27 C25-H25 C26-H261 C26-H262 C26-H263 C27-H271 C27-H272 C27-H273 O1-H1
""";

    /// <summary>§6.10: ubiquitin (1UBQ): i, aa, ss, CA (x y z), side-chain centroid (x y z).</summary>
    public const string UbiquitinText = """
# ubiquitin (PDB 1UBQ, 76 residues): i  aa  ss  CA(x y z)  side-chain centroid(x y z); Å, centred, principal axes; ss: H helix, E strand, C coil (from the file's HELIX/SHEET records)
 1 M E  -11.9   -6.0    3.7   -11.8   -3.5    1.5
 2 Q E   -8.9   -7.9    2.5    -8.1  -11.1    2.0
 3 I E   -6.5   -6.4   -0.1    -6.1   -4.0    0.3
 4 F E   -3.3   -7.7   -1.6    -5.1  -10.5   -2.1
 5 V E    0.0   -5.8   -1.8     0.6   -4.5   -0.5
 6 K E    2.4   -7.3   -4.4     2.1   -8.8   -8.0
 7 T E    6.1   -6.5   -4.0     6.7   -7.2   -2.3
 8 L C    8.8   -6.5   -6.6     9.1   -4.2   -7.8
 9 T C   10.3   -9.7   -5.2    11.0   -9.3   -3.5
10 G E    7.2  -11.8   -5.9     7.2  -11.8   -5.9
11 K E    6.0  -11.8   -2.3     6.3  -11.7    1.3
12 T E    2.3  -10.8   -1.9     0.9  -11.0   -3.1
13 I E    1.2   -9.5    1.5     2.3   -7.7    2.7
14 T E   -2.5   -9.6    2.6    -3.0  -11.4    2.8
15 L E   -3.8   -6.7    4.7    -2.2   -4.6    4.4
16 E E   -7.1   -6.1    6.4    -8.3   -8.8    8.1
17 V E   -8.3   -2.6    5.8    -7.1   -2.1    4.3
18 E C  -11.4   -0.4    5.6   -13.1   -1.3    7.4
19 P C  -12.0    1.9    2.6   -13.5    0.8    2.5
20 S C  -12.0    4.8    5.1   -13.0    4.3    6.7
21 D C   -8.4    4.2    6.0    -8.0    2.1    7.2
22 T C   -5.9    6.7    4.7    -5.4    7.9    6.1
23 I H   -2.9    5.7    2.6    -2.9    5.9    0.6
24 E H   -0.6    6.8    5.5    -0.3    9.4    7.7
25 N H   -2.4    4.2    7.6    -4.2    3.0    8.8
26 V H   -2.0    1.4    5.0    -2.8    1.1    3.3
27 K H    1.8    2.2    4.9     2.7    4.8    2.3
28 A H    2.0    1.9    8.7     1.2    2.8    9.7
29 K H    0.2   -1.5    8.6    -3.4   -1.7    8.5
30 I H    2.8   -2.6    5.9     2.8   -2.2    3.5
31 Q H    5.6   -1.4    8.2     7.1    1.0    9.8
32 D H    4.2   -3.2   11.1     2.8   -3.2   13.1
33 K H    3.9   -6.5    9.2     1.3   -8.1    7.6
34 E H    7.0   -6.6    6.9     6.1   -8.2    4.9
35 G C    9.2   -3.9    8.5     9.2   -3.9    8.5
36 I C    9.5   -1.6    5.5     8.9   -2.5    3.3
37 P C    9.7    2.1    6.5    11.5    1.6    6.3
38 P C    6.8    4.2    5.2     6.4    4.2    7.0
39 D C    9.2    6.7    3.6    11.4    7.7    3.7
40 Q E   10.5    3.9    1.3    12.9    2.3    2.8
41 Q E    7.0    2.8    0.1     5.5    1.3    2.7
42 R E    5.5    3.9   -3.2     6.5    4.6   -7.5
43 L E    1.9    2.4   -3.6     1.0    1.6   -1.2
44 I E    0.4    2.0   -7.1     2.0    2.4   -8.5
45 F E   -3.2    1.2   -8.1    -6.4    0.2   -7.4
46 A C   -4.7    1.5  -11.5    -6.1    1.8  -11.3
47 G C   -1.6    3.3  -12.7    -1.6    3.3  -12.7
48 K E   -1.9    6.1  -10.1    -5.3    6.5  -10.4
49 Q E    0.7    6.6   -7.2     3.6    7.7   -6.4
50 L E   -1.4    6.7   -4.0    -2.6    4.5   -4.0
51 E C   -1.0    9.9   -1.8    -1.2   13.1   -2.4
52 D C   -0.8    9.7    2.0     1.6    9.3    2.7
53 G C   -3.7   11.9    3.0    -3.7   11.9    3.0
54 R C   -6.3   10.3    0.7    -6.6   12.9   -0.4
55 T C   -8.4    7.3    1.8   -10.1    7.9    2.4
56 L H   -8.7    4.0   -0.2    -7.6    1.9    1.1
57 S H  -12.3    5.0   -1.0   -13.3    5.9    0.3
58 D H  -11.0    8.1   -2.8   -10.3   10.0   -1.3
59 Y H   -9.4    5.7   -5.3    -6.0    7.2   -4.6
60 N C  -12.4    3.3   -5.5   -14.2    2.8   -7.2
61 I C  -10.3    0.4   -4.2    -8.0    0.8   -3.3
62 Q C  -12.7   -2.5   -3.6   -15.5   -2.5   -5.4
63 K C  -12.3   -5.8   -1.7   -15.4   -7.6   -1.6
64 E E   -9.7   -8.1   -3.0   -11.7  -10.4   -2.3
65 S E   -7.9   -5.4   -5.1    -8.8   -3.7   -5.0
66 T E   -4.1   -5.7   -5.5    -3.2   -7.2   -6.3
67 L E   -1.9   -2.6   -5.0    -3.0   -1.3   -3.0
68 H E    1.7   -2.7   -6.2     1.0   -3.8   -9.0
69 L E    4.5   -1.7   -3.8     5.4   -1.9   -1.3
70 V E    7.7   -0.2   -5.3     7.8    0.5   -7.1
71 L E   10.5    0.7   -2.9    10.7   -1.0   -1.0
72 R E   12.3    4.0   -3.2    10.1    7.0   -2.3
73 L C   15.6    3.7   -1.1    16.0    1.2   -0.6
74 R C   17.4    6.9    0.1    16.6    9.2    3.1
75 G C   20.9    6.5   -1.2    20.9    6.5   -1.2
76 G C   22.7    4.8   -4.1    22.7    4.8   -4.1
""";

    /// <summary>§6.10: haemoglobin (2HHB): chain, i, aa, ss, CA (x y z); then the four hemes.</summary>
    public const string HemoglobinText = """
# hemoglobin (PDB 2HHB, deoxy): chain A, C = α (141 aa); B, D = β (146 aa). Rows: chain i aa ss CA(x y z); Å, centred, principal axes
A   1 V C   -7.4   -3.7  -18.1
A   2 L C  -11.1   -2.6  -18.2
A   3 S H  -13.1   -5.2  -20.1
A   4 P H  -16.0   -3.9  -22.4
A   5 A H  -18.4   -5.0  -19.7
A   6 D H  -16.5   -2.9  -17.2
A   7 K H  -16.9    0.1  -19.5
A   8 T H  -20.6   -0.5  -19.9
A   9 N H  -20.9   -0.7  -16.1
A  10 V H  -19.0    2.6  -15.5
A  11 K H  -21.0    4.4  -18.2
A  12 A H  -24.2    3.3  -16.8
A  13 A H  -23.4    4.2  -13.2
A  14 W H  -21.8    7.5  -14.0
A  15 G H  -24.7    8.2  -16.3
A  16 K H  -27.0    8.0  -13.2
A  17 V H  -24.6   10.3  -11.3
A  18 G H  -25.3   12.8  -14.0
A  19 A C  -25.6   16.3  -12.6
A  20 H H  -24.7   15.5   -9.1
A  21 A H  -21.2   14.8  -10.1
A  22 G H  -19.8   17.8   -8.4
A  23 E H  -21.8   17.5   -5.2
A  24 Y H  -20.6   13.8   -5.1
A  25 G H  -17.0   14.9   -5.7
A  26 A H  -17.1   17.3   -2.7
A  27 E H  -18.7   14.7   -0.5
A  28 A H  -16.0   12.1   -1.3
A  29 L H  -13.3   14.8   -0.5
A  30 E H  -14.9   15.5    2.9
A  31 R H  -15.1   11.7    3.6
A  32 M H  -11.5   11.4    2.8
A  33 F H  -10.3   14.3    5.0
A  34 L H  -12.3   12.9    8.0
A  35 S H  -11.5    9.2    7.6
A  36 F H   -7.9    9.7    6.5
A  37 P H   -6.6   12.9    8.1
A  38 T H   -3.1   12.5    6.6
A  39 T H   -4.6   13.5    3.2
A  40 K H   -5.1   17.0    4.6
A  41 T H   -1.4   17.6    4.4
A  42 Y H   -1.9   18.3    0.7
A  43 F C   -4.5   21.0    1.3
A  44 P C   -3.0   23.3    4.0
A  45 H C   -4.1   26.3    1.9
A  46 F C   -7.8   25.3    1.9
A  47 D C  -10.6   26.1    4.3
A  48 L C  -11.5   22.5    4.9
A  49 S C  -14.6   23.3    6.9
A  50 H H  -17.8   21.6    5.8
A  51 G H  -19.4   23.2    2.8
A  52 S H  -16.5   25.5    2.0
A  53 A H  -16.4   27.0   -1.5
A  54 Q H  -12.9   25.7   -2.0
A  55 V H  -13.9   22.1   -1.3
A  56 K H  -17.1   22.3   -3.4
A  57 G H  -15.0   23.9   -6.2
A  58 H H  -12.4   21.2   -6.1
A  59 G H  -15.1   18.5   -5.9
A  60 K H  -16.5   19.8   -9.2
A  61 K H  -13.1   19.8  -10.8
A  62 V H  -12.5   16.2   -9.7
A  63 A H  -15.8   15.1  -10.9
A  64 D H  -15.4   16.8  -14.4
A  65 A H  -12.1   15.0  -14.8
A  66 L H  -13.8   11.7  -14.0
A  67 T H  -16.5   12.5  -16.4
A  68 N H  -13.9   13.2  -19.1
A  69 A H  -12.3    9.8  -18.4
A  70 V H  -15.6    7.9  -18.6
A  71 A H  -16.2    9.7  -21.9
A  72 H C  -12.8    8.6  -23.2
A  73 V C  -12.4    5.3  -21.4
A  74 D C  -10.7    3.8  -24.4
A  75 D C   -8.1    6.5  -24.4
A  76 M C   -7.6    7.7  -20.9
A  77 P C   -3.8    8.3  -21.2
A  78 N C   -4.4   10.9  -23.9
A  79 A C   -7.5   12.2  -22.2
A  80 L H   -5.9   12.9  -18.8
A  81 S H   -2.4   13.9  -20.1
A  82 A H   -2.7   17.4  -18.7
A  83 L H   -3.7   16.1  -15.3
A  84 S H   -1.0   13.5  -15.1
A  85 D H    1.7   16.2  -15.9
A  86 L H    0.2   18.4  -13.1
A  87 H H    0.2   15.7  -10.5
A  88 A H    3.6   14.4  -11.5
A  89 H C    5.5   17.7  -11.8
A  90 K C    3.7   20.2   -9.6
A  91 L C    1.4   18.7   -7.0
A  92 R C    3.4   15.5   -6.4
A  93 V C    0.9   13.7   -4.2
A  94 D H    2.3   10.4   -3.0
A  95 P H    0.6    7.6   -4.9
A  96 V H   -0.8    5.9   -1.8
A  97 N H   -3.4    8.7   -1.4
A  98 F H   -5.1    7.9   -4.8
A  99 K H   -6.3    4.6   -3.1
A 100 L H   -7.8    6.6   -0.3
A 101 L H   -9.8    9.0   -2.4
A 102 S H  -10.9    6.1   -4.7
A 103 H H  -12.3    4.2   -1.6
A 104 C H  -14.2    7.4   -0.5
A 105 L H  -15.6    8.0   -4.0
A 106 L H  -16.8    4.4   -4.1
A 107 V H  -18.4    4.9   -0.6
A 108 T H  -20.1    8.1   -1.9
A 109 L H  -21.5    6.3   -5.0
A 110 A H  -22.7    3.4   -2.9
A 111 A H  -24.6    5.8   -0.5
A 112 H H  -26.3    7.6   -3.4
A 113 L C  -27.0    4.9   -6.0
A 114 P C  -28.8    2.1   -4.0
A 115 A C  -30.3    0.5   -7.2
A 116 E C  -27.2    0.6   -9.4
A 117 F C  -24.4   -0.4   -7.0
A 118 T H  -24.5   -4.1   -7.6
A 119 P H  -21.5   -6.4   -7.0
A 120 A H  -20.6   -6.3  -10.6
A 121 V H  -20.8   -2.6  -10.9
A 122 H H  -18.8   -2.1   -7.6
A 123 A H  -16.1   -4.3   -9.1
A 124 S H  -16.2   -2.3  -12.4
A 125 L H  -16.0    1.0  -10.6
A 126 D H  -13.2   -0.0   -8.3
A 127 K H  -11.2   -1.2  -11.3
A 128 F H  -12.0    2.1  -13.2
A 129 L H  -10.8    4.2  -10.3
A 130 A H   -7.6    2.1  -10.1
A 131 S H   -7.0    2.8  -13.8
A 132 V H   -7.6    6.5  -13.4
A 133 S H   -5.1    6.4  -10.4
A 134 T H   -2.6    4.6  -12.5
A 135 V H   -2.7    7.1  -15.4
A 136 L H   -2.5   10.0  -13.0
A 137 T H    0.7    8.6  -11.4
A 138 S H    2.2    7.5  -14.8
A 139 K C    4.7   10.2  -15.2
A 140 Y C    5.9   10.5  -11.6
A 141 R C    9.3    9.0  -12.4
B   1 V C   -8.7    1.4   19.9
B   2 H C   -9.5    4.9   20.8
B   3 L C  -13.0    6.2   19.9
B   4 T H  -14.5    9.2   21.4
B   5 P H  -17.8    8.4   23.2
B   6 E H  -19.6   10.1   20.4
B   7 E H  -17.8    7.9   18.0
B   8 K H  -18.6    4.8   20.0
B   9 S H  -22.3    5.7   20.3
B  10 A H  -22.5    6.3   16.5
B  11 V H  -21.0    2.9   15.7
B  12 T H  -23.3    1.1   18.2
B  13 A H  -26.4    2.9   17.1
B  14 L H  -26.1    2.0   13.4
B  15 W H  -25.1   -1.5   14.2
B  16 G H  -28.3   -2.1   16.0
B  17 K H  -30.0   -1.7   12.7
B  18 V H  -27.8   -4.4   10.9
B  19 N H  -29.1   -7.8    9.9
B  20 V H  -25.8   -9.5   10.7
B  21 D H  -26.5  -12.6    8.6
B  22 E H  -27.5  -10.6    5.5
B  23 V H  -24.7   -8.1    5.7
B  24 G H  -22.3  -10.9    6.4
B  25 G H  -23.3  -12.7    3.2
B  26 E H  -23.2   -9.5    1.2
B  27 A H  -19.7   -8.4    2.4
B  28 L H  -18.1  -11.8    2.0
B  29 G H  -19.5  -12.2   -1.5
B  30 R H  -18.6   -8.8   -2.7
B  31 L H  -15.1   -9.4   -1.3
B  32 L H  -14.8  -12.4   -3.5
B  33 V H  -16.3  -10.6   -6.5
B  34 V H  -14.4   -7.3   -6.4
B  35 Y H  -11.0   -8.9   -5.3
B  36 P H  -11.2  -12.4   -6.9
B  37 W H   -7.8  -13.5   -5.6
B  38 T H   -9.5  -13.8   -2.2
B  39 Q H  -11.3  -16.9   -3.7
B  40 R H   -8.0  -18.8   -3.1
B  41 F H   -9.2  -20.0    0.4
B  42 F C  -12.7  -21.1   -0.7
B  43 E C  -12.5  -23.7   -3.6
B  44 S C  -14.2  -26.2   -1.2
B  45 F C  -17.2  -23.9   -1.6
B  46 G C  -18.4  -24.9   -5.2
B  47 D C  -19.3  -22.5   -7.9
B  48 L C  -17.6  -19.1   -7.7
B  49 S C  -17.5  -18.5  -11.5
B  50 T H  -19.8  -15.5  -11.8
B  51 P H  -21.0  -12.7   -9.5
B  52 D H  -24.4  -14.2   -9.4
B  53 A H  -23.0  -17.6   -8.4
B  54 V H  -20.9  -16.1   -5.7
B  55 M H  -23.6  -14.0   -4.2
B  56 G H  -26.0  -16.9   -4.3
B  57 N H  -23.7  -19.6   -2.9
B  58 P H  -24.9  -20.9    0.4
B  59 K H  -21.4  -21.7    1.7
B  60 V H  -20.3  -18.2    1.0
B  61 K H  -23.4  -16.9    2.9
B  62 A H  -22.7  -19.2    5.8
B  63 H H  -19.0  -18.2    6.1
B  64 G H  -20.1  -14.5    5.9
B  65 K H  -22.4  -15.0    8.8
B  66 K H  -19.8  -16.6   10.9
B  67 V H  -17.2  -13.9   10.1
B  68 L H  -19.6  -11.0   10.8
B  69 G H  -21.0  -12.6   14.0
B  70 A H  -17.4  -12.6   15.4
B  71 F H  -17.1   -9.0   14.2
B  72 S H  -20.3   -8.1   15.9
B  73 D H  -19.2   -9.6   19.2
B  74 G H  -16.0   -7.6   18.9
B  75 L H  -18.0   -4.4   18.8
B  76 A H  -18.8   -5.0   22.4
B  77 H C  -15.0   -5.0   23.3
B  78 L C  -13.7   -2.0   21.4
B  79 D C  -11.5   -1.3   24.4
B  80 N C   -9.6   -4.5   23.9
B  81 L C   -9.8   -5.9   20.4
B  82 K C   -6.4   -7.4   20.6
B  83 G C   -7.3   -9.6   23.5
B  84 T C  -10.7  -10.4   22.0
B  85 F H   -9.2  -11.6   18.7
B  86 A H   -5.9  -13.1   19.9
B  87 T H   -6.9  -16.7   19.4
B  88 L H   -8.5  -16.0   16.1
B  89 S H   -5.3  -14.3   15.0
B  90 E H   -3.2  -17.3   15.9
B  91 L H   -5.6  -19.5   14.1
B  92 H H   -5.7  -17.5   10.9
B  93 C H   -1.9  -17.4   10.8
B  94 D C   -0.8  -20.9   11.9
B  95 K C   -3.6  -23.0   10.5
B  96 L C   -5.5  -21.1    7.9
B  97 H C   -2.6  -19.1    6.3
B  98 V C   -4.6  -16.0    5.5
B  99 D H   -2.5  -13.0    4.5
B 100 P H   -3.8  -10.2    6.8
B 101 E H   -4.2   -7.9    3.8
B 102 N H   -7.5   -9.8    3.3
B 103 F H   -8.7   -8.4    6.6
B 104 R H   -8.2   -4.8    5.3
B 105 L H   -9.9   -5.6    2.0
B 106 L H  -12.8   -7.0    3.9
B 107 G H  -13.1   -4.0    6.1
B 108 N H  -13.2   -1.6    3.1
B 109 V H  -15.9   -3.7    1.4
B 110 L H  -17.8   -3.8    4.7
B 111 V H  -17.8    0.0    4.8
B 112 C H  -19.1    0.1    1.1
B 113 V H  -21.8   -2.3    2.2
B 114 L H  -22.9   -0.2    5.1
B 115 A H  -22.9    2.8    2.9
B 116 H H  -25.1    1.0    0.3
B 117 H H  -27.5   -0.3    3.0
B 118 F C  -27.8    3.0    5.0
B 119 G C  -27.5    5.6    2.3
B 120 K C  -27.6    9.1    3.7
B 121 E C  -27.5    7.9    7.3
B 122 F C  -24.0    6.7    6.7
B 123 T H  -22.6   10.2    7.1
B 124 P H  -18.9   11.2    6.7
B 125 P H  -18.4   11.3   10.6
B 126 V H  -20.0    7.9   10.9
B 127 Q H  -17.9    6.5    8.1
B 128 A H  -14.8    7.9    9.9
B 129 A H  -15.8    6.0   13.1
B 130 Y H  -16.3    2.8   11.2
B 131 Q H  -13.0    3.2    9.4
B 132 K H  -11.3    3.3   12.8
B 133 V H  -13.2    0.2   13.9
B 134 V H  -12.4   -1.8   10.8
B 135 A H   -8.7   -0.9   10.9
B 136 G H   -8.6   -1.9   14.6
B 137 V H  -10.2   -5.2   13.9
B 138 A H   -7.7   -5.9   11.1
B 139 N H   -4.8   -4.9   13.3
B 140 A H   -6.1   -7.1   16.1
B 141 L H   -6.4  -10.1   13.8
B 142 A H   -3.0   -9.5   12.4
B 143 H H   -1.3   -9.1   15.8
B 144 K C   -0.2  -12.6   16.5
B 145 Y C    1.2  -13.3   13.0
B 146 H C    4.9  -14.5   13.0
C   1 V C    7.9    3.6  -18.1
C   2 L C   11.5    2.7  -18.7
C   3 S H   13.6    5.2  -20.4
C   4 P H   16.5    4.1  -22.6
C   5 A H   18.8    5.1  -19.7
C   6 D H   16.8    2.9  -17.3
C   7 K H   17.1   -0.0  -19.7
C   8 T H   20.8    0.5  -20.0
C   9 N H   21.1    0.7  -16.1
C  10 V H   19.1   -2.5  -15.7
C  11 K H   21.0   -4.5  -18.3
C  12 A H   24.2   -3.4  -16.8
C  13 A H   23.3   -4.2  -13.2
C  14 W H   21.7   -7.6  -14.0
C  15 G H   24.7   -8.3  -16.3
C  16 K H   27.1   -8.1  -13.3
C  17 V H   24.6  -10.4  -11.4
C  18 G H   25.2  -12.9  -14.1
C  19 A C   25.7  -16.4  -12.7
C  20 H H   24.8  -15.5   -9.1
C  21 A H   21.2  -14.6  -10.0
C  22 G H   19.9  -17.8   -8.4
C  23 E H   21.8  -17.3   -5.2
C  24 Y H   20.6  -13.7   -4.9
C  25 G H   17.0  -14.8   -5.6
C  26 A H   17.1  -17.2   -2.6
C  27 E H   18.7  -14.5   -0.4
C  28 A H   15.9  -12.0   -1.4
C  29 L H   13.3  -14.6   -0.4
C  30 E H   14.9  -15.3    3.0
C  31 R H   15.2  -11.5    3.6
C  32 M H   11.4  -11.2    2.7
C  33 F H   10.3  -14.1    5.0
C  34 L H   12.3  -12.7    8.0
C  35 S H   11.4   -9.0    7.5
C  36 F H    7.7   -9.4    6.4
C  37 P H    6.5  -12.6    8.1
C  38 T H    3.0  -12.2    6.6
C  39 T H    4.6  -13.3    3.2
C  40 K H    5.2  -16.8    4.7
C  41 T H    1.4  -17.4    4.5
C  42 Y H    1.8  -18.3    0.8
C  43 F C    4.4  -21.0    1.5
C  44 P C    2.9  -23.3    4.2
C  45 H C    4.2  -26.3    2.2
C  46 F C    7.9  -25.2    2.3
C  47 D C   10.6  -25.6    4.8
C  48 L C   11.6  -21.9    5.2
C  49 S C   14.6  -22.6    7.5
C  50 H H   17.9  -21.0    6.6
C  51 G H   19.6  -22.9    3.7
C  52 S H   16.4  -24.7    2.7
C  53 A H   16.7  -26.4   -0.8
C  54 Q H   13.1  -25.5   -1.4
C  55 V H   13.8  -21.8   -1.0
C  56 K H   17.0  -22.0   -3.0
C  57 G H   15.1  -23.7   -5.8
C  58 H H   12.3  -21.2   -5.8
C  59 G H   14.9  -18.4   -5.7
C  60 K H   16.5  -19.8   -8.9
C  61 K H   13.1  -19.8  -10.7
C  62 V H   12.3  -16.2   -9.6
C  63 A H   15.7  -15.1  -10.7
C  64 D H   15.5  -16.8  -14.1
C  65 A H   12.1  -15.2  -14.8
C  66 L H   13.8  -11.7  -14.0
C  67 T H   16.6  -12.6  -16.3
C  68 N H   14.0  -13.4  -18.9
C  69 A H   12.3  -10.0  -18.2
C  70 V H   15.6   -8.1  -18.7
C  71 A H   16.2   -9.8  -22.1
C  72 H C   12.7   -8.8  -23.2
C  73 V C   12.4   -5.6  -21.5
C  74 D C   10.6   -4.0  -24.6
C  75 D C    8.0   -6.8  -24.6
C  76 M C    7.6   -7.9  -21.0
C  77 P C    3.8   -8.6  -21.1
C  78 N C    4.2  -11.4  -23.6
C  79 A C    7.4  -12.8  -22.1
C  80 L H    5.8  -13.1  -18.6
C  81 S H    2.3  -14.0  -19.9
C  82 A H    2.2  -17.5  -18.4
C  83 L H    3.5  -16.3  -15.1
C  84 S H    0.8  -13.5  -15.0
C  85 D H   -1.7  -16.3  -15.6
C  86 L H   -0.2  -18.4  -12.9
C  87 H H   -0.2  -15.8  -10.2
C  88 A H   -3.6  -14.4  -11.2
C  89 H C   -5.6  -17.6  -11.4
C  90 K C   -3.8  -20.2   -9.3
C  91 L C   -1.4  -18.7   -6.7
C  92 R C   -3.5  -15.6   -6.2
C  93 V C   -0.9  -13.7   -4.1
C  94 D H   -2.1  -10.4   -2.9
C  95 P H   -0.6   -7.5   -5.0
C  96 V H    0.9   -5.9   -1.8
C  97 N H    3.4   -8.6   -1.5
C  98 F H    5.1   -7.8   -4.8
C  99 K H    6.4   -4.5   -3.4
C 100 L H    7.8   -6.4   -0.4
C 101 L H    9.8   -8.9   -2.5
C 102 S H   10.9   -6.0   -4.8
C 103 H H   12.4   -4.1   -1.8
C 104 C H   14.2   -7.3   -0.7
C 105 L H   15.6   -7.8   -4.1
C 106 L H   16.9   -4.3   -4.1
C 107 V H   18.5   -4.8   -0.6
C 108 T H   20.2   -8.0   -1.9
C 109 L H   21.6   -6.3   -5.0
C 110 A H   22.8   -3.4   -2.7
C 111 A H   24.6   -5.9   -0.5
C 112 H H   26.3   -7.6   -3.4
C 113 L C   27.0   -4.8   -5.9
C 114 P C   28.8   -2.1   -3.7
C 115 A C   30.0   -0.4   -6.9
C 116 E C   27.2   -0.8   -9.4
C 117 F C   24.4    0.2   -7.0
C 118 T H   24.6    4.0   -7.7
C 119 P H   21.7    6.3   -6.9
C 120 A H   20.7    6.3  -10.6
C 121 V H   21.0    2.6  -10.9
C 122 H H   18.9    2.2   -7.7
C 123 A H   16.2    4.3   -9.3
C 124 S H   16.1    2.4  -12.5
C 125 L H   16.0   -1.0  -10.7
C 126 D H   13.1    0.1   -8.5
C 127 K H   11.2    1.2  -11.6
C 128 F H   12.0   -2.0  -13.4
C 129 L H   10.8   -4.2  -10.4
C 130 A H    7.6   -2.1  -10.3
C 131 S H    7.0   -2.7  -14.0
C 132 V H    7.6   -6.5  -13.5
C 133 S H    5.1   -6.3  -10.6
C 134 T H    2.5   -4.5  -12.7
C 135 V H    2.8   -7.1  -15.4
C 136 L H    2.6  -10.1  -13.0
C 137 T H   -0.7   -8.6  -11.5
C 138 S H   -2.2   -7.5  -14.8
C 139 K C   -4.7  -10.3  -15.1
C 140 Y C   -5.9  -10.5  -11.5
C 141 R C   -9.3   -9.0  -12.3
D   1 V C    8.7   -1.4   19.7
D   2 H C    9.5   -4.9   20.7
D   3 L C   12.8   -6.4   19.8
D   4 T H   14.3   -9.7   21.0
D   5 P H   17.6   -9.0   22.8
D   6 E H   19.4  -10.5   19.8
D   7 E H   17.6   -8.1   17.5
D   8 K H   18.5   -5.2   19.9
D   9 S H   22.0   -6.1   20.0
D  10 A H   22.1   -6.5   16.2
D  11 V H   20.8   -3.0   15.7
D  12 T H   23.0   -1.4   18.4
D  13 A H   26.2   -3.0   17.1
D  14 L H   25.8   -2.0   13.5
D  15 W H   24.8    1.5   14.5
D  16 G H   28.0    2.1   16.4
D  17 K H   29.9    1.8   13.0
D  18 V H   27.5    4.3   11.1
D  19 N H   28.9    7.7   10.2
D  20 V H   25.7    9.5   10.7
D  21 D H   26.6   12.5    8.7
D  22 E H   27.6   10.4    5.6
D  23 V H   24.8    7.8    5.8
D  24 G H   22.2   10.6    6.5
D  25 G H   23.2   12.6    3.3
D  26 E H   23.2    9.4    1.3
D  27 A H   19.7    8.3    2.5
D  28 L H   18.1   11.7    2.0
D  29 G H   19.7   12.0   -1.4
D  30 R H   18.6    8.6   -2.5
D  31 L H   15.0    9.4   -1.4
D  32 L H   14.9   12.4   -3.5
D  33 V H   16.4   10.5   -6.5
D  34 V H   14.4    7.3   -6.4
D  35 Y H   11.1    8.9   -5.4
D  36 P H   11.4   12.4   -6.9
D  37 W H    7.9   13.6   -5.8
D  38 T H    9.5   13.8   -2.3
D  39 Q H   11.4   16.9   -3.7
D  40 R H    8.0   18.7   -3.1
D  41 F H    9.3   19.6    0.4
D  42 F C   12.7   21.0   -0.5
D  43 E C   12.4   23.5   -3.2
D  44 S C   14.4   26.0   -1.1
D  45 F C   17.3   23.6   -1.5
D  46 G C   18.9   24.3   -4.7
D  47 D C   19.4   22.3   -7.8
D  48 L C   17.7   18.8   -7.5
D  49 S C   17.5   18.2  -11.2
D  50 T H   20.0   15.3  -11.7
D  51 P H   21.2   12.5   -9.4
D  52 D H   24.6   14.1   -9.3
D  53 A H   23.2   17.5   -8.4
D  54 V H   21.0   15.9   -5.6
D  55 M H   23.9   13.8   -4.1
D  56 G H   26.3   16.7   -4.1
D  57 N H   23.8   19.3   -2.8
D  58 P H   25.1   20.6    0.4
D  59 K H   21.7   21.5    1.9
D  60 V H   20.6   17.8    1.2
D  61 K H   23.7   16.6    3.1
D  62 A H   23.1   19.0    6.0
D  63 H H   19.5   18.1    6.4
D  64 G H   20.3   14.4    5.9
D  65 K H   22.5   14.7    8.9
D  66 K H   19.9   16.5   11.0
D  67 V H   17.3   13.8   10.3
D  68 L H   19.5   10.8   11.0
D  69 G H   20.8   12.5   14.1
D  70 A H   17.3   12.6   15.5
D  71 F H   16.8    9.1   14.3
D  72 S H   20.0    8.1   16.3
D  73 D H   18.6    9.6   19.4
D  74 G H   15.5    7.4   19.0
D  75 L H   17.6    4.2   18.9
D  76 A H   18.5    5.2   22.5
D  77 H C   14.8    5.0   23.4
D  78 L C   13.5    2.0   21.5
D  79 D C   11.1    1.3   24.3
D  80 N C    9.2    4.5   23.8
D  81 L C    9.5    5.8   20.4
D  82 K C    6.1    7.6   20.6
D  83 G C    7.1    9.7   23.6
D  84 T C   10.6   10.5   22.1
D  85 F H    9.2   11.7   18.8
D  86 A H    5.9   13.4   19.9
D  87 T H    7.2   16.9   19.4
D  88 L H    8.7   16.3   16.0
D  89 S H    5.5   14.4   15.1
D  90 E H    3.4   17.5   15.8
D  91 L H    5.9   19.7   13.9
D  92 H H    5.9   17.6   10.8
D  93 C H    2.0   17.6   10.8
D  94 D C    1.2   21.2   11.9
D  95 K C    4.1   23.2   10.5
D  96 L C    5.7   21.1    7.8
D  97 H C    2.6   19.3    6.3
D  98 V C    4.6   16.2    5.4
D  99 D H    2.5   13.1    4.4
D 100 P H    3.7   10.4    6.8
D 101 E H    4.0    8.0    3.9
D 102 N H    7.4    9.8    3.3
D 103 F H    8.6    8.4    6.7
D 104 R H    8.1    4.8    5.3
D 105 L H    9.9    5.6    2.0
D 106 L H   12.9    7.0    3.9
D 107 G H   12.9    3.9    6.2
D 108 N H   13.2    1.6    3.2
D 109 V H   15.8    3.8    1.5
D 110 L H   17.8    3.8    4.7
D 111 V H   17.8   -0.0    4.7
D 112 C H   19.0    0.0    1.1
D 113 V H   21.9    2.3    2.2
D 114 L H   22.9    0.2    5.2
D 115 A H   23.0   -2.9    2.9
D 116 H H   25.0   -1.0    0.3
D 117 H H   27.4    0.3    2.9
D 118 F C   27.9   -2.9    5.0
D 119 G C   27.6   -5.6    2.4
D 120 K C   27.4   -9.1    3.6
D 121 E C   27.4   -8.0    7.3
D 122 F C   23.9   -6.7    6.8
D 123 T H   22.4  -10.2    7.0
D 124 P H   18.8  -11.1    6.6
D 125 P H   18.3  -11.2   10.4
D 126 V H   19.9   -7.8   10.8
D 127 Q H   17.7   -6.4    8.1
D 128 A H   14.6   -7.9    9.8
D 129 A H   15.6   -6.0   13.0
D 130 Y H   16.2   -2.7   11.2
D 131 Q H   12.8   -3.1    9.4
D 132 K H   11.0   -3.3   12.7
D 133 V H   12.9   -0.2   13.9
D 134 V H   12.2    2.0   10.9
D 135 A H    8.5    0.9   10.9
D 136 G H    8.5    1.9   14.6
D 137 V H   10.1    5.3   13.9
D 138 A H    7.7    6.0   11.0
D 139 N H    4.8    5.1   13.2
D 140 A H    6.1    7.2   16.1
D 141 L H    6.4   10.2   13.8
D 142 A H    2.9    9.8   12.4
D 143 H H    1.3    9.4   15.8
D 144 K C    0.3   13.0   16.3
D 145 Y C   -1.2   13.7   13.0
D 146 H C   -4.8   15.0   12.9
# heme groups: chain  Fe(x y z)  NA NB NC ND (x y z each): the four pyrrole N atoms around the iron (draw a disk of radius 5 Å through them)
A HEM -5.8 16.0 -7.7  -6.8 17.3 -9.0  -7.2 14.5 -8.3  -5.5 14.9 -6.0  -5.2 17.6 -6.6
B HEM -11.8 -15.8 9.0  -12.8 -16.7 10.6  -12.4 -14.0 9.8  -11.4 -15.0 7.3  -11.8 -17.6 8.0
C HEM 5.9 -16.1 -7.6  6.8 -17.3 -8.9  7.0 -14.5 -8.4  5.5 -15.0 -5.9  5.3 -17.7 -6.5
D HEM 11.9 15.8 9.0  13.0 16.8 10.5  12.6 14.1 9.7  11.5 14.8 7.2  11.9 17.6 7.9
""";

    /// <summary>§6.11: yeast tRNA-Phe (1EHZ), 76 nt, modified bases written as their parent base.</summary>
    public const string TrnaSequence = "GCGGAUUUAGCUCAGUUGGGAGAGCGCCAGACUGAAGAUCUGGAGGUCCUGUGUUCGAUCCACAGAAUUCGCACCA";

    /// <summary>§6.11: its 3D trace: i, base, the P atom (C4′ for G1) x y z, the modified base's code in [ ].</summary>
    public const string TrnaText = """
# yeast tRNA-Phe (PDB 1EHZ): i base  x y z  of its P atom (C4' for G1, which has no P); Å, centred, principal axes. modified bases in [ ]
 1 G   24.7   -9.6    6.5
 2 C   26.9   -7.0    2.6
 3 G   26.1   -4.9   -2.7
 4 G   21.9   -3.4   -6.7
 5 A   15.9   -3.3   -8.8
 6 U   10.5   -3.3   -7.9
 7 U    6.2   -2.4   -3.9
 8 U    2.6   -2.3    1.7
 9 A   -1.6   -0.5    3.2
10 G   -5.5   -1.6    7.7  [2MG]
11 C   -3.5   -6.9    6.1
12 U   -1.8  -10.0    1.6
13 C   -0.1   -9.2   -4.0
14 A    1.1   -4.0   -8.3
15 G    1.3    0.9  -11.3
16 U    3.0    6.2  -11.6  [H2U]
17 U    1.1   11.8  -12.6  [H2U]
18 G    7.2   14.0  -11.1
19 G    2.1   16.2   -8.0
20 G   -2.2   17.0   -3.0
21 A   -5.6   15.3   -0.3
22 G   -8.9    9.9   -1.8
23 A  -11.7    4.4   -3.9
24 G  -14.4   -0.2   -6.2
25 C  -16.1   -5.3   -5.3
26 G  -17.2   -9.0   -0.8  [M2G]
27 C  -18.0   -9.5    4.6
28 C  -20.9   -6.7    8.8
29 A  -25.6   -3.4    9.4
30 G  -30.2   -0.5    6.6
31 A  -33.8    1.5    2.4
32 C  -36.7   -0.5   -1.9  [OMC]
33 U  -38.9   -5.5   -3.7
34 G  -41.5  -10.0   -1.9  [OMG]
35 A  -38.8   -9.5    3.1
36 A  -33.5  -10.8    4.6
37 G  -28.3  -11.5    1.5  [YYG]
38 A  -24.3  -11.8   -2.6
39 U  -22.6   -9.4   -6.8  [PSU]
40 C  -22.6   -4.5   -9.6  [5MC]
41 U  -22.8    1.3   -8.3
42 G  -21.8    5.6   -5.1
43 G  -19.6    8.1    0.1
44 A  -16.7    8.6    5.1
45 G  -12.7    6.9    9.4
46 G   -7.7    4.9    9.7  [7MG]
47 U   -1.9    3.8   10.0
48 C    1.8    7.2    6.6
49 C    5.7    2.9    2.1  [5MC]
50 U    6.6    4.9    7.1
51 G    9.3    8.4   10.5
52 U   12.9   12.8   11.3
53 G   15.8   17.2    8.7
54 U   17.6   20.5    3.8  [5MU]
55 U   16.5   23.3   -0.5  [PSU]
56 C   12.3   26.3   -2.2
57 G    9.1   23.8    1.5
58 A    6.7   18.5    2.0  [1MA]
59 U    3.6   12.7    0.5
60 C    8.4   10.1   -1.0
61 C   11.3    8.2   -6.8
62 A   16.6    8.8   -6.3
63 C   21.3    7.9   -3.5
64 A   23.8    5.4    1.1
65 G   23.2    1.6    5.1
66 A   20.1   -2.5    8.3
67 A   16.4   -6.6    9.4
68 U   12.7  -10.7    7.3
69 U   10.9  -14.3    3.3
70 C   12.5  -17.0   -1.6
71 G   16.1  -19.2   -5.8
72 C   21.7  -20.0   -7.5
73 A   27.6  -20.1   -6.0
74 C   31.6  -20.4   -2.0
75 C   33.0  -22.8    2.7
76 A   32.0  -27.1    5.4
""";

    /// <summary>§6.11: its secondary structure, a row an element: element, positions, pairs (tab-separated).</summary>
    public const string TrnaStructureText = """
Acceptor stem	1–7 / 66–72	G1–C72, C2–G71, G3–C70, G4·U69 (wobble), A5–U68, U6–A67, U7–A66
D stem / D loop	10–13 / 22–25; loop 14–21	G10–C25, C11–G24, U12–A23, C13–G22
Anticodon stem / loop	27–31 / 39–43; loop 32–38	C27–G43, C28–G42, A29–U41, G30–C40, A31–U39
Anticodon	34–36	G34 A35 A36 (reads codon 5′-UUC-3′ = Phe; G34 also wobbles with U, so UUU too)
Variable loop	44–48	—
T stem / TΨC loop	49–53 / 61–65; loop 54–60	C49–G65, U50–A64, G51–C63, U52–A62, G53–C61
Discriminator + CCA end	73, 74–76	A73, C74 C75 A76 (amino acid attaches to A76's 3′ (or 2′) OH)
""";

    /// <summary>§6.11: the real molecule's modified bases (the “modified bases” toggle), as the specification lists them.</summary>
    public const string TrnaModifiedText = "m²G10, D16, D17, m²₂G26, Cm32, Gm34, yW37 (wybutosine), Ψ39, m⁵C40, m⁷G46, m⁵C49, T54 (ribothymidine), Ψ55, m¹A58";

    /// <summary>§6.12: the teaching gene's coding strand in its parts (5′→3′), the parts named under it.</summary>
    public const string TeachingGeneText = """
GCGCTATAAAAGGCGCGTCGAGCTCGGTACC ACTCAGCGCCACC ATGGCCTTCAG GTAAGTATCTTCTTTCCTAACTTTCAG CGATTGGCAGAAAGGCTAA GCAATAAAGCC
promoter (TATAAAA at 5–11)     5′ UTR (+1 = A) exon 1 (CDS) intron (GU…AG)              exon 2 (CDS, stop TAA) 3′ UTR (AATAAA)
""";

    public const string TeachingTemplate = "CGCGATATTTTCCGCGCAGCTCGAGCCATGGTGAGTCGCGGTGGTACCGGAAGTCCATTCATAGAAGAAAGGATTGAAAGTCGCTAACCGTCTTTCCGATTCGTTATTTCGG";

    public const string TeachingPreMRNA = "ACUCAGCGCCACCAUGGCCUUCAGGUAAGUAUCUUCUUUCCUAACUUUCAGCGAUUGGCAGAAAGGCUAAGCAAUAAAGCC";

    public const string TeachingMatureMRNA = "ACUCAGCGCCACCAUGGCCUUCAGCGAUUGGCAGAAAGGCUAAGCAAUAAAGCC";

    public const string TeachingCDS = "ATGGCCTTCAGCGATTGGCAGAAAGGCTAA";

    public const string TeachingProtein = "MAFSDWQKG";

    public const string TeachingUnspliced = "MAFR*";

    /// <summary>§6.12: the teaching gene's mutation test vectors: mutation, mutant CDS (changed bases in **), protein, class.</summary>
    public const string TeachingMutationText = """
c.9C>T	ATGGCCTT**T**AGCGATTGGCAGAAAGGCTAA	MAFSDWQKG	silent (Phe3 Phe)
c.5C>T	ATGG**T**CTTCAGCGATTGGCAGAAAGGCTAA	MVFSDWQKG	missense (Ala2Val)
c.19C>T	ATGGCCTTCAGCGATTGG**T**AGAAAGGCTAA	MAFSDW*	nonsense (Gln7*)
A inserted after c.6	ATGGCC**A**TTCAGCGATTGGCAGAAAGGCTAA	MAIQRLAERL… (no stop before the end)	frameshift
c.7 deleted	ATGGCCTCAGCGATTGGCAGAAAGGCTAA	MASAIGRKA… (no stop before the end)	frameshift
c.13_15 deleted	ATGGCCTTCAGCTGGCAGAAAGGCTAA	MAFSWQKG	in-frame deletion (Asp5 lost)
""";

    /// <summary>§6.12: human β-globin's coding sequence (HBB, NM_000518.5 CDS, 444 nt).</summary>
    public const string HbbCDS = "ATGGTGCATCTGACTCCTGAGGAGAAGTCTGCCGTTACTGCCCTGTGGGGCAAGGTGAACGTGGATGAAGTTGGTGGTGAGGCCCTGGGCAGGCTGCTGGTGGTCTACCCTTGGACCCAGAGGTTCTTTGAGTCCTTTGGGGATCTGTCCACTCCTGATGCTGTTATGGGCAACCCTAAGGTGAAGGCTCATGGCAAGAAAGTGCTCGGTGCCTTTAGTGATGGCCTGGCTCACCTGGACAACCTCAAGGGCACCTTTGCCACACTGAGTGAGCTGCACTGTGACAAGCTGCACGTGGATCCTGAGAACTTCAGGCTCCTGGGCAACGTGCTGGTCTGTGTGCTGGCCCATCACTTTGGCAAAGAATTCACCCCACCAGTGCAGGCTGCCTATCAGAAAGTGGTGGCTGGTGTGGCTAATGCCCTGGCCCACAAGTATCACTAA";

    /// <summary>§6.12: its protein (NP_000509.1, 147 aa).</summary>
    public const string HbbProtein = "MVHLTPEEKSAVTALWGKVNVDEVGGEALGRLLVVYPWTQRFFESFGDLSTPDAVMGNPKVKAHGKKVLGAFSDGLAHLDNLKGTFATLSELHCDKLHVDPENFRLLGNVLVCVLAHHFGKEFTPPVQAAYQKVVAGVANALAHKYH";

    /// <summary>§6.12: the sickle-cell (c.20A&gt;T) protein's start.</summary>
    public const string HbbSickleStart = "MVHLTPVEKS";

    /// <summary>§5.3.4: chi-square critical values: df, then p = 0.10, 0.05, 0.01, 0.001 (tab-separated).</summary>
    public const string ChiSquareText = """
1	2.71	3.84	6.63	10.83
2	4.61	5.99	9.21	13.82
3	6.25	7.81	11.34	16.27
4	7.78	9.49	13.28	18.47
5	9.24	11.07	15.09	20.52
6	10.64	12.59	16.81	22.46
7	12.02	14.07	18.48	24.32
8	13.36	15.51	20.09	26.12
9	14.68	16.92	21.67	27.88
10	15.99	18.31	23.21	29.59
""";
}
