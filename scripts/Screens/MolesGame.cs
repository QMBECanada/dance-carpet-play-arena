using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Topos: durante 60 s asoman topos por las 8 casillas exteriores; hay que pisarlos antes de que se escondan.
/// Cada vez salen más rápido y a veces de dos en dos. Las bombas restan. Los aciertos seguidos multiplican los puntos.
/// El centro es la casilla de descanso: ahí nunca sale nada.
/// </summary>
public partial class MolesGame : Node3D
{
    public const string ScenePath = "res://scenes/Moles.tscn";

    private const double Duration = 60;
    private const double RiseTime = 0.18, HideTime = 0.22, HitTime = 0.42;
    private const int BasePoints = 100, QuickBonus = 50;
    private const int MaxOut = 3; // figuras a la vista a la vez (topillos y bombas)
    private const int MissPenalty = 50;
    private const double StunTime = 3;

    private static readonly Pad[] Holes =
    {
        Pad.Cross, Pad.Up, Pad.Circle, Pad.Left, Pad.Right, Pad.Triangle, Pad.Down, Pad.Square,
    };

    private enum Phase { Countdown, Playing, Paused, Over }

    private readonly GameInfo _game = GameCatalog.All.First(g => g.Id == "moles");
    private readonly Dictionary<Pad, Critter> _occupants = new();
    private readonly Random _rng = new();

    private Phase _phase = Phase.Countdown;
    private double _elapsed, _spawnTimer, _clock, _shake, _stun;
    private int _score, _combo, _bestCombo, _hits, _escaped, _bombs, _misses;
    private Pad? _lastSpawn;

    private Mat3D _mat = null!;
    private Camera3D _camera = null!;
    private Vector3 _cameraHome;
    private Label _scoreLabel = null!, _timeLabel = null!, _bigText = null!, _multiplier = null!;
    private PanelContainer _multChip = null!;
    private TimeBar _timeBar = null!;
    private PanelContainer _pausePanel = null!, _endPanel = null!;
    private Label _endScore = null!, _endRecord = null!, _endStats = null!;
    private ScreenFade _fade = null!;
    private ColorRect _stunTint = null!;
    private VBoxContainer _stunBox = null!;
    private Label _stunCount = null!;

    private int Multiplier => Math.Min(1 + _combo / 5, 4);
    private double Progress => Math.Clamp(_elapsed / Duration, 0, 1);

    public override void _Ready()
    {
        Audio.Music("arcade");
        AddChild(new StageBackdrop { Focus = new Vector2(0, -0.2f), BackdropPosition = new Vector3(0, 5, -16) });
        _camera = new Camera3D { Fov = 46 };
        AddChild(_camera);
        _camera.LookAtFromPosition(new Vector3(0, 5.3f, 5.6f), new Vector3(0, 0.25f, -0.55f), Vector3.Up);
        _cameraHome = _camera.Position;

        _mat = new Mat3D();
        AddChild(_mat);

        BuildUi();
        MatInput.Instance.Pressed += OnPressed;
        MatInput.Instance.Released += OnReleased;
        StartCountdown();

        // Solo desarrollo: «-- --test-stun» provoca un aturdimiento a los 4 s para verlo sin pisar una bomba.
        if (OS.GetCmdlineUserArgs().Contains("--test-stun")) GetTree().CreateTimer(4).Timeout += StartStun;
    }

