using Godot;

namespace AlfombraBaile;

/// <summary>Portada al abrir la aplicación: la imagen del juego a pantalla completa y «Pisa START». Cualquier casilla entra.</summary>
public partial class SplashScreen : Control
{
    private Label _press = null!;
    private ScreenFade _fade = null!;
    private double _time;
    private bool _leaving;

    public override void _Ready()
    {
        Audio.Music("menu");
        var background = new ColorRect { Color = new Color("#0A1233") };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var cover = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/portada/portada.png"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
        };
        cover.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(cover);

        _press = Fonts.Label(MatInput.UsingKeyboard ? "PULSA INTRO" : "PISA START", Fonts.Display, 40, HorizontalAlignment.Center);
        _press.AddThemeColorOverride("font_color", Colors.White);
        _press.AddThemeColorOverride("font_outline_color", new Color("#0A1233"));
        _press.AddThemeConstantOverride("outline_size", 14);
        _press.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _press.GrowHorizontal = GrowDirection.Both;
        _press.GrowVertical = GrowDirection.Begin;
        _press.Position += new Vector2(0, -36);
        AddChild(_press);

        _fade = new ScreenFade();
        AddChild(_fade);
        MatInput.Instance.Pressed += OnPressed;
    }

    public override void _ExitTree() => MatInput.Instance.Pressed -= OnPressed;

    public override void _Process(double delta)
    {
        _time += delta;
        _press.Modulate = new Color(1, 1, 1, 0.55f + 0.45f * Mathf.Sin((float)_time * 4));
    }

    private void OnPressed(Pad pad)
    {
        if (_leaving || _time < 0.4) return;
        _leaving = true;
        Audio.Play("start");
        _fade.GoTo(HubScreen.ScenePath);
    }
}
