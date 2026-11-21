using System;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Tenis: torneo de tres partidos (al mejor de 3 juegos) contra rivales cada vez mejores.
/// Cada golpe son dos pisadas: la casilla a la que llega la bola y luego la zona del rival a la que se manda.
/// La zona débil del rival son 3 casillas seguidas del anillo: la central falla más que sus vecinas.
/// </summary>
public partial class TennisGame : Node3D
{
    public const string ScenePath = "res://scenes/Tennis.tscn";

    /// <summary>Sheet: prefijo de las siluetas en assets/tenistas/poses; RunsRight: hacia dónde corre su pose de carrera.</summary>
    private sealed record Rival(string Name, string Level, string Round, float Main, float Side, double AimTime,
        double Flight, Color Shirt, string Sheet, bool RunsRight);

    private static readonly Rival[] Rivals =
    {
        new("Paco Revés", "malo", "1.ª RONDA", 0.8f, 0.6f, 3.0, 1.7, new Color("#66BB6A"), "paco", true),
        new("Lola Drive", "medio", "SEMIFINAL", 0.6f, 0.4f, 2.4, 1.4, new Color("#EC407A"), "lola", false),
        new("Max Ace", "bueno", "FINAL", 0.4f, 0.2f, 1.8, 1.15, new Color("#FF7043"), "max", true),
    };

    private const float OtherFail = 0.05f;
    private const double HitEarly = 0.5, HitLate = 0.15;

    /// <summary>Las 8 casillas exteriores en orden de anillo (agujas del reloj).</summary>
    private static readonly Pad[] Ring =
    {
        Pad.Cross, Pad.Up, Pad.Circle, Pad.Right, Pad.Square, Pad.Down, Pad.Triangle, Pad.Left,
    };

    /// <summary>Al pasar al siguiente partido del torneo la escena se recarga sin volver a la primera ronda.</summary>
    private static bool _continue;
    private static int _round;

    private enum Phase { Intro, Play, Paused, MatchOver }
    private enum Rally { Wait, RivalServe, Incoming, Aim, Outgoing, Dead }
    private enum ReturnKind { Normal, Easy }

    /// <summary>Poses de las láminas, en el orden en que se generaron.</summary>
    private enum Pose { Ready, Run, Forehand, Backhand, Serve, Stretch, Celebrate, Dejected }

    private readonly GameInfo _game = GameCatalog.All.First(g => g.Id == "tennis");
    private readonly Random _rng = new();
    private readonly TennisScore _score = new();
    private Rival _rival = null!;

    private Phase _phase = Phase.Intro;
    private Rally _rally = Rally.Wait;
    private bool _playerServes = true;
    private double _timer, _aimTotal;

    // Golpe propio: la bola cruza la red a mitad de tiempo y se redirige hacia la zona elegida.
    private bool _shot, _aimed;
    private Vector3 _toGoal;
    private float _peakGoal, _ballScale = 1;
    private static readonly Vector3 CourtCenter = new(0, 0.3f, -5.4f);

    // Vuelo de la bola: parábola de _from a _to en _dur segundos con altura extra _peak.
    private Vector3 _from, _to;
    private double _t, _dur;
    private float _peak;
    private Action? _onLand;

    private Pad _incoming;
    private bool _hit;
    private int _weak;
    private bool _rivalFails, _failIsOut;
    private ReturnKind _nextReturn = ReturnKind.Normal;

    private Mat3D _mat = null!, _zones = null!;
    private Node3D _ball = null!, _rivalNode = null!;
    private Sprite3D _rivalSprite = null!;
    private Texture2D[] _poses = null!;
    private double _poseHold;
    private MeshInstance3D _shadow = null!;
    private Vector3 _rivalTarget, _rivalHome = new(0, 0, -9.2f);

    private Label _phaseLabel = null!, _bigText = null!, _youGames = null!, _youPoints = null!, _rivalGames = null!,
        _rivalPoints = null!;
    private ServeDot _youServe = null!, _rivalServe = null!;
    private TimeBar _aimBar = null!;
    private PanelContainer _introPanel = null!, _pausePanel = null!, _overPanel = null!;
    private Label _overTitle = null!, _overText = null!, _overHintStart = null!;
    private ScreenFade _fade = null!;

