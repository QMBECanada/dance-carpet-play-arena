using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Pantalla del circuito: menú (tiempo por estación), descanso entre estaciones con cuenta atrás y resumen final.
/// El tiempo de cada estación lo lleva <see cref="CircuitRunner"/>, que sigue vivo mientras se juega.
/// </summary>
public partial class CircuitScreen : Node3D
{
    private readonly GameInfo _game = GameCatalog.All.First(g => g.Id == "circuit");
    private readonly List<PanelContainer> _rows = new();
    private int _row;
    private double _rest;
    private Label _minutesValue = null!, _title = null!, _big = null!, _info = null!;
    private PanelContainer _menu = null!, _panel = null!;
    private StationDots _dots = null!;
    private ScreenFade _fade = null!;

    public override void _Ready()
    {
        Audio.Music("menu");
        AddChild(new StageBackdrop { Image = "res://assets/portada/fondo.png" });
        AddChild(new Camera3D());
        var ui = Root(this);

        _menu = new PanelContainer { ThemeTypeVariation = "Card", Visible = false, CustomMinimumSize = new Vector2(700, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(Label("CIRCUITO", "Big", HorizontalAlignment.Center));
        var about = Label($"{Circuit.StationIds.Length} estaciones seguidas, con {Circuit.RestSeconds:0} s de descanso entre ellas:\n" +
                          string.Join(" · ", Circuit.Stations.Select(s => s.Name)), "Muted", HorizontalAlignment.Center);
        about.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(about);
        _minutesValue = MenuRow(box, _rows, "Tiempo por estación");
        MenuRow(box, _rows, "¡Empezar!");
        MenuRow(box, _rows, "Volver");
        int? done = Records.Get(_game.Id);
        box.AddChild(Label($"Circuitos completados: {done ?? 0}", "Muted", HorizontalAlignment.Center));
        var hints = Hints(("↑ ↓", "Moverse"), ("← →", "Cambiar"), ("START", "Aceptar"));
        hints.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(hints);
        _menu.AddChild(box);
        ui.AddChild(Anchor(_menu, Control.LayoutPreset.Center, Vector2.Zero));

        _panel = new PanelContainer { ThemeTypeVariation = "Card", Visible = false, CustomMinimumSize = new Vector2(760, 0) };
        var pbox = new VBoxContainer();
        pbox.AddThemeConstantOverride("separation", 12);
        _title = Label("", "Big", HorizontalAlignment.Center);
        _big = Label("", "HugeAccent", HorizontalAlignment.Center);
        _big.AddThemeFontSizeOverride("font_size", 96);
        _info = Label("", "Value", HorizontalAlignment.Center);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dots = new StationDots { CustomMinimumSize = new Vector2(0, 40) };
        foreach (var c in new Control[] { _title, _big, _info, _dots }) pbox.AddChild(c);
        _panel.AddChild(pbox);
        ui.AddChild(Anchor(_panel, Control.LayoutPreset.Center, Vector2.Zero));

        _fade = new ScreenFade();
        ui.AddChild(_fade);
        MatInput.Instance.Pressed += OnPressed;

        switch (Circuit.Current)
        {
            case Circuit.Stage.Rest:
                ShowRest();
                break;
            case Circuit.Stage.Done:
                ShowDone();
                break;
            default:
                Circuit.Current = Circuit.Stage.Menu;
                ShowMenu();
                break;
        }
    }

    public override void _ExitTree() => MatInput.Instance.Pressed -= OnPressed;

    private void ShowMenu()
    {
        _panel.Visible = false;
        RefreshMenu();
        ShowPanel(_menu);
    }

    private void RefreshMenu()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].ThemeTypeVariation = i == _row ? "RowOn" : "RowOff";
        int total = Circuit.Minutes * Circuit.StationIds.Length + (int)(Circuit.RestSeconds * (Circuit.StationIds.Length - 1) / 60);
        _minutesValue.Text = $"‹  {Circuit.Minutes} min (unos {total} min en total)  ›";
    }

    private void ShowRest()
    {
        _rest = Circuit.Index == 0 ? 5 : Circuit.RestSeconds;
        _title.Text = Circuit.Index == 0 ? "¡PREPÁRATE!" : "DESCANSO";
        _dots.Done = Circuit.Index;
        UpdateRest();
        ShowPanel(_panel);
    }

