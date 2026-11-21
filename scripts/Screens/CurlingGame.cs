using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Pantalla del curling: vista cenital que sigue a la piedra, sobre la física de <see cref="CurlingSheet"/>.
/// Antes de lanzar: ← → mano (diestro curva a la derecha, zurdo a la izquierda), ✕ / ○ apuntar, ↓ agacharse y coger
/// fuerza (se pierde en 3 s), ↑ soltar. Con la piedra en marcha, ✕ △ son el barrendero izquierdo y ○ □ el derecho.
/// Modos: solitario (8 piedras) y contra rival (2 entradas de 4 piedras cada uno).
/// </summary>
public partial class CurlingGame : Node3D
{
    public const string ScenePath = "res://scenes/Curling.tscn";

    private const int SoloStones = 8, StonesPerEnd = 4, Ends = 2;
    private const float AimStep = 0.15f;
    private const float StoneDraw = 2 * CurlingSheet.StoneR * 1.12f; // la piedra se dibuja algo mayor para que se lea
    private const float PersonSize = 0.95f, StandingWidth = 0.72f; // los de pie (de espaldas) son más esbeltos
    private const double SettlePause = 2.6; // tiempo para ver cómo ha quedado antes del siguiente tiro

    private enum Phase { Menu, Choose, Charge, Running, RivalThink, Settled, Paused, Over }

    private static bool _rivalMode;
    private static int _levelIndex = 1;
    private const int HumanOption = 3; // tras Fácil, Normal y Difícil: dos jugadores por turnos en la misma alfombra

    /// <summary>Contra otra persona: los dos lanzan y barren por turnos.</summary>
    private static bool Human => _rivalMode && _levelIndex == HumanOption;

    /// <summary>El turno es de una persona (siempre el jugador 1; el 2 también si el rival es humano).</summary>
    private bool HumanTurn => _team == 0 || Human;

    /// <summary>Nombre corto de cada equipo en el marcador.</summary>
    private static string Short(int team) => Human ? (team == 0 ? "J1" : "J2") : (team == 0 ? "TÚ" : "RIVAL");
    private static string Long(int team) => Human ? $"JUGADOR {team + 1}" : (team == 0 ? "TÚ" : "EL RIVAL");
    private static readonly (CurlingLevel Level, string Name)[] Levels =
    {
        (CurlingLevel.Easy, "Fácil"), (CurlingLevel.Normal, "Normal"), (CurlingLevel.Hard, "Difícil"),
    };

    private readonly GameInfo _game = GameCatalog.All.First(g => g.Id == "curling");
    private readonly Random _rng = new();
    private CurlingSheet _sheet = new();
    private Phase _phase = Phase.Menu, _beforePause;

    // Turno actual.
    private readonly bool[] _hands = { true, true }; // diestro (true) o zurdo de cada jugador; se recuerda entre turnos
    private bool RightHanded
    {
        get => _hands[_team];
        set => _hands[_team] = value;
    }
    private float _aim;
    private readonly PowerMeter _power = new();
    private double _pause;
    private double _tension => _power.Value;
    private int _team;            // 0 = jugador (rojo), 1 = rival (amarillo)
    private int _thrownThisEnd;   // piedras lanzadas en la entrada (de los dos)
    private int _end;
    private readonly int[] _endTotals = new int[2];
    private CurlingShot _rivalShot;

    // Vista.
    private Camera3D _camera = null!;
    private float _cameraY = 3.5f;
    private readonly Dictionary<CurlingSheet.Stone, Node3D> _stoneViews = new();
    private Node3D _thrower = null!, _leftSweeper = null!, _rightSweeper = null!, _aimArrow = null!, _curlArrow = null!, _previewStone = null!;
    private MeshInstance3D _aimLine = null!;
    private StandardMaterial3D _previewMat = null!;
    private StandardMaterial3D _throwerMat = null!, _leftMat = null!, _rightMat = null!, _leftSpray = null!, _rightSpray = null!;
    private MeshInstance3D _throwerMesh = null!, _curlMesh = null!, _leftSweeperMesh = null!, _rightSweeperMesh = null!;
    private double _sweepClock;
    private bool _leftFrame, _rightFrame;

    private Label _modeLabel = null!, _turnLabel = null!, _scoreLabel = null!, _bigText = null!, _handLabel = null!, _liveLabel = null!;
    private TimeBar _tensionBar = null!;
    private HouseView _houseView = null!;
    private VBoxContainer _hints = null!;
    private PanelContainer _menu = null!, _pausePanel = null!, _endPanel = null!;
    private readonly List<PanelContainer> _menuRows = new();
    private Label _modeValue = null!, _levelValue = null!, _menuRecord = null!;
    private Label _endTitle = null!, _endScore = null!, _endInfo = null!;
    private int _menuRow;
    private ScreenFade _fade = null!;

    private static Vector3 To3(Vector2 p, float h = 0) => new(p.X, h, -p.Y);

