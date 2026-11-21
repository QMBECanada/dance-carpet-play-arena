using System.Globalization;
using Godot;

namespace AlfombraBaile;

/// <summary>Las casillas de la alfombra. El centro no es un botón: la alfombra lo envía moviendo los ejes.</summary>
public enum Pad { Up, Down, Left, Right, Cross, Circle, Triangle, Square, Select, Start, Center }

/// <summary>Grupo de color de una casilla dentro del tema.</summary>
public enum ColorRole { V, H, Corner, Accent }

/// <summary>Forma que se dibuja sobre cada casilla.</summary>
public enum Glyph { Arrow, Cross, Circle, Triangle, Square, Dot, Pill }

public static class PadInfo
{
    public const int Count = 11;

    /// <summary>Orden en que se piden las casillas al calibrar.</summary>
    public static readonly Pad[] CalibrationOrder =
    {
        Pad.Up, Pad.Down, Pad.Left, Pad.Right, Pad.Cross, Pad.Circle, Pad.Triangle, Pad.Square, Pad.Select, Pad.Start, Pad.Center,
    };

    public static string Label(Pad pad) => pad switch
    {
        Pad.Up => "Arriba",
        Pad.Down => "Abajo",
        Pad.Left => "Izquierda",
        Pad.Right => "Derecha",
        Pad.Cross => "Equis",
        Pad.Circle => "Círculo",
        Pad.Triangle => "Triángulo",
        Pad.Square => "Cuadrado",
        Pad.Select => "Select",
        Pad.Start => "Start",
        _ => "Centro",
    };

    public static Glyph GlyphOf(Pad pad) => pad switch
    {
        Pad.Up or Pad.Down or Pad.Left or Pad.Right => Glyph.Arrow,
        Pad.Cross => Glyph.Cross,
        Pad.Circle => Glyph.Circle,
        Pad.Triangle => Glyph.Triangle,
        Pad.Square => Glyph.Square,
        Pad.Center => Glyph.Dot,
        _ => Glyph.Pill,
    };

    /// <summary>Giro de la flecha en grados (0 = hacia arriba, sentido horario).</summary>
    public static float ArrowAngle(Pad pad) => pad switch
    {
        Pad.Right => 90,
        Pad.Down => 180,
        Pad.Left => 270,
        _ => 0,
    };

    public static ColorRole Role(Pad pad) => pad switch
    {
        Pad.Up or Pad.Down => ColorRole.V,
        Pad.Left or Pad.Right => ColorRole.H,
        Pad.Select or Pad.Start or Pad.Center => ColorRole.Accent,
        _ => ColorRole.Corner,
    };

    /// <summary>Posición en la cuadrícula 3×3 (columna, fila). Select y Start van en la fila -1.</summary>
    public static Vector2I Cell(Pad pad) => pad switch
    {
        Pad.Cross => new(0, 0),
        Pad.Up => new(1, 0),
        Pad.Circle => new(2, 0),
        Pad.Left => new(0, 1),
        Pad.Center => new(1, 1),
        Pad.Right => new(2, 1),
        Pad.Triangle => new(0, 2),
        Pad.Down => new(1, 2),
        Pad.Square => new(2, 2),
        Pad.Select => new(0, -1),
        _ => new(2, -1),
    };
}

/// <summary>Qué señal del mando corresponde a una casilla: un botón o un eje que se aleja de su reposo.</summary>
public readonly record struct Binding(bool IsAxis, int Index, int Sign, float Rest)
{
    public static Binding Button(int index) => new(false, index, 0, 0);
    public static Binding Axis(int index, int sign, float rest) => new(true, index, sign, rest);

    public bool SameInput(Binding other) =>
        IsAxis == other.IsAxis && Index == other.Index && (!IsAxis || Sign == other.Sign);

    public string Describe() => IsAxis ? $"eje {Index} {(Sign > 0 ? "+" : "−")}" : $"botón {Index}";

    public string Serialize() => IsAxis
        ? $"a:{Index}:{Sign}:{Rest.ToString("0.###", CultureInfo.InvariantCulture)}"
        : $"b:{Index}";

    public static Binding? Parse(string text)
    {
        var parts = text.Split(':');
        try
        {
            if (parts[0] == "b" && parts.Length == 2) return Button(int.Parse(parts[1], CultureInfo.InvariantCulture));
            if (parts[0] == "a" && parts.Length == 4)
                return Axis(int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture),
                    float.Parse(parts[3], CultureInfo.InvariantCulture));
        }
        catch (System.Exception e) when (e is System.FormatException or System.OverflowException)
        {
            GD.PushWarning($"Calibración guardada no válida («{text}»): esa casilla habrá que calibrarla de nuevo.");
        }
        return null;
    }
}
