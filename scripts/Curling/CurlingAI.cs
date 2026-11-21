using System;
using System.Linq;

namespace AlfombraBaile;

public enum CurlingLevel { Easy, Normal, Hard }

/// <summary>Un lanzamiento: hacia dónde se apunta, con cuánta tensión y hacia qué lado curva.</summary>
public readonly record struct CurlingShot(float Aim, float Tension, int Curl);

/// <summary>
/// El rival del curling: prueba muchos lanzamientos simulándolos con la misma física (sin barrer), se queda con el
/// que deja mejor la entrada para su equipo y luego falla un poco según el nivel.
/// </summary>
public static class CurlingAI
{
    private static readonly float[] Aims = Enumerable.Range(0, 17).Select(i => -2f + i * 0.25f).ToArray();
    private static readonly float[] Tensions = Enumerable.Range(0, 14).Select(i => 0.35f + i * 0.05f).ToArray();

    /// <summary>Error de puntería (m) y de tensión según el nivel.</summary>
    private static (float Aim, float Tension) Noise(CurlingLevel level) => level switch
    {
        CurlingLevel.Easy => (0.6f, 0.05f),
        CurlingLevel.Normal => (0.3f, 0.025f),
        _ => (0.08f, 0.008f),
    };

    public static CurlingShot Choose(CurlingSheet sheet, int team, CurlingLevel level, Random rng)
    {
        var best = new CurlingShot(0, 0.5f, 1);
        float bestValue = float.MinValue;
        foreach (int curl in new[] { 1, -1 })
        foreach (float aim in Aims)
        foreach (float tension in Tensions)
        {
            var trial = sheet.Clone();
            trial.Throw(team, aim, tension, curl);
            trial.SettleWithoutSweeping();
            float value = Evaluate(trial, team);
            if (value > bestValue)
            {
                bestValue = value;
                best = new CurlingShot(aim, tension, curl);
            }
        }

        var (aimNoise, tensionNoise) = Noise(level);
        return best with
        {
            Aim = best.Aim + Gaussian(rng) * aimNoise,
            Tension = Math.Clamp(best.Tension + Gaussian(rng) * tensionNoise, 0, 1),
        };
    }

    /// <summary>Lo buena que es una posición para <paramref name="team"/>: puntos de la entrada y, a igualdad, cercanía.</summary>
    public static float Evaluate(CurlingSheet sheet, int team)
    {
        var (scorer, points) = sheet.EndScore();
        float value = scorer == team ? points * 10 : -points * 10;
        var mine = sheet.InPlay.Where(s => s.Team == team && CurlingSheet.InHouse(s)).Select(CurlingSheet.Distance).DefaultIfEmpty(10).Min();
        return value - mine;
    }

    private static float Gaussian(Random rng)
    {
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
    }
}