    public override void _ExitTree()
    {
        MatInput.Instance.Pressed -= OnPressed;
        MatInput.Instance.Released -= OnReleased;
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Ui.Root(this);

        var scoreBox = new VBoxContainer();
        scoreBox.AddThemeConstantOverride("separation", -6);
        scoreBox.AddChild(Ui.Label("PUNTOS", "Muted"));
        var scoreLine = new HBoxContainer();
        scoreLine.AddThemeConstantOverride("separation", 16);
        _scoreLabel = Ui.Label("0", "Huge");
        _multChip = new PanelContainer { ThemeTypeVariation = "RowOn", Visible = false, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        _multiplier = Ui.Label("×2", "Accent");
        _multChip.AddChild(_multiplier);
        scoreLine.AddChild(_scoreLabel);
        scoreLine.AddChild(_multChip);
        scoreBox.AddChild(scoreLine);
        ui.AddChild(Ui.Anchor(scoreBox, Control.LayoutPreset.TopLeft, new Vector2(44, 26)));

        var timeBox = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        timeBox.AddThemeConstantOverride("separation", 6);
        _timeLabel = Ui.Label("1:00", "Big", HorizontalAlignment.Center);
        _timeBar = new TimeBar { CustomMinimumSize = new Vector2(440, 14) };
        timeBox.AddChild(_timeLabel);
        timeBox.AddChild(_timeBar);
        ui.AddChild(Ui.Anchor(timeBox, Control.LayoutPreset.CenterTop, new Vector2(0, 30)));

        var recordBox = new VBoxContainer();
        recordBox.AddThemeConstantOverride("separation", -6);
        var recordTitle = Ui.Label("RÉCORD", "Muted", HorizontalAlignment.Right);
        int? best = Records.Get(_game.Id);
        var recordValue = Ui.Label(best?.ToString() ?? "—", "Big", HorizontalAlignment.Right);
        recordBox.AddChild(recordTitle);
        recordBox.AddChild(recordValue);
        ui.AddChild(Ui.Anchor(recordBox, Control.LayoutPreset.TopRight, new Vector2(-44, 26)));

        ui.AddChild(Ui.Anchor(Ui.Hints(("SELECT", "Pausa")), Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        _bigText = Ui.Label("", "Huge", HorizontalAlignment.Center);
        _bigText.AddThemeFontSizeOverride("font_size", 130);
        _bigText.CustomMinimumSize = new Vector2(900, 180);
        _bigText.VerticalAlignment = VerticalAlignment.Center;
        ui.AddChild(Ui.Anchor(_bigText, Control.LayoutPreset.Center, new Vector2(0, -40)));

        // Aturdimiento por bomba: velo rojizo y cuenta atrás; mientras dura no responde ninguna casilla.
        _stunTint = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _stunTint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_stunTint);
        _stunBox = new VBoxContainer { Visible = false, CustomMinimumSize = new Vector2(700, 0) };
        _stunBox.AddThemeConstantOverride("separation", -10);
        _stunBox.AddChild(Ui.Label("¡ATURDIDO!", "Huge", HorizontalAlignment.Center));
        _stunCount = Ui.Label("3", "HugeAccent", HorizontalAlignment.Center);
        _stunCount.AddThemeFontSizeOverride("font_size", 120);
        _stunBox.AddChild(_stunCount);
        ui.AddChild(Ui.Anchor(_stunBox, Control.LayoutPreset.Center, new Vector2(0, -60)));

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _endPanel = Panel(ui, "¡SE ACABÓ!", out var endBox, ("START", "Otra vez"), ("SELECT", "Volver"));
        _endScore = Ui.Label("", "HugeAccent", HorizontalAlignment.Center);
        _endRecord = Ui.Label("", "Heading", HorizontalAlignment.Center);
        _endStats = Ui.Label("", "Muted", HorizontalAlignment.Center);
        endBox.AddChild(_endScore);
        endBox.MoveChild(_endScore, 1);
        endBox.AddChild(_endRecord);
        endBox.MoveChild(_endRecord, 2);
        endBox.AddChild(_endStats);
        endBox.MoveChild(_endStats, 3);

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    private void Big(string text, Color color, double hold = 0.55)
     => FlashText(_bigText, text, color, hold);

    // ---------------------------------------------------------------- partida

    private void StartCountdown()
    {
        _phase = Phase.Countdown;
        var tween = CreateTween();
        var theme = Themes.Current;
        foreach (string n in new[] { "3", "2", "1" })
        {
            tween.TweenCallback(Callable.From(() =>
            {
                Big(n, theme.Text, 0.35);
                Audio.Play("click", 0.8f + (3 - int.Parse(n)) * 0.12f);
            }));
            tween.TweenInterval(0.7);
        }
        tween.TweenCallback(Callable.From(() =>
        {
            Big("¡YA!", theme.AccentText, 0.4);
            Audio.Play("start");
            _phase = Phase.Playing;
            _spawnTimer = 0.3;
        }));
    }

    private LoopSound _fuse = null!;

    public override void _Process(double delta)
    {
        _clock += delta;
        UpdateCamera(delta);
        // Mecha chisporroteando mientras haya una bomba fuera.
        _fuse ??= AddLoop("mecha", -14);
        bool bombOut = _phase == Phase.Playing && _occupants.Values.Any(c => c.IsBomb && c.Phase is Critter.State.Rising or Critter.State.Up);
        _fuse.Set(bombOut ? 1 : 0);

        if (_phase == Phase.Paused) return;

        if (_phase == Phase.Playing)
        {
            _elapsed += delta;
            UpdateStun(delta);
            _spawnTimer -= delta;
            if (_spawnTimer <= 0 && _elapsed < Duration - 0.6) SpawnWave();
            if (_elapsed >= Duration) Finish();
        }

        foreach (var critter in _occupants.Values.ToList()) UpdateCritter(critter, delta);

        double left = Math.Max(0, Duration - _elapsed);
        int secs = (int)Math.Ceiling(left);
        _timeLabel.Text = $"{secs / 60}:{secs % 60:00}";
        _timeBar.Value = (float)(left / Duration);
        _timeBar.Hurry = left <= 10 && _phase == Phase.Playing;
        _scoreLabel.Text = _score.ToString();
    }

    private void SpawnWave()
    {
        double p = Progress;
        Spawn();
        if (p > 0.2 && _rng.NextDouble() < 0.1 + 0.35 * p) Spawn();
        _spawnTimer = Mathf.Lerp(1.05, 0.45, p) * (0.85 + _rng.NextDouble() * 0.3);
    }

    private void Spawn()
    {
        // Cuenta todo lo que se ve, también los que se esconden o acaban de recibir el golpe.
        if (_occupants.Count >= MaxOut) return;
        var free = Holes.Where(h => !_occupants.ContainsKey(h) && h != _lastSpawn).ToArray();
        if (free.Length == 0) return;
        var pad = free[_rng.Next(free.Length)];
        bool bomb = _elapsed > 8 && _rng.NextDouble() < 0.13;
        // Topillo dorado: raro, muy rápido y vale el quíntuple.
        bool golden = !bomb && _elapsed > 5 && _rng.NextDouble() < 0.07;
        double p = Progress;
        var critter = new Critter
        {
            Pad = pad,
            Type = bomb ? Critter.Kind.Bomb : golden ? Critter.Kind.Golden : Critter.Kind.Mole,
            UpTime = bomb ? 1.7 : golden ? 0.75 : Mathf.Lerp(1.5, 0.8, p),
            Position = _mat.TileSurface(pad),
        };
        _mat.AddChild(critter);
        Audio.Impact("impact_soft_medium", -16, 1.4f); // tierra al asomar
        Fx.Picture(_mat, critter.Position + new Vector3(0, 0.26f, 0.2f), "tierra", 0.7f, 0.45);
        if (golden)
        {
            Audio.Play("sparkle", 1.3f);
            Audio.Impact("impact_glass_light", -6, 1.5f);
        }
        _occupants[pad] = critter;
        _lastSpawn = pad;
    }

    private LoopSound AddLoop(string synth, float db)
    {
        var loop = new LoopSound(synth, db);
        AddChild(loop);
        return loop;
    }

    private void UpdateCritter(Critter c, double delta)
    {
        c.T += delta;
        c.Idle(_clock);
        switch (c.Phase)
        {
            case Critter.State.Rising:
                c.SetLift(EaseOutBack((float)(c.T / RiseTime)));
                if (c.T >= RiseTime) Advance(c, Critter.State.Up);
                break;
            case Critter.State.Up:
                c.SetLift(1);
                if (c.T >= c.UpTime)
                {
                    Advance(c, Critter.State.Hiding);
                    if (!c.IsBomb) Escaped(c);
                }
                break;
            case Critter.State.Hiding:
                c.SetLift(1 - (float)(c.T / HideTime));
                if (c.T >= HideTime) Remove(c);
                break;
            case Critter.State.Hit:
                float t = (float)c.T;
                c.SetSquash(Mathf.Lerp(1f, 0.65f, Mathf.Min(t / 0.1f, 1)));
                c.SetLift(1 - Mathf.Clamp((t - 0.12f) / 0.3f, 0, 1));
                if (c.T >= HitTime) Remove(c);
                break;
        }
    }

    private static void Advance(Critter c, Critter.State state)
    {
        c.Phase = state;
        c.T = 0;
    }

    private void Remove(Critter c)
    {
        _occupants.Remove(c.Pad);
        c.Vanish();
    }

    private void Escaped(Critter c)
    {
        _escaped++;
        if (_combo >= 5) Audio.Play("miss");
        _combo = 0;
        UpdateMultiplier();
        var t = Themes.Current;
        Fx.Popup(_mat, c.Position + Vector3.Up * 0.8f, "¡SE FUE!", new Color(t.Text, 0.75f), t.Bg, 64);
    }

    private void OnPressed(Pad pad)
    {
        _mat.SetPressed(pad, true);
        switch (_phase)
        {
            case Phase.Playing:
                if (_stun > 0) return; // aturdido: ninguna casilla responde, ni siquiera la pausa
                if (pad == Pad.Select) Pause();
                else Whack(pad);
                break;
            case Phase.Countdown:
                if (pad == Pad.Select) _fade.GoTo(HubScreen.ScenePath);
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

    private void OnReleased(Pad pad) => _mat.SetPressed(pad, false);

    private void Whack(Pad pad)
    {
        // Pisar un agujero vacío resta puntos y corta la racha (el centro es descanso y no cuenta).
        if (!Holes.Contains(pad)) return;
        if (!_occupants.TryGetValue(pad, out var c))
        {
            MissedStep(pad);
            return;
        }
        bool hittable = c.Phase is Critter.State.Rising or Critter.State.Up || (c.Phase == Critter.State.Hiding && c.T < 0.08);
        if (!hittable)
        {
            MissedStep(pad);
            return;
        }

        var theme = Themes.Current;
        var top = c.Position + Vector3.Up * 0.45f;
        if (c.IsBomb)
        {
            _combo = 0;
            _bombs++;
            Audio.Synth("explosion", -1);
            Fx.Picture(_mat, top, "explosion", 2.4f, 0.8, 0.3f);
            Fx.Picture(_mat, top, "golpe", 1.4f, 0.35, 0.5f);
            Fx.Burst(_mat, top, new Color(1, 0.55f, 0.15f), 24, 5f);
            Fx.Flash(_mat, c.Position, new Color(1, 0.45f, 0.1f));
            Fx.Popup(_mat, top + Vector3.Up * 0.5f, "¡BOOM!", theme.H, theme.Bg, 130);
            UpdateMultiplier();
            StartStun();
        }
        else
        {
            bool quick = c.Phase == Critter.State.Rising || c.T < 0.35;
            int before = Multiplier;
            _combo++;
            _bestCombo = Math.Max(_bestCombo, _combo);
            _hits++;
            int points = (BasePoints * Multiplier + (quick ? QuickBonus : 0)) * (c.IsGolden ? 5 : 1);
            _score += points;
            Audio.Impact("impact_punch_heavy", -2, 1f + Math.Min(_combo, 12) * 0.03f); // martillazo
            Audio.Play("pop", 1.5f + Math.Min(_combo, 12) * 0.04f);                   // y el «¡ay!» del topillo
            Fx.Picture(_mat, top, "golpe", 1.1f, 0.3, 0.5f);
            Fx.Burst(_mat, top, c.IsGolden ? new Color("#FFD23F") : theme.Accent, c.IsGolden ? 30 : 16);
            Fx.Popup(_mat, top + Vector3.Up * 0.5f, $"+{points}", theme.Accent, theme.Bg);
            if (c.IsGolden)
            {
                Big("¡DORADO!", new Color("#FFC21A"), 0.45);
                Audio.Play("stars3");
            }
            if (quick) Fx.Popup(_mat, top + new Vector3(0, 0.15f, 0.3f), "¡RÁPIDO!", theme.V, theme.Bg, 56);
            if (Multiplier > before)
            {
                Audio.Play("combo");
                Big($"×{Multiplier}", theme.AccentText, 0.4);
            }
            UpdateMultiplier();
        }
        Advance(c, Critter.State.Hit);
    }

    private void MissedStep(Pad pad)
    {
        _misses++;
        _score = Math.Max(0, _score - MissPenalty);
        _combo = 0;
        UpdateMultiplier();
        Audio.Play("miss", 1.3f);
        var t = Themes.Current;
        Fx.Popup(_mat, _mat.TileSurface(pad) + Vector3.Up * 0.5f, $"−{MissPenalty}", t.H, t.Bg, 70);
    }

    private void UpdateMultiplier()
    {
        bool show = Multiplier > 1;
        if (show && (!_multChip.Visible || _multiplier.Text != $"×{Multiplier}")) Pop(_multChip, 0.6f);
        _multChip.Visible = show;
        _multiplier.Text = $"×{Multiplier}";
    }

    private void StartStun()
    {
        // Los demás topillos huyen asustados de la explosión.
        foreach (var other in _occupants.Values)
            if (!other.IsBomb && other.Phase is Critter.State.Rising or Critter.State.Up)
            {
                other.Scared = true;
                Advance(other, Critter.State.Hiding);
            }
        _stun = StunTime;
        _shake = 0.7;
        _stunTint.Visible = true;
        _stunBox.Visible = true;
        Pop(_stunBox, 0.5f);
        foreach (var pad in PadInfo.CalibrationOrder) _mat.SetDim(pad, 0.3f);
    }

    private void UpdateStun(double delta)
    {
        if (_stun <= 0) return;
        _stun -= delta;
        var t = Themes.Current;
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_clock * 9);
        _stunTint.Color = new Color(t.H, (0.16f + 0.1f * pulse) * Mathf.Min(1, (float)_stun * 2));
        string count = Math.Max(1, (int)Math.Ceiling(_stun)).ToString();
        if (_stunCount.Text != count)
        {
            _stunCount.Text = count;
            Pop(_stunCount, 0.6f);
            Audio.Play("click", 0.7f);
        }
        if (_stun <= 0) EndStun(announce: true);
    }

    private void EndStun(bool announce)
    {
        _stun = 0;
        _stunTint.Visible = false;
        _stunBox.Visible = false;
        _stunCount.Text = "3";
        foreach (var pad in PadInfo.CalibrationOrder) _mat.SetDim(pad, 1);
        if (!announce) return;
        Big("¡SIGUE!", Themes.Current.AccentText, 0.3);
        Audio.Play("start", 1.15f);
    }

    private void Pause()
    {
        _phase = Phase.Paused;
        Audio.Play("pop");
        ShowPanel(_pausePanel);
    }

    private void Resume()
    {
        _pausePanel.Visible = false;
        Audio.Play("click");
        _phase = Phase.Playing;
    }

    private void Finish()
    {
        _phase = Phase.Over;
        EndStun(announce: false);
        foreach (var c in _occupants.Values)
            if (c.Phase is Critter.State.Rising or Critter.State.Up)
            {
                c.Scared = true;
                Advance(c, Critter.State.Hiding);
            }

        bool record = Records.Submit(_game, _score);
        Audio.Play("bonk", 1.3f);
        Big("¡TIEMPO!", Themes.Current.AccentText, 0.6);

        GetTree().CreateTimer(1.1).Timeout += () =>
        {
            _endScore.Text = $"{_score} PUNTOS";
            _endRecord.Text = record ? "¡NUEVO RÉCORD!" : $"Récord: {Records.Get(_game.Id)}";
            _endRecord.ThemeTypeVariation = record ? "Heading" : "Muted";
            _endStats.Text = $"Topos: {_hits}   ·   Se escaparon: {_escaped}   ·   Fallos: {_misses}   ·   Bombas: {_bombs}   ·   Mejor racha: {_bestCombo}";
            ShowPanel(_endPanel);
            Audio.Play(record ? "stars3" : "sparkle");
            if (record) Fx.Burst(_mat, new Vector3(0, 1.5f, 0), Themes.Current.Accent, 60, 6);
        };
    }

    // ---------------------------------------------------------------- efectos

    private void UpdateCamera(double delta)
    {
        if (_shake <= 0)
        {
            _camera.Position = _cameraHome;
            return;
        }
        _shake = Math.Max(0, _shake - delta);
        float amount = (float)_shake * 0.35f;
        _camera.Position = _cameraHome + new Vector3((GD.Randf() - 0.5f) * amount, (GD.Randf() - 0.5f) * amount, 0);
    }

    private static float EaseOutBack(float x)
    {
        x = Mathf.Clamp(x, 0, 1);
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2);
    }
}

/// <summary>Barra del tiempo restante; en los últimos segundos cambia de color y late.</summary>
public partial class TimeBar : Control
{
    private float _value = 1;
    public bool Hurry { get; set; }

    public float Value
    {
        get => _value;
        set
        {
            _value = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        float r = Size.Y / 2;
        var bg = new StyleBoxFlat { BgColor = new Color(t.Text, 0.12f) };
        bg.SetCornerRadiusAll((int)r);
        DrawStyleBox(bg, new Rect2(Vector2.Zero, Size));
        if (_value <= 0) return;

        float pulse = Hurry ? 0.75f + 0.25f * Mathf.Sin(Time.GetTicksMsec() / 90f) : 1;
        var fill = new StyleBoxFlat { BgColor = new Color(Hurry ? t.H : t.Accent, pulse) };
        fill.SetCornerRadiusAll((int)r);
        DrawStyleBox(fill, new Rect2(Vector2.Zero, new Vector2(Mathf.Max(Size.X * _value, Size.Y), Size.Y)));
    }
}
