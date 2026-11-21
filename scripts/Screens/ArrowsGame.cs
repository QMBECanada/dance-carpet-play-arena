using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Flechas que caen: las figuras caen por sus columnas al ritmo de una canción (Suno) y hay que pisar cuando llegan
/// a su diana. Las notas salen del ritmo medido de cada canción (assets/musica/*.json, Bastidor/scripts/analizar_ritmo.py):
/// golpes en los pulsos con bombo claro, corcheas en las canciones más difíciles y saltos (dos a la vez) de vez en cuando.
/// Modo Normal: ← ↓ ↑ →. Modo Completo: las 8 casillas, agrupadas como en la alfombra.
/// </summary>
public partial class ArrowsGame : Node3D
{
    public const string ScenePath = "res://scenes/Arrows.tscn";

    private sealed record Song(string Id, string Title, string Level, double Lead, float Offbeats, float Doubles, float Thin);

    private static readonly Song[] Songs =
    {
        new("calentamiento", "Calentamiento", "Fácil", 2.0, 0.0f, 0.05f, 0.35f),
        new("latino", "Latino", "Fácil", 2.0, 0.05f, 0.06f, 0.3f),
        new("pop", "Pop", "Fácil", 1.9, 0.08f, 0.08f, 0.25f),
        new("ritmo", "Ritmo", "Media", 1.6, 0.22f, 0.12f, 0.1f),
        new("disco", "Disco", "Media", 1.6, 0.25f, 0.12f, 0.1f),
        new("atope", "A tope", "Difícil", 1.3, 0.42f, 0.18f, 0f),
        new("rock", "Rock", "Difícil", 1.2, 0.35f, 0.2f, 0.05f),
    };

    private static readonly Pad[] NormalLanes = { Pad.Left, Pad.Down, Pad.Up, Pad.Right };
    private static readonly Pad[] FullLanes = { Pad.Cross, Pad.Left, Pad.Triangle, Pad.Down, Pad.Up, Pad.Circle, Pad.Right, Pad.Square };

    // Ventanas de precisión (segundos a cada lado del golpe) y su valor.
    private const double Perfect = 0.05, Good = 0.10, Ok = 0.16;

    public sealed class Note
    {
        public int Lane;
        public double Time;
        public bool Done, Hit;
    }

    private enum Phase { Menu, Calibrate, Play, Paused, Over }
    private enum Judge { Perfect, Good, Ok, Miss }

    private static int _songIndex;
    private static bool _full;

    private Phase _phase = Phase.Menu;
    private Song _song = Songs[0];
    private SongInfo _data = new();
    private Pad[] _lanes = NormalLanes;
    private readonly List<Note> _notes = new();
    private AudioStreamPlayer _music = null!;
    private double _songTime;
    private float _energy = 0.7f;
    private int _score, _combo, _maxCombo;
    private readonly int[] _counts = new int[4];

    private NoteField _field = null!;
    private Label _scoreLabel = null!, _comboLabel = null!, _judgeLabel = null!, _songLabel = null!;
    private TimeBar _energyBar = null!;
    private Control _hud = null!;
    private PanelContainer _menu = null!, _pausePanel = null!, _endPanel = null!, _calibPanel = null!;
    private readonly List<PanelContainer> _menuRows = new();
    private Label _songValue = null!, _modeValue = null!, _menuRecord = null!, _calibText = null!;
    private Label _endGrade = null!, _endScore = null!, _endRecord = null!, _endStats = null!;
    private int _menuRow;
    private ScreenFade _fade = null!;

    // Ajuste de ritmo: golpes de metrónomo y diferencias medidas.
    private const double CalibBpm = 100;
    private const int CalibBeats = 16, CalibSkip = 4;
    private double _calibStart;
    private int _calibTick;
    private readonly List<double> _calibDiffs = new();

    private int Multiplier => Math.Min(1 + _combo / 10, 4);

