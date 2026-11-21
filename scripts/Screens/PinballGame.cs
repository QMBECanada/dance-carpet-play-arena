using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;
using static AlfombraBaile.PinballTable;

namespace AlfombraBaile;

/// <summary>
/// Pantalla del pinball: dibuja la mesa (vista cenital ortográfica sobre el dibujo de GPT), hace sonar y muestra lo que
/// cuenta <see cref="PinballTable"/>, que tiene toda la física y las reglas. Columna izquierda de la alfombra = palanca
/// izquierda, derecha = derecha; la fila da la fuerza (arriba suave, centro normal, abajo fuerte). Con la bola en el
/// lanzador, ↓ tensa (se destensa en 2 s) y ↑ dispara; con la bola en juego, ↑ y ↓ menean la mesa. El centro es descanso.
/// </summary>
public partial class PinballGame : Node3D
{
    public const string ScenePath = "res://scenes/Pinball.tscn";

    /// <summary>Una luz de la mesa: cambia entre su dibujo encendido y apagado.</summary>
    private sealed class Light
    {
        public StandardMaterial3D Mat = null!;
        public Texture2D On = null!, Off = null!;
        public void Set(bool on) => Mat.AlbedoTexture = on ? On : Off;
    }

    private enum Phase { Play, Paused, Over }

    private readonly GameInfo _game = GameCatalog.All.First(g => g.Id == "pinball");
    private PinballTable _table = new();
    private Phase _phase = Phase.Play;

    // ---------------------------------------------------------------- vista

    private readonly Dictionary<Ball, Node3D> _ballViews = new();
    private readonly List<Node3D> _targetViews = new();
    private readonly List<(StandardMaterial3D Cap, Node3D View)> _bumperViews = new();
    private readonly float[] _bumperFlash = new float[3];
    private readonly List<Light> _laneLights = new();
    private Node3D _leftView = null!, _rightView = null!;
    private StandardMaterial3D _ballMat = null!;
    private float _spin, _spinAngle;
    private Node3D _spinner = null!;
    private Node3D _plunger = null!;
    private readonly List<StandardMaterial3D> _slingMats = new();
    private readonly List<(int Value, Light Light)> _multInserts = new();
    private Light _star = null!, _arrows = null!;
    private float _slingFlash;
    private Camera3D _camera = null!;
    private Vector3 _cameraHome;
    private float _shake;
    private Label _scoreLabel = null!, _multLabel = null!, _bigText = null!;
    private BallsView _ballsView = null!;
    private TimeBar _tensionBar = null!;
    private VBoxContainer _laneHints = null!, _playHints = null!;
    private PanelContainer _pausePanel = null!, _endPanel = null!;
    private Label _endScore = null!, _endRecord = null!;
    private ScreenFade _fade = null!;
    private LoopSound? _roll;

    private static Vector3 To3(Vector2 p, float h = 0) => new(p.X - 5, h, -p.Y);

    public override void _Ready()
    {
        Audio.Music("pinball");
        AddChild(new StageBackdrop { Focus = new Vector2(0, -10), BackdropPosition = new Vector3(0, -2, -10), Fog = false });
        // Vista desde arriba, sin perspectiva: la mesa entera en vertical en el centro de la pantalla.
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 21.3f };
        AddChild(_camera);
        _camera.LookAtFromPosition(new Vector3(0, 30, -10.17f), new Vector3(0, 0, -10.17f), Vector3.Forward);
        _cameraHome = _camera.Position;

        _table.Happened += OnTableEvent;
        BuildTable();
        BuildUi();
        MatInput.Instance.Pressed += OnPressed;
        Big("BOLA 1", Themes.Current.AccentText, 0.8);
        Audio.Play("start");

