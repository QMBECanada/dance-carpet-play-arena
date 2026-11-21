using System.Collections.Generic;
using System;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Piezas de interfaz comunes. Todos los colores salen de un Theme de Godot generado a partir del tema actual, con
/// variaciones con nombre («Title», «Muted», «Chip»…), así que cambiar de tema es reasignar un único objeto.
/// </summary>
public static class Ui
{
    public static Theme Build(GameTheme t)
    {
        var th = new Theme { DefaultFont = Fonts.Body, DefaultFontSize = 20 };
        th.SetColor("font_color", "Label", t.Text);
        th.SetColor("default_color", "RichTextLabel", t.Text);

        void LabelVar(string name, Font font, int size, Color color)
        {
            th.SetTypeVariation(name, "Label");
            th.SetFont("font", name, font);
            th.SetFontSize("font_size", name, size);
            th.SetColor("font_color", name, color);
        }
        LabelVar("Title", Fonts.Display, 34, t.Text);
        LabelVar("TitleAccent", Fonts.Display, 34, t.AccentText);
        LabelVar("Big", Fonts.Display, 42, t.Text);
        LabelVar("Huge", Fonts.Display, 58, t.Text);
        LabelVar("HugeAccent", Fonts.Display, 58, t.AccentText);
        LabelVar("Heading", Fonts.Display, 26, t.Text);
        LabelVar("Muted", Fonts.Body, 19, new Color(t.Text, 0.72f));
        LabelVar("Key", Fonts.Bold, 14, t.Text);
        LabelVar("Accent", Fonts.Bold, 22, t.AccentText);
        LabelVar("Value", Fonts.Bold, 22, t.Text);

        void PanelVar(string name, Color bg, Color border, int radius, int padX, int padY, int borderWidth = 2,
            Color? shadow = null)
        {
            var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            sb.SetBorderWidthAll(borderWidth);
            sb.SetCornerRadiusAll(radius);
            sb.ContentMarginLeft = sb.ContentMarginRight = padX;
            sb.ContentMarginTop = sb.ContentMarginBottom = padY;
            if (shadow is { } s)
            {
                sb.ShadowColor = s;
                sb.ShadowSize = 30;
            }
            th.SetTypeVariation(name, "PanelContainer");
            th.SetStylebox("panel", name, sb);
        }
        PanelVar("Chip", new Color(t.Text, 0.06f), new Color(t.Text, 0.3f), 8, 9, 3, 1);
        PanelVar("Card", t.Bg, t.Accent, 28, 44, 34, 2, new Color(t.Accent, 0.25f));
        PanelVar("RowOn", new Color(t.Accent, 0.2f), t.Accent, 14, 20, 12);
        PanelVar("RowOff", new Color(t.Text, 0.04f), new Color(t.Text, 0), 14, 20, 12);
        PanelVar("Caption", new Color(t.Bg, 0.78f), new Color(t.Text, 0.12f), 24, 30, 12, 1);
        PanelVar("Status", new Color(t.Bg, 0.6f), new Color(t.Text, 0.18f), 999, 14, 6, 1);
        return th;
    }

