using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Ejercicio: series de pasos al ritmo de la música. Cada ejercicio (assets/ejercicios/ejercicios.txt) es un ciclo de
/// pasos «este pie a esta casilla»; el pie izquierdo usa las columnas izquierda y central, el derecho la central y la
/// derecha. Una flecha curva del color del pie indica de dónde a dónde moverlo. Un paso cada dos pulsos.
/// </summary>
public partial class WorkoutGame : Node3D
{
    public const string ScenePath = "res://scenes/Workout.tscn";

    private sealed record Tempo(string Name, string Song);

    private static readonly Tempo[] Tempos =
    {
        new("Suave · Calentamiento", "calentamiento"), new("Suave · Latino", "latino"), new("Media · Pop", "pop"),
        new("Media · Ritmo", "ritmo"), new("Media · Disco", "disco"), new("Rápida · A tope", "atope"), new("Rápida · Rock", "rock"),
    };

    public sealed class Step
    {
        public int Exercise;
        public bool Left;
        public Pad From, To;
        public double Time;
        public bool Done, Hit;
    }

    private const int StepBeats = 2, MinStepsPerExercise = 16, IntroBeats = 4, RestBeats = 4, FirstBeat = 8, ShortSession = 8;
    private const double OnTime = 0.15, Late = 0.3;

    private enum Phase { Menu, Play, Paused, Over }

    private static int _tempo = 3;
    private static bool _long;

    private Phase _phase = Phase.Menu;
    private List<Exercise> _all = new();
    private List<Exercise> _session = new();
    private readonly List<Step> _steps = new();
    private readonly List<(double Time, string Text)> _cues = new();
    private SongInfo _song = new();
    private AudioStreamPlayer _music = null!;
    private double _songTime;
    private int _onTime, _good, _missed, _cueIndex;
    private Pad _left = Pad.Left, _right = Pad.Right;

    private Mat3D _mat = null!;
    private Camera3D _camera = null!;
    private FootLayer _layer = null!;
    private Label _exerciseLabel = null!, _progressLabel = null!, _precisionLabel = null!, _instruction = null!, _bigText = null!;
    private Control _hud = null!;
    private BeatDot _beat = null!;
    private PanelContainer _menu = null!, _pausePanel = null!, _endPanel = null!;
    private readonly List<PanelContainer> _menuRows = new();
    private Label _tempoValue = null!, _sessionValue = null!, _menuRecord = null!;
    private Label _endScore = null!, _endRecord = null!, _endStats = null!;
    private int _menuRow;
    private ScreenFade _fade = null!;

    public override void _Ready()
    {
        Audio.Music(null);
        AddChild(new StageBackdrop { Focus = new Vector2(0, -0.3f), BackdropPosition = new Vector3(0, 4, -16) });
        _camera = new Camera3D { Fov = 46 };
        AddChild(_camera);
        _camera.LookAtFromPosition(new Vector3(0, 6.9f, 3.7f), new Vector3(0, 0, -0.35f), Vector3.Up);
        _mat = new Mat3D();
        AddChild(_mat);

        _music = new AudioStreamPlayer { VolumeDb = -6 };
        AddChild(_music);
        _music.Finished += () =>
        {
            if (_phase == Phase.Play) Finish();
        };

        _all = Exercises.Parse(FileAccess.GetFileAsString("res://assets/ejercicios/ejercicios.txt"), GD.PushWarning);
        BuildUi();
        MatInput.Instance.Pressed += OnPressed;
        MatInput.Instance.Released += OnReleased;
        if (Circuit.Active)
        {
            // En el circuito: ritmo medio, sesión corta, sin menú.
            _tempo = 3;
            _long = false;
            StartSession();
        }
        else OpenMenu();
    }

    public override void _ExitTree()
    {
        MatInput.Instance.Pressed -= OnPressed;
        MatInput.Instance.Released -= OnReleased;
    }

