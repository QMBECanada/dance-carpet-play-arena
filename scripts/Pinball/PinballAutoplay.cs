using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlfombraBaile;

/// <summary>
/// Partida automática y reproducible del pinball (azar con semilla fija y un jugador sencillo) que devuelve la
/// trayectoria muestreada. Sirve de «foto» de la física: si un cambio de código la altera, la prueba de regresión
/// lo detecta. Si el cambio es a propósito (ajustar fuerzas…), se vuelve a grabar con «-- --grabar-fisica=ruta».
/// </summary>
public static class PinballAutoplay
{
    public const int DefaultSteps = 24000; // 100 s de juego

    public static List<string> Run(int seed = 1, int steps = DefaultSteps)
    {
        var table = new PinballTable(new Random(seed));
        FlipperInput[] levels = { FlipperInput.Strong, FlipperInput.Normal, FlipperInput.Soft };
        int[] holds = { 10, 60, 200 };
        int launchWait = 0, launches = 0;
        var lines = new List<string>();
        var inv = CultureInfo.InvariantCulture;
        for (int step = 0; step < steps && !table.IsOver; step++)
        {
            if (table.BallOnPlunger is not null)
            {
                launchWait++;
                if (launchWait == 30) table.PullPlunger();
                if (launchWait == 30 + holds[launches % holds.Length])
                {
                    table.ReleasePlunger();
                    launches++;
                }
            }
            else launchWait = 0;

            var level = levels[step / 600 % levels.Length];
            table.SetFlippers(
                table.Balls.Any(b => b.Pos.Y < 4.2f && b.Pos.X < PinballTable.Axis && b.Vel.Y < 0) ? level : FlipperInput.None,
                table.Balls.Any(b => b.Pos.Y < 4.2f && b.Pos.X >= PinballTable.Axis && b.Vel.Y < 0) ? level : FlipperInput.None);
            if (step == 3000) table.StartMultiball();
            if (step is 9000 or 9050 or 9100 or 9150) table.Nudge(forward: true);

            table.Advance(PinballTable.Step);
            if (step % 24 == 0)
                lines.Add(string.Create(inv, $"{step} s={table.Score} b={table.BallsLeft} m={table.Multiplier} t={table.Tilted} ") +
                          string.Join(" ", table.Balls.Select(b => string.Create(inv, $"({b.Pos.X:R},{b.Pos.Y:R},{b.Vel.X:R},{b.Vel.Y:R})"))));
        }
        lines.Add($"fin score={table.Score} balls={table.BallsLeft} over={table.IsOver}");
        return lines;
    }
}
