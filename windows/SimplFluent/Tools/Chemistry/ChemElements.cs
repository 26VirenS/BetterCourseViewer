using System.Globalization;

namespace Simpl.Tools;

/// <summary>
/// An element as the chemistry builders read it (the Mac's PTElement and PTExtra, the part LChem and MBChem use): its
/// symbol, name, atomic mass, group, period, block, Pauling electronegativity, CPK colour (the 3D atoms') and covalent and
/// van der Waals radii (pm).
/// </summary>
internal sealed record ChemElement(int Number, string Symbol, string Name, double Mass, int? Group, int Period, string Block,
    double? En, string Cpk, double? Covalent, double? VanDerWaals);

/// <summary>
/// The elements for the Lewis maker and the molecule builder, from the same data as the Mac's PeriodicData.swift and
/// PeriodicExtraData.swift (Bowserinator's Periodic-Table-JSON, CC BY-SA 3.0): number, symbol, name, mass, group, period,
/// block, electronegativity, CPK colour, covalent radius, van der Waals radius — read once.
/// </summary>
internal static class ChemElements
{
    private const string Data = """
        1|H|Hydrogen|1.008|1|1|s|2.2|ffffff|32|120
        2|He|Helium|4.003|18|1|s||d9ffff|46|140
        3|Li|Lithium|6.94|1|2|s|0.98|cc80ff|133|182
        4|Be|Beryllium|9.012|2|2|s|1.57|c2ff00|102|153
        5|B|Boron|10.81|13|2|p|2.04|ffb5b5|85|192
        6|C|Carbon|12.011|14|2|p|2.55|909090|75|170
        7|N|Nitrogen|14.007|15|2|p|3.04|3050f8|71|155
        8|O|Oxygen|15.999|16|2|p|3.44|ff0d0d|63|152
        9|F|Fluorine|18.998|17|2|p|3.98|90e050|64|147
        10|Ne|Neon|20.18|18|2|p||b3e3f5|67|154
        11|Na|Sodium|22.99|1|3|s|0.93|ab5cf2|155|227
        12|Mg|Magnesium|24.305|2|3|s|1.31|8aff00|139|173
        13|Al|Aluminium|26.982|13|3|p|1.61|bfa6a6|126|184
        14|Si|Silicon|28.085|14|3|p|1.9|f0c8a0|116|210
        15|P|Phosphorus|30.974|15|3|p|2.19|ff8000|111|180
        16|S|Sulfur|32.06|16|3|p|2.58|ffff30|103|180
        17|Cl|Chlorine|35.45|17|3|p|3.16|1ff01f|99|175
        18|Ar|Argon|39.948|18|3|p||80d1e3|96|188
        19|K|Potassium|39.098|1|4|s|0.82|8f40d4|196|275
        20|Ca|Calcium|40.078|2|4|s|1|3dff00|171|231
        21|Sc|Scandium|44.956|3|4|d|1.36|e6e6e6|148|211
        22|Ti|Titanium|47.867|4|4|d|1.54|bfc2c7|136|
        23|V|Vanadium|50.942|5|4|d|1.63|a6a6ab|134|
        24|Cr|Chromium|51.996|6|4|d|1.66|8a99c7|122|
        25|Mn|Manganese|54.938|7|4|d|1.55|9c7ac7|119|
        26|Fe|Iron|55.845|8|4|d|1.83|e06633|116|
        27|Co|Cobalt|58.933|9|4|d|1.88|f090a0|111|
        28|Ni|Nickel|58.693|10|4|d|1.91|50d050|110|163
        29|Cu|Copper|63.546|11|4|d|1.9|c88033|112|140
        30|Zn|Zinc|65.382|12|4|d|1.65|7d80b0|118|139
        31|Ga|Gallium|69.723|13|4|p|1.81|c28f8f|124|187
        32|Ge|Germanium|72.631|14|4|p|2.01|668f8f|121|211
        33|As|Arsenic|74.922|15|4|p|2.18|bd80e3|121|185
        34|Se|Selenium|78.972|16|4|p|2.55|ffa100|116|190
        35|Br|Bromine|79.904|17|4|p|2.96|a62929|114|185
        36|Kr|Krypton|83.798|18|4|p|3|5cb8d1|117|202
        37|Rb|Rubidium|85.468|1|5|s|0.82|702eb0|210|303
        38|Sr|Strontium|87.621|2|5|s|0.95|00ff00|185|249
        39|Y|Yttrium|88.906|3|5|d|1.22|94ffff|163|
        40|Zr|Zirconium|91.224|4|5|d|1.33|94e0e0|154|
        41|Nb|Niobium|92.906|5|5|d|1.6|73c2c9|147|
        42|Mo|Molybdenum|95.951|6|5|d|2.16|54b5b5|138|
        43|Tc|Technetium|98|7|5|d|1.9|3b9e9e|128|
        44|Ru|Ruthenium|101.072|8|5|d|2.2|248f8f|125|
        45|Rh|Rhodium|102.906|9|5|d|2.28|0a7d8c|125|
        46|Pd|Palladium|106.421|10|5|d|2.2|006985|120|163
        47|Ag|Silver|107.868|11|5|d|1.93|c0c0c0|128|172
        48|Cd|Cadmium|112.414|12|5|d|1.69|ffd98f|136|158
        49|In|Indium|114.818|13|5|p|1.78|a67573|142|193
        50|Sn|Tin|118.711|14|5|p|1.96|668080|140|217
        51|Sb|Antimony|121.76|15|5|p|2.05|9e63b5|140|206
        52|Te|Tellurium|127.603|16|5|p|2.1|d47a00|136|206
        53|I|Iodine|126.904|17|5|p|2.66|940094|133|198
        54|Xe|Xenon|131.294|18|5|p|2.6|429eb0|131|216
        55|Cs|Cesium|132.905|1|6|s|0.79|57178f|232|343
        56|Ba|Barium|137.328|2|6|s|0.89|00c900|196|268
        57|La|Lanthanum|138.905|3|6|f|1.1|70d4ff|180|
        58|Ce|Cerium|140.116|3|6|f|1.12|ffffc7|163|
        59|Pr|Praseodymium|140.908|3|6|f|1.13|d9ffc7|176|
        60|Nd|Neodymium|144.242|3|6|f|1.14|c7ffc7|174|
        61|Pm|Promethium|145|3|6|f|1.13|a3ffc7|173|
        62|Sm|Samarium|150.362|3|6|f|1.17|8fffc7|172|
        63|Eu|Europium|151.964|3|6|f|1.2|61ffc7|168|
        64|Gd|Gadolinium|157.253|3|6|f|1.2|45ffc7|169|
        65|Tb|Terbium|158.925|3|6|f|1.1|30ffc7|168|
        66|Dy|Dysprosium|162.5|3|6|f|1.22|1fffc7|167|
        67|Ho|Holmium|164.93|3|6|f|1.23|00ff9c|166|
        68|Er|Erbium|167.259|3|6|f|1.24|00e675|165|
        69|Tm|Thulium|168.934|3|6|f|1.25|00d452|164|
        70|Yb|Ytterbium|173.045|3|6|f|1.1|00bf38|170|
        71|Lu|Lutetium|174.967|3|6|d|1.27|00ab24|162|
        72|Hf|Hafnium|178.492|4|6|d|1.3|4dc2ff|152|
        73|Ta|Tantalum|180.948|5|6|d|1.5|4da6ff|146|
        74|W|Tungsten|183.841|6|6|d|2.36|2194d6|137|
        75|Re|Rhenium|186.207|7|6|d|1.9|267dab|131|
        76|Os|Osmium|190.233|8|6|d|2.2|266696|129|
        77|Ir|Iridium|192.217|9|6|d|2.2|175487|122|
        78|Pt|Platinum|195.085|10|6|d|2.28|d0d0e0|123|175
        79|Au|Gold|196.967|11|6|d|2.54|ffd123|124|166
        80|Hg|Mercury|200.592|12|6|d|2|b8b8d0|133|155
        81|Tl|Thallium|204.38|13|6|p|1.62|a6544d|144|196
        82|Pb|Lead|207.21|14|6|p|1.87|575961|144|202
        83|Bi|Bismuth|208.98|15|6|p|2.02|9e4fb5|151|207
        84|Po|Polonium|209|16|6|p|2|ab5c00|145|197
        85|At|Astatine|210|17|6|p|2.2|754f45|147|202
        86|Rn|Radon|222|18|6|p|2.2|428296|142|220
        87|Fr|Francium|223|1|7|s|0.79|420066||348
        88|Ra|Radium|226|2|7|s|0.9|007d00|201|283
        89|Ac|Actinium|227|3|7|f|1.1|70abfa|186|
        90|Th|Thorium|232.038|3|7|f|1.3|00baff|175|
        91|Pa|Protactinium|231.036|3|7|f|1.5|00a1ff|169|
        92|U|Uranium|238.029|3|7|f|1.38|008fff|170|186
        93|Np|Neptunium|237|3|7|f|1.36|0080ff|171|
        94|Pu|Plutonium|244|3|7|f|1.28|006bff|172|
        95|Am|Americium|243|3|7|f|1.13|545cf2|166|
        96|Cm|Curium|247|3|7|f|1.28|785ce3|166|
        97|Bk|Berkelium|247|3|7|f|1.3|8a4fe3||
        98|Cf|Californium|251|3|7|f|1.3|a136d4||
        99|Es|Einsteinium|252|3|7|f|1.3|b31fd4||
        100|Fm|Fermium|257|3|7|f|1.3|b31fba||
        101|Md|Mendelevium|258|3|7|f|1.3|b30da6||
        102|No|Nobelium|259|3|7|f|1.3|bd0d87||
        103|Lr|Lawrencium|266|3|7|d|1.3|c70066||
        104|Rf|Rutherfordium|267|4|7|d||cc0059||
        105|Db|Dubnium|268|5|7|d||d1004f||
        106|Sg|Seaborgium|269|6|7|d||d90045||
        107|Bh|Bohrium|270|7|7|d||e00038||
        108|Hs|Hassium|269|8|7|d||e6002e||
        109|Mt|Meitnerium|278|9|7|d||eb0026||
        110|Ds|Darmstadtium|281|10|7|d||||
        111|Rg|Roentgenium|282|11|7|d||||
        112|Cn|Copernicium|285|12|7|d||||
        113|Nh|Nihonium|286|13|7|p||||
        114|Fl|Flerovium|289|14|7|p||||
        115|Mc|Moscovium|289|15|7|p||||
        116|Lv|Livermorium|293|16|7|p||||
        117|Ts|Tennessine|294|17|7|p||||
        118|Og|Oganesson|294|18|7|p||||
""";

    public static readonly IReadOnlyList<ChemElement> All = Parse();
    private static readonly Dictionary<string, ChemElement> BySymbol = All.ToDictionary(e => e.Symbol, StringComparer.Ordinal);

    public static ChemElement? Of(string symbol) => BySymbol.TryGetValue(symbol, out var e) ? e : null;

    private static List<ChemElement> Parse()
    {
        static double? Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        var list = new List<ChemElement>();
        foreach (var line in Data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split('|');
            if (f.Length < 11) continue;
            list.Add(new ChemElement(int.Parse(f[0], CultureInfo.InvariantCulture), f[1], f[2], Num(f[3]) ?? 0, Num(f[4]) is { } g ? (int)g : null,
                Num(f[5]) is { } p ? (int)p : 0, f[6], Num(f[7]), f[8], Num(f[9]), Num(f[10])));
        }
        return list;
    }
}