    public override void _Ready()
    {
        Audio.Music("estadio");
        if (!_continue) _round = 0;
        _continue = false;
        _rival = Rivals[_round];

        AddChild(new StageBackdrop { Focus = new Vector2(0, -2), BackdropPosition = new Vector3(0, 6, -24) });
        var camera = new Camera3D { Fov = 50 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 7.8f, 10.6f), new Vector3(0, 0, -2.0f), Vector3.Up);

        BuildCourt();
        _mat = new Mat3D { Position = new Vector3(0, 0, 3.4f) };
        AddChild(_mat);
        _zones = new Mat3D { ZonesOnly = true, Position = new Vector3(0, 0.01f, -5.4f), Scale = new Vector3(2.05f, 0.2f, 2.0f) };
        AddChild(_zones);
        _zones.SetFlipRows(Settings.TennisFlipRows);
        BuildRival();
        BuildBall();
        BuildUi();

        MatInput.Instance.Pressed += OnPressed;
        MatInput.Instance.Released += OnReleased;
        if (Circuit.Active) StartMatch(); // en el circuito, sin presentación
        else ShowIntro();
    }

    public override void _ExitTree()
    {
        MatInput.Instance.Pressed -= OnPressed;
        MatInput.Instance.Released -= OnReleased;
    }

    // ---------------------------------------------------------------- escena

    private void BuildCourt()
    {
        var t = Themes.Current;
        var court = new StandardMaterial3D { AlbedoColor = t.Floor.Lerp(t.V, 0.22f), Roughness = 0.9f };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(10, 18.5f), Material = court },
            Position = new Vector3(0, 0.004f, -2.25f),
        });

        var line = new StandardMaterial3D
        {
            AlbedoColor = new Color(t.Light ? t.Text : Colors.White, 0.8f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        void Line(float x, float z, float w, float d) => AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w, 0.01f, d), Material = line }, Position = new Vector3(x, 0.01f, z),
        });
        Line(-4.6f, -2.25f, 0.08f, 17.5f);
        Line(4.6f, -2.25f, 0.08f, 17.5f);
        Line(0, -11f, 9.28f, 0.08f);
        Line(0, 6.5f, 9.28f, 0.08f);

        // Red: malla translúcida, cinta blanca y postes.
        var mesh = new StandardMaterial3D
        {
            AlbedoColor = new Color(t.Bg.Darkened(0.3f), 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(9.8f, 0.85f, 0.03f), Material = mesh }, Position = new Vector3(0, 0.43f, 0) });
        var white = new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 0.5f };
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(9.8f, 0.07f, 0.06f), Material = white }, Position = new Vector3(0, 0.88f, 0) });
        foreach (float x in new[] { -4.95f, 4.95f })
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1f, Material = white },
                Position = new Vector3(x, 0.5f, 0),
            });
    }

    private void BuildRival()
    {
        _rivalNode = new Node3D { Position = _rivalHome };
        AddChild(_rivalNode);
        _poses = Enumerable.Range(0, 8)
            .Select(i => GD.Load<Texture2D>($"res://assets/tenistas/poses/{_rival.Sheet}_{i}.png")).ToArray();

        const float height = 3.9f; // alto del lienzo de cada pose en unidades de la escena
        float pixel = height / _poses[0].GetHeight();
        _rivalSprite = new Sprite3D
        {
            Texture = _poses[0],
            PixelSize = pixel,
            Position = new Vector3(0, height / 2, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            Modulate = Themes.Current.Light ? _rival.Shirt.Darkened(0.25f) : _rival.Shirt,
        };
        _rivalNode.AddChild(_rivalSprite);
        _rivalTarget = _rivalHome;
    }

    /// <summary>Muestra una pose durante <paramref name="hold"/> s; flip la voltea en horizontal.</summary>
    private void SetPose(Pose pose, double hold, bool flip = false)
    {
        _rivalSprite.Texture = _poses[(int)pose];
        _rivalSprite.FlipH = flip;
        _poseHold = hold;
    }

    /// <summary>Sin pose forzada: corre si se está moviendo y, si no, espera preparado.</summary>
    private void AutoPose(double delta)
    {
        if (_poseHold > 0)
        {
            _poseHold -= delta;
            return;
        }
        float dx = _rivalTarget.X - _rivalNode.Position.X;
        float dist = _rivalNode.Position.DistanceTo(_rivalTarget);
        if (dist > 0.15f)
        {
            _rivalSprite.Texture = _poses[(int)Pose.Run];
            _rivalSprite.FlipH = (dx > 0) != _rival.RunsRight;
        }
        else
        {
            _rivalSprite.Texture = _poses[(int)Pose.Ready];
            _rivalSprite.FlipH = false;
        }
    }

    private void BuildBall()
    {
        _ball = new Node3D();
        AddChild(_ball);
        var felt = new StandardMaterial3D
        {
            AlbedoColor = new Color("#D7F23B"), EmissionEnabled = true, Emission = new Color("#D7F23B") * 0.35f, Roughness = 0.8f,
        };
        _ball.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f, Material = felt } });

        _shadow = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.005f,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0, 0, 0, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_shadow);
        _ball.Visible = _shadow.Visible = false;
    }

    private static MeshInstance3D Mesh(Node3D parent, PrimitiveMesh mesh, Color color, Vector3 pos)
    {
        mesh.Material = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.6f };
        var mi = new MeshInstance3D { Mesh = mesh, Position = pos };
        parent.AddChild(mi);
        return mi;
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Root(this);

        var board = new PanelContainer { ThemeTypeVariation = "Status" };
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 2);
        _youServe = new ServeDot();
        _rivalServe = new ServeDot();
        _youGames = Label("0", "Value", HorizontalAlignment.Center);
        _youPoints = Label("0", "Accent", HorizontalAlignment.Center);
        _rivalGames = Label("0", "Value", HorizontalAlignment.Center);
        _rivalPoints = Label("0", "Accent", HorizontalAlignment.Center);
        foreach (var points in new[] { _youPoints, _rivalPoints }) points.CustomMinimumSize = new Vector2(52, 0);
        grid.AddChild(_youServe);
        grid.AddChild(Label("TÚ", "Value"));
        grid.AddChild(_youGames);
        grid.AddChild(_youPoints);
        grid.AddChild(_rivalServe);
        grid.AddChild(Label(_rival.Name.ToUpperInvariant(), "Value"));
        grid.AddChild(_rivalGames);
        grid.AddChild(_rivalPoints);
        board.AddChild(grid);
        ui.AddChild(Anchor(board, Control.LayoutPreset.TopLeft, new Vector2(40, 28)));

        var phaseBox = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        phaseBox.AddThemeConstantOverride("separation", 6);
        _phaseLabel = Label("", "Big", HorizontalAlignment.Center);
        _aimBar = new TimeBar { CustomMinimumSize = new Vector2(420, 12), Visible = false };
        phaseBox.AddChild(_phaseLabel);
        phaseBox.AddChild(_aimBar);
        ui.AddChild(Anchor(phaseBox, Control.LayoutPreset.CenterTop, new Vector2(0, 30)));

        var roundBox = new VBoxContainer();
        roundBox.AddThemeConstantOverride("separation", -4);
        roundBox.AddChild(Label(_rival.Round, "Accent", HorizontalAlignment.Right));
        roundBox.AddChild(Label($"Rival {_rival.Level}", "Muted", HorizontalAlignment.Right));
        ui.AddChild(Anchor(roundBox, Control.LayoutPreset.TopRight, new Vector2(-40, 30)));

        ui.AddChild(Anchor(Hints(("SELECT", "Pausa")), Control.LayoutPreset.BottomLeft, new Vector2(40, -34)));

        _bigText = Label("", "Huge", HorizontalAlignment.Center);
        _bigText.AddThemeFontSizeOverride("font_size", 96);
        _bigText.CustomMinimumSize = new Vector2(1100, 140);
        _bigText.VerticalAlignment = VerticalAlignment.Center;
        ui.AddChild(Anchor(_bigText, Control.LayoutPreset.Center, new Vector2(0, -90)));

        _introPanel = Panel(ui, _rival.Round, out var introBox, ("START", "¡A jugar!"), ("SELECT", "Volver"));
        _introPanel.CustomMinimumSize = new Vector2(720, 0);
        var vs = Label($"contra {_rival.Name}", "HugeAccent", HorizontalAlignment.Center);
        vs.AddThemeFontSizeOverride("font_size", 44);
        _levelLabel = Label("", "Muted", HorizontalAlignment.Center);
        var rules = Label("1. Pisa la casilla a la que llega la bola.\n2. Antes de que llegue a la red, pisa la zona a la que la mandas: la que más brilla es su punto débil.",
            "Value", HorizontalAlignment.Center);
        rules.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _optionsLabel = Label("", "Accent", HorizontalAlignment.Center);
        var optionHints = Hints(("△", "Cambiar zonas"), ("□", "Cambiar ritmo"));
        optionHints.Alignment = BoxContainer.AlignmentMode.Center;
        foreach (var (control, i) in new (Control, int)[] { (vs, 1), (_levelLabel, 2), (rules, 3), (_optionsLabel, 4), (optionHints, 5) })
        {
            introBox.AddChild(control);
            introBox.MoveChild(control, i);
        }
        UpdateOptions();

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _overPanel = Panel(ui, "", out var overBox, ("START", "…"), ("SELECT", "Volver"));
        _overPanel.CustomMinimumSize = new Vector2(680, 0);
        _overTitle = (Label)overBox.GetChild(0);
        _overText = Label("", "Muted", HorizontalAlignment.Center);
        _overText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        overBox.AddChild(_overText);
        overBox.MoveChild(_overText, 1);
        _overHintStart = (Label)((HBoxContainer)((HBoxContainer)overBox.GetChild(2)).GetChild(0)).GetChild(1);

        _fade = new ScreenFade();
        ui.AddChild(_fade);
        UpdateBoard();
    }

    private Label _levelLabel = null!, _optionsLabel = null!;

    public static string ZonesText => Settings.TennisFlipRows ? "Desde el rival" : "Desde tu lado";
    public static string PaceText => Settings.TennisFast ? "Rápido" : "Normal";

    /// <summary>Tiempo de vuelo de tu golpe hasta la zona rival; la red llega a la mitad.</summary>
    private double FlightTime(bool serve) => _rival.AimTime * (Settings.TennisFast ? 2.0 / 3.0 : 1.0) + (serve ? 1 : 0);

    private void UpdateOptions()
    {
        _levelLabel.Text = $"Rival {_rival.Level}  ·  {FlightTime(false) / 2:0.0} s para apuntar  ·  al mejor de 3 juegos";
        _optionsLabel.Text = $"Zonas: {ZonesText.ToLowerInvariant()}   ·   Ritmo: {PaceText.ToLowerInvariant()}";
    }

    private void ToggleOption(bool zones)
    {
        if (zones) Settings.TennisFlipRows = !Settings.TennisFlipRows;
        else Settings.TennisFast = !Settings.TennisFast;
        Settings.Save();
        _zones.SetFlipRows(Settings.TennisFlipRows);
        UpdateOptions();
        Pop(_optionsLabel, 0.8f);
        Audio.Play("click");
    }

    private void Big(string text, Color color, double hold = 0.6)
     => FlashText(_bigText, text, color, hold);

    private void SetPhase(string text, Color? color = null)
    {
        _phaseLabel.Text = text;
        _phaseLabel.AddThemeColorOverride("font_color", color ?? Themes.Current.Text);
    }

    private void UpdateBoard()
    {
        _youGames.Text = _score.GamesYou.ToString();
        _rivalGames.Text = _score.GamesRival.ToString();
        _youPoints.Text = _score.PointLabel(you: true);
        _rivalPoints.Text = _score.PointLabel(you: false);
        _youServe.On = _playerServes;
        _rivalServe.On = !_playerServes;
    }

    // ---------------------------------------------------------------- flujo del partido

    private void ShowIntro()
    {
        _phase = Phase.Intro;
        ShowPanel(_introPanel);
    }

    private void StartMatch()
    {
        _introPanel.Visible = false;
        _phase = Phase.Play;
        Audio.Play("start");
        NextPoint();
    }

    private void NextPoint()
    {
        _mat.ClearAll();
        _zones.ClearAll();
        _aimBar.Visible = false;
        _rivalTarget = _rivalHome;
        _nextReturn = ReturnKind.Normal;
        UpdateBoard();

        if (_playerServes)
        {
            PlaceBall(new Vector3(0, 1.0f, 3.9f));
            SetPhase("TU SAQUE", Themes.Current.AccentText);
            BeginAim(serve: true);
        }
        else
        {
            _rally = Rally.RivalServe;
            PlaceBall(_rivalHome + new Vector3(0.5f, 2.6f, 0.4f));
            SetPhase("SACA EL RIVAL");
            _timer = 1.0;
            SetPose(Pose.Serve, 1.0);
        }
    }

    private void BeginIncoming(ReturnKind kind)
    {
        var options = Ring.Where(p => p != _incoming).ToArray();
        _incoming = options[_rng.Next(options.Length)];
        double dur = _rival.Flight * (kind == ReturnKind.Easy ? 1.3 : 1.0);
        _rally = Rally.Incoming;
        _hit = false;
        _mat.SetBlink(_incoming, true);
        Fly(BallPos, TileWorld(_mat, _incoming) + Vector3.Up * 0.35f, dur, 1.8f, null);
        SetPhase("¡GOLPEA!");
    }

    private void Hit()
    {
        _hit = true;
        _mat.SetBlink(_incoming, false);
        var theme = Themes.Current;
        bool perfect = Math.Abs(_t - _dur) < 0.12;
        // Raquetazo: más seco y fuerte si es perfecto.
        Audio.Impact(perfect ? "impact_punch_medium" : "impact_generic_light", perfect ? -2 : -4, perfect ? 1.1f : 1.25f);
        Fx.Burst(this, BallPos, theme.Accent, 14, 2.5f);
        Fx.Popup(this, BallPos + Vector3.Up * 0.6f, perfect ? "¡PERFECTO!" : "¡TOC!", theme.Accent, theme.Bg, perfect ? 80 : 90);
        BeginAim(serve: false);
    }

    private void BeginAim(bool serve)
    {
        _rally = Rally.Aim;
        _aimed = false;
        _aimTotal = FlightTime(serve);
        _weak = _rng.Next(Ring.Length);
        foreach (var pad in Ring) _zones.SetDim(pad, 0.18f);
        foreach (int i in new[] { _weak - 1, _weak + 1 })
        {
            var side = Ring[Mathf.PosMod(i, Ring.Length)];
            _zones.SetDim(side, 1);
            _zones.SetGlow(side, 0.45f);
        }
        _zones.SetDim(Ring[_weak], 1);
        _zones.SetGlow(Ring[_weak], 1);
        _zones.SetBlink(Ring[_weak], true);

        _aimBar.Visible = true;
        _aimBar.Hurry = false;
        if (!serve) SetPhase("¡APUNTA!", Themes.Current.AccentText);
        // La bola sale ya hacia el rival, baja: si nadie la eleva eligiendo zona, se queda en la red.
        FlyShot(BallPos, CourtCenter, _aimTotal);
    }

    private void Shoot(Pad aimed)
    {
        _aimBar.Visible = false;
        _zones.ClearAll();
        foreach (var pad in Ring) _zones.SetDim(pad, 0.3f);
        _aimed = true;

        float chance;
        {
            int dist = RingDistance(Array.IndexOf(Ring, aimed), _weak);
            chance = dist == 0 ? _rival.Main : dist == 1 ? _rival.Side : OtherFail;
            _nextReturn = dist <= 1 ? ReturnKind.Easy : ReturnKind.Normal;
            _zones.SetDim(aimed, 1);
            _zones.SetPressed(aimed, true);
        }
        _rivalFails = _rng.NextDouble() < chance;
        _failIsOut = _rng.NextDouble() < 0.5;

        var landing = TileWorld(_zones, aimed) + Vector3.Up * 0.3f;
        // Si no la alcanza, el rival se queda corto; si no, llega a tiempo.
        var reach = new Vector3(landing.X, 0, landing.Z - 0.6f);
        _rivalTarget = _rivalFails && !_failIsOut ? _rivalNode.Position.Lerp(reach, 0.4f) : reach;

        Audio.Impact("impact_generic_light", -9, 1.5f);
        SetPhase("");
        _rally = Rally.Outgoing;
        // La misma bola cambia de rumbo y se eleva para pasar la red; llega al final del tiempo de vuelo.
        _toGoal = landing;
        _peakGoal = 1.45f - (_from.Y + landing.Y) / 2;
        _onLand = RivalTurn;
    }

    /// <summary>No se eligió zona a tiempo: la bola se queda en la red y el punto es del rival.</summary>
    private void IntoTheNet()
    {
        SetPhase("");
        _rally = Rally.Dead;
        _aimBar.Visible = false;
        _zones.ClearAll();
        foreach (var pad in Ring) _zones.SetDim(pad, 0.3f);
        var net = new Vector3(BallPos.X, Mathf.Min(BallPos.Y, 0.6f), 0.08f);
        Audio.Impact("impact_soft_medium", -2, 0.75f); // la bola contra la red
        Big("¡A LA RED!", Themes.Current.H);
        Fly(net, new Vector3(net.X, 0.15f, 0.5f), 0.4, 0.05f, null);
        PointTo(you: false, delay: 1.2);
    }

    private void RivalTurn()
    {
        var theme = Themes.Current;
        Audio.Impact("impact_soft_medium", -9, 1.3f); // bote en el campo del rival
        if (_rivalFails && !_failIsOut)
        {
            Big("¡NO LLEGA!", theme.AccentText);
            SetPose(Pose.Stretch, 1.0, flip: BallPos.X > _rivalNode.Position.X);
            Fly(BallPos, BallPos + new Vector3(0, -0.3f, -5), 0.6, 0.5f, null);
            _rally = Rally.Dead;
            PointTo(you: true, delay: 1.2);
            return;
        }

        Swing();
        if (_rivalFails)
        {
            var outside = new Vector3(_rng.Next(2) == 0 ? -6.5f : 6.5f, 0.1f, 2 + (float)_rng.NextDouble() * 3);
            _rally = Rally.Dead;
            Fly(BallPos, outside, 1.0, 1.8f, () =>
            {
                Big("¡FUERA!", theme.AccentText);
                Audio.Impact("impact_soft_medium", -6, 1.25f); // bote fuera de la pista
                PointTo(you: true, delay: 1.1);
            });
            return;
        }
        BeginIncoming(_nextReturn);
    }

    private void Missed()
    {
        _mat.SetBlink(_incoming, false);
        _rally = Rally.Dead;
        Audio.Play("miss");
        Fly(BallPos, BallPos + new Vector3(0, -0.25f, 5), 0.5, 0.4f, null);
        Big("PUNTO RIVAL", Themes.Current.H);
        PointTo(you: false, delay: 1.2);
    }

    private void PointTo(bool you, double delay)
    {
        var result = _score.Add(you);
        UpdateBoard();
        if (you) Audio.Play("sparkle");

        GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (_phase is Phase.MatchOver) return;
            SetPose(you ? Pose.Dejected : Pose.Celebrate, result == TennisScore.Result.Match ? 999 : 1.1);
            var theme = Themes.Current;
            switch (result)
            {
                case TennisScore.Result.Point:
                    string call = _score.Call();
                    if (call != "") Big(call, theme.Text, 0.7);
                    Continue(1.0);
                    break;
                case TennisScore.Result.Game:
                    Big(you ? "¡JUEGO PARA TI!" : "JUEGO PARA EL RIVAL", you ? theme.AccentText : theme.H, 1.0);
                    Audio.Play(you ? "combo" : "miss");
                    _playerServes = !_playerServes;
                    UpdateBoard();
                    Continue(1.8);
                    break;
                case TennisScore.Result.Match:
                    MatchOver(you);
                    break;
            }
        };
    }

    private void Continue(double delay)
    {
        _rally = Rally.Wait;
        GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (_phase == Phase.Play) NextPoint();
            else if (_phase == Phase.Paused) _resumeToNextPoint = true;
        };
    }

    private bool _resumeToNextPoint;

    private void MatchOver(bool won)
    {
        _phase = Phase.MatchOver;
        SetPhase("");
        _aimBar.Visible = false;
        var theme = Themes.Current;
        int wins = _round + (won ? 1 : 0);
        bool record = Records.Submit(_game, wins);
        string recordText = record ? "  ·  ¡Nuevo récord!" : "";

        if (won && _round == Rivals.Length - 1)
        {
            _overTitle.Text = "¡CAMPEÓN!";
            _overText.Text = $"Has ganado el torneo: {_score.GamesYou}-{_score.GamesRival} contra {_rival.Name}.{recordText}";
            _overHintStart.Text = "Otro torneo";
            Audio.Play("stars3");
            Fx.Burst(this, new Vector3(0, 2, 0), theme.Accent, 80, 7);
        }
        else if (won)
        {
            _overTitle.Text = "¡GANASTE!";
            _overText.Text = $"{_score.GamesYou}-{_score.GamesRival} contra {_rival.Name}. Pasas a la {Rivals[_round + 1].Round.ToLowerInvariant()}.{recordText}";
            _overHintStart.Text = "Siguiente partido";
            Audio.Play("stars3");
        }
        else
        {
            _overTitle.Text = "ELIMINADO";
            _overText.Text = $"{_score.GamesYou}-{_score.GamesRival} contra {_rival.Name} en la {_rival.Round.ToLowerInvariant()}.{recordText}";
            _overHintStart.Text = "Otro torneo";
            Audio.Play("miss");
        }
        _overPanel.SetMeta("advance", won && _round < Rivals.Length - 1);
        Big(won ? "¡PARTIDO!" : "FIN DEL PARTIDO", won ? theme.AccentText : theme.H, 0.9);
        GetTree().CreateTimer(1.4).Timeout += () => ShowPanel(_overPanel);
    }

    // ---------------------------------------------------------------- cada fotograma

    public override void _Process(double delta)
    {
        if (_phase is Phase.Paused) return;

        _rivalNode.Position = _rivalNode.Position.MoveToward(_rivalTarget, (float)delta * 7);
        AutoPose(delta);
        UpdateBall(delta);
        if (_phase != Phase.Play) return;

        switch (_rally)
        {
            case Rally.RivalServe:
                _timer -= delta;
                if (_timer <= 0)
                {
                    Swing();
                    BeginIncoming(ReturnKind.Normal);
                }
                break;
            case Rally.Incoming:
                if (!_hit && _t >= _dur - HitEarly && _t - delta < _dur - HitEarly && MatInput.Instance.IsDown(_incoming)) Hit();
                else if (!_hit && _t >= _dur + HitLate) Missed();
                break;
            case Rally.Aim:
                double toNet = _aimTotal / 2 - _t;
                _aimBar.Value = (float)Math.Max(0, toNet / (_aimTotal / 2));
                _aimBar.Hurry = toNet < 0.4;
                if (toNet <= 0 && !_aimed) IntoTheNet();
                break;
        }
    }

    private void UpdateBall(double delta)
    {
        if (!_ball.Visible) return;
        _t += delta;
        float k = _dur <= 0 ? 1 : Mathf.Clamp((float)(_t / _dur), 0, 1);
        Vector3 pos;
        if (_shot)
        {
            float follow = 1 - Mathf.Exp(-(float)delta * 12);
            _to = _to.Lerp(_toGoal, follow);
            _peak = Mathf.Lerp(_peak, _peakGoal, follow);
            // Profundidad por tramos: la red (z = 0) siempre a mitad de tiempo.
            float z = k < 0.5f ? Mathf.Lerp(_from.Z, 0, k / 0.5f) : Mathf.Lerp(0, _to.Z, (k - 0.5f) / 0.5f);
            pos = new Vector3(Mathf.Lerp(_from.X, _to.X, k), Mathf.Lerp(_from.Y, _to.Y, k) + 4 * _peak * k * (1 - k), z);
            // Tamaño exagerado: enorme al golpearla, mediana sobre la red, normal en la zona rival.
            _ballScale = z > 0
                ? Mathf.Lerp(1.8f, 4.5f, Mathf.Clamp(z / Mathf.Max(_from.Z, 0.01f), 0, 1))
                : Mathf.Lerp(1.8f, 1.0f, Mathf.Clamp(z / Mathf.Min(_to.Z, -0.01f), 0, 1));
        }
        else
        {
            pos = _from.Lerp(_to, k) + Vector3.Up * (4 * _peak * k * (1 - k));
            _ballScale = Mathf.Lerp(_ballScale, 1, 1 - Mathf.Exp(-(float)delta * 6));
        }
        _ball.Position = pos;
        _ball.Scale = Vector3.One * _ballScale;
        _ball.RotateX((float)delta * 9);
        _shadow.Position = new Vector3(pos.X, 0.02f, pos.Z);
        float s = Mathf.Clamp(1.2f - pos.Y * 0.25f, 0.35f, 1.2f);
        _shadow.Scale = new Vector3(s, 1, s);

        if (k >= 1 && _onLand is { } land)
        {
            _onLand = null;
            land();
        }
    }

    private void Fly(Vector3 from, Vector3 to, double duration, float peak, Action? onLand)
    {
        _from = from;
        _to = to;
        _dur = duration;
        _peak = peak;
        _t = 0;
        _onLand = onLand;
        _shot = false;
        _ball.Visible = _shadow.Visible = true;
    }

    /// <summary>Golpe propio hacia la pista rival: empieza bajo (camino de la red) hasta que se elige zona.</summary>
    private void FlyShot(Vector3 from, Vector3 to, double duration)
    {
        Fly(from, to, duration, 0, null);
        _shot = true;
        _toGoal = to;
        _peak = _peakGoal = 0.55f - (from.Y + to.Y) / 2;
    }

    private void PlaceBall(Vector3 at) => Fly(at, at, 0, 0, null);

    private Vector3 BallPos => _ball.Position;

    private static Vector3 TileWorld(Mat3D mat, Pad pad) => mat.ToGlobal(mat.TileSurface(pad));

    private static int RingDistance(int a, int b)
    {
        int d = Math.Abs(a - b) % Ring.Length;
        return Math.Min(d, Ring.Length - d);
    }

    /// <summary>Golpe del rival: de derecha si la bola le queda a su derecha (a la izquierda de la pantalla), si no de revés.</summary>
    private void Swing()
    {
        bool forehand = BallPos.X <= _rivalNode.Position.X + 0.2f;
        SetPose(forehand ? Pose.Forehand : Pose.Backhand, 0.45);
        Audio.Impact("impact_generic_light", -6, 1.05f); // raquetazo del rival, algo más lejano
    }

    // ---------------------------------------------------------------- pisadas

    private void OnPressed(Pad pad)
    {
        _mat.SetPressed(pad, true);
        switch (_phase)
        {
            case Phase.Intro:
                if (pad == Pad.Triangle) ToggleOption(zones: true);
                else if (pad == Pad.Square) ToggleOption(zones: false);
                else if (pad is Pad.Start or Pad.Circle) StartMatch();
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
            case Phase.Play:
                if (pad == Pad.Select) Pause();
                else if (_rally == Rally.Incoming && !_hit && pad == _incoming && _t >= _dur - HitEarly) Hit();
                else if (_rally == Rally.Aim && !_aimed && _t < _aimTotal / 2 && Ring.Contains(pad)) Shoot(pad);
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle) Resume();
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
            case Phase.MatchOver when _overPanel.Visible:
                if (pad is Pad.Start or Pad.Circle)
                {
                    bool advance = (bool)_overPanel.GetMeta("advance", false);
                    _continue = advance;
                    if (advance) _round++;
                    _fade.GoTo(ScenePath);
                }
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
        }
    }

    private void OnReleased(Pad pad) => _mat.SetPressed(pad, false);

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
        _phase = Phase.Play;
        if (_resumeToNextPoint)
        {
            _resumeToNextPoint = false;
            NextPoint();
        }
    }
}

/// <summary>Bolita que marca quién saca.</summary>
public partial class ServeDot : Control
{
    private bool _on;

    public bool On
    {
        get => _on;
        set
        {
            _on = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(16, 16);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public override void _Draw()
    {
        if (_on) DrawCircle(Size / 2, 6, new Color("#D7F23B"));
    }
}