    // ---------------------------------------------------------------- ejercicios

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Root(this);
        _layer = new FootLayer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_layer);

        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_hud);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", -2);
        _exerciseLabel = Label("", "Heading");
        _progressLabel = Label("", "Muted");
        left.AddChild(_exerciseLabel);
        left.AddChild(_progressLabel);
        _hud.AddChild(Anchor(left, Control.LayoutPreset.TopLeft, new Vector2(44, 28)));

        _beat = new BeatDot { CustomMinimumSize = new Vector2(60, 60) };
        _hud.AddChild(Anchor(_beat, Control.LayoutPreset.CenterTop, new Vector2(0, 24)));

        var right = new VBoxContainer();
        right.AddChild(Label("PRECISIÓN", "Muted", HorizontalAlignment.Right));
        _precisionLabel = Label("—", "Big", HorizontalAlignment.Right);
        right.AddChild(_precisionLabel);
        _hud.AddChild(Anchor(right, Control.LayoutPreset.TopRight, new Vector2(-44, 26)));

        _instruction = Label("", "Big", HorizontalAlignment.Center);
        _instruction.CustomMinimumSize = new Vector2(900, 0);
        _hud.AddChild(Anchor(_instruction, Control.LayoutPreset.CenterBottom, new Vector2(0, -40)));
        _hud.AddChild(Anchor(Hints(("SELECT", "Pausa")), Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        _bigText = Label("", "Huge", HorizontalAlignment.Center);
        _bigText.AddThemeFontSizeOverride("font_size", 80);
        _bigText.CustomMinimumSize = new Vector2(1100, 120);
        _bigText.VerticalAlignment = VerticalAlignment.Center;
        _hud.AddChild(Anchor(_bigText, Control.LayoutPreset.CenterTop, new Vector2(0, 110)));

        _menu = new PanelContainer { ThemeTypeVariation = "Card", Visible = false, CustomMinimumSize = new Vector2(680, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(Label("EJERCICIO", "Big", HorizontalAlignment.Center));
        _tempoValue = MenuRow(box, "Ritmo");
        _sessionValue = MenuRow(box, "Sesión");
        MenuRow(box, "Volver");
        _menuRecord = Label("", "Muted", HorizontalAlignment.Center);
        _menuRecord.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_menuRecord);
        var hints = Hints(("↑ ↓", "Moverse"), ("← →", "Cambiar"), ("START", "¡Empezar!"));
        hints.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(hints);
        _menu.AddChild(box);
        ui.AddChild(Anchor(_menu, Control.LayoutPreset.Center, Vector2.Zero));

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _endPanel = Panel(ui, "¡SESIÓN TERMINADA!", out var endBox, ("START", "Otra vez"), ("SELECT", "Menú"));
        _endPanel.CustomMinimumSize = new Vector2(640, 0);
        _endScore = Label("", "HugeAccent", HorizontalAlignment.Center);
        _endRecord = Label("", "Heading", HorizontalAlignment.Center);
        _endStats = Label("", "Muted", HorizontalAlignment.Center);
        _endStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (var (label, i) in new[] { (_endScore, 1), (_endRecord, 2), (_endStats, 3) })
        {
            endBox.AddChild(label);
            endBox.MoveChild(label, i);
        }

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    private Label MenuRow(VBoxContainer box, string name) => Ui.MenuRow(box, _menuRows, name);

    private string RecordKey => $"workout_{Tempos[_tempo].Song}_{(_long ? "completa" : "corta")}";

    private void RefreshMenu()
    {
        for (int i = 0; i < _menuRows.Count; i++) _menuRows[i].ThemeTypeVariation = i == _menuRow ? "RowOn" : "RowOff";
        _tempoValue.Text = $"‹  {Tempos[_tempo].Name}  ›";
        _sessionValue.Text = $"‹  {(_long ? $"Completa ({_all.Count} ejercicios)" : $"Corta ({Math.Min(ShortSession, _all.Count)} ejercicios)")}  ›";
        int? best = Records.Get(RecordKey);
        _menuRecord.Text = $"El pie izquierdo usa las columnas izquierda y central; el derecho, la central y la derecha.\n" +
                           $"Mejor precisión: {(best is { } b ? $"{b} %" : "—")}";
    }

    private void Big(string text, Color color, double hold = 0.8)
     => FlashText(_bigText, text, color, hold, 0.6f, 0.3);

    // ---------------------------------------------------------------- menú

    private void OpenMenu()
    {
        _phase = Phase.Menu;
        _music.Stop();
        _hud.Visible = false;
        _layer.Visible = false;
        _mat.ClearAll();
        _endPanel.Visible = false;
        _pausePanel.Visible = false;
        RefreshMenu();
        ShowPanel(_menu);
    }

    private void MenuPad(Pad pad)
    {
        switch (pad)
        {
            case Pad.Up or Pad.Down:
                _menuRow = Mathf.PosMod(_menuRow + (pad == Pad.Up ? -1 : 1), _menuRows.Count);
                Audio.Play("click", 1.1f);
                break;
            case Pad.Left or Pad.Right:
                if (_menuRow == 0) _tempo = Mathf.PosMod(_tempo + (pad == Pad.Left ? -1 : 1), Tempos.Length);
                else if (_menuRow == 1) _long = !_long;
                Audio.Play("pop");
                break;
            case Pad.Start or Pad.Circle:
                if (_menuRow == 2) _fade.GoTo(HubScreen.ScenePath);
                else StartSession();
                return;
            case Pad.Select or Pad.Cross:
                _fade.GoTo(HubScreen.ScenePath);
                return;
        }
        RefreshMenu();
    }

    // ---------------------------------------------------------------- sesión

    private void StartSession()
    {
        var tempo = Tempos[_tempo];
        _song = SongInfo.Load(tempo.Song);
        // Sesión corta: ejercicios repartidos por toda la lista (de lo más sencillo a lo más complicado).
        _session = _long || _all.Count <= ShortSession
            ? _all.ToList()
            : Enumerable.Range(0, ShortSession).Select(i => _all[i * _all.Count / ShortSession]).ToList();
        BuildSchedule();

        _onTime = _good = _missed = 0;
        _cueIndex = 0;
        _left = _session[0].LeftStart;
        _right = _session[0].RightStart;
        _menu.Visible = false;
        _hud.Visible = true;
        _layer.Visible = true;
        _layer.Setup(_camera, _mat);
        _precisionLabel.Text = "—";
        _songTime = 0;
        _music.Stream = GD.Load<AudioStream>($"res://assets/musica/{tempo.Song}.mp3");
        _music.Play();
        _phase = Phase.Play;
        Audio.Play("start");
    }

    /// <summary>
    /// Reparte la sesión sobre los pulsos de la canción: para cada ejercicio, 4 pulsos de presentación, tantas vueltas
    /// como haga falta para llegar a 16 pasos (un paso cada 2 pulsos) y 4 pulsos de descanso.
    /// </summary>
    private void BuildSchedule()
    {
        _steps.Clear();
        _cues.Clear();
        double beat = FirstBeat;
        double end = _song.Duration - 1.5;
        for (int e = 0; e < _session.Count; e++)
        {
            var ex = _session[e];
            if (_song.BeatTime(beat + IntroBeats + StepBeats) > end) break;
            _cues.Add((_song.BeatTime(beat), $"{e}|intro"));
            beat += IntroBeats;
            int cycles = (int)Math.Ceiling(MinStepsPerExercise / (double)ex.Steps.Length);
            Pad l = ex.LeftStart, r = ex.RightStart;
            for (int c = 0; c < cycles; c++)
            foreach (var (left, to) in ex.Steps)
            {
                double time = _song.BeatTime(beat);
                if (time > end) goto done;
                _steps.Add(new Step { Exercise = e, Left = left, From = left ? l : r, To = to, Time = time });
                if (left) l = to;
                else r = to;
                beat += StepBeats;
            }
            _cues.Add((_song.BeatTime(beat), $"{e}|rest"));
            beat += RestBeats;
        }
        done:
        _cues.Add((_song.BeatTime(beat), "end|end"));
    }

    public override void _Process(double delta)
    {
        if (_phase != Phase.Play) return;
        _songTime = SongInfo.Advance(_songTime, delta, _music);
        double now = _songTime - Settings.RhythmOffset;

        _beat.Phase = (float)Mathf.PosMod((now - _song.Offset) / _song.Beat, 1.0);

        while (_cueIndex < _cues.Count && now >= _cues[_cueIndex].Time) Cue(_cues[_cueIndex++].Text);

        foreach (var step in _steps)
        {
            if (step.Done || step.Time > now - Late) continue;
            step.Done = true;
            Judge(step, false, 0);
        }

        // La próxima flecha y la siguiente (más tenue); los pies, donde estén ahora.
        var next = _steps.FirstOrDefault(s => !s.Done);
        var after = next is null ? null : _steps.SkipWhile(s => s != next).Skip(1).FirstOrDefault();
        foreach (var pad in PadInfo.CalibrationOrder) _mat.SetBlink(pad, false);
        if (next is not null && next.Time - now < _song.Beat * StepBeats * 1.2)
        {
            _mat.SetBlink(next.To, true);
            _instruction.Text = $"PIE {(next.Left ? "IZQUIERDO" : "DERECHO")}  →  {PadInfo.Label(next.To).ToUpperInvariant()}";
            _instruction.AddThemeColorOverride("font_color", next.Left ? FootLayer.LeftColor : FootLayer.RightColor);
        }
        _layer.Update(_left, _right, next, after, now);
        UpdateProgress(next);
    }

    private void Cue(string cue)
    {
        var parts = cue.Split('|');
        if (parts[1] == "end")
        {
            Finish();
            return;
        }
        int e = int.Parse(parts[0]);
        if (parts[1] == "intro")
        {
            var ex = _session[e];
            _left = ex.LeftStart;
            _right = ex.RightStart;
            Big(ex.Name.ToUpperInvariant(), Themes.Current.AccentText, 1.2);
            Audio.Play("sparkle", 1.1f);
        }
        else
        {
            Big(e + 1 < _session.Count ? "¡BIEN! RESPIRA" : "¡ÚLTIMO EJERCICIO HECHO!", Themes.Current.V, 1.0);
            _instruction.Text = "";
        }
    }

    private void UpdateProgress(Step? next)
    {
        int e = next?.Exercise ?? _session.Count - 1;
        _exerciseLabel.Text = _session[Math.Max(e, 0)].Name.ToUpperInvariant();
        int done = _steps.Count(s => s.Done && s.Exercise == e), total = _steps.Count(s => s.Exercise == e);
        double minutes = _songTime / 60;
        _progressLabel.Text = $"Ejercicio {e + 1} de {_session.Count}  ·  paso {done}/{total}  ·  {Tempos[_tempo].Name}  ·  {(int)minutes}:{(int)(_songTime % 60):00}";
    }

    private int Precision()
    {
        int judged = _onTime + _good + _missed;
        return judged == 0 ? 0 : (int)Math.Round(100.0 * (_onTime + 0.7 * _good) / judged);
    }

    private void Judge(Step step, bool hit, double diff)
    {
        if (step.Left) _left = step.To;
        else _right = step.To;
        var t = Themes.Current;
        var at = _mat.ToGlobal(_mat.TileSurface(step.To)) + Vector3.Up * 0.6f;
        if (!hit)
        {
            _missed++;
            Fx.Popup(this, at, "¡VAMOS!", t.H, t.Bg, 54);
        }
        else if (diff <= OnTime)
        {
            _onTime++;
            Fx.Popup(this, at, "¡A TIEMPO!", t.AccentText, t.Bg, 54);
            Audio.Impact("impact_soft_medium", -12, 1.3f);
        }
        else
        {
            _good++;
            Fx.Popup(this, at, "BIEN", t.V, t.Bg, 54);
        }
        _precisionLabel.Text = $"{Precision()} %";
    }

    private void Finish()
    {
        if (_phase != Phase.Play) return;
        _phase = Phase.Over;
        _music.Stop();
        _mat.ClearAll();
        int precision = Precision();
        bool record = Records.Submit(RecordKey, precision);
        Records.Submit(GameCatalog.All.First(g => g.Id == "workout"), precision);
        int steps = _onTime + _good + _missed;
        _endScore.Text = $"{precision} % DE PRECISIÓN";
        _endRecord.Text = record ? "¡NUEVO RÉCORD!" : $"Mejor precisión: {Records.Get(RecordKey)?.ToString() ?? "—"} %";
        _endRecord.ThemeTypeVariation = record ? "Heading" : "Muted";
        _endStats.Text = $"{(int)(_songTime / 60)}:{(int)(_songTime % 60):00} de ejercicio · {steps} pasos\n" +
                         $"A tiempo {_onTime} · Bien {_good} · Fallados {_missed}";
        Audio.Play(record ? "stars3" : "sparkle");
        ShowPanel(_endPanel);
    }

    // ---------------------------------------------------------------- pisadas

    private void OnPressed(Pad pad)
    {
        _mat.SetPressed(pad, true);
        switch (_phase)
        {
            case Phase.Menu:
                MenuPad(pad);
                break;
            case Phase.Play:
                if (pad == Pad.Select)
                {
                    _phase = Phase.Paused;
                    _music.StreamPaused = true;
                    ShowPanel(_pausePanel);
                    return;
                }
                double now = _songTime - Settings.RhythmOffset;
                var step = _steps.FirstOrDefault(s => !s.Done && s.To == pad && Math.Abs(s.Time - now) <= Late);
                if (step is null) return;
                step.Done = true;
                step.Hit = true;
                Judge(step, true, Math.Abs(step.Time - now));
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _pausePanel.Visible = false;
                    _music.StreamPaused = false;
                    _phase = Phase.Play;
                }
                else if (pad is Pad.Select or Pad.Cross) OpenMenu();
                break;
            case Phase.Over when _endPanel.Visible:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _endPanel.Visible = false;
                    StartSession();
                }
                else if (pad is Pad.Select or Pad.Cross) OpenMenu();
                break;
        }
    }

    private void OnReleased(Pad pad) => _mat.SetPressed(pad, false);
}

