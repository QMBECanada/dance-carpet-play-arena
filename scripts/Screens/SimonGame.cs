using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Simón dice: el juego enseña una secuencia de casillas, cada una con su nota, y hay que repetirla pisando.
/// Cada acierto añade un paso. A partir de 8 pasos algunos pasos piden pisar dos casillas a la vez.
/// 3 vidas: al fallar se vuelve a enseñar la misma secuencia. El récord es la secuencia más larga completada.
/// </summary>
public partial class SimonGame : Node3D
{
    public const string ScenePath = "res://scenes/Simon.tscn";

    private const int Lives = 3;
    private const int DoublesFrom = 8;
    private const double Gap = 0.16;

    /// <summary>Las 8 casillas exteriores en el sentido de las agujas del reloj, con su nota (pentatónica de Do).</summary>
    private static readonly (Pad Pad, float Hz)[] Notes =
    {
        (Pad.Cross, 261.63f), (Pad.Up, 293.66f), (Pad.Circle, 329.63f), (Pad.Right, 392.00f),
        (Pad.Square, 440.00f), (Pad.Down, 523.25f), (Pad.Triangle, 587.33f), (Pad.Left, 659.25f),
    };

    private enum Phase { Intro, Show, Input, Between, Paused, Over }

    private readonly GameInfo _game = GameCatalog.All.First(g => g.Id == "simon");
    private readonly List<Pad[]> _sequence = new();
    private readonly Dictionary<Pad, AudioStreamWav> _tones = new();
    private readonly HashSet<Pad> _stepHit = new();
    private readonly Random _rng = new();

    private Phase _phase = Phase.Intro, _beforePause;
    private int _lives = Lives, _best, _showIndex, _inputIndex, _mistakes;
    private double _timer;
    private bool _showLit;

    private Mat3D _mat = null!;
    private Label _bestLabel = null!, _phaseLabel = null!, _stepLabel = null!, _bigText = null!;
    private LivesView _livesView = null!;
    private PanelContainer _pausePanel = null!, _endPanel = null!;
    private Label _endScore = null!, _endRecord = null!, _endStats = null!;
    private ScreenFade _fade = null!;

    private double OnTime => Mathf.Lerp(0.62, 0.3, Math.Min(_sequence.Count / 16.0, 1));

    public override void _Ready()
    {
        Audio.Music(null);
        AddChild(new StageBackdrop { Focus = new Vector2(0, -0.3f), BackdropPosition = new Vector3(0, 4, -16) });
        var camera = new Camera3D { Fov = 48 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 6.2f, 4.6f), new Vector3(0, 0, -0.8f), Vector3.Up);

        _mat = new Mat3D();
        AddChild(_mat);
        foreach (var (pad, hz) in Notes) _tones[pad] = Audio.Tone(hz);

        BuildUi();
        MatInput.Instance.Pressed += OnPressed;
        MatInput.Instance.Released += OnReleased;

