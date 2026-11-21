using System.Collections.Generic;
using System.Linq;

namespace AlfombraBaile;

/// <summary>
/// Laberintos en texto (assets/laberintos/nivelN.txt): # pared, . suelo, S salida, G meta, O agujero, ~ hielo,
/// M imán, D puerta, C punto de control. Sin Godot, para poder comprobarlos en las pruebas.
/// </summary>
public static class MazeLevels
{
    public const string Cells = "#.SGO~MDC";

    public static string[] Parse(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0 && !l.StartsWith("# ") && l.All(c => Cells.Contains(c))).ToArray();

    /// <summary>Casillas a las que se llega desde la salida sin atravesar paredes ni agujeros (las puertas acaban abriéndose).</summary>
    public static HashSet<(int X, int Y)> Reachable(string[] map)
    {
        var start = Find(map, 'S').First();
        var seen = new HashSet<(int, int)> { start };
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (ny < 0 || ny >= map.Length || nx < 0 || nx >= map[ny].Length) continue;
                if ("#OM".Contains(map[ny][nx]) || !seen.Add((nx, ny))) continue;
                queue.Enqueue((nx, ny));
            }
        }
        return seen;
    }

    public static IEnumerable<(int X, int Y)> Find(string[] map, char c) =>
        map.SelectMany((row, y) => row.Select((ch, x) => (ch, x, y))).Where(t => t.ch == c).Select(t => (t.x, t.y));
}