    public override void _Ready()
    {
        Audio.Music(null);
        AddChild(new StageBackdrop { Focus = new Vector2(0, -4), BackdropPosition = new Vector3(0, 4, -16) });
        var camera = new Camera3D { Fov = 50 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 3, 6), new Vector3(0, 1, -4), Vector3.Up);

        _music = new AudioStreamPlayer { VolumeDb = -4 };
        AddChild(_music);
        _music.Finished += () =>
        {
            if (_phase == Phase.Play) Finish(failed: false);
        };

        BuildUi();
        MatInput.Instance.Pressed += OnPressed;
        MatInput.Instance.Released += OnReleased;
        if (Circuit.Active)
        {
            // En el circuito: canción media, modo normal, sin menú.
            _songIndex = 3; // Ritmo
            _full = false;
            _menu.Visible = false;
            StartSong();
        }
        else OpenMenu();
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

        _field = new NoteField();
        _field.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _field.MouseFilter = Control.MouseFilterEnum.Ignore;
        ui.AddChild(_field);

        _hud = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_hud);

        var scoreBox = new VBoxContainer();
        scoreBox.AddThemeConstantOverride("separation", -6);
        scoreBox.AddChild(Label("PUNTOS", "Muted"));
        _scoreLabel = Label("0", "Huge");
        scoreBox.AddChild(_scoreLabel);
        _comboLabel = Label("", "Accent");
        scoreBox.AddChild(_comboLabel);
        _hud.AddChild(Anchor(scoreBox, Control.LayoutPreset.TopLeft, new Vector2(44, 26)));

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 4);
        _songLabel = Label("", "Heading", HorizontalAlignment.Right);
        right.AddChild(_songLabel);
        right.AddChild(Label("ENERGÍA", "Muted", HorizontalAlignment.Right));
        _energyBar = new TimeBar { CustomMinimumSize = new Vector2(240, 14), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        right.AddChild(_energyBar);
        _hud.AddChild(Anchor(right, Control.LayoutPreset.TopRight, new Vector2(-44, 30)));

        _judgeLabel = Label("", "Huge", HorizontalAlignment.Center);
        _judgeLabel.AddThemeFontSizeOverride("font_size", 64);
        _judgeLabel.CustomMinimumSize = new Vector2(700, 90);
        _judgeLabel.VerticalAlignment = VerticalAlignment.Center;
        _hud.AddChild(Anchor(_judgeLabel, Control.LayoutPreset.Center, new Vector2(0, -40)));
        _hud.AddChild(Anchor(Hints(("SELECT", "Pausa")), Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        // Menú: canción, modo, ajuste de ritmo.
        _menu = new PanelContainer { ThemeTypeVariation = "Card", Visible = false, CustomMinimumSize = new Vector2(700, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(Label("FLECHAS QUE CAEN", "Big", HorizontalAlignment.Center));
        _songValue = MenuRow(box, "Canción");
        _modeValue = MenuRow(box, "Modo");
        MenuRow(box, "Ajustar el ritmo");
        MenuRow(box, "Volver");
        _menuRecord = Label("", "Muted", HorizontalAlignment.Center);
        box.AddChild(_menuRecord);
        var hints = Hints(("↑ ↓", "Moverse"), ("← →", "Cambiar"), ("START", "¡A bailar!"));
        hints.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(hints);
        _menu.AddChild(box);
        ui.AddChild(Anchor(_menu, Control.LayoutPreset.Center, Vector2.Zero));

        _calibPanel = Panel(ui, "AJUSTAR EL RITMO", out var calibBox, ("SELECT", "Cancelar"));
        _calibText = Label("", "Value", HorizontalAlignment.Center);
        _calibText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        calibBox.AddChild(_calibText);
        calibBox.MoveChild(_calibText, 1);

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _endPanel = Panel(ui, "", out var endBox, ("START", "Otra vez"), ("SELECT", "Menú"));
        _endPanel.CustomMinimumSize = new Vector2(640, 0);
        _endGrade = (Label)endBox.GetChild(0);
        _endGrade.AddThemeFontSizeOverride("font_size", 96);
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

    private void RefreshMenu()
    {
        for (int i = 0; i < _menuRows.Count; i++) _menuRows[i].ThemeTypeVariation = i == _menuRow ? "RowOn" : "RowOff";
        var song = Songs[_songIndex];
        _songValue.Text = $"‹  {song.Title} ({song.Level})  ›";
        _modeValue.Text = $"‹  {(_full ? "Completo (8 casillas)" : "Normal (4 flechas)")}  ›";
        int? best = Records.Get(RecordKey(song));
        int ms = (int)Math.Round(Settings.RhythmOffset * 1000);
        _menuRecord.Text = $"Récord: {(best?.ToString() ?? "—")}   ·   Ajuste de ritmo: {ms} ms";
    }

    private string RecordKey(Song song) => $"arrows_{song.Id}_{(_full ? "completo" : "normal")}";

    private void ShowJudge(Judge judge)
    {
        var t = Themes.Current;
        (string text, Color color) = judge switch
        {
            Judge.Perfect => ("¡PERFECTO!", t.AccentText),
            Judge.Good => ("¡BIEN!", t.V),
            Judge.Ok => ("REGULAR", t.Text),
            _ => ("FALLO", t.H),
        };
        _judgeLabel.Text = text;
        _judgeLabel.AddThemeColorOverride("font_color", color);
        _judgeLabel.Modulate = Colors.White;
        Pop(_judgeLabel, 0.7f);
        var tween = _judgeLabel.CreateTween();
        tween.TweenInterval(0.25);
        tween.TweenProperty(_judgeLabel, "modulate:a", 0.0, 0.2);
    }

    // ---------------------------------------------------------------- menú

    private void OpenMenu()
    {
        _phase = Phase.Menu;
        _music.Stop();
        _hud.Visible = false;
        _field.Visible = false;
        _endPanel.Visible = false;
        _calibPanel.Visible = false;
        RefreshMenu();
        ShowPanel(_menu);
    }

    private void MenuPad(Pad pad)
    {
        switch (pad)
        {
            case Pad.Up:
                _menuRow = Mathf.PosMod(_menuRow - 1, _menuRows.Count);
                Audio.Play("click", 1.1f);
                break;
            case Pad.Down:
                _menuRow = Mathf.PosMod(_menuRow + 1, _menuRows.Count);
                Audio.Play("click", 1.1f);
                break;
            case Pad.Left or Pad.Right:
                int step = pad == Pad.Left ? -1 : 1;
                if (_menuRow == 0) _songIndex = Mathf.PosMod(_songIndex + step, Songs.Length);
                else if (_menuRow == 1) _full = !_full;
                Audio.Play("pop", step < 0 ? 0.95f : 1.05f);
                break;
            case Pad.Start or Pad.Circle:
                if (_menuRow == 2) StartCalibration();
                else if (_menuRow == 3) _fade.GoTo(HubScreen.ScenePath);
                else StartSong();
                return;
            case Pad.Select or Pad.Cross:
                _fade.GoTo(HubScreen.ScenePath);
                return;
        }
        RefreshMenu();
    }

    // ---------------------------------------------------------------- partida

    private void StartSong()
    {
        _song = Songs[_songIndex];
        _lanes = _full ? FullLanes : NormalLanes;
        _data = SongInfo.Load(_song.Id);
        BuildChart();

        _score = _combo = _maxCombo = 0;
        Array.Clear(_counts);
        _energy = 0.7f;
        _scoreLabel.Text = "0";
        _comboLabel.Text = "";
        _songLabel.Text = $"{_song.Title.ToUpperInvariant()} · {(_full ? "COMPLETO" : "NORMAL")}";
        _energyBar.Value = _energy;

        _menu.Visible = false;
        _hud.Visible = true;
        _field.Visible = true;
        _field.Setup(_lanes, _notes, _song.Lead, _full);
        _songTime = 0;
        _music.Stream = GD.Load<AudioStream>($"res://assets/musica/{_song.Id}.mp3");
        _music.Play();
        _phase = Phase.Play;
        Audio.Play("start");
    }

    /// <summary>
    /// Coloca las notas sobre la rejilla medida: pulsos con golpe claro (en la fácil se aligeran), corcheas fuertes según
    /// la canción y saltos al principio de algunos compases. La semilla es fija por canción y modo: siempre igual.
    /// </summary>
    private void BuildChart()
    {
        _notes.Clear();
        var rng = new Random(StableHash(_song.Id + (_full ? "C" : "N")));
        double half = _data.Beat / 2;
        var beats = _data.Eighths.Where((_, i) => i % 2 == 0).OrderBy(v => v).ToArray();
        float threshold = beats.Length > 0 ? beats[beats.Length / 2] * 0.6f : 0;
        int lanes = _lanes.Length, last = -1, beforeLast = -1;

        int PickLane(int avoid)
        {
            int lane;
            do lane = rng.Next(lanes);
            while (lane == avoid || (lane == last && lane == beforeLast));
            return lane;
        }

        for (int i = 0; i < _data.Eighths.Length; i++)
        {
            double time = _data.BeatTime(i / 2.0);
            int beat = i / 2;
            bool onBeat = i % 2 == 0;
            if (beat < 8) continue;                    // la entrada de batería deja tiempo para prepararse
            if (time > _data.Duration - 2.5) break;
            float strength = _data.Eighths[i];
            if (onBeat)
            {
                if (strength < threshold * 0.8f) continue;          // puentes sin bombo: menos flechas
                if (beat % 2 == 1 && rng.NextDouble() < _song.Thin) continue;
            }
            else if (strength < threshold || rng.NextDouble() >= _song.Offbeats) continue;

            int lane = PickLane(onBeat ? -1 : last);
            _notes.Add(new Note { Lane = lane, Time = time });
            beforeLast = last;
            last = lane;
            if (onBeat && beat % 4 == 0 && rng.NextDouble() < _song.Doubles)
                _notes.Add(new Note { Lane = PickLane(lane), Time = time });
        }
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in text) h = (h ^ c) * 16777619;
            return (int)(h & 0x7FFFFFFF);
        }
    }

    public override void _Process(double delta)
    {
        if (_phase == Phase.Calibrate)
        {
            CalibrationTick();
            return;
        }
        if (_phase != Phase.Play) return;

        // Tiempo de la canción: el reloj propio sigue al reproductor (compensando la latencia de audio) sin saltos.
        _songTime = SongInfo.Advance(_songTime, delta, _music);
        double judgeTime = _songTime - Settings.RhythmOffset;

        foreach (var note in _notes)
        {
            if (note.Done || note.Time > judgeTime - Ok) continue;
            note.Done = true;
            Register(Judge.Miss, note.Lane);
            if (_phase != Phase.Play) return;
        }
        _field.SongTime = _songTime;
        _field.QueueRedraw();
    }

    private void Hit(Pad pad)
    {
        int lane = Array.IndexOf(_lanes, pad);
        if (lane < 0) return;
        _field.Press(lane, true);
        double judgeTime = _songTime - Settings.RhythmOffset;
        Note? best = null;
        double bestDiff = double.MaxValue;
        foreach (var note in _notes)
        {
            if (note.Done || note.Lane != lane) continue;
            double diff = Math.Abs(note.Time - judgeTime);
            if (diff < bestDiff)
            {
                best = note;
                bestDiff = diff;
            }
            if (note.Time > judgeTime + Ok) break;
        }
        if (best is null || bestDiff > Ok) return;   // pisar sin flecha cerca no penaliza

        best.Done = true;
        best.Hit = true;
        Register(bestDiff <= Perfect ? Judge.Perfect : bestDiff <= Good ? Judge.Good : Judge.Ok, lane);
    }

    private void Register(Judge judge, int lane)
    {
        _counts[(int)judge]++;
        ShowJudge(judge);
        if (judge == Judge.Miss)
        {
            _combo = 0;
            Audio.Impact("impact_soft_medium", -16, 0.8f);
            // Unos 14 fallos seguidos (17 en la fácil) vacían la barra; los aciertos la recargan.
            _energy -= _song.Id == "calentamiento" ? 0.04f : 0.05f;
        }
        else
        {
            _combo++;
            _maxCombo = Math.Max(_maxCombo, _combo);
            _score += (judge == Judge.Perfect ? 300 : judge == Judge.Good ? 200 : 100) * Multiplier;
            _energy += judge == Judge.Perfect ? 0.03f : 0.02f;
            _field.Flash(lane);
            if (judge == Judge.Perfect) Audio.Impact("impact_glass_light", -18, 1.3f);
            else Audio.Impact("impact_generic_light", -18, 1.2f);
        }
        _energy = Mathf.Clamp(_energy, 0, 1);
        _energyBar.Value = _energy;
        _energyBar.Hurry = _energy < 0.25f;
        _scoreLabel.Text = _score.ToString();
        _comboLabel.Text = _combo >= 5 ? $"{_combo} SEGUIDAS  ·  ×{Multiplier}" : "";
        if (_energy <= 0) Finish(failed: true);
    }

    private void Finish(bool failed)
    {
        _phase = Phase.Over;
        _music.Stop();
        int total = _notes.Count;
        double accuracy = total == 0 ? 0 : (_counts[0] * 3 + _counts[1] * 2 + _counts[2]) / (3.0 * total);
        string grade = failed ? "—" : accuracy >= 0.95 ? "S" : accuracy >= 0.85 ? "A" : accuracy >= 0.70 ? "B" : "C";
        bool record = !failed && Records.Submit(RecordKey(_song), _score);
        if (!failed) Records.Submit(GameCatalog.All.First(g => g.Id == "arrows"), _score);

        _endGrade.Text = failed ? "¡SIN ENERGÍA!" : $"NOTA {grade}";
        _endScore.Text = $"{_score} PUNTOS";
        _endRecord.Text = record ? "¡NUEVO RÉCORD!" : $"Récord: {Records.Get(RecordKey(_song))?.ToString() ?? "—"}";
        _endRecord.ThemeTypeVariation = record ? "Heading" : "Muted";
        _endStats.Text = $"Perfectos {_counts[0]} · Bien {_counts[1]} · Regular {_counts[2]} · Fallos {_counts[3]}\n" +
                         $"Racha máxima {_maxCombo} · Precisión {accuracy:P0}";
        Audio.Play(failed ? "miss" : record ? "stars3" : "sparkle");
        GetTree().CreateTimer(0.6).Timeout += () => ShowPanel(_endPanel);
    }

    // ---------------------------------------------------------------- ajuste de ritmo

    private void StartCalibration()
    {
        _phase = Phase.Calibrate;
        _menu.Visible = false;
        _calibDiffs.Clear();
        _calibTick = 0;
        _calibStart = Time.GetTicksUsec() / 1e6 + 1.0;
        _calibText.Text = "Escucha el metrónomo y pisa cualquier flecha justo en cada golpe.\nLos 4 primeros son para coger el ritmo.";
        ShowPanel(_calibPanel);
    }

    private double CalibBeatTime(int i) => _calibStart + i * 60.0 / CalibBpm;

    private void CalibrationTick()
    {
        double now = Time.GetTicksUsec() / 1e6;
        if (_calibTick < CalibBeats && now >= CalibBeatTime(_calibTick))
        {
            Audio.Play("click", _calibTick % 4 == 0 ? 1.3f : 1f);
            _calibTick++;
        }
        if (_calibTick >= CalibBeats && now > CalibBeatTime(CalibBeats - 1) + 0.6) EndCalibration();
    }

    private void CalibrationPress()
    {
        double now = Time.GetTicksUsec() / 1e6;
        int nearest = (int)Math.Round((now - _calibStart) * CalibBpm / 60.0);
        if (nearest < CalibSkip || nearest >= CalibBeats) return;
        _calibDiffs.Add(now - CalibBeatTime(nearest));
        _calibText.Text = $"Golpe {nearest - CalibSkip + 1} de {CalibBeats - CalibSkip}…";
    }

    private void EndCalibration()
    {
        if (_calibDiffs.Count >= 6)
        {
            var sorted = _calibDiffs.OrderBy(d => d).ToList();
            Settings.RhythmOffset = Math.Clamp(sorted[sorted.Count / 2], -0.2, 0.3);
            Settings.Save();
            Audio.Play("sparkle");
        }
        else Audio.Play("miss");
        _calibPanel.Visible = false;
        OpenMenu();
    }

    // ---------------------------------------------------------------- pisadas

    private void OnPressed(Pad pad)
    {
        switch (_phase)
        {
            case Phase.Menu:
                MenuPad(pad);
                break;
            case Phase.Calibrate:
                if (pad == Pad.Select) EndCalibration();
                else if (pad is not (Pad.Start or Pad.Center)) CalibrationPress();
                break;
            case Phase.Play:
                if (pad == Pad.Select)
                {
                    _phase = Phase.Paused;
                    _music.StreamPaused = true;
                    ShowPanel(_pausePanel);
                }
                else Hit(pad);
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _pausePanel.Visible = false;
                    _music.StreamPaused = false;
                    _phase = Phase.Play;
                }
                else if (pad is Pad.Select or Pad.Cross)
                {
                    _pausePanel.Visible = false;
                    OpenMenu();
                }
                break;
            case Phase.Over when _endPanel.Visible:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _endPanel.Visible = false;
                    StartSong();
                }
                else if (pad is Pad.Select or Pad.Cross) OpenMenu();
                break;
        }
    }

    private void OnReleased(Pad pad)
    {
        int lane = Array.IndexOf(_lanes, pad);
        if (lane >= 0) _field.Press(lane, false);
    }
}

