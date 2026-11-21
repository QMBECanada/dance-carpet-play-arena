using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Laberinto: un tablero que se inclina hacia la casilla que pisas (las 8 direcciones) y una bola de acero que rueda
/// hasta la meta. Cuatro niveles (assets/laberintos/nivelN.txt): el 1 sin trampas, el 2 con hielo, el 3 con hielo e
/// imán y el 4 con hielo, imán y puertas, más algunos agujeros. Caer devuelve la bola al inicio o al último punto de
/// control; el tiempo sigue corriendo. El récord es el tiempo.
/// </summary>
public partial class MazeGame : Node3D
{
    public const string ScenePath = "res://scenes/Maze.tscn";

    private sealed record Level(string Name, string Traps, double Par3, double Par2);

    private static readonly Level[] Levels =
    {
        new("Nivel 1", "Sin trampas", 25, 40),
        new("Nivel 2", "Hielo y agujeros", 35, 55),
        new("Nivel 3", "Hielo, imanes y agujeros", 45, 70),
        new("Nivel 4", "Hielo, imanes, puertas y agujeros", 55, 85),
    };

    // Física (unidades = casillas del laberinto).
    private const float BallR = 0.3f, Accel = 15f, MaxSpeed = 8.5f;
    private const float Friction = 1.1f, IceFriction = 0.06f, Bounce = 0.35f;
    private const float FallRadius = 0.3f, GoalRadius = 0.33f, MagnetRange = 1.9f, MagnetPull = 10f;
    private const float MaxTiltDeg = 8f, TiltIn = 2.2f, TiltOut = 4f, BoardLift = 2f;
    private const double Step = 1.0 / 240.0, DoorPeriod = 3.2, DoorClosed = 1.7;

    private enum Phase { Menu, Play, Falling, Won, Paused, Over }

    private sealed class Door
    {
        public Vector2I Cell;
        public double Offset;
        public bool Closed;
        public Node3D View = null!;
    }

    private static int _levelIndex;
    private static bool _runFromStart;
    private static double _runTime;

    private Phase _phase = Phase.Menu, _beforePause;
    private string[] _map = Array.Empty<string>();
    private int _w, _h;
    private Vector2 _start, _respawn, _goal, _pos, _vel, _tilt;
    private readonly List<Vector2> _holes = new(), _magnets = new();
    private readonly List<(Vector2 Pos, Light Light)> _checkpoints = new();
    private readonly Dictionary<Vector2I, Door> _doors = new();
    private readonly List<Node3D> _spinners = new();

    // Puertas: altura cuando están abiertas (hundidas en el tablero) y velocidad al subir o bajar.
    private const float DoorOpenY = -0.48f, DoorSpeed = 3f, MagnetSpinSpeed = 2.5f;
    private double _time, _accum, _fallTimer, _clock;
    private int _falls;
    private LoopSound? _roll;

    private Node3D _board = null!, _ball = null!;
    private Label _levelLabel = null!, _timeLabel = null!, _infoLabel = null!, _bigText = null!;
    private PanelContainer _menu = null!, _pausePanel = null!, _endPanel = null!;
    private Label _menuLevel = null!, _menuRecord = null!, _endTitle = null!, _endTime = null!, _endInfo = null!, _endHint = null!;
    private Stars _stars = null!;
    private ScreenFade _fade = null!;

    private Level Current => Levels[_levelIndex];

    private sealed class Light
    {
        public StandardMaterial3D Mat = null!;
        public Texture2D On = null!, Off = null!;
        public void Set(bool on) => Mat.AlbedoTexture = on ? On : Off;
    }

    public override void _Ready()
    {
        Audio.Music("chillout");
        AddChild(new StageBackdrop { Focus = new Vector2(0, 0), BackdropPosition = new Vector3(0, 4, -18), Fog = false });
        var camera = new Camera3D { Fov = 46 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 15.5f + BoardLift, 8.5f), new Vector3(0, BoardLift, 0.8f), Vector3.Up);

