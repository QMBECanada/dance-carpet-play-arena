using Godot;

namespace AlfombraBaile;

/// <summary>Fuentes y dibujo vectorial compartido: iconos de juegos y símbolos de las casillas.</summary>
public static class Fonts
{
    private static Font? _display, _body, _bold;

    public static Font Display => _display ??= GD.Load<Font>("res://assets/fonts/Bungee-Regular.ttf");
    public static Font Body => _body ??= GD.Load<Font>("res://assets/fonts/Outfit-Medium.tres");
    public static Font Bold => _bold ??= GD.Load<Font>("res://assets/fonts/Outfit-Bold.tres");

    public static Label Label(string text, Font font, int size, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, HorizontalAlignment = align };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}

public static class Painter
{
    /// <summary>Icono de un juego en un cuadro de 64 unidades, centrado en <paramref name="center"/>.</summary>
    public static void Icon(CanvasItem ci, string icon, Vector2 center, float size, Color c)
    {
        float s = size / 64f;
        Vector2 o = center - new Vector2(32, 32) * s;
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        float w = 4.2f * s;
        var soft = new Color(c, c.A * 0.55f);

        switch (icon)
        {
            case "mole":
                ci.DrawPolyline(Ellipse(P(32, 50), 24 * s, 7 * s), c, w, true);
                ci.DrawLine(P(16, 49), P(16, 30), c, w, true);
                ci.DrawArc(P(32, 30), 16 * s, Mathf.Pi, Mathf.Tau, 24, c, w, true);
                ci.DrawLine(P(48, 30), P(48, 49), c, w, true);
                ci.DrawCircle(P(26, 29), 2.8f * s, c);
                ci.DrawCircle(P(38, 29), 2.8f * s, c);
                ci.DrawLine(P(29, 37), P(35, 37), c, w, true);
                break;
            case "simon":
                for (int q = 0; q < 4; q++)
                {
                    float a0 = Mathf.DegToRad(q * 90 + 4), a1 = Mathf.DegToRad(q * 90 + 86);
                    ci.DrawColoredPolygon(Sector(P(32, 32), 7 * s, 27 * s, a0, a1), q % 2 == 0 ? c : soft);
                }
                break;
            case "arrows":
                ci.DrawColoredPolygon(new[] { P(20, 4), P(32, 18), P(24, 18), P(24, 32), P(16, 32), P(16, 18), P(8, 18) }, c);
                ci.DrawColoredPolygon(new[] { P(44, 30), P(56, 44), P(48, 44), P(48, 60), P(40, 60), P(40, 44), P(32, 44) }, soft);
                break;
            case "bolt":
                ci.DrawColoredPolygon(new[] { P(36, 4), P(36, 26), P(52, 26), P(24, 60), P(28, 36), P(12, 36) }, c);
                break;
            case "fit":
                ci.DrawArc(P(32, 36), 20 * s, 0, Mathf.Tau, 40, c, w, true);
                ci.DrawLine(P(32, 36), P(32, 24), c, w, true);
                ci.DrawLine(P(26, 6), P(38, 6), c, w, true);
                ci.DrawLine(P(32, 6), P(32, 16), c, w, true);
                break;
            case "tennis":
                ci.DrawArc(P(32, 32), 24 * s, 0, Mathf.Tau, 48, c, w, true);
                ci.DrawArc(P(3.75f, 32), 16.25f * s, -1.04f, 1.04f, 16, c, w, true);
                ci.DrawArc(P(60.25f, 32), 16.25f * s, Mathf.Pi - 1.04f, Mathf.Pi + 1.04f, 16, c, w, true);
                break;
            case "spell":
                ci.DrawRect(new Rect2(P(4, 18), new Vector2(16, 28) * s), c);
                ci.DrawRect(new Rect2(P(24, 18), new Vector2(16, 28) * s), soft);
                ci.DrawRect(new Rect2(P(44, 18), new Vector2(16, 28) * s), c);
                break;
            case "maze":
                ci.DrawRect(new Rect2(P(6, 6), new Vector2(52, 52) * s), c, false, w);
                ci.DrawPolyline(new[] { P(6, 20), P(42, 20), P(42, 44) }, c, w, true);
                ci.DrawPolyline(new[] { P(22, 58), P(22, 32), P(32, 32) }, c, w, true);
                ci.DrawCircle(P(14, 13), 4.5f * s, soft);
                ci.DrawCircle(P(50, 50), 5 * s, c);
                break;
            case "curling":
                ci.DrawCircle(P(32, 38), 22 * s, soft);
                ci.DrawCircle(P(32, 38), 16 * s, c);
                ci.DrawLine(P(24, 30), P(42, 30), Colors.White, 6 * s, true);
                ci.DrawLine(P(42, 30), P(42, 40), Colors.White, 6 * s, true);
                ci.DrawLine(P(46, 8), P(58, 2), c, 4 * s, true);
                break;
            case "circuit":
                ci.DrawArc(P(32, 34), 22 * s, -2.4f, 2.0f, 32, c, w * 1.3f, true);
                ci.DrawColoredPolygon(new[] { P(10, 14), P(22, 8), P(20, 22) }, c);
                ci.DrawLine(P(32, 34), P(32, 22), soft, w, true);
                ci.DrawLine(P(32, 34), P(41, 38), soft, w, true);
                break;
            case "pinball":
                ci.DrawArc(P(44, 20), 9 * s, 0, Mathf.Tau, 28, soft, w, true);
                ci.DrawCircle(P(22, 26), 8 * s, c);
                ci.DrawLine(P(8, 44), P(26, 54), c, 7 * s, true);
                ci.DrawLine(P(56, 44), P(38, 54), c, 7 * s, true);
                break;
            case "quiz":
                int fontSize = (int)(60 * s);
                ci.DrawString(Fonts.Display, new Vector2(o.X, center.Y + fontSize * 0.36f), "?", HorizontalAlignment.Center,
                    64 * s, fontSize, c);
                break;
        }
    }