/// <summary>
/// Las columnas: dianas abajo y figuras cayendo hacia ellas. Las flechas son un mismo dibujo girado; las de ↑↓ azules,
/// las de ←→ rojas y las figuras ✕ ○ △ □ crema, como en la alfombra.
/// </summary>
public partial class NoteField : Control
{
    private Pad[] _lanes = Array.Empty<Pad>();
    private List<ArrowsGame.Note> _notes = new();
    private double _lead = 1.6;
    private bool _full;
    private bool[] _down = Array.Empty<bool>();
    private float[] _flash = Array.Empty<float>();

    public double SongTime { get; set; }

    private static readonly Dictionary<string, Texture2D> Cache = new();

    private static Texture2D Tex(string name)
    {
        if (!Cache.TryGetValue(name, out var tex))
        {
            tex = GD.Load<Texture2D>($"res://assets/flechas/{name}.png");
            Cache[name] = tex;
        }
        return tex;
    }

    public void Setup(Pad[] lanes, List<ArrowsGame.Note> notes, double lead, bool full)
    {
        _lanes = lanes;
        _notes = notes;
        _lead = lead;
        _full = full;
        _down = new bool[lanes.Length];
        _flash = new float[lanes.Length];
    }

    public void Press(int lane, bool down)
    {
        if (lane < _down.Length) _down[lane] = down;
    }

