using Godot;

namespace AlfombraBaile;

/// <summary>Reflejo pequeño de la alfombra en una esquina de la pantalla: se ilumina lo que se está pisando.</summary>
public partial class MatMirror : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        MatInput.Instance.Pressed += OnPad;
        MatInput.Instance.Released += OnPad;
        Themes.Changed += QueueRedraw;
        MatInput.Instance.DeviceChanged += QueueRedraw;
    }

    public override void _ExitTree()
    {
        MatInput.Instance.Pressed -= OnPad;
        MatInput.Instance.Released -= OnPad;
        Themes.Changed -= QueueRedraw;
        MatInput.Instance.DeviceChanged -= QueueRedraw;
    }

    private void OnPad(Pad _) => QueueRedraw();

    public override void _Draw()
    {
        var theme = Themes.Current;
        float gap = Size.X * 0.04f;
        float tile = (Size.X - gap * 4) / 3;
        float pillH = tile * 0.32f;
        var box = new StyleBoxFlat
        {
            BgColor = new Color(theme.Floor.Darkened(theme.Light ? 0.06f : 0.3f), 0.85f),
        };
        box.SetCornerRadiusAll((int)(tile * 0.18f));
        DrawStyleBox(box, new Rect2(Vector2.Zero, Size));

        foreach (var pad in PadInfo.CalibrationOrder)
        {
            var cell = PadInfo.Cell(pad);
            bool down = MatInput.Instance.IsDown(pad);
            Rect2 rect = cell.Y < 0
                ? new Rect2(gap + cell.X * (tile + gap), gap, tile, pillH)
                : new Rect2(gap + cell.X * (tile + gap), gap * 2 + pillH + cell.Y * (tile + gap), tile, tile);

            var color = pad == Pad.Center ? theme.Floor.Lerp(theme.Text, 0.25f) : theme.Role(PadInfo.Role(pad));
            var sb = new StyleBoxFlat { BgColor = down ? color.Lightened(0.15f) : new Color(color, 0.42f) };
            sb.SetCornerRadiusAll((int)(tile * 0.16f));
            if (down)
            {
                sb.ShadowColor = new Color(color, 0.8f);
                sb.ShadowSize = (int)(tile * 0.22f);
                sb.SetBorderWidthAll(2);
                sb.BorderColor = theme.Light ? theme.Text : Colors.White;
            }
            DrawStyleBox(sb, rect);
            var ink = pad == Pad.Center ? theme.Text : theme.Ink;
            var inkColor = new Color(ink, down ? 0.95f : 0.6f);
            if (MatInput.UsingKeyboard)
            {
                // Sin alfombra: la tecla que corresponde a cada casilla.
                string key = MatInput.KeyName(pad);
                int size = (int)(cell.Y < 0 ? pillH * 0.62f : tile * 0.42f);
                var font = Fonts.Bold;
                float ascent = font.GetAscent(size), descent = font.GetDescent(size);
                var baseline = new Vector2(rect.Position.X, rect.GetCenter().Y + (ascent - descent) / 2);
                DrawString(font, baseline, key, HorizontalAlignment.Center, rect.Size.X, size, inkColor);
            }
            else
            {
                Painter.PadGlyph(this, pad, rect.GetCenter(), Mathf.Min(rect.Size.X, rect.Size.Y) * 0.55f, inkColor);
            }
        }
    }

    /// <summary>Alto que necesita para un ancho dado.</summary>
    public static float HeightFor(float width)
    {
        float gap = width * 0.04f;
        float tile = (width - gap * 4) / 3;
        return gap * 5 + tile * 3 + tile * 0.32f;
    }
}
