using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AlfombraBaile;

/// <summary>Un ejercicio: cada paso es «este pie (izquierdo o no) a esta casilla».</summary>
public sealed record Exercise(string Name, Pad LeftStart, Pad RightStart, (bool Left, Pad To)[] Steps);

/// <summary>Lectura y comprobación de assets/ejercicios/ejercicios.txt (sin Godot, para poder probarlo).</summary>
public static class Exercises
{
    private static readonly Dictionary<string, Pad> Symbols = new()
    {
        ["✕"] = Pad.Cross, ["X"] = Pad.Cross, ["↑"] = Pad.Up, ["○"] = Pad.Circle, ["O"] = Pad.Circle,
        ["←"] = Pad.Left, ["·"] = Pad.Center, ["→"] = Pad.Right,
        ["△"] = Pad.Triangle, ["↓"] = Pad.Down, ["□"] = Pad.Square,
    };

    /// <summary>Lee los ejercicios y descarta (avisando con <paramref name="warn"/>) los que no son válidos.</summary>
    public static List<Exercise> Parse(string text, Action<string>? warn = null)
    {
        text = text.Replace("\r", "");
        var list = new List<Exercise>();
        foreach (Match m in Regex.Matches(text, @"^# (.+)\ninicio: I (\S+) D (\S+)\npasos: (.+)$", RegexOptions.Multiline))
        {
            string name = m.Groups[1].Value.Trim();
            try
            {
                var steps = m.Groups[4].Value.Split(',').Select(s => s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Select(p => (p[0] == "I", Symbols[p[1]])).ToArray();
                var ex = new Exercise(name, Symbols[m.Groups[2].Value], Symbols[m.Groups[3].Value], steps);
                if (Valid(ex)) list.Add(ex);
                else warn?.Invoke($"Ejercicio «{name}» no válido: revisa zonas de los pies y que vuelva al inicio.");
            }
            catch (Exception e) when (e is KeyNotFoundException or IndexOutOfRangeException)
            {
                warn?.Invoke($"Ejercicio «{name}»: paso mal escrito o casilla desconocida.");
            }
        }
        return list;
    }

    /// <summary>
    /// El pie izquierdo solo pisa las columnas izquierda y central, el derecho la central y la derecha; nunca los dos
    /// en la misma casilla; y al acabar los pasos los pies vuelven a donde empezaron (para repetirlo encadenado).
    /// </summary>
    public static bool Valid(Exercise ex)
    {
        Pad l = ex.LeftStart, r = ex.RightStart;
        bool Allowed(bool left, Pad pad) => left ? PadInfo.Cell(pad).X <= 1 : PadInfo.Cell(pad).X >= 1;
        if (ex.Steps.Length == 0 || !Allowed(true, l) || !Allowed(false, r) || l == r) return false;
        foreach (var (left, to) in ex.Steps)
        {
            if (!Allowed(left, to) || to == (left ? r : l)) return false;
            if (left) l = to;
            else r = to;
        }
        return l == ex.LeftStart && r == ex.RightStart;
    }
}