    public void Flash(int lane) => _flash[lane] = 1;

    private float LaneSize => _full ? 92 : 120;

    /// <summary>Centro horizontal de cada columna; en modo completo se separan los grupos izquierda, centro y derecha.</summary>
    private float LaneX(int lane)
    {
        float size = LaneSize, gap = _full ? 34 : 0;
        int group = !_full ? 0 : lane < 3 ? 0 : lane < 5 ? 1 : 2;
        float total = _lanes.Length * size + (_full ? 2 * gap : 0);
        return Size.X / 2 - total / 2 + size * (lane + 0.5f) + group * gap;
    }

    private float TargetY => Size.Y * 0.82f;
    private float TopY => -LaneSize;

    private static (string Note, string Target, float Angle) Look(Pad pad) => pad switch
    {
        Pad.Up => ("flecha_azul", "diana_flecha", 0),
        Pad.Down => ("flecha_azul", "diana_flecha", Mathf.Pi),
        Pad.Left => ("flecha_roja", "diana_flecha", -Mathf.Pi / 2),
        Pad.Right => ("flecha_roja", "diana_flecha", Mathf.Pi / 2),
        Pad.Cross => ("equis", "diana_equis", 0),
        Pad.Circle => ("circulo", "diana_circulo", 0),
        Pad.Triangle => ("triangulo", "diana_triangulo", 0),
        _ => ("cuadrado", "diana_cuadrado", 0),
    };

