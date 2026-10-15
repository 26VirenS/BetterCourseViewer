using Simpl.Tools.Verbs;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Verb Conjugations' engine (Tools/Study/Verbs/VerbEngine.cs) against the language data's own test vectors (the
/// data's check.mjs, which the engine is a port of): the spec's vectors first (hablar, ser, tener; aller, manger; sein,
/// anfangen; essere; ser), then the rest. An expected value is six forms joined by "|", a few persons as "2=form;5=form",
/// or one form for a single-form tense (a participle, a gerund).
/// </summary>
public class VerbsTests
{
    private static void Check(string lang, string verb, string tense, string expected)
    {
        var L = VerbLanguage.Load(lang);
        var got = VerbEngine.Conjugate(L, verb, tense);
        if (expected.Contains('|')) Assert.Equal(expected.Split('|'), got.Forms);
        else if (expected.Contains('='))
            foreach (var pair in expected.Split(';'))
            {
                var at = pair.IndexOf('=');
                Assert.Equal(pair[(at + 1)..], got.Forms[int.Parse(pair[..at])]);
            }
        else Assert.Equal(expected, got.One);
    }

    [Theory]
    [InlineData("hablar", "presente", "hablo|hablas|habla|hablamos|habláis|hablan")]
    [InlineData("ser", "preterito", "fui|fuiste|fue|fuimos|fuisteis|fueron")]
    [InlineData("tener", "presente", "tengo|tienes|tiene|tenemos|tenéis|tienen")]
    [InlineData("comer", "presente", "como|comes|come|comemos|coméis|comen")]
    [InlineData("vivir", "preterito", "viví|viviste|vivió|vivimos|vivisteis|vivieron")]
    [InlineData("hablar", "imperfectoSubj", "hablara|hablaras|hablara|habláramos|hablarais|hablaran")]
    [InlineData("tener", "imperfectoSubj", "tuviera|tuvieras|tuviera|tuviéramos|tuvierais|tuvieran")]
    [InlineData("buscar", "preterito", "0=busqué")]
    [InlineData("buscar", "presenteSubj", "busque|busques|busque|busquemos|busquéis|busquen")]
    [InlineData("empezar", "presente", "empiezo|empiezas|empieza|empezamos|empezáis|empiezan")]
    [InlineData("empezar", "presenteSubj", "empiece|empieces|empiece|empecemos|empecéis|empiecen")]
    [InlineData("dormir", "preterito", "dormí|dormiste|durmió|dormimos|dormisteis|durmieron")]
    [InlineData("dormir", "presenteSubj", "duerma|duermas|duerma|durmamos|durmáis|duerman")]
    [InlineData("pedir", "presente", "pido|pides|pide|pedimos|pedís|piden")]
    [InlineData("seguir", "presente", "sigo|sigues|sigue|seguimos|seguís|siguen")]
    [InlineData("conocer", "presente", "0=conozco;1=conoces")]
    [InlineData("leer", "preterito", "leí|leíste|leyó|leímos|leísteis|leyeron")]
    [InlineData("leer", "gerundio", "leyendo")]
    [InlineData("construir", "presente", "construyo|construyes|construye|construimos|construís|construyen")]
    [InlineData("hacer", "futuro", "haré|harás|hará|haremos|haréis|harán")]
    [InlineData("ir", "imperfecto", "iba|ibas|iba|íbamos|ibais|iban")]
    [InlineData("decir", "imperativo", "—|di|diga|digamos|decid|digan")]
    [InlineData("hablar", "imperativo", "—|habla|hable|hablemos|hablad|hablen")]
    [InlineData("jugar", "presenteSubj", "juegue|juegues|juegue|juguemos|juguéis|jueguen")]
    [InlineData("estar", "presente", "estoy|estás|está|estamos|estáis|están")]
    [InlineData("volver", "participio", "vuelto")]
    [InlineData("coger", "presente", "0=cojo;1=coges")]
    [InlineData("llegar", "preterito", "0=llegué;2=llegó")]
    [InlineData("continuar", "presente", "0=continúo;3=continuamos")]
    [InlineData("enviar", "presente", "0=envío;5=envían")]
    [InlineData("poner", "condicional", "pondría|pondrías|pondría|pondríamos|pondríais|pondrían")]
    public void Spanish(string verb, string tense, string expected) => Check("es", verb, tense, expected);

    [Theory]
    [InlineData("aller", "present", "vais|vas|va|allons|allez|vont")]
    [InlineData("manger", "present", "3=mangeons")]
    [InlineData("parler", "present", "parle|parles|parle|parlons|parlez|parlent")]
    [InlineData("finir", "present", "finis|finis|finit|finissons|finissez|finissent")]
    [InlineData("vendre", "present", "vends|vends|vend|vendons|vendez|vendent")]
    [InlineData("manger", "imparfait", "mangeais|mangeais|mangeait|mangions|mangiez|mangeaient")]
    [InlineData("commencer", "present", "3=commençons")]
    [InlineData("commencer", "passeSimple", "commençai|commenças|commença|commençâmes|commençâtes|commencèrent")]
    [InlineData("acheter", "present", "achète|achètes|achète|achetons|achetez|achètent")]
    [InlineData("acheter", "futur", "0=achèterai;3=achèterons")]
    [InlineData("appeler", "present", "appelle|appelles|appelle|appelons|appelez|appellent")]
    [InlineData("jeter", "futur", "0=jetterai")]
    [InlineData("nettoyer", "present", "nettoie|nettoies|nettoie|nettoyons|nettoyez|nettoient")]
    [InlineData("préférer", "present", "préfère|préfères|préfère|préférons|préférez|préfèrent")]
    [InlineData("préférer", "futur", "0=préférerai")]
    [InlineData("être", "passeCompose", "ai été|as été|a été|avons été|avez été|ont été")]
    [InlineData("aller", "passeCompose", "suis allé|es allé|est allé|sommes allés|êtes allés|sont allés")]
    [InlineData("prendre", "subjonctifPresent", "prenne|prennes|prenne|prenions|preniez|prennent")]
    [InlineData("boire", "present", "bois|bois|boit|buvons|buvez|boivent")]
    [InlineData("venir", "passeSimple", "vins|vins|vint|vînmes|vîntes|vinrent")]
    [InlineData("faire", "futur", "ferai|feras|fera|ferons|ferez|feront")]
    [InlineData("parler", "imperatif", "—|parle|—|parlons|parlez|—")]
    [InlineData("finir", "imperatif", "—|finis|—|finissons|finissez|—")]
    [InlineData("avoir", "imperatif", "—|aie|—|ayons|ayez|—")]
    [InlineData("vendre", "participePasse", "vendu")]
    [InlineData("finir", "participePresent", "finissant")]
    [InlineData("apprendre", "passeCompose", "0=ai appris")]
    [InlineData("devenir", "passeCompose", "0=suis devenu;5=sont devenus")]
    [InlineData("pouvoir", "conditionnel", "pourrais|pourrais|pourrait|pourrions|pourriez|pourraient")]
    [InlineData("envoyer", "futur", "0=enverrai")]
    [InlineData("ouvrir", "imperatif", "1=ouvre")]
    [InlineData("savoir", "subjonctifPresent", "sache|saches|sache|sachions|sachiez|sachent")]
    public void French(string verb, string tense, string expected) => Check("fr", verb, tense, expected);

    [Theory]
    [InlineData("sein", "praesens", "bin|bist|ist|sind|seid|sind")]
    [InlineData("anfangen", "praesens", "fange an|fängst an|fängt an|fangen an|fangt an|fangen an")]
    [InlineData("lernen", "praesens", "lerne|lernst|lernt|lernen|lernt|lernen")]
    [InlineData("arbeiten", "praesens", "arbeite|arbeitest|arbeitet|arbeiten|arbeitet|arbeiten")]
    [InlineData("arbeiten", "praeteritum", "arbeitete|arbeitetest|arbeitete|arbeiteten|arbeitetet|arbeiteten")]
    [InlineData("arbeiten", "partizip2", "gearbeitet")]
    [InlineData("heißen", "praesens", "1=heißt")]
    [InlineData("studieren", "partizip2", "studiert")]
    [InlineData("besuchen", "partizip2", "besucht")]
    [InlineData("lächeln", "praesens", "0=lächle;3=lächeln")]
    [InlineData("haben", "praeteritum", "hatte|hattest|hatte|hatten|hattet|hatten")]
    [InlineData("gehen", "perfekt", "bin gegangen|bist gegangen|ist gegangen|sind gegangen|seid gegangen|sind gegangen")]
    [InlineData("lernen", "perfekt", "0=habe gelernt")]
    [InlineData("lernen", "futur1", "werde lernen|wirst lernen|wird lernen|werden lernen|werdet lernen|werden lernen")]
    [InlineData("lernen", "konjunktiv2", "0=würde lernen")]
    [InlineData("sein", "konjunktiv2", "wäre|wärst|wäre|wären|wärt|wären")]
    [InlineData("fahren", "praesens", "fahre|fährst|fährt|fahren|fahrt|fahren")]
    [InlineData("lesen", "praesens", "1=liest;2=liest")]
    [InlineData("halten", "praesens", "1=hältst;2=hält")]
    [InlineData("finden", "praeteritum", "fand|fandest|fand|fanden|fandet|fanden")]
    [InlineData("nehmen", "imperativ", "—|nimm|—|nehmen wir|nehmt|nehmen Sie")]
    [InlineData("anfangen", "perfekt", "0=habe angefangen")]
    [InlineData("anfangen", "imperativ", "1=fang an;5=fangen Sie an")]
    [InlineData("aufstehen", "praeteritum", "0=stand auf")]
    [InlineData("vorbereiten", "partizip2", "vorbereitet")]
    [InlineData("verstehen", "partizip2", "verstanden")]
    [InlineData("wissen", "praesens", "weiß|weißt|weiß|wissen|wisst|wissen")]
    [InlineData("können", "konjunktiv2", "könnte|könntest|könnte|könnten|könntet|könnten")]
    [InlineData("werden", "praesens", "werde|wirst|wird|werden|werdet|werden")]
    [InlineData("bringen", "praeteritum", "0=brachte")]
    [InlineData("ankommen", "perfekt", "2=ist angekommen")]
    public void German(string verb, string tense, string expected) => Check("de", verb, tense, expected);

    [Theory]
    [InlineData("essere", "presente", "sono|sei|è|siamo|siete|sono")]
    [InlineData("parlare", "presente", "parlo|parli|parla|parliamo|parlate|parlano")]
    [InlineData("credere", "presente", "credo|credi|crede|crediamo|credete|credono")]
    [InlineData("dormire", "presente", "dormo|dormi|dorme|dormiamo|dormite|dormono")]
    [InlineData("capire", "presente", "capisco|capisci|capisce|capiamo|capite|capiscono")]
    [InlineData("cercare", "presente", "1=cerchi;3=cerchiamo")]
    [InlineData("cercare", "futuro", "0=cercherò")]
    [InlineData("pagare", "congiuntivoPresente", "0=paghi;4=paghiate")]
    [InlineData("mangiare", "presente", "1=mangi;3=mangiamo")]
    [InlineData("mangiare", "futuro", "0=mangerò")]
    [InlineData("cominciare", "condizionale", "0=comincerei")]
    [InlineData("studiare", "presente", "1=studi;3=studiamo")]
    [InlineData("studiare", "futuro", "0=studierò")]
    [InlineData("parlare", "passatoRemoto", "parlai|parlasti|parlò|parlammo|parlaste|parlarono")]
    [InlineData("prendere", "passatoRemoto", "presi|prendesti|prese|prendemmo|prendeste|presero")]
    [InlineData("avere", "passatoRemoto", "ebbi|avesti|ebbe|avemmo|aveste|ebbero")]
    [InlineData("fare", "imperfetto", "facevo|facevi|faceva|facevamo|facevate|facevano")]
    [InlineData("andare", "futuro", "andrò|andrai|andrà|andremo|andrete|andranno")]
    [InlineData("volere", "condizionale", "vorrei|vorresti|vorrebbe|vorremmo|vorreste|vorrebbero")]
    [InlineData("parlare", "imperativo", "—|parla|parli|parliamo|parlate|parlino")]
    [InlineData("prendere", "imperativo", "—|prendi|prenda|prendiamo|prendete|prendano")]
    [InlineData("finire", "imperativo", "—|finisci|finisca|finiamo|finite|finiscano")]
    [InlineData("parlare", "congiuntivoImperfetto", "parlassi|parlassi|parlasse|parlassimo|parlaste|parlassero")]
    [InlineData("essere", "congiuntivoImperfetto", "fossi|fossi|fosse|fossimo|foste|fossero")]
    [InlineData("bere", "presente", "bevo|bevi|beve|beviamo|bevete|bevono")]
    [InlineData("uscire", "presente", "esco|esci|esce|usciamo|uscite|escono")]
    [InlineData("venire", "participio", "venuto")]
    [InlineData("dire", "gerundio", "dicendo")]
    [InlineData("scrivere", "participio", "scritto")]
    [InlineData("stare", "imperativo", "1=stai;2=stia")]
    [InlineData("dovere", "presente", "devo|devi|deve|dobbiamo|dovete|devono")]
    public void Italian(string verb, string tense, string expected) => Check("it", verb, tense, expected);

    [Theory]
    [InlineData("ser", "presente", "sou|és|é|somos|sois|são")]
    [InlineData("falar", "presente", "falo|falas|fala|falamos|falais|falam")]
    [InlineData("comer", "presente", "como|comes|come|comemos|comeis|comem")]
    [InlineData("partir", "presente", "parto|partes|parte|partimos|partis|partem")]
    [InlineData("falar", "preteritoPerfeito", "falei|falaste|falou|falámos|falastes|falaram")]
    [InlineData("comer", "preteritoPerfeito", "comi|comeste|comeu|comemos|comestes|comeram")]
    [InlineData("ter", "presente", "tenho|tens|tem|temos|tendes|têm")]
    [InlineData("ter", "preteritoPerfeito", "tive|tiveste|teve|tivemos|tivestes|tiveram")]
    [InlineData("ir", "preteritoPerfeito", "fui|foste|foi|fomos|fostes|foram")]
    [InlineData("fazer", "futuro", "farei|farás|fará|faremos|fareis|farão")]
    [InlineData("falar", "imperfeitoSubj", "falasse|falasses|falasse|falássemos|falásseis|falassem")]
    [InlineData("comer", "imperfeitoSubj", "comesse|comesses|comesse|comêssemos|comêsseis|comessem")]
    [InlineData("ter", "imperfeitoSubj", "tivesse|tivesses|tivesse|tivéssemos|tivésseis|tivessem")]
    [InlineData("ficar", "presenteSubj", "fique|fiques|fique|fiquemos|fiqueis|fiquem")]
    [InlineData("ficar", "preteritoPerfeito", "0=fiquei")]
    [InlineData("começar", "presenteSubj", "0=comece")]
    [InlineData("chegar", "preteritoPerfeito", "0=cheguei")]
    [InlineData("conhecer", "presente", "0=conheço;1=conheces")]
    [InlineData("conhecer", "presenteSubj", "0=conheça")]
    [InlineData("dormir", "presente", "durmo|dormes|dorme|dormimos|dormis|dormem")]
    [InlineData("dormir", "presenteSubj", "durma|durmas|durma|durmamos|durmais|durmam")]
    [InlineData("preferir", "presente", "0=prefiro;1=preferes")]
    [InlineData("subir", "presente", "subo|sobes|sobe|subimos|subis|sobem")]
    [InlineData("seguir", "presente", "0=sigo;1=segues")]
    [InlineData("passear", "presente", "passeio|passeias|passeia|passeamos|passeais|passeiam")]
    [InlineData("pôr", "presente", "ponho|pões|põe|pomos|pondes|põem")]
    [InlineData("falar", "imperativo", "—|fala|fale|falemos|falai|falem")]
    [InlineData("ser", "imperativo", "—|sê|seja|sejamos|sede|sejam")]
    [InlineData("estar", "presente", "estou|estás|está|estamos|estais|estão")]
    [InlineData("ver", "participio", "visto")]
    [InlineData("possuir", "preteritoPerfeito", "possuí|possuíste|possuiu|possuímos|possuístes|possuíram")]
    [InlineData("vir", "presente", "venho|vens|vem|vimos|vindes|vêm")]
    public void Portuguese(string verb, string tense, string expected) => Check("pt", verb, tense, expected);

    [Theory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("pt")]
    public void EveryKnownVerbConjugatesInEveryTense(string lang)
    {
        // (the data's own validation: every common, irregular, stem-changing and separable verb, in every tense, six
        //  non-empty forms or one; the purely regular reading may fail for a verb no class holds — sein, tun — but
        //  must come back, and a table of it still shows, every cell tinted)
        var L = VerbLanguage.Load(lang);
        Assert.Equal(6, L.Pronouns.Count);
        Assert.Equal(6, L.ImperativePronouns.Count);
        Assert.True(L.Known.Count >= 300);
        foreach (var verb in L.Known)
            foreach (var t in L.Tenses)
            {
                var f = VerbEngine.Conjugate(L, verb, t.Id);
                Assert.Equal(t.Single ? 1 : 6, f.Forms.Count);
                Assert.All(f.Forms, x => Assert.False(string.IsNullOrWhiteSpace(x)));
                try { VerbEngine.Conjugate(L, verb, t.Id, ignoreIrregular: verb); } catch (InvalidOperationException) { /* (no class holds it: fine) */ }
            }
        var table = VerbEngine.Make(L, L.Known[0]);
        Assert.Equal(L.Tenses.Count, table.Forms.Count);
    }

    [Fact]
    public void ATableTintsOnlyTheIrregularCells()
    {
        var L = VerbLanguage.Load("es");
        var t = VerbEngine.Make(L, "tener");
        Assert.True(t.Irregular["presente"][0]);  // tengo
        Assert.False(t.Irregular["presente"][3]); // tenemos
        var regular = VerbEngine.Make(L, "hablar");
        Assert.All(regular.Irregular.Values, cells => Assert.All(cells, c => Assert.False(c)));
    }

    [Fact]
    public void TheSuggestionsStartWithWhatWasTyped()
    {
        var L = VerbLanguage.Load("es");
        var s = VerbEngine.Suggest(L, "ten");
        Assert.Contains("tener", s);
        Assert.All(s.Take(s.Count(x => x.StartsWith("ten"))), x => Assert.StartsWith("ten", x));
        Assert.Empty(VerbEngine.Suggest(L, ""));
        Assert.True(VerbEngine.CanConjugate(L, "cantar"));
        Assert.False(VerbEngine.CanConjugate(L, "xyz"));
    }
}
