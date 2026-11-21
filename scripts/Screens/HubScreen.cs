using System.Collections.Generic;
using System.Text;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Pantalla principal: carrusel 3D de fichas. ← → elige, START u O entra, SELECT abre los ajustes.
/// </summary>
public partial class HubScreen : Node3D
{
    public const string ScenePath = "res://scenes/Hub.tscn";

    private enum Mode { Browse, Launching, Info, Settings, Credits }

    private static int _remembered;

    private readonly List<Card3D> _cards = new();
    private Mode _mode = Mode.Browse;
    private float _scroll;
    private int _target;
    private float _launch;
    private double _time;
    private Tween? _scrollTween;

    private Label _name = null!, _blurb = null!, _status = null!;
    private RichTextLabel _record = null!;
    private SideArrow _left = null!, _right = null!;
    private PanelContainer _info = null!;
    private Label _infoName = null!, _infoBlurb = null!;
    private SettingsPanel _settings = null!;
    private PanelContainer _credits = null!;
    private ColorRect _dim = null!;
    private ScreenFade _fade = null!;

    private int Count => _cards.Count;
    private int CenterIndex => Mathf.PosMod(_target, Count);
    private GameInfo Current => _cards[CenterIndex].Game;

    public override void _Ready()
    {
        Audio.Music("menu");
        var mat = MatInput.Instance;
        if (mat.HasMat && !mat.IsCalibrated && !CalibrationScreen.Dismissed)
        {
            Callable.From(() => GetTree().ChangeSceneToFile(CalibrationScreen.ScenePath)).CallDeferred();
            return;
        }

        AddChild(new StageBackdrop { Focus = new Vector2(0, 0.6f), Image = "res://assets/portada/fondo.png" });
        var camera = new Camera3D { Fov = 46 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 1.45f, 6.6f), new Vector3(0, 1.05f, 0), Vector3.Up);

        var carousel = new Node3D { Position = new Vector3(0, 1.3f, 0) };
        AddChild(carousel);
        foreach (var game in GameCatalog.All)
        {
            var card = new Card3D(game);
            carousel.AddChild(card);
            _cards.Add(card);
        }
        _target = _remembered;
        _scroll = _target;

        BuildUi();
        UpdateCaption();
        UpdateStatus();