        BuildUi();
        MatInput.Instance.Pressed += OnPressed;
        OpenMenu();
    }

    public override void _ExitTree() => MatInput.Instance.Pressed -= OnPressed;

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Root(this);

        var left = new VBoxContainer();
        left.AddThemeConstantOverride("separation", -4);
        _levelLabel = Label("", "Heading");
        _infoLabel = Label("", "Muted");
        left.AddChild(_levelLabel);
        left.AddChild(_infoLabel);
        ui.AddChild(Anchor(left, Control.LayoutPreset.TopLeft, new Vector2(44, 28)));

        _timeLabel = Label("0,0 s", "Huge", HorizontalAlignment.Center);
        _timeLabel.CustomMinimumSize = new Vector2(320, 0);
        ui.AddChild(Anchor(_timeLabel, Control.LayoutPreset.CenterTop, new Vector2(0, 20)));

        string keys = MatInput.UsingKeyboard ? "Q W E · A D · Z X C" : "✕ ↑ ○ · ← → · △ ↓ □";
        ui.AddChild(Anchor(Hints((keys, "Inclinar el tablero"), ("SELECT", "Pausa")), Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        _bigText = Label("", "Huge", HorizontalAlignment.Center);
        _bigText.AddThemeFontSizeOverride("font_size", 96);
        _bigText.CustomMinimumSize = new Vector2(1000, 140);
        _bigText.VerticalAlignment = VerticalAlignment.Center;
        ui.AddChild(Anchor(_bigText, Control.LayoutPreset.Center, new Vector2(0, -60)));

        _menu = Panel(ui, "LABERINTO", out var menuBox, ("← →", "Nivel"), ("START", "¡A jugar!"), ("SELECT", "Volver"));
        _menu.CustomMinimumSize = new Vector2(640, 0);
        _menuLevel = Label("", "HugeAccent", HorizontalAlignment.Center);
        _menuLevel.AddThemeFontSizeOverride("font_size", 44);
        _menuRecord = Label("", "Muted", HorizontalAlignment.Center);
        _menuRecord.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var how = Label("Pisa hacia donde quieres inclinar el tablero; las esquinas inclinan en diagonal.", "Value",
            HorizontalAlignment.Center);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (var (label, i) in new[] { (_menuLevel, 1), (_menuRecord, 2), (how, 3) })
        {
            menuBox.AddChild(label);
            menuBox.MoveChild(label, i);
        }

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _endPanel = Panel(ui, "", out var endBox, ("START", "…"), ("SELECT", "Menú"));
        _endPanel.CustomMinimumSize = new Vector2(640, 0);
        _endTitle = (Label)endBox.GetChild(0);
        _stars = new Stars { CustomMinimumSize = new Vector2(0, 70) };
        _endTime = Label("", "HugeAccent", HorizontalAlignment.Center);
        _endInfo = Label("", "Muted", HorizontalAlignment.Center);
        _endInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (var (control, i) in new (Control, int)[] { (_stars, 1), (_endTime, 2), (_endInfo, 3) })
        {
            endBox.AddChild(control);
            endBox.MoveChild(control, i);
        }
        _endHint = (Label)((HBoxContainer)((HBoxContainer)endBox.GetChild(4)).GetChild(0)).GetChild(1);

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    private void Big(string text, Color color, double hold = 0.6)
     => FlashText(_bigText, text, color, hold);

    private static string Seconds(double s) => $"{s:0.0} s".Replace('.', ',');

    private static string LevelKey(int i) => $"maze_nivel{i + 1}";

    // ---------------------------------------------------------------- menú

    private void OpenMenu()
    {
        _phase = Phase.Menu;
        _endPanel.Visible = false;
        _pausePanel.Visible = false;
        LoadLevel(_levelIndex);
        RefreshMenu();
        ShowPanel(_menu);
    }

    private void RefreshMenu()
    {
        _menuLevel.Text = $"‹  {Current.Name}  ›";
        int? best = Records.Get(LevelKey(_levelIndex));
        int? total = Records.Get("maze");
        _menuRecord.Text = $"{Current.Traps}\nRécord: {(best is { } b ? Seconds(b / 10.0) : "—")}" +
                           (_levelIndex == 0 ? $"   ·   Los 4 seguidos: {(total is { } t ? $"{t} s" : "—")}" : "");
    }

    // ---------------------------------------------------------------- niveles

    private void LoadLevel(int index)
    {
        _levelIndex = index;
        _board?.QueueFree();
        _holes.Clear();
        _magnets.Clear();
        _checkpoints.Clear();
        _doors.Clear();
        _spinners.Clear();

        string text = FileAccess.GetFileAsString($"res://assets/laberintos/nivel{index + 1}.txt");
        _map = MazeLevels.Parse(text);
        _h = _map.Length;
        _w = _map[0].Length;

        // Elevado sobre el suelo del escenario: al inclinarse (hasta 8°), ningún borde baja de él y no se corta.
        _board = new Node3D { Position = new Vector3(0, BoardLift, 0) };
        AddChild(_board);
        var t = Themes.Current;
        var baseMat = new StandardMaterial3D { AlbedoColor = new Color("#0F1C33"), Roughness = 0.45f, Metallic = 0.15f };
        var wallMat = new StandardMaterial3D
        {
            AlbedoColor = new Color("#D9DFE7"), Metallic = 0.5f, Roughness = 0.28f, EmissionEnabled = true,
            Emission = new Color("#2E3542"),
        };
        var iceMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.94f, 1f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true, Emission = new Color(0.25f, 0.5f, 0.6f), Roughness = 0.05f, Metallic = 0.3f,
        };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("#04070D"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var steel = new StandardMaterial3D { AlbedoColor = new Color("#C8D0DA"), Metallic = 0.6f, Roughness = 0.25f };
        var amber = new Color("#FFC21A");
        var doorMat = new StandardMaterial3D { AlbedoColor = amber, EmissionEnabled = true, Emission = amber * 0.35f, Roughness = 0.4f };

        // Base y marco.
        _board.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(_w + 0.6f, 0.3f, _h + 0.6f), Material = baseMat },
            Position = new Vector3(0, -0.15f, 0),
        });

        for (int y = 0; y < _h; y++)
        for (int x = 0; x < _w; x++)
        {
            char c = _map[y][x];
            var center = Cell3(x, y);
            var cell = new Vector2(x + 0.5f, y + 0.5f);
            switch (c)
            {
                case '#':
                    _board.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1, 0.5f, 1), Material = wallMat }, Position = center + new Vector3(0, 0.25f, 0),
                    });
                    break;
                case '~':
                    _board.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(1, 0.02f, 1), Material = iceMat }, Position = center + new Vector3(0, 0.012f, 0),
                    });
                    break;
                case 'O' or 'M':
                    AddHole(center, dark, steel);
                    (c == 'O' ? _holes : _magnets).Add(cell);
                    if (c == 'M') AddMagnet(center);
                    break;
                case 'G':
                    _goal = cell;
                    AddHole(center, dark, steel);
                    AddPart("estrella_on", center + new Vector3(0, 0.03f, 0), 0.62f);
                    _board.AddChild(new OmniLight3D { Position = center + Vector3.Up * 0.8f, LightColor = new Color("#FFC21A"), LightEnergy = 1.6f, OmniRange = 2.2f });
                    break;
                case 'S':
                    _start = cell;
                    AddPart("luz_amarilla_off", center + new Vector3(0, 0.012f, 0), 0.8f);
                    break;
                case 'C':
                    var light = new Light
                    {
                        Mat = PartMat("luz_cian_off"), On = PartTex("luz_cian_on"), Off = PartTex("luz_cian_off"),
                    };
                    AddDecal(light.Mat, center + new Vector3(0, 0.012f, 0), 0.85f);
                    _checkpoints.Add((cell, light));
                    break;
                case 'D':
                    var view = new Node3D { Position = center };
                    view.AddChild(new MeshInstance3D
                    {
                        Mesh = new BoxMesh { Size = new Vector3(0.94f, 0.5f, 0.94f), Material = doorMat }, Position = new Vector3(0, 0.25f, 0),
                    });
                    _board.AddChild(view);
                    _doors[new Vector2I(x, y)] = new Door { Cell = new Vector2I(x, y), Offset = _doors.Count * 0.8, View = view };
                    break;
            }
        }

        _ball = new Node3D();
        var ballTex = PartTex("bola");
        _ball.AddChild(new Sprite3D
        {
            Texture = ballTex, PixelSize = BallR * 2.1f / ballTex.GetWidth(), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Shaded = false, AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        });
        _board.AddChild(_ball);

        _respawn = _start;
        _pos = _start;
        _vel = Vector2.Zero;
        _tilt = Vector2.Zero;
        _levelLabel.Text = Current.Name.ToUpperInvariant();
        PlaceBall(1);
    }

    private Vector3 Cell3(int x, int y) => new(x + 0.5f - _w / 2f, 0, y + 0.5f - _h / 2f);
    private Vector3 Board3(Vector2 p, float h = 0) => new(p.X - _w / 2f, h, p.Y - _h / 2f);

    private static Texture2D PartTex(string name) => GD.Load<Texture2D>($"res://assets/pinball/{name}.png");

    private static StandardMaterial3D PartMat(string name) => new()
    {
        AlbedoTexture = PartTex(name), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
    };

    private void AddDecal(Material mat, Vector3 at, float size) => _board.AddChild(new MeshInstance3D
    {
        Mesh = new PlaneMesh { Size = new Vector2(size, size), Material = mat }, Position = at,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    });

    private void AddPart(string name, Vector3 at, float size) => AddDecal(PartMat(name), at, size);

    private void AddHole(Vector3 at, Material dark, Material steel)
    {
        _board.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.38f, Height = 0.01f, Material = dark }, Position = at + new Vector3(0, 0.008f, 0),
        });
        _board.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.36f, OuterRadius = 0.46f, Material = steel }, Position = at + new Vector3(0, 0.02f, 0),
        });
    }

    /// <summary>Imán: dos aros rojo y azul que giran alrededor de su agujero.</summary>
    private void AddMagnet(Vector3 at)
    {
        var spin = new Node3D { Position = at + new Vector3(0, 0.05f, 0) };
        foreach (var (color, radius) in new[] { (new Color("#E8413C"), 0.62f), (new Color("#29A8E0"), 0.8f) })
        {
            spin.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh
                {
                    InnerRadius = radius - 0.05f, OuterRadius = radius,
                    Material = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color * 0.6f },
                },
                Scale = new Vector3(1, 0.4f, 1),
            });
        }
        _spinners.Add(spin);
        _board.AddChild(spin);
    }

    private void StartLevel()
    {
        _menu.Visible = false;
        _endPanel.Visible = false;
        LoadLevel(_levelIndex);
        _time = 0;
        _falls = 0;
        foreach (var (_, light) in _checkpoints) light.Set(false);
        _phase = Phase.Play;
        Big("¡YA!", Themes.Current.AccentText, 0.4);
        Audio.Play("start");
        UpdateInfo();
    }

    private void UpdateInfo() => _infoLabel.Text = $"{Current.Traps}  ·  Caídas: {_falls}";

    // ---------------------------------------------------------------- física

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_phase is Phase.Paused or Phase.Menu or Phase.Over or Phase.Won)
        {
            _roll?.Set(0);
            AnimateBoard(delta, Vector2.Zero);
            return;
        }

        _time += delta;
        _timeLabel.Text = Seconds(_time);
        var target = TiltInput();
        // Solo desarrollo: «-- --inclinar» mantiene el tablero inclinado en diagonal para verlo.
        if (OS.GetCmdlineUserArgs().Contains("--inclinar")) target = new Vector2(0.707f, -0.707f);
        float rate = target == Vector2.Zero ? TiltOut : TiltIn;
        _tilt = _tilt.MoveToward(target, rate * (float)delta);

        UpdateDoors();
        if (_phase == Phase.Falling)
        {
            _fallTimer -= delta;
            float k = (float)Math.Clamp(_fallTimer / 0.6, 0, 1);
            PlaceBall(k);
            if (_fallTimer <= 0) Respawn();
        }
        else
        {
            _accum += delta;
            while (_accum >= Step && _phase == Phase.Play)
            {
                _accum -= Step;
                Simulate((float)Step);
            }
            PlaceBall(1);
        }
        AnimateBoard(delta, _tilt);
        // Rodar de la bola: más fuerte y grave cuanto más rápido va.
        if (_roll is null)
        {
            _roll = new LoopSound("rodar", -10);
            AddChild(_roll);
        }
        float speed = _phase == Phase.Play ? _vel.Length() : 0;
        _roll.Set(Mathf.Clamp(speed / MaxSpeed, 0, 1), 0.7f + speed * 0.06f);
    }

    /// <summary>Hacia dónde se inclina el tablero según las casillas pisadas (la suma, normalizada).</summary>
    private static Vector2 TiltInput()
    {
        var input = MatInput.Instance;
        var dir = Vector2.Zero;
        void Add(Pad pad, float x, float y)
        {
            if (input.IsDown(pad)) dir += new Vector2(x, y);
        }
        Add(Pad.Up, 0, -1);
        Add(Pad.Down, 0, 1);
        Add(Pad.Left, -1, 0);
        Add(Pad.Right, 1, 0);
        Add(Pad.Cross, -0.707f, -0.707f);
        Add(Pad.Circle, 0.707f, -0.707f);
        Add(Pad.Triangle, -0.707f, 0.707f);
        Add(Pad.Square, 0.707f, 0.707f);
        return dir.LengthSquared() > 1 ? dir.Normalized() : dir;
    }

    private void Simulate(float dt)
    {
        var cellPos = new Vector2I((int)_pos.X, (int)_pos.Y);
        bool onIce = Inside(cellPos) && _map[cellPos.Y][cellPos.X] == '~';

        _vel += _tilt * Accel * dt;
        foreach (var magnet in _magnets)
        {
            var d = magnet - _pos;
            float dist = d.Length();
            if (dist < MagnetRange && dist > 0.01f) _vel += d / dist * MagnetPull * (1 - dist / MagnetRange) * dt;
        }
        _vel *= Mathf.Exp(-(onIce ? IceFriction : Friction) * dt);
        if (_vel.Length() > MaxSpeed) _vel = _vel.Normalized() * MaxSpeed;
        _pos += _vel * dt;

        // Choques con las paredes (y las puertas cerradas) de alrededor.
        for (int y = cellPos.Y - 1; y <= cellPos.Y + 1; y++)
        for (int x = cellPos.X - 1; x <= cellPos.X + 1; x++)
        {
            if (!Inside(new Vector2I(x, y)) || !Solid(x, y)) continue;
            var q = new Vector2(Mathf.Clamp(_pos.X, x, x + 1), Mathf.Clamp(_pos.Y, y, y + 1));
            var d = _pos - q;
            float dist = d.Length();
            if (dist >= BallR) continue;
            var n = dist > 1e-4f ? d / dist : (_pos - new Vector2(x + 0.5f, y + 0.5f)).Normalized();
            _pos = q + n * BallR;
            float vn = _vel.Dot(n);
            if (vn < 0)
            {
                _vel -= (1 + Bounce) * vn * n;
                if (-vn > 1.5f) Audio.Impact("impact_metal_light", -16 + Mathf.Min(-vn, 8) * 1.5f, 0.8f + Mathf.Min(-vn, 8) * 0.04f);
            }
        }

        foreach (var (pos, light) in _checkpoints)
        {
            if ((_pos - pos).Length() > 0.45f || _respawn == pos) continue;
            _respawn = pos;
            light.Set(true);
            Big("¡CONTROL!", Themes.Current.V, 0.4);
            Audio.Play("combo");
        }

        if ((_pos - _goal).Length() < GoalRadius)
        {
            Win();
            return;
        }
        foreach (var hole in _holes.Concat(_magnets))
        {
            if ((_pos - hole).Length() >= FallRadius) continue;
            Fall(hole);
            return;
        }
    }

    private bool Inside(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < _w && c.Y < _h;

    private bool Solid(int x, int y)
    {
        char c = _map[y][x];
        if (c == '#') return true;
        return c == 'D' && _doors[new Vector2I(x, y)].Closed;
    }

    private void UpdateDoors()
    {
        var ballCell = new Vector2I((int)_pos.X, (int)_pos.Y);
        float move = DoorSpeed * (float)GetProcessDeltaTime();
        foreach (var door in _doors.Values)
        {
            double phase = Mathf.PosMod(_time + door.Offset, DoorPeriod);
            bool closed = phase < DoorClosed;
            // No cerrar encima de la bola: espera a que salga.
            if (closed && !door.Closed && ballCell == door.Cell) closed = false;
            door.Closed = closed;
            var p = door.View.Position;
            door.View.Position = new Vector3(p.X, Mathf.MoveToward(p.Y, closed ? 0 : DoorOpenY, move), p.Z);
        }
    }

    private void Fall(Vector2 hole)
    {
        _phase = Phase.Falling;
        _fallTimer = 0.6;
        _falls++;
        _pos = hole;
        _vel = Vector2.Zero;
        Audio.Impact("impact_tin_medium", -3, 0.75f);
        Audio.Play("miss");
        Big("¡AL AGUJERO!", Themes.Current.H, 0.5);
        UpdateInfo();
    }

    private void Respawn()
    {
        _pos = _respawn;
        _vel = Vector2.Zero;
        _tilt = Vector2.Zero;
        _phase = Phase.Play;
        Audio.Play("pop", 1.2f);
    }

    private void PlaceBall(float scale)
    {
        _ball.Position = Board3(_pos, BallR * scale);
        _ball.Scale = Vector3.One * Mathf.Max(scale, 0.05f);
    }

    private void AnimateBoard(double delta, Vector2 tilt)
    {
        float max = Mathf.DegToRad(MaxTiltDeg);
        var target = new Vector3(tilt.Y * max, 0, -tilt.X * max);
        _board.Rotation = _board.Rotation.Lerp(target, 1 - Mathf.Exp(-(float)delta * 12));
        foreach (var spinner in _spinners) spinner.RotateY((float)delta * MagnetSpinSpeed);
    }

    // ---------------------------------------------------------------- meta

    private void Win()
    {
        _phase = Phase.Won;
        Audio.Play("stars3");
        Big("¡META!", Themes.Current.AccentText, 0.7);
        int tenths = (int)Math.Round(_time * 10);
        bool record = Records.Submit(LevelKey(_levelIndex), tenths, lowerIsBetter: true);
        int stars = _time <= Current.Par3 ? 3 : _time <= Current.Par2 ? 2 : 1;

        if (_levelIndex == 0) _runTime = 0;
        _runTime += _time;
        bool last = _levelIndex == Levels.Length - 1;
        bool totalRecord = false;
        if (last && _runFromStart) totalRecord = Records.Submit(GameCatalog.All.First(g => g.Id == "maze"), (int)Math.Round(_runTime));

        _endTitle.Text = last ? "¡LABERINTO SUPERADO!" : $"¡{Current.Name.ToUpperInvariant()} SUPERADO!";
        _stars.Count = stars;
        _endTime.Text = Seconds(_time);
        _endInfo.Text = (record ? "¡Nuevo récord del nivel!" : $"Récord del nivel: {Seconds((Records.Get(LevelKey(_levelIndex)) ?? tenths) / 10.0)}") +
                        $"   ·   Caídas: {_falls}" +
                        (last && _runFromStart ? $"\nLos 4 niveles: {Math.Round(_runTime)} s{(totalRecord ? " · ¡récord!" : "")}" : "");
        _endHint.Text = last ? "Otra vez" : "Siguiente nivel";
        GetTree().CreateTimer(1.0).Timeout += () => ShowPanel(_endPanel);
    }

    // ---------------------------------------------------------------- pisadas

    private void OnPressed(Pad pad)
    {
        switch (_phase)
        {
            case Phase.Menu:
                if (pad is Pad.Left or Pad.Right)
                {
                    _levelIndex = Mathf.PosMod(_levelIndex + (pad == Pad.Left ? -1 : 1), Levels.Length);
                    LoadLevel(_levelIndex);
                    RefreshMenu();
                    Audio.Play("pop");
                }
                else if (pad is Pad.Start or Pad.Circle)
                {
                    _runFromStart = _levelIndex == 0;
                    StartLevel();
                }
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
            case Phase.Play or Phase.Falling:
                if (pad == Pad.Select)
                {
                    _beforePause = _phase;
                    _phase = Phase.Paused;
                    ShowPanel(_pausePanel);
                }
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _pausePanel.Visible = false;
                    _phase = _beforePause;
                }
                else if (pad is Pad.Select or Pad.Cross) OpenMenu();
                break;
            case Phase.Won when _endPanel.Visible:
                if (pad is Pad.Start or Pad.Circle)
                {
                    if (_levelIndex < Levels.Length - 1) _levelIndex++;
                    else
                    {
                        _levelIndex = 0;
                        _runFromStart = true;
                    }
                    StartLevel();
                }
                else if (pad is Pad.Select or Pad.Cross) OpenMenu();
                break;
        }
    }
}

/// <summary>Estrellas de la pantalla final (0 a 3).</summary>
public partial class Stars : Control
{
    private int _count;

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        for (int i = 0; i < 3; i++)
        {
            var c = new Vector2(Size.X / 2 + (i - 1) * 70, Size.Y / 2);
            var points = new Vector2[10];
            for (int k = 0; k < 10; k++)
            {
                float a = -Mathf.Pi / 2 + k * Mathf.Pi / 5;
                float r = k % 2 == 0 ? 30 : 13;
                points[k] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            DrawColoredPolygon(points, i < _count ? t.AccentText : new Color(t.Text, 0.15f));
        }
    }
}
