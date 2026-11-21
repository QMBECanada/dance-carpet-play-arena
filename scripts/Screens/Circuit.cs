using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Estado del Modo Circuito: una sesión que encadena varios juegos (estaciones) de unos minutos cada uno, con un
/// descanso entre medias, como una clase de gimnasio. Vive entre cambios de escena.
/// </summary>
public static class Circuit
{
    public const string ScenePath = "res://scenes/Circuit.tscn";
    public const double RestSeconds = 20;
    public static readonly int[] MinuteOptions = { 1, 2, 3 };

    /// <summary>Juegos del circuito, en orden: alternan esfuerzo físico y de cabeza.</summary>
    public static readonly string[] StationIds = { "moles", "arrows", "workout", "tennis", "simon", "spell" };

    public enum Stage { Menu, Rest, Station, Done }

    public static Stage Current { get; set; } = Stage.Menu;
    public static int Index { get; set; }
    public static int Minutes { get; set; } = 2;
    public static double StationLeft { get; set; }
    public static long StartSteps { get; set; }
    public static double StartSeconds { get; set; }

    /// <summary>Hay un circuito en marcha (los juegos se saltan su menú y empiezan solos).</summary>
    public static bool Active => Current is Stage.Rest or Stage.Station;

    public static IReadOnlyList<GameInfo> Stations =>
        StationIds.Select(id => GameCatalog.All.First(g => g.Id == id)).ToList();

    public static GameInfo Station => Stations[Index];

    public static void Cancel() => Current = Stage.Menu;
}

/// <summary>Puntos de progreso del circuito: estaciones hechas y pendientes.</summary>
public partial class StationDots : Control
{
    private int _done;

    public int Done
    {
        get => _done;
        set
        {
            _done = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        int n = Circuit.StationIds.Length;
        const float gap = 46;
        float x0 = Size.X / 2 - (n - 1) * gap / 2;
        for (int i = 0; i < n; i++)
        {
            var c = new Vector2(x0 + i * gap, Size.Y / 2);
            if (i < _done) DrawCircle(c, 13, t.AccentText);
            else DrawArc(c, 12, 0, Mathf.Tau, 24, new Color(t.Text, 0.35f), 3, true);
            if (i == _done && _done < n) DrawCircle(c, 6, t.AccentText);
        }
    }
}
