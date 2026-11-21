using System;
using System.Collections.Generic;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Ajustes sobre el carrusel, para ver los cambios de tema en directo. ↑ ↓ elige fila, ← → cambia el valor,
/// START u O acepta y SELECT o X cierra.
/// </summary>
public partial class SettingsPanel : PanelContainer
{
    private enum Row { Theme, Music, Sfx, Fullscreen, TennisZones, TennisPace, Calibrate, Credits, Back }

    private readonly List<PanelContainer> _rows = new();
    private readonly List<Label> _values = new();
    private ThemeSwatch _swatch = null!;
    private int _selected;

    public event Action? Closed;
    public event Action? CalibrateRequested;
    public event Action? CreditsRequested;

    public override void _Ready()
    {
        ThemeTypeVariation = "Card";
        CustomMinimumSize = new Vector2(640, 0);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(Ui.Label("AJUSTES", "Heading"));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        AddRow(box, "Tema", withValue: true);
        AddRow(box, "Música de fondo", withValue: true);
        AddRow(box, "Efectos de sonido", withValue: true);
        AddRow(box, "Pantalla completa", withValue: true);
        AddRow(box, "Tenis: zonas del rival", withValue: true);
        AddRow(box, "Tenis: ritmo", withValue: true);
        AddRow(box, "Calibrar la alfombra", withValue: false);
        AddRow(box, "Créditos", withValue: false);
        AddRow(box, "Volver", withValue: false);

        _swatch = new ThemeSwatch();
        ((HBoxContainer)_values[(int)Row.Theme].GetParent()).AddChild(_swatch);
        _swatch.GetParent().MoveChild(_swatch, 0);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        box.AddChild(Ui.Hints(("↑ ↓", "Moverse"), ("← →", "Cambiar"), ("START", "Aceptar"), ("SELECT", "Cerrar")));
        AddChild(box);
        Refresh();
    }

    private void AddRow(VBoxContainer box, string name, bool withValue)
    {
        var row = new PanelContainer { ThemeTypeVariation = "RowOff" };
        var line = new HBoxContainer();
        var label = Ui.Label(name, "Value");
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(label);

        var valueBox = new HBoxContainer();
        valueBox.AddThemeConstantOverride("separation", 12);
        var value = Ui.Label("", "Accent");
        if (withValue) valueBox.AddChild(value);
        line.AddChild(valueBox);
        row.AddChild(line);
        box.AddChild(row);
        _rows.Add(row);
        _values.Add(value);
    }

    /// <summary>Abre el panel; <paramref name="keepSelection"/> mantiene la fila elegida (al volver de Créditos).</summary>
    public void Open(bool keepSelection = false)
    {
        if (!keepSelection) _selected = 0;
        Visible = true;
        Modulate = new Color(1, 1, 1, 0);
        Scale = Vector2.One * 0.96f;
        PivotOffset = Size / 2;
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(this, "modulate:a", 1.0, 0.2);
        tween.TweenProperty(this, "scale", Vector2.One, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        Refresh();
    }

    private void Close()
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0.0, 0.15);
        tween.TweenCallback(Callable.From(() =>
        {
            Visible = false;
            Closed?.Invoke();
        }));
    }

    public void HandlePad(Pad pad)
    {
        switch (pad)
        {
            case Pad.Up:
                Select(-1);
                break;
            case Pad.Down:
                Select(1);
                break;
            case Pad.Left:
                Change(-1);
                break;
            case Pad.Right:
                Change(1);
                break;
            case Pad.Start or Pad.Circle:
                Activate();
                break;
            case Pad.Select or Pad.Cross:
                Audio.Play("click", 0.9f);
                Close();
                break;
        }
    }

    private void Select(int step)
    {
        _selected = Mathf.PosMod(_selected + step, _rows.Count);
        Audio.Play("click", 1.1f);
        Refresh();
    }

    private void Change(int step)
    {
        switch ((Row)_selected)
        {
            case Row.Theme:
                int i = Themes.IndexOf(Themes.Current);
                Themes.Set(Themes.All[Mathf.PosMod(i + step, Themes.All.Length)]);
                Audio.Play("pop", step < 0 ? 0.95f : 1.05f);
                break;
            case Row.Fullscreen:
                ToggleFullscreen();
                break;
            case Row.Sfx:
                Settings.SfxLevel = Math.Clamp(Settings.SfxLevel + step, 0, 4);
                Settings.Save();
                Audio.Impact("impact_glass_light", -6); // una muestra para oír el volumen
                break;
            case Row.Music:
                Settings.MusicLevel = Math.Clamp(Settings.MusicLevel + step, 0, 4);
                Settings.Save();
                Audio.RefreshMusicVolume();
                Audio.Play("click");
                break;
            case Row.TennisZones or Row.TennisPace:
                ToggleTennis((Row)_selected);
                break;
        }
        Refresh();
    }

    private void Activate()
    {
        switch ((Row)_selected)
        {
            case Row.Theme:
                Change(1);
                break;
            case Row.Fullscreen:
                ToggleFullscreen();
                Refresh();
                break;
            case Row.TennisZones or Row.TennisPace:
                ToggleTennis((Row)_selected);
                Refresh();
                break;
            case Row.Calibrate:
                Audio.Play("start");
                CalibrateRequested?.Invoke();
                break;
            case Row.Credits:
                Audio.Play("pop");
                Visible = false;
                CreditsRequested?.Invoke();
                break;
            case Row.Back:
                Audio.Play("click", 0.9f);
                Close();
                break;
        }
    }

    private static void ToggleTennis(Row row)
    {
        if (row == Row.TennisZones) Settings.TennisFlipRows = !Settings.TennisFlipRows;
        else Settings.TennisFast = !Settings.TennisFast;
        Settings.Save();
        Audio.Play("click");
    }

    private static void ToggleFullscreen()
    {
        Settings.Fullscreen = !Settings.Fullscreen;
        Settings.Save();
        Settings.ApplyWindowMode();
        Audio.Play("click");
    }

    private void Refresh()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].ThemeTypeVariation = i == _selected ? "RowOn" : "RowOff";
        _values[(int)Row.Theme].Text = $"‹  {Themes.Current.Name}  ›";
        _values[(int)Row.Music].Text = $"‹  {(Settings.MusicLevel == 0 ? "Apagada" : $"{Settings.MusicLevel * 25} %")}  ›";
        _values[(int)Row.Sfx].Text = $"‹  {(Settings.SfxLevel == 0 ? "Apagados" : $"{Settings.SfxLevel * 25} %")}  ›";
        _values[(int)Row.Fullscreen].Text = $"‹  {(Settings.Fullscreen ? "Sí" : "No")}  ›";
        _values[(int)Row.TennisZones].Text = $"‹  {TennisGame.ZonesText}  ›";
        _values[(int)Row.TennisPace].Text = $"‹  {TennisGame.PaceText}  ›";
        _swatch?.QueueRedraw();
    }
}

/// <summary>Tres puntos con los colores principales del tema actual.</summary>
public partial class ThemeSwatch : Control
{
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(62, 26);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        var colors = new[] { t.V, t.H, t.Accent };
        for (int i = 0; i < colors.Length; i++)
        {
            var c = new Vector2(12 + i * 18, Size.Y / 2);
            DrawCircle(c, 12, t.Bg);
            DrawCircle(c, 10, colors[i]);
        }
    }
}