    public override void _Process(double delta)
    {
        for (int i = 0; i < _flash.Length; i++) _flash[i] = Mathf.Max(0, _flash[i] - (float)delta * 5);
    }

    public override void _Draw()
    {
        if (_lanes.Length == 0) return;
        var t = Themes.Current;
        float size = LaneSize;

        // Fondo de las columnas.
        float left = LaneX(0) - size / 2 - 12, rightX = LaneX(_lanes.Length - 1) + size / 2 + 12;
        var bg = new StyleBoxFlat { BgColor = new Color(t.Bg.Darkened(0.2f), 0.72f) };
        bg.SetCornerRadiusAll(18);
        DrawStyleBox(bg, new Rect2(left, 0, rightX - left, Size.Y));
        for (int i = 0; i < _lanes.Length; i++)
        {
            if (_down[i])
                DrawRect(new Rect2(LaneX(i) - size / 2, 0, size, Size.Y), new Color(t.Accent, 0.08f));
        }

        // Dianas.
        for (int i = 0; i < _lanes.Length; i++)
        {
            var (_, target, angle) = Look(_lanes[i]);
            float scale = _down[i] ? 1.08f : 1f;
            var color = _down[i] ? Colors.White * 1.6f : Colors.White;
            DrawPart(target, new Vector2(LaneX(i), TargetY), size * 0.86f * scale, angle, color);
        }

        // Notas: de las más lejanas a las más cercanas.
        float travel = TargetY - TopY;
        for (int n = _notes.Count - 1; n >= 0; n--)
        {
            var note = _notes[n];
            if (note.Hit) continue;
            double ahead = note.Time - SongTime;
            if (ahead > _lead || ahead < -0.4) continue;
            float y = TargetY - (float)(ahead / _lead) * travel;
            var (tex, _, angle) = Look(_lanes[note.Lane]);
            var color = note.Done ? new Color(1, 1, 1, 0.3f) : Colors.White;
            DrawPart(tex, new Vector2(LaneX(note.Lane), y), size * 0.82f, angle, color);
        }

        // Destellos de acierto.
        for (int i = 0; i < _lanes.Length; i++)
        {
            if (_flash[i] <= 0) continue;
            DrawPart("destello", new Vector2(LaneX(i), TargetY), size * (1.2f + (1 - _flash[i]) * 0.6f), 0,
                new Color(1, 1, 1, _flash[i]));
        }
    }

    private void DrawPart(string name, Vector2 center, float width, float angle, Color modulate)
    {
        var tex = Tex(name);
        float height = width * tex.GetHeight() / tex.GetWidth();
        DrawSetTransform(center, angle, Vector2.One);
        DrawTextureRect(tex, new Rect2(-width / 2, -height / 2, width, height), false, modulate);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