    /// <summary>Capa de interfaz a pantalla completa que sigue el tema.</summary>
    public static Control Root(Node parent)
    {
        var layer = new CanvasLayer();
        parent.AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = Build(Themes.Current) };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        Action refresh = () => root.Theme = Build(Themes.Current);
        Themes.Changed += refresh;
        root.TreeExiting += () => Themes.Changed -= refresh;
        return root;
    }

    public static Label Label(string text, string variation, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        return new Label { Text = text, ThemeTypeVariation = variation, HorizontalAlignment = align };
    }

    /// <summary>
    /// Con teclado, traduce el nombre de una casilla a su tecla (START → INTRO, △ → Z…).
    /// Las flechas se dejan igual porque las del teclado también valen.
    /// </summary>
    public static string KeyText(string key)
    {
        if (!MatInput.UsingKeyboard) return key;
        return key switch
        {
            "START" => MatInput.KeyName(Pad.Start),
            "SELECT" => MatInput.KeyName(Pad.Select),
            "✕" or "X" => MatInput.KeyName(Pad.Cross),
            "○" or "O" => MatInput.KeyName(Pad.Circle),
            "△" => MatInput.KeyName(Pad.Triangle),
            "□" => MatInput.KeyName(Pad.Square),
            _ => key,
        };
    }

    /// <summary>Indicación de control: una «tecla» (casilla) y lo que hace.</summary>
    public static HBoxContainer Hint(string key, string action)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var chip = new PanelContainer { ThemeTypeVariation = "Chip" };
        chip.AddChild(Label(KeyText(key), "Key"));
        box.AddChild(chip);
        var text = Label(action, "Muted");
        text.VerticalAlignment = VerticalAlignment.Center;
        box.AddChild(text);
        return box;
    }

    public static HBoxContainer Hints(params (string Key, string Action)[] items)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 26);
        foreach (var (key, action) in items) row.AddChild(Hint(key, action));
        return row;
    }

    /// <summary>Fila de menú: nombre a la izquierda y valor a la derecha. Se añade a <paramref name="rows"/> para resaltarla.</summary>
    public static Label MenuRow(VBoxContainer box, List<PanelContainer> rows, string name)
    {
        var row = new PanelContainer { ThemeTypeVariation = "RowOff" };
        var line = new HBoxContainer();
        var label = Label(name, "Value");
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(label);
        var value = Label("", "Accent");
        line.AddChild(value);
        row.AddChild(line);
        box.AddChild(row);
        rows.Add(row);
        return value;
    }

    /// <summary>Panel central con título y fila de indicaciones; se añaden más filas en <paramref name="box"/>.</summary>
    public static PanelContainer Panel(Control ui, string title, out VBoxContainer box, params (string, string)[] hints)
    {
        var panel = new PanelContainer { ThemeTypeVariation = "Card", Visible = false, CustomMinimumSize = new Vector2(560, 0) };
        box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        box.AddChild(Label(title, "Big", HorizontalAlignment.Center));
        var row = Hints(hints);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(row);
        panel.AddChild(box);
        ui.AddChild(Anchor(panel, Control.LayoutPreset.Center, Vector2.Zero));
        return panel;
    }

    /// <summary>
    /// Muestra un aviso grande («¡BIEN!», «¡TIEMPO!»…) con rebote y lo desvanece tras <paramref name="hold"/> segundos.
    /// Cancela la animación del aviso anterior: si no, su desvanecimiento borraría antes de tiempo el nuevo.
    /// </summary>
    public static void FlashText(Label label, string text, Color color, double hold, float from = 0.4f, double fade = 0.25)
    {
        if (label.HasMeta("flash") && label.GetMeta("flash").AsGodotObject() is Tween previous) previous.Kill();
        label.Text = text;
        label.AddThemeColorOverride("font_color", color);
        label.Modulate = Colors.White;
        Pop(label, from);
        var tween = label.CreateTween();
        tween.TweenInterval(hold);
        tween.TweenProperty(label, "modulate:a", 0.0, fade);
        label.SetMeta("flash", tween);
    }

    /// <summary>Aparición con rebote desde una escala menor.</summary>
    public static void Pop(Control control, float from = 0.5f)
    {
        control.PivotOffset = control.Size / 2;
        control.CreateTween().TweenProperty(control, "scale", Vector2.One, 0.3).From(Vector2.One * from)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public static void ShowPanel(PanelContainer panel)
    {
        panel.Visible = true;
        panel.Modulate = new Color(1, 1, 1, 0);
        panel.CreateTween().TweenProperty(panel, "modulate:a", 1.0, 0.25);
        Pop(panel, 0.9f);
    }

    /// <summary>Coloca un control en una esquina o borde con margen.</summary>
    public static T Anchor<T>(T control, Control.LayoutPreset preset, Vector2 offset) where T : Control
    {
        control.SetAnchorsPreset(preset);
        control.GrowHorizontal = preset is Control.LayoutPreset.TopRight or Control.LayoutPreset.BottomRight
            or Control.LayoutPreset.CenterRight
            ? Control.GrowDirection.Begin
            : preset is Control.LayoutPreset.CenterTop or Control.LayoutPreset.CenterBottom or Control.LayoutPreset.Center
                ? Control.GrowDirection.Both
                : Control.GrowDirection.End;
        control.GrowVertical = preset is Control.LayoutPreset.BottomLeft or Control.LayoutPreset.BottomRight
            or Control.LayoutPreset.CenterBottom
            ? Control.GrowDirection.Begin
            : preset is Control.LayoutPreset.CenterLeft or Control.LayoutPreset.CenterRight or Control.LayoutPreset.Center
                ? Control.GrowDirection.Both
                : Control.GrowDirection.End;
        control.Position += offset;
        return control;
    }
}

/// <summary>Fundido a color de fondo al entrar y salir de una pantalla.</summary>
public partial class ScreenFade : ColorRect
{
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Color = Themes.Current.Bg;
        CreateTween().TweenProperty(this, "modulate:a", 0.0, 0.45).From(1.0);
    }

    public void GoTo(string scene)
    {
        Color = Themes.Current.Bg;
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 1.0, 0.3);
        tween.TweenCallback(Callable.From(() => GetTree().ChangeSceneToFile(scene)));
    }
}

/// <summary>Flecha lateral del carrusel que se enciende al pisar ← o →.</summary>
public partial class SideArrow : Control
{
    private float _lit;
    public bool PointsRight { get; set; }

    public float Lit
    {
        get => _lit;
        set
        {
            _lit = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(64, 64);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
        Themes.Changed += QueueRedraw;
    }

    public override void _ExitTree() => Themes.Changed -= QueueRedraw;

    public void Flash()
    {
        Lit = 1;
        CreateTween().TweenMethod(Callable.From<float>(v => Lit = v), 1.0f, 0.0f, 0.35)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        var c = Size / 2;
        float r = Size.X / 2 * (1 + _lit * 0.15f);
        if (_lit > 0) DrawCircle(c, r + 10 * _lit, new Color(t.H, 0.35f * _lit));
        DrawCircle(c, r, new Color(t.Text, 0.1f).Lerp(t.H, _lit));
        float s = PointsRight ? 1 : -1;
        var ink = t.Text.Lerp(t.Ink, _lit);
        DrawColoredPolygon(new[] { c + new Vector2(10 * s, 0), c + new Vector2(-6 * s, -12), c + new Vector2(-6 * s, 12) }, ink);
    }
}