/// <summary>
/// Capa 2D sobre la alfombra 3D: las huellas de cada pie donde están y la flecha curva (del color del pie) que va de
/// su casilla a la siguiente. La siguiente flecha se ve más tenue para anticipar el movimiento.
/// </summary>
public partial class FootLayer : Control
{
    public static readonly Color LeftColor = new("#E8413C");
    public static readonly Color RightColor = new("#29A8E0");

    private Camera3D? _camera;
    private Mat3D? _mat;
    private Pad _left, _right;
    private WorkoutGame.Step? _next, _after;
    private double _now;

    public void Setup(Camera3D camera, Mat3D mat)
    {
        _camera = camera;
        _mat = mat;
    }

    public void Update(Pad left, Pad right, WorkoutGame.Step? next, WorkoutGame.Step? after, double now)
    {
        _left = left;
        _right = right;
        _next = next;
        _after = after;
        _now = now;
        QueueRedraw();
    }

    private Vector2 Screen(Pad pad) =>
        _camera!.UnprojectPosition(_mat!.ToGlobal(_mat.TileSurface(pad)));

    public override void _Draw()
    {
        if (_camera is null || _mat is null) return;
        DrawFoot(_left, true);
        DrawFoot(_right, false);
        if (_after is not null) DrawArrow(_after, 0.3f);
        if (_next is not null)
        {
            // La flecha se aviva al acercarse su momento.
            float soon = (float)Math.Clamp(1 - (_next.Time - _now) / 1.2, 0.35, 1);
            DrawArrow(_next, soon);
        }
    }