        AddStep();
        Big("¡PREPÁRATE!", Themes.Current.AccentText, 0.8);
        Audio.Play("start");
        _timer = 1.4;
    }

    public override void _ExitTree()
    {
        MatInput.Instance.Pressed -= OnPressed;
        MatInput.Instance.Released -= OnReleased;
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Root(this);

        var bestBox = new VBoxContainer();
        bestBox.AddThemeConstantOverride("separation", -6);
        bestBox.AddChild(Label("PASOS", "Muted"));
        _bestLabel = Label("0", "Huge");
        bestBox.AddChild(_bestLabel);
        ui.AddChild(Anchor(bestBox, Control.LayoutPreset.TopLeft, new Vector2(44, 26)));

        var phaseBox = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) };
        phaseBox.AddThemeConstantOverride("separation", 0);
        _phaseLabel = Label("", "Big", HorizontalAlignment.Center);
        _stepLabel = Label("", "Muted", HorizontalAlignment.Center);
        phaseBox.AddChild(_phaseLabel);
        phaseBox.AddChild(_stepLabel);
        ui.AddChild(Anchor(phaseBox, Control.LayoutPreset.CenterTop, new Vector2(0, 30)));

        var rightBox = new VBoxContainer();
        rightBox.AddThemeConstantOverride("separation", 4);
        _livesView = new LivesView { Lives = Lives, Max = Lives, CustomMinimumSize = new Vector2(132, 40) };
        _livesView.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        int? record = Records.Get(_game.Id);
        rightBox.AddChild(_livesView);
        rightBox.AddChild(Label($"Récord: {(record?.ToString() ?? "—")}", "Muted", HorizontalAlignment.Right));
        ui.AddChild(Anchor(rightBox, Control.LayoutPreset.TopRight, new Vector2(-44, 34)));

        ui.AddChild(Anchor(Hints(("SELECT", "Pausa")), Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        _bigText = Label("", "Huge", HorizontalAlignment.Center);
        _bigText.AddThemeFontSizeOverride("font_size", 110);
        _bigText.CustomMinimumSize = new Vector2(1000, 160);
        _bigText.VerticalAlignment = VerticalAlignment.Center;
        ui.AddChild(Anchor(_bigText, Control.LayoutPreset.Center, new Vector2(0, -60)));

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _endPanel = Panel(ui, "¡SE ACABÓ!", out var endBox, ("START", "Otra vez"), ("SELECT", "Volver"));
        _endScore = Label("", "HugeAccent", HorizontalAlignment.Center);
        _endRecord = Label("", "Heading", HorizontalAlignment.Center);
        _endStats = Label("", "Muted", HorizontalAlignment.Center);
        foreach (var (label, index) in new[] { (_endScore, 1), (_endRecord, 2), (_endStats, 3) })
        {
            endBox.AddChild(label);
            endBox.MoveChild(label, index);
        }

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    private void Big(string text, Color color, double hold = 0.5)
     => FlashText(_bigText, text, color, hold);

    private void SetPhaseText(string phase, Color color, string step = "")
    {
        _phaseLabel.Text = phase;
        _phaseLabel.AddThemeColorOverride("font_color", color);
        _stepLabel.Text = step;
    }

    // ---------------------------------------------------------------- secuencia

    private void AddStep()
    {
        var pads = Notes.Select(n => n.Pad).ToArray();
        var last = _sequence.Count > 0 ? _sequence[^1] : Array.Empty<Pad>();
        bool doubleStep = _sequence.Count >= DoublesFrom && _rng.NextDouble() < 0.3;

        // Evita repetir exactamente el paso anterior para que se distinga la secuencia.
        Pad first;
        do first = pads[_rng.Next(pads.Length)];
        while (last.Length == 1 && last[0] == first && !doubleStep);

        if (!doubleStep)
        {
            _sequence.Add(new[] { first });
            return;
        }
        Pad second;
        do second = pads[_rng.Next(pads.Length)];
        while (second == first);
        _sequence.Add(new[] { first, second });
    }

    private void StartShow()
    {
        _phase = Phase.Show;
        _showIndex = 0;
        _showLit = false;
        _timer = 0.5;
        ClearMat();
        SetPhaseText("MIRA…", Themes.Current.Text, StepsText(_sequence.Count));
    }

    private void StartInput()
    {
        _phase = Phase.Input;
        _inputIndex = 0;
        _stepHit.Clear();
        ClearMat();
        SetPhaseText("¡TU TURNO!", Themes.Current.AccentText, $"0 / {_sequence.Count}");
    }

    private void PlayStep(Pad[] step)
    {
        foreach (var pad in step) Audio.PlayStream(_tones[pad], step.Length > 1 ? -11 : -8);
    }

    private void ClearMat()
    {
        foreach (var pad in PadInfo.CalibrationOrder) _mat.SetPressed(pad, false);
    }

    public override void _Process(double delta)
    {
        if (_phase is Phase.Paused or Phase.Over) return;
        _timer -= delta;
        if (_timer > 0) return;

        switch (_phase)
        {
            case Phase.Intro:
                StartShow();
                break;
            case Phase.Show:
                if (_showLit)
                {
                    foreach (var pad in _sequence[_showIndex]) _mat.SetPressed(pad, false);
                    _showLit = false;
                    _showIndex++;
                    _timer = Gap;
                    if (_showIndex >= _sequence.Count)
                    {
                        _timer = 0;
                        StartInput();
                    }
                }
                else
                {
                    var step = _sequence[_showIndex];
                    foreach (var pad in step) _mat.SetPressed(pad, true);
                    PlayStep(step);
                    _showLit = true;
                    _timer = step.Length > 1 ? OnTime * 1.5 : OnTime;
                }
                break;
            case Phase.Between:
                StartShow();
                break;
        }
    }

    // ---------------------------------------------------------------- pisadas

    private void OnPressed(Pad pad)
    {
        switch (_phase)
        {
            case Phase.Intro or Phase.Show or Phase.Between:
                if (pad == Pad.Select) Pause();
                break;
            case Phase.Input:
                if (pad == Pad.Select) Pause();
                else if (pad is not (Pad.Center or Pad.Start)) Step(pad);
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle) Resume();
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
            case Phase.Over when _endPanel.Visible:
                if (pad is Pad.Start or Pad.Circle) _fade.GoTo(ScenePath);
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
        }
    }

    private void OnReleased(Pad pad)
    {
        if (_phase == Phase.Input) _mat.SetPressed(pad, false);
    }

    private void Step(Pad pad)
    {
        var expected = _sequence[_inputIndex];
        _mat.SetPressed(pad, true);
        if (!expected.Contains(pad))
        {
            Mistake(expected);
            return;
        }

        Audio.PlayStream(_tones[pad], -8);
        _stepHit.Add(pad);
        // Paso doble: cuenta cuando las dos casillas están pisadas a la vez.
        if (!expected.All(MatInput.Instance.IsDown)) return;

        _stepHit.Clear();
        _inputIndex++;
        _stepLabel.Text = $"{_inputIndex} / {_sequence.Count}";
        if (_inputIndex < _sequence.Count) return;

        // Secuencia completa.
        _best = Math.Max(_best, _sequence.Count);
        _bestLabel.Text = _best.ToString();
        Pop(_bestLabel, 0.6f);
        bool doublesNow = _sequence.Count + 1 == DoublesFrom;
        Big(doublesNow ? "¡AHORA DE DOS EN DOS!" : Praise(), Themes.Current.AccentText, doublesNow ? 1.1 : 0.45);
        Audio.Play(doublesNow ? "combo" : "sparkle");
        SetPhaseText("¡BIEN!", Themes.Current.AccentText, StepsText(_sequence.Count));
        AddStep();
        _phase = Phase.Between;
        _timer = doublesNow ? 1.8 : 1.1;
    }

    private static string StepsText(int n) => n == 1 ? "1 paso" : $"{n} pasos";

    private string Praise()
    {
        string[] words = { "¡BIEN!", "¡GENIAL!", "¡ESO ES!", "¡PERFECTO!", "¡QUÉ MEMORIA!" };
        return words[_rng.Next(words.Length)];
    }

    private void Mistake(Pad[] expected)
    {
        _lives--;
        _mistakes++;
        _livesView.Lives = _lives;
        Audio.Play("miss");
        Audio.Impact("impact_soft_medium", -3, 0.7f);
        var t = Themes.Current;

        // Enseña dónde había que pisar.
        ClearMat();
        foreach (var pad in expected) _mat.SetPressed(pad, true);

        if (_lives <= 0)
        {
            Finish();
            return;
        }
        Big("¡UY!", t.H, 0.5);
        SetPhaseText("OTRA VEZ", t.H, $"Te quedan {_lives} {(_lives == 1 ? "vida" : "vidas")}");
        _phase = Phase.Between;
        _timer = 1.6;
    }

    private void Pause()
    {
        _beforePause = _phase;
        _phase = Phase.Paused;
        Audio.Play("pop");
        ShowPanel(_pausePanel);
    }

    private void Resume()
    {
        _pausePanel.Visible = false;
        Audio.Play("click");
        // Retomar a mitad de enseñar la secuencia la confundiría: se vuelve a enseñar entera.
        if (_beforePause == Phase.Show)
        {
            StartShow();
            return;
        }
        _phase = _beforePause;
    }

    private void Finish()
    {
        _phase = Phase.Over;
        SetPhaseText("", Themes.Current.Text);
        bool record = Records.Submit(_game, _best);
        Big("¡FIN!", Themes.Current.H, 0.6);

        GetTree().CreateTimer(1.3).Timeout += () =>
        {
            ClearMat();
            _endScore.Text = StepsText(_best).ToUpperInvariant();
            _endRecord.Text = record ? "¡NUEVO RÉCORD!" : $"Récord: {Records.Get(_game.Id)?.ToString() ?? "—"}";
            _endRecord.ThemeTypeVariation = record ? "Heading" : "Muted";
            _endStats.Text = $"Secuencia más larga: {_best}   ·   Fallos: {_mistakes}";
            ShowPanel(_endPanel);
            Audio.Play(record ? "stars3" : "sparkle");
            if (record) Fx.Burst(_mat, new Vector3(0, 1.5f, 0), Themes.Current.Accent, 60, 6);
        };
    }
}