        mat.Pressed += OnPad;
        mat.DeviceChanged += UpdateStatus;
        Themes.Changed += OnThemeChanged;
    }

    public override void _ExitTree()
    {
        if (_cards.Count == 0) return;
        MatInput.Instance.Pressed -= OnPad;
        MatInput.Instance.DeviceChanged -= UpdateStatus;
        Themes.Changed -= OnThemeChanged;
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        var ui = Ui.Root(this);

        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 14);
        title.AddChild(Ui.Label("DANCE CARPET", "TitleAccent"));
        title.AddChild(Ui.Label("PLAY ARENA", "Title"));
        var head = new VBoxContainer();
        head.AddThemeConstantOverride("separation", 0);
        head.AddChild(title);
        // Actividad: pasos y tiempo jugando, hoy y en total.
        var activity = Ui.Label("", "Muted");
        if (Activity.Instance is { } a)
            activity.Text = $"Hoy: {Activity.Thousands(a.TodaySteps)} pasos · {Activity.Duration(a.TodaySeconds)}" +
                            $"     Total: {Activity.Thousands(a.TotalSteps)} pasos · {Activity.Duration(a.TotalSeconds)}";
        head.AddChild(activity);
        ui.AddChild(Ui.Anchor(head, Control.LayoutPreset.TopLeft, new Vector2(44, 30)));

        var statusChip = new PanelContainer { ThemeTypeVariation = "Status" };
        _status = Ui.Label("", "Muted");
        statusChip.AddChild(_status);
        ui.AddChild(Ui.Anchor(statusChip, Control.LayoutPreset.TopRight, new Vector2(-44, 36)));

        var caption = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0), Alignment = BoxContainer.AlignmentMode.Center };
        caption.AddThemeConstantOverride("separation", 4);
        _name = Ui.Label("", "Big", HorizontalAlignment.Center);
        _blurb = Ui.Label("", "Muted", HorizontalAlignment.Center);
        _record = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _record.AddThemeFontOverride("normal_font", Fonts.Body);
        _record.AddThemeFontOverride("bold_font", Fonts.Bold);
        _record.AddThemeFontSizeOverride("normal_font_size", 21);
        _record.AddThemeFontSizeOverride("bold_font_size", 21);
        caption.AddChild(_name);
        caption.AddChild(_blurb);
        caption.AddChild(_record);
        // Sobre un panel: debajo, la imagen de fondo tiene dibujada la alfombra y el texto se perdería.
        var captionPanel = new PanelContainer { ThemeTypeVariation = "Caption" };
        captionPanel.AddChild(caption);
        ui.AddChild(Ui.Anchor(captionPanel, Control.LayoutPreset.CenterBottom, new Vector2(0, -96)));

        _left = new SideArrow { PointsRight = false, CustomMinimumSize = new Vector2(64, 64) };
        _right = new SideArrow { PointsRight = true, CustomMinimumSize = new Vector2(64, 64) };
        ui.AddChild(Ui.Anchor(_left, Control.LayoutPreset.CenterLeft, new Vector2(44, -60)));
        ui.AddChild(Ui.Anchor(_right, Control.LayoutPreset.CenterRight, new Vector2(-44, -60)));

        var hints = Ui.Hints(("← →", "Elegir"), ("START", "Jugar"), ("SELECT", "Ajustes"), ("ESPACIO", "Salir"));
        ui.AddChild(Ui.Anchor(hints, Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        const float mirrorWidth = 132;
        var mirror = new MatMirror { CustomMinimumSize = new Vector2(mirrorWidth, MatMirror.HeightFor(mirrorWidth)) };
        ui.AddChild(Ui.Anchor(mirror, Control.LayoutPreset.BottomRight, new Vector2(-44, -30)));

        // Velo detrás de los paneles (ajustes, créditos, «próximamente»): el carrusel no se ve a través.
        _dim = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(_dim);

        _info = new PanelContainer { ThemeTypeVariation = "Card", Visible = false };
        var infoBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        infoBox.AddThemeConstantOverride("separation", 10);
        _infoName = Ui.Label("", "Huge", HorizontalAlignment.Center);
        var soon = Ui.Label("PRÓXIMAMENTE", "HugeAccent", HorizontalAlignment.Center);
        _infoBlurb = Ui.Label("", "Muted", HorizontalAlignment.Center);
        var infoHints = Ui.Hints(("SELECT", "Volver"));
        infoHints.Alignment = BoxContainer.AlignmentMode.Center;
        infoBox.AddChild(_infoName);
        infoBox.AddChild(soon);
        infoBox.AddChild(_infoBlurb);
        infoBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        infoBox.AddChild(infoHints);
        _info.AddChild(infoBox);
        ui.AddChild(Ui.Anchor(_info, Control.LayoutPreset.Center, Vector2.Zero));

        _settings = new SettingsPanel { Visible = false };
        _settings.Closed += () => _mode = Mode.Browse;
        _settings.CalibrateRequested += () => _fade.GoTo(CalibrationScreen.ScenePath);
        _settings.CreditsRequested += OpenCredits;
        ui.AddChild(Ui.Anchor(_settings, Control.LayoutPreset.Center, Vector2.Zero));
        _credits = BuildCredits(ui);

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    private void UpdateCaption()
    {
        var game = Current;
        _name.Text = game.Name.ToUpperInvariant();
        _blurb.Text = game.Blurb;
        int? best = Records.Get(game.Id);
        string accent = Themes.Current.AccentText.ToHtml(false);
        _record.Text = best is null
            ? "[color=#" + new Color(Themes.Current.Text, 0.6f).ToHtml() + "]Sin récord todavía[/color]"
            : $"Récord  [b][color=#{accent}]{FormatNumber(best.Value)}[/color][/b]  {game.Unit}";
    }

    private void UpdateStatus()
    {
        var mat = MatInput.Instance;
        _status.Text = !mat.HasMat ? "Sin alfombra: usando el teclado"
            : mat.IsCalibrated ? "Alfombra lista"
            : "Alfombra sin calibrar · SELECT → Calibrar";
    }

    private void OnThemeChanged()
    {
        foreach (var card in _cards) card.ApplyTheme(Themes.Current);
        UpdateCaption();
    }

    private static string FormatNumber(int value)
    {
        string digits = System.Math.Abs(value).ToString();
        var sb = new StringBuilder();
        for (int i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append('.');
            sb.Append(digits[i]);
        }
        return (value < 0 ? "-" : "") + sb;
    }

    // ---------------------------------------------------------------- control

    private void OnPad(Pad pad)
    {
        switch (_mode)
        {
            case Mode.Browse:
                if (pad == Pad.Left) Move(-1);
                else if (pad == Pad.Right) Move(1);
                else if (pad is Pad.Start or Pad.Circle) Launch();
                else if (pad == Pad.Select) OpenSettings();
                break;
            case Mode.Info:
                if (pad is Pad.Select or Pad.Cross or Pad.Start or Pad.Circle) CloseInfo();
                break;
            case Mode.Settings:
                _settings.HandlePad(pad);
                break;
            case Mode.Credits:
                if (pad is Pad.Select or Pad.Cross or Pad.Start or Pad.Circle) CloseCredits();
                break;
        }
    }

    private void Move(int step)
    {
        _target += step;
        _remembered = CenterIndex;
        (step < 0 ? _left : _right).Flash();
        Audio.Play("click", step < 0 ? 0.92f : 1.08f);
        UpdateCaption();

        _scrollTween?.Kill();
        _scrollTween = CreateTween();
        _scrollTween.TweenMethod(Callable.From<float>(v => _scroll = v), _scroll, (float)_target, 0.55)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void Launch()
    {
        _mode = Mode.Launching;
        Audio.Play("start");
        var game = Current;
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(v => _launch = v), 0f, 1f, 0.75)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(() =>
        {
            if (game.Scene is { } scene)
            {
                _fade.GoTo(scene);
                return;
            }
            _infoName.Text = game.Name.ToUpperInvariant();
            _infoBlurb.Text = game.Blurb;
            _info.Visible = true;
            _info.Modulate = new Color(1, 1, 1, 0);
            CreateTween().TweenProperty(_info, "modulate:a", 1.0, 0.25);
            _mode = Mode.Info;
        }));
    }

    private void CloseInfo()
    {
        _mode = Mode.Launching;
        var tween = CreateTween();
        tween.TweenProperty(_info, "modulate:a", 0.0, 0.2);
        tween.TweenCallback(Callable.From(() => _info.Visible = false));
        tween.TweenMethod(Callable.From<float>(v => _launch = v), 1f, 0f, 0.6)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => _mode = Mode.Browse));
    }

    private static PanelContainer BuildCredits(Control ui)
    {
        var panel = Ui.Panel(ui, "CRÉDITOS", out var box, ("SELECT", "Volver"));
        panel.CustomMinimumSize = new Vector2(640, 0);
        int at = 1;
        void Add(Control c) => box.AddChild(c);
        var people = new (string Role, string Name, string? Link, string? Uri)[]
        {
            ("Programación, diseño y test", "Claude", "claude.ai", "https://claude.ai"),
            ("FDE, arnés y dirección", "Vicente Simón", "linkedin.com/in/vicentesimon", "https://www.linkedin.com/in/vicentesimon"),
            ("Planteamiento de juegos", "Claude y Vicente Simón", null, null),
            ("Ayudante de diseño visual", "GPT", "chatgpt.com", "https://chatgpt.com"),
            ("Música", "Suno", "suno.com", "https://suno.com/"),
            ("Efectos de sonido", "Kenney (CC0)", "kenney.nl", "https://kenney.nl"),
        };
        foreach (var (role, name, link, uri) in people)
        {
            var entry = new VBoxContainer();
            entry.AddThemeConstantOverride("separation", 0);
            entry.AddChild(Ui.Label(role.ToUpperInvariant(), "Muted", HorizontalAlignment.Center));
            entry.AddChild(Ui.Label(name, "Heading", HorizontalAlignment.Center));
            Add(entry);
            box.MoveChild(entry, at++);
            if (link is null || uri is null) continue;
            var linkButton = new LinkButton
            {
                Text = link, Uri = uri, FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, Underline = LinkButton.UnderlineMode.OnHover,
            };
            linkButton.AddThemeFontOverride("font", Fonts.Bold);
            linkButton.AddThemeFontSizeOverride("font_size", 20);
            linkButton.AddThemeColorOverride("font_color", Themes.Current.AccentText);
            entry.AddChild(linkButton);
        }
        var thanks = Ui.Label("Tipografías: Bungee y Outfit (SIL OFL)  ·  Algunos efectos, generados por código  ·  Hecho con Godot",
            "Muted", HorizontalAlignment.Center);
        thanks.AddThemeFontSizeOverride("font_size", 15);
        Add(thanks);
        box.MoveChild(thanks, at);
        return panel;
    }

    private void OpenCredits()
    {
        _mode = Mode.Credits;
        Ui.ShowPanel(_credits);
    }

    private void CloseCredits()
    {
        Audio.Play("click", 0.9f);
        _credits.Visible = false;
        _mode = Mode.Settings;
        _settings.Open(keepSelection: true);
    }

    private void OpenSettings()
    {
        _mode = Mode.Settings;
        Audio.Play("pop");
        _settings.Open();
    }

    // ---------------------------------------------------------------- carrusel

    public override void _Process(double delta)
    {
        if (_cards.Count == 0) return;
        _dim.Visible = _mode is Mode.Settings or Mode.Credits or Mode.Info;
        _dim.Color = new Color(Themes.Current.Bg, 0.82f);
        _time += delta;
        int n = Count;
        float cw = Card3D.Width;
        float launch = _launch;

        for (int i = 0; i < n; i++)
        {
            float d = Mathf.PosMod(i - _scroll + n / 2f, n) - n / 2f;
            float a = Mathf.Abs(d), s = Mathf.Sign(d);
            float near = Mathf.Max(0, 1 - a);

            var pos = new Vector3(
                s * cw * (0.62f * Mathf.Min(a, 1) + 0.5f * a),
                cw * 0.07f * near + Mathf.Sin((float)_time * 1.3f + i * 0.9f) * 0.035f,
                -a * cw * 0.85f + cw * 0.25f * near);
            float ry = -s * Mathf.Min(a * 32, 70);
            float scale = a > 3 ? Mathf.Clamp(3.8f - a, 0, 0.8f) / 0.8f : 1;

            if (launch > 0)
            {
                if (i == CenterIndex)
                {
                    pos = pos.Lerp(new Vector3(0, -0.1f, 3.6f), launch);
                    ry += launch * 360;
                }
                else
                {
                    scale *= 1 - launch;
                }
            }

            var card = _cards[i];
            card.Visible = scale > 0.01f;
            card.Position = pos;
            card.RotationDegrees = new Vector3(0, ry, 0);
            card.Scale = Vector3.One * Mathf.Max(scale, 0.01f);
            card.SetLook(1 - 0.17f * a, Mathf.Clamp(1 - a * 2.5f, 0, 1) * (1 - launch));
        }
    }
}