    private void UpdateRest()
    {
        _big.Text = $"{(int)System.Math.Ceiling(_rest)}";
        var next = Circuit.Station;
        _info.Text = $"Siguiente: {next.Name.ToUpperInvariant()}  ·  estación {Circuit.Index + 1} de {Circuit.StationIds.Length}\n" +
                     $"{next.Blurb}\n\n{(MatInput.UsingKeyboard ? "INTRO" : "START")}: empezar ya   ·   {(MatInput.UsingKeyboard ? "ESC" : "SELECT")}: dejar el circuito";
    }

    private void ShowDone()
    {
        Circuit.Current = Circuit.Stage.Menu;
        int count = (Records.Get(_game.Id) ?? 0) + 1;
        Records.Submit(_game.Id, count);
        var a = Activity.Instance;
        long steps = a is null ? 0 : a.TotalSteps - Circuit.StartSteps;
        double seconds = a is null ? 0 : a.TotalSeconds - Circuit.StartSeconds;
        _title.Text = "¡CIRCUITO COMPLETADO!";
        _big.Text = $"{Activity.Thousands(steps)} PASOS";
        _big.AddThemeFontSizeOverride("font_size", 72);
        _info.Text = $"{Activity.Duration(seconds)} de juego en {Circuit.StationIds.Length} estaciones de {Circuit.Minutes} min\n" +
                     $"Circuitos completados: {count}\n\n{(MatInput.UsingKeyboard ? "INTRO" : "START")}: otro circuito   ·   " +
                     $"{(MatInput.UsingKeyboard ? "ESC" : "SELECT")}: volver";
        _dots.Done = Circuit.StationIds.Length;
        Audio.Play("stars3");
        ShowPanel(_panel);
    }

    public override void _Process(double delta)
    {
        if (Circuit.Current != Circuit.Stage.Rest) return;
        _rest -= delta;
        if (_rest <= 0)
        {
            StartStation();
            return;
        }
        string before = _big.Text;
        UpdateRest();
        if (_big.Text != before && _rest < 3.5) Audio.Play("click", 1.2f);
    }

    private void StartStation()
    {
        Circuit.Current = Circuit.Stage.Station;
        Circuit.StationLeft = Circuit.Minutes * 60;
        Audio.Play("start");
        _fade.GoTo(Circuit.Station.Scene!);
    }

    private void OnPressed(Pad pad)
    {
        if (_menu.Visible)
        {
            switch (pad)
            {
                case Pad.Up or Pad.Down:
                    _row = Mathf.PosMod(_row + (pad == Pad.Up ? -1 : 1), _rows.Count);
                    Audio.Play("click", 1.1f);
                    break;
                case Pad.Left or Pad.Right:
                    if (_row == 0)
                    {
                        int i = System.Array.IndexOf(Circuit.MinuteOptions, Circuit.Minutes);
                        Circuit.Minutes = Circuit.MinuteOptions[Mathf.PosMod(i + (pad == Pad.Left ? -1 : 1), Circuit.MinuteOptions.Length)];
                        Audio.Play("pop");
                    }
                    break;
                case Pad.Start or Pad.Circle:
                    if (_row == 2) _fade.GoTo(HubScreen.ScenePath);
                    else Begin();
                    return;
                case Pad.Select or Pad.Cross:
                    _fade.GoTo(HubScreen.ScenePath);
                    return;
            }
            RefreshMenu();
            return;
        }

        if (Circuit.Current == Circuit.Stage.Rest)
        {
            if (pad is Pad.Start or Pad.Circle) StartStation();
            else if (pad == Pad.Select)
            {
                Circuit.Cancel();
                _fade.GoTo(HubScreen.ScenePath);
            }
            return;
        }

        // Resumen final.
        if (pad is Pad.Start or Pad.Circle)
        {
            Circuit.Cancel();
            _fade.GoTo(Circuit.ScenePath);
        }
        else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
    }

    private void Begin()
    {
        Circuit.Index = 0;
        Circuit.StartSteps = Activity.Instance?.TotalSteps ?? 0;
        Circuit.StartSeconds = Activity.Instance?.TotalSeconds ?? 0;
        Circuit.Current = Circuit.Stage.Rest;
        _menu.Visible = false;
        Audio.Play("start");
        ShowRest();
    }
}
