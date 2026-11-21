using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AlfombraBaile;

/// <summary>
/// Palabras y adivinanzas para Deletrear y Adivinanza, leídas de assets/palabras/*.txt (se pueden editar sin
/// tocar código). Se guardan con tildes para mostrarlas bien; para jugar se
/// comparan sin tildes (la Ñ sí cuenta como letra propia). Solo valen las que caben en las 8 casillas: como mucho
/// 8 letras distintas.
/// </summary>
public static class WordBank
{
    private const string SpellFile = "res://assets/palabras/deletrear.txt";
    private const string RiddleFile = "res://assets/palabras/adivinanzas.txt";

    private static string[][]? _tiers;
    private static (string Clue, string Answer)[]? _riddles;

    /// <summary>Palabras para Deletrear en 5 niveles por longitud: hasta 4 letras, 5, 6, 7 y 8 o más.</summary>
    public static string[][] SpellTiers => _tiers ??= LoadTiers();

    /// <summary>Adivinanzas: pista y respuesta.</summary>
    public static (string Clue, string Answer)[] Riddles => _riddles ??= LoadRiddles();

    private static string[][] LoadTiers() => ParseTiers(Read(SpellFile));

    /// <summary>Palabras de un texto (una por línea) repartidas en 5 niveles; se descartan las que no caben.</summary>
    public static string[][] ParseTiers(string text)
    {
        var tiers = Enumerable.Range(0, 5).Select(_ => new List<string>()).ToArray();
        foreach (string word in Lines(text))
        {
            if (!Fits(word)) continue;
            int n = Plain(word).Length;
            tiers[n <= 4 ? 0 : Math.Min(n - 4, 4)].Add(word);
        }
        return tiers.Select(t => t.ToArray()).ToArray();
    }

    private static (string, string)[] LoadRiddles() => ParseRiddles(Read(RiddleFile));

    /// <summary>Adivinanzas de un texto (una por línea: «pista | RESPUESTA»); se descartan las mal escritas o que no caben.</summary>
    public static (string Clue, string Answer)[] ParseRiddles(string text) =>
        Lines(text)
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length == 2 && Fits(parts[1].Trim()))
            .Select(parts => (parts[0].Trim(), parts[1].Trim()))
            .ToArray();

    private static string Read(string path)
    {
        string text = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text)) Godot.GD.PushWarning($"No se pudo leer {path}");
        return text;
    }

    /// <summary>Líneas útiles de un texto: sin vacías ni comentarios (#).</summary>
    public static IEnumerable<string> Lines(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0 && !line.StartsWith('#')) yield return line;
        }
    }

    /// <summary>Mayúsculas y sin tildes (la Ñ se mantiene): así se compara lo que se pisa.</summary>
    public static string Plain(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text.ToUpperInvariant())
        {
            sb.Append(ch switch
            {
                'Á' => 'A', 'É' => 'E', 'Í' => 'I', 'Ó' => 'O', 'Ú' or 'Ü' => 'U',
                _ => ch,
            });
        }
        return sb.ToString();
    }

    /// <summary>Cabe en la alfombra: como mucho 8 letras distintas.</summary>
    public static bool Fits(string word) => Plain(word).Distinct().Count() <= 8;

    /// <summary>Letras frecuentes en español para rellenar las casillas sobrantes.</summary>
    private const string Fillers = "AEOSRNIDLCTUMPBGVYQHFZJÑ";

    /// <summary>Reparte las letras de la respuesta (sin repetir) y relleno hasta tener 8, en orden aleatorio.</summary>
    public static List<char> TileLetters(string answer, Random rng)
    {
        var letters = Plain(answer).Distinct().ToList();
        var pool = Fillers.Where(c => !letters.Contains(c)).ToList();
        // Las primeras letras de relleno son las más comunes: confunden más que una Ñ o una J.
        while (letters.Count < 8)
        {
            int pick = rng.Next(Math.Min(pool.Count, 12));
            letters.Add(pool[pick]);
            pool.RemoveAt(pick);
        }
        return letters.OrderBy(_ => rng.Next()).ToList();
    }
}