    /// <summary>Símbolo de una casilla en 2D (para el reflejo de la alfombra en pantalla).</summary>
    public static void PadGlyph(CanvasItem ci, Pad pad, Vector2 center, float size, Color c)
    {
        float r = size / 2;
        float w = Mathf.Max(1.5f, size * 0.13f);
        switch (PadInfo.GlyphOf(pad))
        {
            case Glyph.Arrow:
                float rot = Mathf.DegToRad(PadInfo.ArrowAngle(pad));
                Vector2 T(float x, float y) => center + new Vector2(x, y).Rotated(rot) * r;
                ci.DrawColoredPolygon(new[] { T(0, -0.9f), T(0.8f, 0), T(0.3f, 0), T(0.3f, 0.85f), T(-0.3f, 0.85f), T(-0.3f, 0), T(-0.8f, 0) }, c);
                break;
            case Glyph.Cross:
                ci.DrawLine(center + new Vector2(-r, -r) * 0.7f, center + new Vector2(r, r) * 0.7f, c, w, true);
                ci.DrawLine(center + new Vector2(-r, r) * 0.7f, center + new Vector2(r, -r) * 0.7f, c, w, true);
                break;
            case Glyph.Circle:
                ci.DrawArc(center, r * 0.75f, 0, Mathf.Tau, 24, c, w, true);
                break;
            case Glyph.Triangle:
                ci.DrawPolyline(new[]
                {
                    center + new Vector2(0, -0.8f) * r, center + new Vector2(0.85f, 0.65f) * r,
                    center + new Vector2(-0.85f, 0.65f) * r, center + new Vector2(0, -0.8f) * r,
                }, c, w, true);
                break;
            case Glyph.Square:
                ci.DrawRect(new Rect2(center - Vector2.One * r * 0.68f, Vector2.One * r * 1.36f), c, false, w);
                break;
            case Glyph.Dot:
                ci.DrawCircle(center, r * 0.28f, c);
                break;
        }
    }

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry, int n = 32)
    {
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.Tau * i / n;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return pts;
    }

    private static Vector2[] Sector(Vector2 c, float r0, float r1, float a0, float a1, int n = 14)
    {
        var pts = new Vector2[(n + 1) * 2];
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.Lerp(a0, a1, (float)i / n);
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            pts[i] = c + dir * r1;
            pts[pts.Length - 1 - i] = c + dir * r0;
        }
        return pts;
    }
}