/// <summary>Corazones de vidas: llenos los que quedan, huecos los perdidos.</summary>
public partial class LivesView : Control
{
    private int _lives;
    public int Max { get; set; } = 3;

    public int Lives
    {
        get => _lives;
        set
        {
            bool lost = value < _lives;
            _lives = value;
            QueueRedraw();
            if (lost) Ui.Pop(this, 1.3f);
        }
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        float size = Size.Y;
        for (int i = 0; i < Max; i++)
        {
            var c = new Vector2(Size.X - size / 2 - i * (size + 8), size / 2);
            bool full = i < _lives;
            DrawHeart(c, size * 0.5f, full ? t.H : new Color(t.Text, 0.18f));
        }
    }

    private void DrawHeart(Vector2 c, float r, Color color)
    {
        var points = new List<Vector2>();
        for (int i = 0; i < 40; i++)
        {
            float a = Mathf.Tau * i / 40;
            float x = 16 * Mathf.Pow(Mathf.Sin(a), 3);
            float y = 13 * Mathf.Cos(a) - 5 * Mathf.Cos(2 * a) - 2 * Mathf.Cos(3 * a) - Mathf.Cos(4 * a);
            points.Add(c + new Vector2(x, -y - 2) * (r / 17));
        }
        DrawColoredPolygon(points.ToArray(), color);
    }
}