    private static Texture2D? _leftFoot, _rightFoot;

    /// <summary>Huella de zapatilla (lámina de GPT) del color del pie, algo girada hacia fuera, a la escala de la casilla.</summary>
    private void DrawFoot(Pad pad, bool left)
    {
        var tex = left
            ? _leftFoot ??= GD.Load<Texture2D>("res://assets/ejercicios/huella_izquierda.png")
            : _rightFoot ??= GD.Load<Texture2D>("res://assets/ejercicios/huella_derecha.png");
        var c = Screen(pad);
        // Alto de la huella: algo menos que la distancia en pantalla entre dos casillas de la misma columna.
        float tile = (Screen(Pad.Center) - Screen(Pad.Up)).Length();
        float h = tile * 0.82f, w = h * tex.GetWidth() / tex.GetHeight();
        DrawSetTransform(c, left ? -0.16f : 0.16f, Vector2.One);
        DrawTextureRect(tex, new Rect2(-w / 2 + 4, -h / 2 + 5, w, h), false, new Color(0, 0, 0, 0.35f));
        DrawTextureRect(tex, new Rect2(-w / 2, -h / 2, w, h), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>Flecha curva de la casilla de origen a la de destino, abombada hacia fuera del lado de su pie.</summary>
    private void DrawArrow(WorkoutGame.Step step, float strength)
    {
        var a = Screen(step.From);
        var b = Screen(step.To);
        var color = step.Left ? LeftColor : RightColor;
        var d = b - a;
        float len = d.Length();
        if (len < 1) return;
        var perp = new Vector2(-d.Y, d.X) / len;
        if (step.Left ? perp.X > 0 : perp.X < 0) perp = -perp;
        if (Mathf.Abs(d.X) > Mathf.Abs(d.Y)) perp = new Vector2(0, -1); // movimientos laterales: arco por arriba
        var control = (a + b) / 2 + perp * len * 0.35f;

        const int n = 24;
        var points = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n * 0.86f; // la punta se dibuja aparte
            points[i] = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
        }
        var tip = b;
        var dir = (tip - points[n]).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        var head = new[] { tip, points[n] - side * 22, points[n] + side * 22 };

        var outline = new Color(0, 0, 0, 0.45f * strength);
        DrawPolyline(points, outline, 22, true);
        DrawColoredPolygon(new[] { tip + dir * 4, points[n] - side * 27 - dir * 3, points[n] + side * 27 - dir * 3 }, outline);
        var fill = new Color(color, strength);
        DrawPolyline(points, fill, 14, true);
        DrawColoredPolygon(head, fill);
    }
}

/// <summary>Punto que late con cada pulso de la música.</summary>
public partial class BeatDot : Control
{
    private float _phase;

    public float Phase
    {
        get => _phase;
        set
        {
            _phase = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        float pulse = 1 - Mathf.Clamp(_phase * 3, 0, 1);
        DrawCircle(Size / 2, 14 + pulse * 12, new Color(t.AccentText, 0.25f + pulse * 0.6f));
        DrawCircle(Size / 2, 10, t.AccentText);
    }
}