        // Solo desarrollo: «-- --multibola» saca la segunda bola a los 3 s; «-- --grabar-fisica=ruta» graba la física.
        if (OS.GetCmdlineUserArgs().Contains("--multibola")) GetTree().CreateTimer(3).Timeout += () => _table.StartMultiball();
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--grabar-fisica=")) Callable.From(() => RecordPhysics(arg["--grabar-fisica=".Length..])).CallDeferred();
    }

    public override void _ExitTree() => MatInput.Instance.Pressed -= OnPressed;

    // ---------------------------------------------------------------- construcción de la mesa

    private static Texture2D Tex(string name) => GD.Load<Texture2D>($"res://assets/pinball/{name}.png");

    /// <summary>Material para una pieza dibujada (lámina de GPT): sin luz propia, con transparencia.</summary>
    private static StandardMaterial3D PartMat(string name) => new()
    {
        AlbedoTexture = Tex(name),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
    };

    private static Vector3 Dir3(Vector2 d) => new(d.X, 0, -d.Y);

    /// <summary>Calcomanía plana: un cuadrado con la pieza, estirado según los ejes u (ancho) y v (alto de la imagen).</summary>
    private static MeshInstance3D Decal(Node3D parent, Material mat, Vector3 center, Vector3 u, Vector3 v)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One, Material = mat },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(new Basis(u, v, Vector3.Up), center),
        };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>Calcomanía girada <paramref name="angle"/> radianes en el plano de la mesa, dentro de su propio nodo.</summary>
    private Node3D FlatPart(Material mat, Vector2 center, float width, float height, float angle = 0, float lift = 0.02f)
    {
        var holder = new Node3D { Position = To3(center, lift) };
        AddChild(holder);
        var u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var v = new Vector2(-u.Y, u.X);
        Decal(holder, mat, Vector3.Zero, Dir3(u * width), Dir3(v * height));
        return holder;
    }

    private Light MakeLight(string name, bool on = false)
    {
        var light = new Light { Mat = PartMat(name + "_off"), On = Tex(name + "_on"), Off = Tex(name + "_off") };
        light.Set(on);
        return light;
    }

    private void BuildTable()
    {
        // Superficie: el dibujo de la mesa, sin luz propia para que se vea con sus colores.
        var tableTex = Tex("mesa");
        float tw = tableTex.GetWidth() * K, th = tableTex.GetHeight() * K;
        var center = Px(tableTex.GetWidth() / 2f, tableTex.GetHeight() / 2f);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh
            {
                Size = new Vector2(tw, th),
                Material = new StandardMaterial3D
                {
                    AlbedoTexture = tableTex, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                },
            },
            Position = To3(center, 0.006f), // justo encima del suelo del escenario
        });

        foreach (var sling in _table.Slings) SlingPart(sling);

        // Carriles de arriba: una luz con su letra en cada carril.
        const string letters = "COOL";
        for (int i = 0; i < _table.Lanes.Count; i++)
        {
            var c = _table.Lanes[i].C;
            var light = MakeLight("luz_amarilla");
            FlatPart(light.Mat, c, 0.95f, 0.95f, 0, 0.01f);
            AddChild(new Label3D
            {
                Text = letters[i].ToString(), Font = Fonts.Display, FontSize = 120, PixelSize = 0.004f, Modulate = new Color("#3A2400"),
                RotationDegrees = new Vector3(-90, 0, 0), Position = To3(c, 0.05f),
            });
            _laneLights.Add(light);
        }

        // Multiplicadores ×2 a ×5 entre el logo y las gomas.
        for (int m = 2; m <= 5; m++)
        {
            var c = new Vector2(Axis - 1.5f + (m - 2), 6.25f);
            var light = MakeLight("luz_cian");
            FlatPart(light.Mat, c, 0.8f, 0.8f, 0, 0.01f);
            AddChild(new Label3D
            {
                Text = $"×{m}", Font = Fonts.Display, FontSize = 70, PixelSize = 0.004f, Modulate = new Color("#06303A"),
                RotationDegrees = new Vector3(-90, 0, 0), Position = To3(c, 0.05f),
            });
            _multInserts.Add((m, light));
        }

        // Flechas hacia arriba y estrella de «bola salvada» sobre el desagüe.
        _arrows = MakeLight("flechas");
        FlatPart(_arrows.Mat, new Vector2(Axis, 4.45f), 0.62f, 0.93f, 0, 0.01f);
        _star = MakeLight("estrella");
        FlatPart(_star.Mat, new Vector2(Axis, 2.95f), 0.85f, 0.82f, 0, 0.01f);

        var targetMat = PartMat("diana");
        foreach (var target in _table.Targets)
            _targetViews.Add(FlatPart(targetMat, (target.A + target.B) / 2, 0.42f, 0.7f, 0, 0.02f));

        string[] caps = { "seta_roja", "seta_azul", "seta_amarilla" };
        for (int i = 0; i < _table.Bumpers.Count; i++)
        {
            var cap = PartMat(caps[i]);
            _bumperViews.Add((cap, FlatPart(cap, _table.Bumpers[i].C, 1.55f, 1.6f, 0, 0.03f)));
        }

        // Palancas: el pivote está al 21 % del ancho del dibujo; la derecha es la izquierda en espejo.
        var flipTex = Tex("palanca");
        float pxScale = FlipperLen / 197f;
        float fw = flipTex.GetWidth() * pxScale, fh = flipTex.GetHeight() * pxScale;
        float offset = (flipTex.GetWidth() / 2f - 62f) * pxScale;
        var flipMat = PartMat("palanca");
        Node3D FlipperView(Flipper f)
        {
            var view = new Node3D { Position = To3(f.Pivot, 0.04f) };
            Decal(view, flipMat, new Vector3(offset, 0, 0), new Vector3(fw, 0, 0), new Vector3(0, 0, f.Left ? -fh : fh));
            AddChild(view);
            return view;
        }
        _leftView = FlipperView(_table.Left);
        _rightView = FlipperView(_table.Right);

        // Agujero: hueco oscuro con aro cromado.
        var dark = new StandardMaterial3D { AlbedoColor = new Color("#05080F"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var steel = new StandardMaterial3D { AlbedoColor = new Color("#C8D0DA"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var steelDark = new StandardMaterial3D { AlbedoColor = new Color("#7C8696"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.45f, Height = 0.01f, Material = dark },
            Position = To3(SaucerC, 0.012f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.55f, Material = steel },
            Position = To3(SaucerC, 0.02f),
        });
        AddChild(new Label3D
        {
            Text = "SUAVE", Font = Fonts.Display, FontSize = 46, PixelSize = 0.004f, Modulate = new Color("#7CE7FF"),
            RotationDegrees = new Vector3(-90, 0, 0), Position = To3(SaucerC + new Vector2(0, -0.8f), 0.05f),
        });

        // Molinete: dos postes y una paleta que gira sobre el eje horizontal (desde arriba se ve estrecharse).
        foreach (float sx in new[] { -1f, 1f })
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.1f, Material = steelDark },
                Position = To3(new Vector2(Axis + sx * (SpinnerHalf + 0.1f), SpinnerY), 0.05f),
            });
        _spinner = new Node3D { Position = To3(new Vector2(Axis, SpinnerY), 0.06f) };
        _spinner.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(SpinnerHalf * 2, 0.04f, 0.32f), Material = steel } });
        AddChild(_spinner);
        AddChild(new Label3D
        {
            Text = "FUERTE", Font = Fonts.Display, FontSize = 46, PixelSize = 0.004f, Modulate = new Color("#FFD23F"),
            RotationDegrees = new Vector3(-90, 0, 0), Position = To3(new Vector2(Axis, SpinnerY - 0.55f), 0.05f),
        });

        // Lanzador de muelle y bola (vista desde arriba, como el resto).
        _plunger = new Node3D();
        Decal(_plunger, PartMat("lanzador"), Vector3.Zero, new Vector3(0.3f, 0, 0), new Vector3(0, 0, -1.06f));
        AddChild(_plunger);
        _ballMat = PartMat("bola");
    }

    private Node3D MakeBallView()
    {
        var view = new Node3D();
        float size = BallR * 2 * 141f / 133f;
        Decal(view, _ballMat, Vector3.Zero, new Vector3(size, 0, 0), new Vector3(0, 0, -size * 148f / 141f));
        AddChild(view);
        return view;
    }

    /// <summary>
    /// Goma: el dibujo se deforma (transformación afín) para que sus esquinas caigan en las tres esquinas de la goma
    /// de la mesa. En la imagen, las esquinas están en (38 %, 99 %) arriba, (8 %, 8 %) abajo-izquierda y (87 %, 11 %)
    /// abajo-derecha.
    /// </summary>
    private void SlingPart(Sling sling)
    {
        var sTop = new Vector2(0.38f, 0.99f);
        var sCorner = new Vector2(0.08f, 0.08f);
        var sBottom = new Vector2(0.87f, 0.11f);
        Vector2 e1 = sTop - sCorner, e2 = sBottom - sCorner;
        Vector2 w1 = sling.Top - sling.Corner, w2 = sling.Bottom - sling.Corner;
        float det = e1.X * e2.Y - e2.X * e1.Y;
        // [U V] · [e1 e2] = [w1 w2]  →  [U V] = [w1 w2] · inv([e1 e2])
        var u = (w1 * e2.Y - w2 * e1.Y) / det;
        var v = (w2 * e1.X - w1 * e2.X) / det;
        var origin = sling.Corner - u * sCorner.X - v * sCorner.Y;
        var mid = origin + u * 0.5f + v * 0.5f;

        var mat = PartMat("goma");
        _slingMats.Add(mat);
        Decal(this, mat, To3(mid, 0.02f), Dir3(u), Dir3(v));
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Root(this);
        var scoreBox = new VBoxContainer();
        scoreBox.AddThemeConstantOverride("separation", -6);
        scoreBox.AddChild(Label("PUNTOS", "Muted"));
        _scoreLabel = Label("0", "Huge");
        scoreBox.AddChild(_scoreLabel);
        _multLabel = Label("", "Accent");
        scoreBox.AddChild(_multLabel);
        ui.AddChild(Anchor(scoreBox, Control.LayoutPreset.TopLeft, new Vector2(44, 26)));

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 4);
        right.AddChild(Label("BOLAS", "Muted", HorizontalAlignment.Right));
        _ballsView = new BallsView { Count = PinballTable.StartBalls, CustomMinimumSize = new Vector2(120, 30), SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        right.AddChild(_ballsView);
        int? record = Records.Get(_game.Id);
        right.AddChild(Label($"Récord: {(record?.ToString() ?? "—")}", "Muted", HorizontalAlignment.Right));
        _tensionBar = new TimeBar { CustomMinimumSize = new Vector2(200, 14), Value = 0, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        right.AddChild(Label("LANZADOR", "Muted", HorizontalAlignment.Right));
        right.AddChild(_tensionBar);
        ui.AddChild(Anchor(right, Control.LayoutPreset.TopRight, new Vector2(-44, 30)));

        bool kb = MatInput.UsingKeyboard;
        // En columna, a la izquierda de la mesa, para no taparla.
        VBoxContainer Column(params (string, string)[] items)
        {
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 8);
            foreach (var (key, action) in items) column.AddChild(Hint(key, action));
            return column;
        }
        _laneHints = Column(("↓", "Tensar"), ("↑", "Lanzar"), ("SELECT", "Pausa"));
        _playHints = Column((kb ? "Q A Z" : "✕ ← △", "Palanca izquierda"), (kb ? "E D C" : "○ → □", "Palanca derecha"),
            ("↑ ↓", "Menear"), ("SELECT", "Pausa"));
        var hints = new VBoxContainer();
        hints.AddThemeConstantOverride("separation", 10);
        var strength = Label("Palancas:\narriba suave\ncentro normal\nabajo fuerte", "Muted");
        hints.AddChild(strength);
        hints.AddChild(_laneHints);
        hints.AddChild(_playHints);
        ui.AddChild(Anchor(hints, Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        _bigText = Label("", "Huge", HorizontalAlignment.Center);
        _bigText.AddThemeFontSizeOverride("font_size", 90);
        _bigText.CustomMinimumSize = new Vector2(1000, 130);
        _bigText.VerticalAlignment = VerticalAlignment.Center;
        ui.AddChild(Anchor(_bigText, Control.LayoutPreset.Center, new Vector2(0, -60)));

        _pausePanel = Panel(ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _endPanel = Panel(ui, "¡FIN DE LA PARTIDA!", out var endBox, ("START", "Otra vez"), ("SELECT", "Volver"));
        _endScore = Label("", "HugeAccent", HorizontalAlignment.Center);
        _endRecord = Label("", "Heading", HorizontalAlignment.Center);
        endBox.AddChild(_endScore);
        endBox.MoveChild(_endScore, 1);
        endBox.AddChild(_endRecord);
        endBox.MoveChild(_endRecord, 2);

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    private void Big(string text, Color color, double hold = 0.5) => FlashText(_bigText, text, color, hold);

    // ---------------------------------------------------------------- lo que cuenta la mesa

    private void OnTableEvent(PinballEvent e)
    {
        var t = Themes.Current;
        switch (e.Kind)
        {
            case PinballEventKind.Score:
                _scoreLabel.Text = FormatScore(_table.Score);
                if (e.Value >= 250) Fx.Popup(this, To3(e.At, 0.8f), $"+{e.Value}", t.Accent, t.Bg, 70);
                break;
            case PinballEventKind.Launch:
                Audio.Synth("muelle", -4, e.Pitch);
                break;
            case PinballEventKind.FlipperUp:
                Audio.Impact("impact_plank_medium", -9, 1.35f);
                break;
            case PinballEventKind.Sling:
                _slingFlash = 1;
                Audio.Impact("impact_plate_light", -5, 1.2f);
                break;
            case PinballEventKind.Bumper:
                _bumperFlash[e.Value] = 1;
                Audio.Impact("impact_metal_light", -3, e.Pitch * 1.1f);
                Audio.Impact("impact_bell_heavy", -18, 1.6f + e.Value * 0.15f); // cada seta con su campanita
                break;
            case PinballEventKind.SaucerCatch:
                Big("¡SUAVE!", new Color("#7CE7FF"), 0.5);
                Audio.Impact("impact_tin_medium", -5, 1.1f);
                Audio.Play("combo");
                break;
            case PinballEventKind.SaucerEject:
                Audio.Play("bonk", 1.4f);
                break;
            case PinballEventKind.Spinner:
                _spin = Mathf.Max(_spin, e.Value * 2.5f);
                if (e.Value > 0) Audio.Impact("impact_tin_medium", -11, 1.5f + Math.Min(e.Value, 15) * 0.03f);
                if (e.Value >= 10)
                {
                    Big($"¡FUERTE! {e.Value} VUELTAS", t.AccentText, 0.5);
                    Audio.Play("sparkle");
                }
                break;
            case PinballEventKind.LaneLit:
                Audio.Play("sparkle");
                break;
            case PinballEventKind.SkillShot:
                Big("¡TIRO PERFECTO!", t.AccentText, 0.7);
                Audio.Play("stars3");
                break;
            case PinballEventKind.Cool:
                Big($"¡STAY COOL! ×{e.Value}", t.AccentText, 0.8);
                Audio.Play("combo");
                break;
            case PinballEventKind.TargetDown:
                Audio.Impact("impact_plank_medium", -4, 0.95f);
                break;
            case PinballEventKind.ExtraBall:
                _ballsView.Count = _table.BallsLeft;
                Big("¡BOLA EXTRA!", t.AccentText, 0.8);
                Audio.Play("stars3");
                break;
            case PinballEventKind.Multiball:
                Big("¡MULTIBOLA!", t.AccentText, 0.9);
                Audio.Play("stars3");
                break;
            case PinballEventKind.TargetsBonus:
                Big("¡DIANAS!", t.AccentText, 0.6);
                Audio.Play("combo");
                break;
            case PinballEventKind.MultiballEnd:
                Big("FIN DE LA MULTIBOLA", t.H, 0.6);
                Audio.Play("miss", 1.2f);
                break;
            case PinballEventKind.BallSaved:
                Big("¡BOLA SALVADA!", t.AccentText, 0.7);
                Audio.Play("combo");
                break;
            case PinballEventKind.BallLost:
                _ballsView.Count = _table.BallsLeft;
                Audio.Impact("impact_tin_medium", -4, 0.7f); // la bola cae por el desagüe
                Audio.Play("miss");
                if (!_table.IsOver) Big($"BOLA {e.Value}", t.H, 0.8);
                break;
            case PinballEventKind.GameOver:
                GameOver();
                break;
            case PinballEventKind.Nudge:
                _shake = 0.25f;
                Audio.Impact("impact_wood_light", -2, 0.7f); // golpe a la mesa
                if (WarnNudges(e.Value)) Big("¡CUIDADO!", t.H, 0.4);
                break;
            case PinballEventKind.Tilt:
                Big("TILT", t.H, 1.2);
                Audio.Play("miss", 0.7f);
                break;
        }
    }

    private static string FormatScore(int score) => score.ToString("N0").Replace(",", ".");

    private void GameOver()
    {
        _phase = Phase.Over;
        int score = _table.Score;
        bool record = Records.Submit(_game, score);
        Big("¡FIN!", Themes.Current.H, 0.6);
        GetTree().CreateTimer(1.2).Timeout += () =>
        {
            _endScore.Text = $"{FormatScore(score)} PUNTOS";
            _endRecord.Text = record ? "¡NUEVO RÉCORD!" : $"Récord: {Records.Get(_game.Id)?.ToString() ?? "—"}";
            _endRecord.ThemeTypeVariation = record ? "Heading" : "Muted";
            ShowPanel(_endPanel);
            Audio.Play(record ? "stars3" : "sparkle");
        };
    }

    // ---------------------------------------------------------------- controles

    private void OnPressed(Pad pad)
    {
        switch (_phase)
        {
            case Phase.Play:
                if (pad == Pad.Select)
                {
                    _phase = Phase.Paused;
                    ShowPanel(_pausePanel);
                    Audio.Play("pop");
                }
                else if (_table.BallOnPlunger is not null)
                {
                    if (pad == Pad.Down)
                    {
                        _table.PullPlunger();
                        Audio.Play("click", 0.7f);
                    }
                    else if (pad == Pad.Up) _table.ReleasePlunger();
                }
                else if (pad is Pad.Up or Pad.Down) _table.Nudge(pad == Pad.Up);
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle)
                {
                    _pausePanel.Visible = false;
                    _phase = Phase.Play;
                }
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
            case Phase.Over when _endPanel.Visible:
                if (pad is Pad.Start or Pad.Circle) _fade.GoTo(ScenePath);
                else if (pad is Pad.Select or Pad.Cross) _fade.GoTo(HubScreen.ScenePath);
                break;
        }
    }

    /// <summary>Lo que pide cada columna de la alfombra (la casilla más fuerte pisada gana).</summary>
    private static FlipperInput ColumnInput(Pad soft, Pad normal, Pad strong)
    {
        var input = MatInput.Instance;
        if (input.IsDown(strong)) return FlipperInput.Strong;
        if (input.IsDown(normal)) return FlipperInput.Normal;
        if (input.IsDown(soft)) return FlipperInput.Soft;
        return FlipperInput.None;
    }

    public override void _Process(double delta)
    {
        // Rodar de las bolas sobre la mesa, según la más rápida.
        if (_roll is null)
        {
            _roll = new LoopSound("rodar", -16);
            AddChild(_roll);
        }
        float speed = _phase is Phase.Paused ? 0 : _table.Balls.Where(b => !b.Resting && !b.Captured).Select(b => b.Vel.Length()).DefaultIfEmpty(0).Max();
        _roll.Set(Mathf.Clamp(speed / 18f, 0, 1), 0.8f + speed * 0.02f);
        if (_phase is Phase.Paused) return;
        _table.SetFlippers(ColumnInput(Pad.Cross, Pad.Left, Pad.Triangle), ColumnInput(Pad.Circle, Pad.Right, Pad.Square));
        _table.Advance(delta);
        Render(delta);
    }

    // ---------------------------------------------------------------- grabación (solo desarrollo)

    /// <summary>
    /// «-- --grabar-fisica=ruta»: juega sola 100 s de simulación (sin esperar a tiempo real) con azar fijo y un jugador
    /// automático, y guarda la trayectoria. Sirve para comprobar que un cambio de código no altera la física.
    /// </summary>
    private void RecordPhysics(string path)
    {
        var lines = PinballAutoplay.Run();
        System.IO.File.WriteAllLines(path, lines);
        GD.Print($"Física grabada en {path}: {lines.Count} líneas · {lines[^1]}");
        GetTree().Quit();
    }

    // ---------------------------------------------------------------- dibujo

    private void Render(double delta)
    {
        // Bolas: crear o quitar sus dibujos según las que haya en la mesa.
        foreach (var ball in _table.Balls)
            if (!_ballViews.ContainsKey(ball)) _ballViews[ball] = MakeBallView();
        foreach (var gone in _ballViews.Keys.Except(_table.Balls).ToList())
        {
            _ballViews[gone].QueueFree();
            _ballViews.Remove(gone);
        }
        foreach (var (ball, view) in _ballViews)
        {
            view.Position = To3(ball.Pos, 0.06f);
            view.Visible = !_table.IsOver;
        }

        bool onPlunger = _table.BallOnPlunger is not null;
        // La punta del muelle toca la bola en reposo; al tensar baja un poco.
        _plunger.Position = To3(new Vector2(LaunchRest.X, 0.42f - (float)_table.Tension * 0.3f), 0.02f);
        _tensionBar.Value = onPlunger ? (float)_table.Tension : 0;
        _laneHints.Visible = onPlunger;
        _playHints.Visible = !onPlunger;

        _leftView.Rotation = new Vector3(0, _table.Left.Angle, 0);
        _rightView.Rotation = new Vector3(0, _table.Right.Angle, 0);
        for (int i = 0; i < _targetViews.Count; i++) _targetViews[i].Visible = _table.Targets[i].Active;
        for (int i = 0; i < _bumperViews.Count; i++)
        {
            _bumperFlash[i] = Mathf.Max(0, _bumperFlash[i] - (float)delta * 4);
            _bumperViews[i].Cap.AlbedoColor = Colors.White * (1 + _bumperFlash[i] * 0.6f);
            _bumperViews[i].View.Scale = Vector3.One * (1 + _bumperFlash[i] * 0.12f);
        }

        bool blink = Mathf.Sin((float)_table.Clock * 10) > 0;
        for (int i = 0; i < _laneLights.Count; i++)
        {
            bool skill = _table.SkillLive && onPlunger && i == _table.SkillLane;
            _laneLights[i].Set(_table.Lanes[i].Lit || (skill && blink));
        }
        foreach (var (value, light) in _multInserts) light.Set(_table.Multiplier >= value);
        _multLabel.Text = _table.Multiplier > 1 ? $"×{_table.Multiplier}" : "";
        _slingFlash = Mathf.Max(0, _slingFlash - (float)delta * 5);
        foreach (var mat in _slingMats) mat.AlbedoColor = Colors.White * (1 + _slingFlash * 0.6f);
        _star.Set(_table.BallSave > 0 && !onPlunger && blink);
        _arrows.Set(Mathf.Sin((float)_table.Clock * 4) > 0);
        _spinAngle += _spin * (float)delta * Mathf.Tau;
        _spin = Mathf.Max(0, _spin - (float)delta * 6);
        _spinner.Rotation = new Vector3(_spinAngle, 0, 0);

        _shake = Mathf.Max(0, _shake - (float)delta);
        _camera.Position = _cameraHome + new Vector3((GD.Randf() - 0.5f) * _shake, 0, (GD.Randf() - 0.5f) * _shake);
    }
}

/// <summary>Bolas que quedan, dibujadas como pequeñas esferas.</summary>
public partial class BallsView : Control
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
        float r = Size.Y * 0.38f;
        for (int i = 0; i < _count; i++)
        {
            var c = new Vector2(Size.X - r - i * (r * 2 + 8), Size.Y / 2);
            DrawCircle(c, r, t.Light ? t.Text.Lightened(0.3f) : new Color("#D5DAE2"));
            DrawCircle(c + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.3f, Colors.White);
        }
    }
}
