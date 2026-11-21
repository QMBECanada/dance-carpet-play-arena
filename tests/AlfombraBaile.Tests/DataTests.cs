namespace AlfombraBaile.Tests;

/// <summary>
/// Comprueban los archivos de datos que se pueden editar a mano (palabras, adivinanzas, laberintos y ejercicios):
/// si alguien se equivoca al retocarlos, estas pruebas lo dicen antes de jugar.
/// </summary>
public class DataTests
{
    private static string Data(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "datos", path));

    // ---------------------------------------------------------------- palabras

    [Fact]
    public void Todas_las_palabras_caben_en_la_alfombra_y_no_se_repiten()
    {
        var words = WordBank.Lines(Data("palabras/deletrear.txt")).ToList();
        Assert.True(words.Count >= 200, $"solo hay {words.Count} palabras");
        Assert.All(words, w => Assert.True(WordBank.Fits(w), $"«{w}» tiene más de 8 letras distintas"));
        var repeated = words.GroupBy(WordBank.Plain).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(repeated.Count == 0, "repetidas: " + string.Join(", ", repeated));
    }

    [Fact]
    public void Hay_palabras_en_los_cinco_niveles()
    {
        var tiers = WordBank.ParseTiers(Data("palabras/deletrear.txt"));
        Assert.Equal(5, tiers.Length);
        Assert.All(tiers, t => Assert.True(t.Length >= 15, $"un nivel solo tiene {t.Length} palabras"));
    }

    [Fact]
    public void Todas_las_adivinanzas_estan_bien_escritas_y_caben()
    {
        var lines = WordBank.Lines(Data("palabras/adivinanzas.txt")).ToList();
        var riddles = WordBank.ParseRiddles(Data("palabras/adivinanzas.txt"));
        var lost = lines.Where(l => riddles.All(r => !l.EndsWith(r.Answer))).ToList();
        Assert.True(lost.Count == 0, "no válidas (falta «|» o no caben): " + string.Join(" / ", lost));
        Assert.True(riddles.Length >= 200, $"solo hay {riddles.Length} adivinanzas");
        Assert.All(riddles, r => Assert.False(string.IsNullOrWhiteSpace(r.Clue)));
    }

    [Fact]
    public void Ninguna_pista_contiene_su_respuesta()
    {
        foreach (var (clue, answer) in WordBank.ParseRiddles(Data("palabras/adivinanzas.txt")))
            Assert.False(WordBank.Plain(clue).Contains(WordBank.Plain(answer)), $"la pista «{clue}» delata «{answer}»");
    }

    // ---------------------------------------------------------------- laberintos

    public static IEnumerable<object[]> Levels() => Enumerable.Range(1, 4).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Levels))]
    public void Cada_laberinto_tiene_salida_meta_alcanzable_y_es_rectangular(int level)
    {
        var map = MazeLevels.Parse(Data($"laberintos/nivel{level}.txt"));
        Assert.True(map.Length > 2);
        Assert.All(map, row => Assert.Equal(map[0].Length, row.Length));
        Assert.Single(MazeLevels.Find(map, 'S'));
        Assert.Single(MazeLevels.Find(map, 'G'));
        Assert.All(map[0], c => Assert.Equal('#', c));
        Assert.All(map[^1], c => Assert.Equal('#', c));
        Assert.All(map, row => Assert.True(row[0] == '#' && row[^1] == '#', "el borde debe ser pared"));

        var reachable = MazeLevels.Reachable(map);
        Assert.Contains(MazeLevels.Find(map, 'G').First(), reachable);
        foreach (var checkpoint in MazeLevels.Find(map, 'C')) Assert.Contains(checkpoint, reachable);
    }

    [Fact]
    public void Las_trampas_aparecen_en_su_nivel()
    {
        string Cells(int level) => string.Concat(MazeLevels.Parse(Data($"laberintos/nivel{level}.txt")));
        Assert.DoesNotContain('~', Cells(1));
        Assert.DoesNotContain('O', Cells(1));
        Assert.Contains('~', Cells(2));
        Assert.Contains('M', Cells(3));
        Assert.Contains('D', Cells(4));
    }

    // ---------------------------------------------------------------- ejercicios

    [Fact]
    public void Los_veinte_ejercicios_son_validos()
    {
        var warnings = new List<string>();
        var list = Exercises.Parse(Data("ejercicios/ejercicios.txt"), warnings.Add);
        Assert.True(warnings.Count == 0, string.Join("\n", warnings));
        Assert.Equal(20, list.Count);
        Assert.Equal(list.Count, list.Select(e => e.Name).Distinct().Count());
    }

    [Fact]
    public void Un_ejercicio_que_saca_un_pie_de_su_zona_no_es_valido()
    {
        var bad = new Exercise("Malo", Pad.Left, Pad.Right, new[] { (true, Pad.Circle), (true, Pad.Left) });
        Assert.False(Exercises.Valid(bad));
        var notCyclic = new Exercise("Sin vuelta", Pad.Left, Pad.Right, new[] { (true, Pad.Cross) });
        Assert.False(Exercises.Valid(notCyclic));
    }
}