    public override void _Ready()
    {
        Audio.Music("folk"); // el curling nació en Escocia
        AddChild(new StageBackdrop { Focus = new Vector2(0, -10), BackdropPosition = new Vector3(0, -2, -20), Fog = false });
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 9f };
        AddChild(_camera);
        PlaceCamera(true);

        BuildSheet();
        BuildPeople();
        BuildUi();
        _sheet.Hit += (_, _, speed) => Audio.Impact("impact_mining", -10 + Math.Min(speed, 3f) * 3, 0.85f + Math.Min(speed, 3f) * 0.08f);
        MatInput.Instance.Pressed += OnPressed;
        OpenMenu();
    }

    public override void _ExitTree() => MatInput.Instance.Pressed -= OnPressed;

    // ---------------------------------------------------------------- dibujos

    private static Texture2D Tex(string name) => GD.Load<Texture2D>($"res://assets/curling/{name}.png");

    private static StandardMaterial3D PartMat(string name) => new()
    {
        AlbedoTexture = Tex(name),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
    };

    /// <summary>Calcomanía plana sobre el hielo; un ancho negativo la voltea (zurdo, barrendero derecho).</summary>
    private static MeshInstance3D Decal(Node3D parent, Material mat, Vector3 center, float width, float height)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One, Material = mat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(new Vector3(width, 0, 0), new Vector3(0, 0, -height), Vector3.Up), center),
        };
        parent.AddChild(mi);
        return mi;
    }

    private static float HeightFor(string name, float width)
    {
        var tex = Tex(name);
        return width * tex.GetHeight() / tex.GetWidth();
    }

    private void Line(float y, float thickness, Color color)
    {
        var mat = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(CurlingSheet.HalfWidth * 2, thickness), Material = mat },
            Position = To3(new Vector2(0, y), 0.012f),
        });
    }

    private void BuildSheet()
    {
        const float start = -3f, end = CurlingSheet.BackY + 3f;
        float length = end - start;
        var ice = new StandardMaterial3D
        {
            AlbedoTexture = Tex("hielo"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureRepeat = true, Uv1Scale = new Vector3(CurlingSheet.HalfWidth * 2 / 1.2f, length / 1.2f, 1),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(CurlingSheet.HalfWidth * 2, length), Material = ice },
            Position = To3(new Vector2(0, (start + end) / 2), 0.005f),
        });
        // Bandas laterales oscuras.
        var board = new StandardMaterial3D { AlbedoColor = new Color("#1B2A44"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        foreach (float side in new[] { -1f, 1f })
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.2f, length), Material = board },
                Position = To3(new Vector2(side * (CurlingSheet.HalfWidth + 0.15f), (start + end) / 2), 0.1f),
            });

        // Casa, líneas y punto de lanzamiento.
        float house = CurlingSheet.HouseR * 2 * 1.02f;
        Decal(this, PartMat("casa"), To3(CurlingSheet.Tee, 0.01f), house, HeightFor("casa", house));
        Line(CurlingSheet.HogY, 0.1f, new Color("#E8413C"));
        Line(CurlingSheet.TeeY, 0.03f, new Color("#22324F"));
        Line(CurlingSheet.BackY, 0.03f, new Color("#22324F"));
        var center = new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.2f, 0.31f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(0.03f, end), Material = center },
            Position = To3(new Vector2(0, end / 2), 0.011f),
        });
        var hack = new StandardMaterial3D { AlbedoColor = new Color("#2B2B33"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(0.4f, 0.25f), Material = hack },
            Position = To3(new Vector2(0, -0.55f), 0.011f),
        });
    }

    private void BuildPeople()
    {
        _throwerMat = PartMat("lanzador_de_pie");
        _thrower = new Node3D();
        _throwerMesh = Decal(_thrower, _throwerMat, Vector3.Zero, PersonSize, HeightFor("lanzador_de_pie", PersonSize));
        AddChild(_thrower);

        _leftMat = PartMat("barrendero_quieto");
        _rightMat = PartMat("barrendero_quieto");
        _leftSweeper = new Node3D();
        _rightSweeper = new Node3D();
        float sh = HeightFor("barrendero_quieto", PersonSize);
        _leftSweeperMesh = Decal(_leftSweeper, _leftMat, Vector3.Zero, PersonSize, sh);
        _rightSweeperMesh = Decal(_rightSweeper, _rightMat, Vector3.Zero, -PersonSize, sh);
        _leftSpray = PartMat("hielo_polvo");
        _rightSpray = PartMat("hielo_polvo");
        Decal(_leftSweeper, _leftSpray, new Vector3(0.45f, 0.01f, -0.55f), 0.45f, HeightFor("hielo_polvo", 0.45f));
        Decal(_rightSweeper, _rightSpray, new Vector3(-0.45f, 0.01f, -0.55f), -0.45f, HeightFor("hielo_polvo", 0.45f));
        AddChild(_leftSweeper);
        AddChild(_rightSweeper);

        _aimArrow = new Node3D();
        Decal(_aimArrow, PartMat("flecha_apuntar"), new Vector3(0, 0, -0.6f), 0.35f, HeightFor("flecha_apuntar", 0.35f));
        AddChild(_aimArrow);
        // Piedra en la mano mientras se apunta (la de verdad aparece al soltar) y línea de puntería.
        _previewMat = PartMat("piedra_roja");
        _previewStone = new Node3D();
        Decal(_previewStone, _previewMat, Vector3.Zero, StoneDraw, HeightFor("piedra_roja", StoneDraw));
        AddChild(_previewStone);
        var lineMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.85f, 1f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _aimLine = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(0.05f, 1), Material = lineMat } };
        AddChild(_aimLine);
        _curlArrow = new Node3D();
        _curlMesh = Decal(_curlArrow, PartMat("flecha_curva"), Vector3.Zero, 0.5f, HeightFor("flecha_curva", 0.5f));
        AddChild(_curlArrow);
    }

    private Node3D StoneView(CurlingSheet.Stone stone)
    {
        if (_stoneViews.TryGetValue(stone, out var view)) return view;
        view = new Node3D();
        string name = stone.Team == 0 ? "piedra_roja" : "piedra_amarilla";
        Decal(view, PartMat(name), Vector3.Zero, StoneDraw, HeightFor(name, StoneDraw));
        AddChild(view);
        _stoneViews[stone] = view;
        return view;
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Root(this);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", 2);
        _modeLabel = Label("", "Heading");
        _turnLabel = Label("", "Muted");
        _scoreLabel = Label("", "Big");
        left.AddChild(_modeLabel);
        left.AddChild(_turnLabel);
        left.AddChild(_scoreLabel);
        _liveLabel = Label("", "Accent");
        left.AddChild(_liveLabel);
        ui.AddChild(Anchor(left, Control.LayoutPreset.TopLeft, new Vector2(44, 28)));

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 6);
        right.AddChild(Label("LA CASA", "Muted", HorizontalAlignment.Right));
        _houseView = new HouseView { CustomMinimumSize = new Vector2(230, 230), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        right.AddChild(_houseView);
        _handLabel = Label("", "Value", HorizontalAlignment.Right);
        right.AddChild(_handLabel);
        right.AddChild(Label("FUERZA", "Muted", HorizontalAlignment.Right));
        _tensionBar = new TimeBar { CustomMinimumSize = new Vector2(230, 14), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        right.AddChild(_tensionBar);
        ui.AddChild(Anchor(right, Control.LayoutPreset.TopRight, new Vector2(-44, 28)));

        _hints = new VBoxContainer();
        _hints.AddThemeConstantOverride("separation", 8);
        ui.AddChild(Anchor(_hints, Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        _bigText = Label("", "Huge", HorizontalAlignment.Center);
        _bigText.AddThemeFontSizeOverride("font_size", 80);
        _bigText.CustomMinimumSize = new Vector2(1000, 120);
        _bigText.VerticalAlignment = VerticalAlignment.Center;
        ui.AddChild(Anchor(_bigText, Control.LayoutPreset.Center, new Vector2(0, -80)));

        _menu = new PanelContainer { ThemeTypeVariation = "Card", Visible = false, CustomMinimumSize = new Vector2(660, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(Label("CURLING", "Big", HorizontalAlignment.Center));
        _modeValue = MenuRow(box, _menuRows, "Modo");
        _levelValue = MenuRow(box, _menuRows, "Rival");
        MenuRow(box, _menuRows, "Volver");
        _menuRecord = Label("", "Muted", HorizontalAlignment.Center);
        _menuRecord.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_menuRecord);
        var hints = Hints(("↑ ↓", "Moverse"), ("← →", "Cambiar"), ("START", "¡A jugar!"));
        hints.Alignment = BoxContainer.AlignmentMode.Center;
        box.AddChild(hints);
        _menu.AddChild(box);
        ui.AddChild(Anchor(_menu, Control.LayoutPreset.Center, Vector2.Zero));

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Menú"));
        _endPanel = Panel(ui, "", out var endBox, ("START", "Otra vez"), ("SELECT", "Menú"));
        _endPanel.CustomMinimumSize = new Vector2(640, 0);
        _endTitle = (Label)endBox.GetChild(0);
        _endScore = Label("", "HugeAccent", HorizontalAlignment.Center);
        _endInfo = Label("", "Muted", HorizontalAlignment.Center);
        _endInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        endBox.AddChild(_endScore);
        endBox.MoveChild(_endScore, 1);
        endBox.AddChild(_endInfo);
        endBox.MoveChild(_endInfo, 2);

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    private void Big(string text, Color color, double hold = 0.8) => FlashText(_bigText, text, color, hold);

    /// <summary>Indicaciones de abajo a la izquierda; sin tecla, la línea es solo una nota.</summary>
    private void SetHints(params (string Key, string Action)[] items)
    {
        foreach (var child in _hints.GetChildren()) child.QueueFree();
        foreach (var (key, action) in items) _hints.AddChild(key == "" ? Label(action, "Muted") : Hint(key, action));
    }

    private string Keys(string mat, string keyboard) => MatInput.UsingKeyboard ? keyboard : mat;

    private string SoloKey => "curling";
    private string WinsKey => $"curling_victorias_{Levels[_levelIndex].Level.ToString().ToLowerInvariant()}";

    private void RefreshMenu()
    {
        for (int i = 0; i < _menuRows.Count; i++) _menuRows[i].ThemeTypeVariation = i == _menuRow ? "RowOn" : "RowOff";
        _modeValue.Text = $"‹  {(_rivalMode ? "Contra rival" : "Solitario")}  ›";
        _levelValue.Text = _rivalMode ? $"‹  {(Human ? "Humano" : Levels[_levelIndex].Name)}  ›" : "—";
        _menuRecord.Text = Human
            ? "2 entradas de 4 piedras cada jugador, por turnos en la misma alfombra. Jugador 1 rojo, jugador 2 amarillo."
            : _rivalMode
            ? $"2 entradas de 4 piedras cada uno. Victorias contra el rival {Levels[_levelIndex].Name.ToLowerInvariant()}: {Records.Get(WinsKey) ?? 0}"
            : $"8 piedras. Puntos al final: centro 10, rojo 6, blanco 3, azul 1. Récord: {Records.Get(SoloKey)?.ToString() ?? "—"}";
    }

    // ---------------------------------------------------------------- partida

    private void OpenMenu()
    {
        _phase = Phase.Menu;
        _pausePanel.Visible = false;
        _endPanel.Visible = false;
        RefreshMenu();
        ShowPanel(_menu);
        SetHints();
    }

    private void StartGame()
    {
        _menu.Visible = false;
        _end = 0;
        Array.Clear(_endTotals);
        StartEnd();
        Audio.Play("start");
    }

    private void StartEnd()
    {
        foreach (var view in _stoneViews.Values) view.QueueFree();
        _stoneViews.Clear();
        _sheet = new CurlingSheet();
        _sheet.Hit += (_, _, speed) => Audio.Impact("impact_mining", -10 + Math.Min(speed, 3f) * 3, 0.85f + Math.Min(speed, 3f) * 0.08f);
        _thrownThisEnd = 0;
        if (_rivalMode) Big($"ENTRADA {_end + 1}", Themes.Current.AccentText, 0.9);
        NextTurn();
    }

    private int StonesThisEnd => _rivalMode ? StonesPerEnd * 2 : SoloStones;

    /// <summary>En la segunda entrada empieza el rival (en la primera, el jugador).</summary>
    private int TeamFor(int throwIndex) => _rivalMode ? (throwIndex + _end) % 2 : 0;

    private void NextTurn()
    {
        _team = TeamFor(_thrownThisEnd);
        _aim = 0;
        _power.Stop();
        UpdateLabels();
        if (_team == 1 && !Human)
        {
            _phase = Phase.RivalThink;
            _pause = 0.6;
            Big("TURNO DEL RIVAL", new Color("#F2B807"), 0.6);
            SetHints(("SELECT", "Pausa"));
            return;
        }
        _phase = Phase.Choose;
        if (Human) Big($"TURNO DEL JUGADOR {_team + 1}", _team == 0 ? new Color("#E8413C") : new Color("#F2B807"), 0.9);
        SetHints((Keys("← →", "A D"), "Diestro / zurdo"), (Keys("✕ ○", "Q E"), "Apuntar"), ("↓", "Agacharse y coger fuerza"),
            ("SELECT", "Pausa"));
    }

    private void UpdateLabels()
    {
        int stoneNumber = _rivalMode ? _thrownThisEnd / 2 + 1 : _thrownThisEnd + 1;
        _modeLabel.Text = _rivalMode ? $"ENTRADA {_end + 1} DE {Ends}" : "SOLITARIO";
        _turnLabel.Text = _rivalMode
            ? $"Piedra {Math.Min(stoneNumber, StonesPerEnd)} de {StonesPerEnd} · " +
              (Human ? $"juega el jugador {_team + 1}" : $"rival {Levels[_levelIndex].Name.ToLowerInvariant()}")
            : $"Piedra {Math.Min(stoneNumber, SoloStones)} de {SoloStones}";
        _scoreLabel.Text = _rivalMode
            ? $"{Short(0)} {_endTotals[0]}  ·  {Short(1)} {_endTotals[1]}"
            : $"{_sheet.InPlay.Sum(CurlingSheet.SoloPoints)} PUNTOS";
        _handLabel.Text = HumanTurn ? (RightHanded ? "Diestro · curva →" : "Zurdo · curva ←") : "";
        _liveLabel.Text = _rivalMode ? LiveText() : "";
    }

    /// <summary>Quién se llevaría la entrada si acabase ahora (en curling solo se suma al final de cada entrada).</summary>
    private string LiveText()
    {
        var (team, points) = _sheet.EndScore();
        return team < 0 ? "En la casa: nadie puntúa" : $"En la casa: {Short(team)} +{points}";
    }

    private void Release()
    {
        int curl = RightHanded ? 1 : -1;
        _sheet.Throw(_team, _aim, (float)_tension, curl);
        _phase = Phase.Running;
        Audio.Play("start", 0.8f + (float)_tension * 0.4f);
        SetHints((Keys("✕ △", "Q Z"), "Barrendero izq. →"), (Keys("○ □", "E C"), "Barrendero der. ←"),
            ("", "Arriba fuerte, abajo suave"), ("", "Los dos arriba: más lejos"), ("", "Los dos abajo: frena"));
    }

    private void StoneStopped()
    {
        _thrownThisEnd++;
        _phase = Phase.Settled;
        _pause = SettlePause;
        UpdateLabels();
        var last = _sheet.Stones[^1];
        var t = Themes.Current;
        if (last.Out) Big("FUERA", t.H, 0.6);
        else if (_rivalMode)
        {
            var (team, points) = _sheet.EndScore();
            if (team >= 0)
            {
                Big($"{Short(team)} +{points}", team == 0 ? (Human ? new Color("#E8413C") : t.AccentText) : new Color("#F2B807"), 0.7);
                Audio.Play(team == 0 || Human ? "sparkle" : "pop");
            }
        }
        else if (!_rivalMode && CurlingSheet.SoloPoints(last) > 0)
        {
            int points = CurlingSheet.SoloPoints(last);
            Big($"+{points}", t.AccentText, 0.6);
            Audio.Play(points >= 6 ? "sparkle" : "pop");
        }
    }

    private void AfterPause()
    {
        if (_thrownThisEnd < StonesThisEnd)
        {
            NextTurn();
            return;
        }
        if (!_rivalMode)
        {
            FinishSolo();
            return;
        }
        var (team, points) = _sheet.EndScore();
        if (team >= 0) _endTotals[team] += points;
        string text = team < 0 ? "ENTRADA EN BLANCO"
            : Human ? $"ENTRADA PARA EL JUGADOR {team + 1} +{points}"
            : team == 0 ? $"¡ENTRADA PARA TI! +{points}" : $"ENTRADA DEL RIVAL +{points}";
        Big(text, team == 0 || Human ? Themes.Current.AccentText : Themes.Current.H, 1.3);
        Audio.Play(team == 0 || Human ? "stars3" : "miss");
        _end++;
        UpdateLabels();
        if (_end >= Ends)
        {
            GetTree().CreateTimer(1.6).Timeout += FinishRival;
            _phase = Phase.Over;
            return;
        }
        _phase = Phase.Settled;
        _pause = 1.8;
        _thrownThisEnd = int.MaxValue; // al acabar la pausa empieza otra entrada
    }

    private void FinishSolo()
    {
        _phase = Phase.Over;
        int score = _sheet.InPlay.Sum(CurlingSheet.SoloPoints);
        bool record = Records.Submit(_game, score);
        _endTitle.Text = "¡FIN!";
        _endScore.Text = $"{score} PUNTOS";
        int inHouse = _sheet.InPlay.Count(CurlingSheet.InHouse);
        _endInfo.Text = (record ? "¡Nuevo récord!" : $"Récord: {Records.Get(SoloKey)?.ToString() ?? "—"}") +
                        $"   ·   {inHouse} de {SoloStones} piedras en la casa";
        Audio.Play(record ? "stars3" : "sparkle");
        ShowPanel(_endPanel);
    }

    private void FinishRival()
    {
        int me = _endTotals[0], rival = _endTotals[1];
        if (Human)
        {
            // Entre dos personas no se guarda récord.
            _endTitle.Text = me == rival ? "EMPATE" : $"¡GANA EL JUGADOR {(me > rival ? 1 : 2)}!";
            _endScore.Text = $"{me} – {rival}";
            _endInfo.Text = "Jugador 1 (rojo) contra jugador 2 (amarillo)";
            Audio.Play("stars3");
            ShowPanel(_endPanel);
            return;
        }
        _endTitle.Text = me > rival ? "¡GANASTE!" : me < rival ? "GANA EL RIVAL" : "EMPATE";
        _endScore.Text = $"{me} – {rival}";
        if (me > rival) Records.Submit(WinsKey, (Records.Get(WinsKey) ?? 0) + 1);
        _endInfo.Text = $"Contra el rival {Levels[_levelIndex].Name.ToLowerInvariant()}   ·   victorias: {Records.Get(WinsKey) ?? 0}";
        Audio.Play(me > rival ? "stars3" : me == rival ? "sparkle" : "miss");
        ShowPanel(_endPanel);
    }

    // ---------------------------------------------------------------- cada fotograma

    private LoopSound? _slide;

    public override void _Process(double delta)
    {
        // Roce de las piedras sobre el hielo: más fuerte y más agudo cuanto más rápido va la más rápida.
        if (_slide is null)
        {
            _slide = new LoopSound("roce", -8);
            AddChild(_slide);
        }
        float fastest = _phase is Phase.Paused or Phase.Menu ? 0 : _sheet.InPlay.Select(st => st.Vel.Length()).DefaultIfEmpty(0).Max();
        _slide.Set(Mathf.Clamp(fastest / 2.4f, 0, 1), 0.7f + fastest * 0.15f);
        if (_phase is Phase.Paused or Phase.Menu) return;

        switch (_phase)
        {
            case Phase.Charge:
                _power.Advance(delta, CurlingSheet.TensionTime);
                break;
            case Phase.Running:
                if (HumanTurn)
                    _sheet.SetSweep(SweepFrom(Pad.Cross, Pad.Triangle), SweepFrom(Pad.Circle, Pad.Square));
                _sheet.Advance(delta);
                if (!_sheet.Moving) StoneStopped();
                break;
            case Phase.RivalThink:
                _pause -= delta;
                if (_pause <= 0) RivalThrow();
                break;
            case Phase.Settled:
                _pause -= delta;
                if (_pause <= 0)
                {
                    if (_thrownThisEnd == int.MaxValue) StartEnd();
                    else AfterPause();
                }
                break;
        }
        Render(delta);
    }

    private static SweepInput SweepFrom(Pad strong, Pad soft)
    {
        var input = MatInput.Instance;
        if (input.IsDown(strong)) return SweepInput.Strong;
        if (input.IsDown(soft)) return SweepInput.Soft;
        return SweepInput.None;
    }

    private void RivalThrow()
    {
        _rivalShot = CurlingAI.Choose(_sheet, 1, Levels[_levelIndex].Level, _rng);
        _aim = _rivalShot.Aim;
        _power.Stop(_rivalShot.Tension);
        _sheet.Throw(1, _rivalShot.Aim, _rivalShot.Tension, _rivalShot.Curl);
        _sheet.SetSweep(SweepInput.None, SweepInput.None);
        _phase = Phase.Running;
        Audio.Play("start", 0.9f);
    }

    // ---------------------------------------------------------------- dibujo

    private void Render(double delta)
    {
        foreach (var stone in _sheet.Stones)
        {
            var view = StoneView(stone);
            view.Visible = !stone.Out;
            view.Position = To3(stone.Pos, 0.03f);
        }

        var thrown = _sheet.Thrown;
        bool aiming = _phase is Phase.Choose or Phase.Charge;
        bool rightHand = HumanTurn ? RightHanded : _rivalShot.Curl >= 0;

        // Lanzador: de pie eligiendo, agachado cogiendo fuerza y deslizando al soltar; luego se queda atrás.
        string pose = _phase switch
        {
            Phase.Charge => "lanzador_agachado",
            Phase.Running when thrown is not null && thrown.Pos.Y < 4 => "lanzador_soltando",
            _ => "lanzador_de_pie",
        };
        _throwerMat.AlbedoTexture = Tex(pose);
        float tw = pose == "lanzador_de_pie" ? StandingWidth : PersonSize, th = HeightFor(pose, tw);
        // En la lámina, el agachado sujeta la piedra por debajo de él (mira hacia la cámara): volteo vertical para que
        // la sujete por delante, mirando a la casa, sin cambiar de mano.
        if (pose == "lanzador_agachado") th = -th;
        _throwerMesh.Transform = new Transform3D(new Basis(new Vector3(rightHand ? tw : -tw, 0, 0), new Vector3(0, 0, -th), Vector3.Up), Vector3.Zero);
        float throwerY = thrown is not null && thrown.Pos.Y < 4 ? thrown.Pos.Y - 0.45f : -0.45f;
        _thrower.Position = To3(new Vector2(rightHand ? -0.18f : 0.18f, throwerY), 0.04f);
        _thrower.Modulate(_team == 1);

        // Barrenderos: acompañan a la piedra del jugador mientras se mueve.
        bool sweepersOn = thrown is not null && HumanTurn && thrown.Pos.Y > 3;
        _leftSweeper.Visible = _rightSweeper.Visible = sweepersOn;
        if (sweepersOn)
        {
            _sweepClock += delta;
            var input = MatInput.Instance;
            // A los lados de la piedra, algo por delante: los cepillos barren delante de ella sin taparla.
            UpdateSweeper(_leftSweeper, _leftSweeperMesh, _leftMat, _leftSpray, thrown!.Pos + new Vector2(-0.62f, 0.75f), input.IsDown(Pad.Cross) || input.IsDown(Pad.Triangle), 1);
            UpdateSweeper(_rightSweeper, _rightSweeperMesh, _rightMat, _rightSpray, thrown.Pos + new Vector2(0.62f, 0.75f), input.IsDown(Pad.Circle) || input.IsDown(Pad.Square), -1);
        }

        // Flechas de apuntar y de curva.
        _aimArrow.Visible = _curlArrow.Visible = _aimLine.Visible = aiming && HumanTurn;
        _previewStone.Visible = aiming || _phase == Phase.RivalThink;
        _previewMat.AlbedoTexture = Tex(_team == 0 ? "piedra_roja" : "piedra_amarilla");
        _previewStone.Position = To3(Vector2.Zero, 0.03f);
        if (aiming)
        {
            var dir = new Vector2(_aim, CurlingSheet.TeeY).Normalized();
            float angle = -Mathf.Atan2(dir.X, dir.Y);
            _aimArrow.Position = To3(dir * 0.9f, 0.05f);
            _aimArrow.Rotation = new Vector3(0, angle, 0);
            // Línea de puntería: desde la piedra hasta 7 m por delante, en la dirección apuntada.
            const float lineLength = 7f;
            _aimLine.Position = To3(dir * (1.4f + lineLength / 2), 0.02f);
            _aimLine.Rotation = new Vector3(0, angle, 0);
            _aimLine.Scale = new Vector3(1, 1, lineLength);
            _curlArrow.Position = To3(new Vector2(RightHanded ? 0.45f : -0.45f, 1.1f), 0.05f);
            float cw = 0.5f, ch = HeightFor("flecha_curva", cw);
            _curlMesh.Transform = new Transform3D(new Basis(new Vector3(RightHanded ? cw : -cw, 0, 0), new Vector3(0, 0, -ch), Vector3.Up), Vector3.Zero);
        }

        _tensionBar.Value = (float)_tension;
        _tensionBar.Visible = HumanTurn;
        _houseView.Update(_sheet, aiming && HumanTurn ? _aim : null, _rivalMode);
        if (_phase == Phase.Running && !_rivalMode) _scoreLabel.Text = $"{_sheet.InPlay.Sum(CurlingSheet.SoloPoints)} PUNTOS";
        if (_rivalMode && _phase != Phase.Over) _liveLabel.Text = LiveText();

        PlaceCamera(false, (float)delta);
    }

    /// <param name="side">1 = barrendero izquierdo (dibujo tal cual), −1 = derecho (dibujo en espejo).</param>
    private void UpdateSweeper(Node3D sweeper, MeshInstance3D mesh, StandardMaterial3D mat, StandardMaterial3D spray, Vector2 at, bool sweeping, int side)
    {
        sweeper.Position = To3(at, 0.05f);
        bool frame = (int)(_sweepClock * 8) % 2 == 0;
        // Un «shh» del cepillo en cada pasada.
        if (sweeping && frame != (side > 0 ? _leftFrame : _rightFrame)) Audio.Synth("cepillo", -12, side > 0 ? 1f : 1.12f);
        if (side > 0) _leftFrame = frame;
        else _rightFrame = frame;
        string pose = sweeping ? (frame ? "barrendero_1" : "barrendero_2") : "barrendero_quieto";
        mat.AlbedoTexture = Tex(pose);
        mat.AlbedoColor = _team == 1 ? new Color(1f, 0.92f, 0.45f) : Colors.White; // los del jugador 2, de amarillo
        float w = pose == "barrendero_quieto" ? StandingWidth : PersonSize, h = HeightFor(pose, w);
        mesh.Transform = new Transform3D(new Basis(new Vector3(side * w, 0, 0), new Vector3(0, 0, -h), Vector3.Up), Vector3.Zero);
        spray.AlbedoColor = new Color(1, 1, 1, sweeping ? 0.9f : 0);
    }

    /// <summary>La cámara sigue a la piedra en marcha; entre tiros vuelve a la casa o al punto de lanzamiento.</summary>
    private void PlaceCamera(bool instant, float delta = 0)
    {
        // En marcha sigue a la piedra lanzada o, si ya se paró tras un choque, a la que siga moviéndose más adelantada.
        var followed = _sheet.Thrown ?? _sheet.InPlay.Where(st => st.Moving).OrderByDescending(st => st.Pos.Y).FirstOrDefault();
        float target = _phase switch
        {
            Phase.Running when followed is not null => Mathf.Clamp(followed.Pos.Y + 2.2f, 3.5f, CurlingSheet.TeeY - 0.6f),
            Phase.Running => CurlingSheet.TeeY - 0.6f,
            Phase.Settled or Phase.Over => CurlingSheet.TeeY - 0.6f,
            _ => 2.6f, // apuntando: se ve al lanzador entero y el principio de la pista
        };
        _cameraY = instant ? target : Mathf.Lerp(_cameraY, target, 1 - Mathf.Exp(-delta * 3));
        _camera.LookAtFromPosition(new Vector3(0, 30, -_cameraY), new Vector3(0, 0, -_cameraY), Vector3.Forward);
    }

    // ---------------------------------------------------------------- pisadas

    private void OnPressed(Pad pad)
    {
        switch (_phase)
        {
            case Phase.Menu:
                MenuPad(pad);
                break;
            case Phase.Choose:
                if (pad is Pad.Left or Pad.Right)
                {
                    RightHanded = pad == Pad.Right;
                    UpdateLabels();
                    Audio.Play("click", RightHanded ? 1.1f : 0.9f);
                }
                else if (pad is Pad.Cross or Pad.Circle)
                {
                    _aim = Mathf.Clamp(_aim + (pad == Pad.Circle ? AimStep : -AimStep), -CurlingSheet.MaxAim, CurlingSheet.MaxAim);
                    Audio.Play("click", 1.2f);
                }
                else if (pad == Pad.Down)
                {
                    _phase = Phase.Charge;
                    _power.Start();
                    Audio.Play("click", 0.7f);
                    SetHints(("↑", "¡Soltar!"), ("", "La fuerza sube y baja"), ("", "Suelta en el momento justo"));
                }
                else if (pad == Pad.Select) Pause();
                break;
            case Phase.Charge:
                if (pad == Pad.Up) Release();
                else if (pad == Pad.Select) Pause();
                break;
            case Phase.Running or Phase.RivalThink or Phase.Settled:
                if (pad == Pad.Select) Pause();
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _pausePanel.Visible = false;
                    _phase = _beforePause;
                }
                else if (pad is Pad.Select or Pad.Cross) OpenMenu();
                break;
            case Phase.Over when _endPanel.Visible:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _endPanel.Visible = false;
                    StartGame();
                }
                else if (pad is Pad.Select or Pad.Cross) OpenMenu();
                break;
        }
    }

    private void Pause()
    {
        _beforePause = _phase;
        _phase = Phase.Paused;
        ShowPanel(_pausePanel);
        Audio.Play("pop");
    }

    private void MenuPad(Pad pad)
    {
        switch (pad)
        {
            case Pad.Up or Pad.Down:
                _menuRow = Mathf.PosMod(_menuRow + (pad == Pad.Up ? -1 : 1), _menuRows.Count);
                if (_menuRow == 1 && !_rivalMode) _menuRow = Mathf.PosMod(_menuRow + (pad == Pad.Up ? -1 : 1), _menuRows.Count);
                Audio.Play("click", 1.1f);
                break;
            case Pad.Left or Pad.Right:
                if (_menuRow == 0) _rivalMode = !_rivalMode;
                else if (_menuRow == 1) _levelIndex = Mathf.PosMod(_levelIndex + (pad == Pad.Left ? -1 : 1), Levels.Length + 1);
                Audio.Play("pop");
                break;
            case Pad.Start or Pad.Circle:
                if (_menuRow == 2) _fade.GoTo(HubScreen.ScenePath);
                else StartGame();
                return;
            case Pad.Select or Pad.Cross:
                _fade.GoTo(HubScreen.ScenePath);
                return;
        }
        RefreshMenu();
    }
}

/// <summary>Atajo para teñir al lanzador del rival de amarillo (mismo dibujo, otro equipo).</summary>
internal static class ThrowerTint
{
    public static void Modulate(this Node3D thrower, bool rival)
    {
        foreach (var child in thrower.GetChildren())
            if (child is MeshInstance3D { Mesh: QuadMesh { Material: StandardMaterial3D mat } })
                mat.AlbedoColor = rival ? new Color(1f, 0.92f, 0.45f) : Colors.White;
    }
}

/// <summary>La casa vista de cerca en una esquina: anillos y piedras, para ver quién está más cerca del centro.</summary>
public partial class HouseView : Control
{
    private readonly List<(Vector2 Pos, int Team)> _stones = new();

    private float? _aim;

    /// <summary>Piedras cerca de la casa y, mientras se apunta, a qué punto de la línea del centro se apunta.</summary>
    private readonly HashSet<Vector2> _counting = new();

    public void Update(CurlingSheet sheet, float? aim = null, bool markCounting = false)
    {
        _aim = aim;
        _counting.Clear();
        if (markCounting) foreach (var s in sheet.CountingStones()) _counting.Add(s.Pos);
        _stones.Clear();
        foreach (var s in sheet.InPlay)
            if ((s.Pos - CurlingSheet.Tee).Length() < CurlingSheet.HouseR + 0.6f) _stones.Add((s.Pos, s.Team));
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = Size / 2;
        float scale = Size.X / 2 / (CurlingSheet.HouseR + 0.4f);
        var bg = new StyleBoxFlat { BgColor = new Color("#E8F2F8") };
        bg.SetCornerRadiusAll(16);
        DrawStyleBox(bg, new Rect2(Vector2.Zero, Size));
        var rings = new (float R, Color Color)[]
        {
            (CurlingSheet.HouseR, new Color("#2E6BD6")), (1.22f, Colors.White), (0.61f, new Color("#E8413C")), (0.15f, Colors.White),
        };
        foreach (var (r, color) in rings) DrawCircle(c, r * scale, color);
        if (_aim is { } aim)
        {
            var p = c + new Vector2(aim, 0) * scale;
            var cyan = new Color("#2FD6FF");
            DrawArc(p, 9, 0, Mathf.Tau, 24, cyan, 3, true);
            DrawLine(p + new Vector2(-14, 0), p + new Vector2(14, 0), cyan, 2);
            DrawLine(p + new Vector2(0, -14), p + new Vector2(0, 14), cyan, 2);
        }
        foreach (var (pos, team) in _stones)
        {
            var p = c + new Vector2(pos.X - CurlingSheet.Tee.X, -(pos.Y - CurlingSheet.Tee.Y)) * scale;
            float r = CurlingSheet.StoneR * scale;
            if (_counting.Contains(pos)) DrawArc(p, r + 6, 0, Mathf.Tau, 24, new Color("#2FD6FF"), 3, true); // puntúa
            DrawCircle(p, r + 1.5f, new Color("#3A3F48"));
            DrawCircle(p, r * 0.6f, team == 0 ? new Color("#E8413C") : new Color("#F2B807"));
        }
    }
}
