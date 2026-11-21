using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static AlfombraBaile.Ui;

namespace AlfombraBaile;

/// <summary>
/// Base de los juegos de letras: reparte letras por las 8 casillas exteriores y hay que pisar la palabra en orden.
/// Una letra repetida se pisa dos veces en la misma casilla. Cada juego decide qué palabra toca y qué pasa al
/// acertar o fallar.
/// </summary>
public abstract partial class LetterGame : Node3D
{
    protected static readonly Pad[] Ring =
    {
        Pad.Cross, Pad.Up, Pad.Circle, Pad.Right, Pad.Square, Pad.Down, Pad.Triangle, Pad.Left,
    };

    protected enum Phase { Wait, Play, Paused, Over }

    protected readonly Random Rng = new();
    protected Phase State = Phase.Wait;
    private Phase _beforePause;

    protected Mat3D Mat = null!;
    protected Label Clue = null!, ScoreLabel = null!, BigText = null!;
    protected LetterSlots Slots = null!;
    protected Control Ui = null!;
    protected VBoxContainer RightBox = null!;
    private PanelContainer _pausePanel = null!, _endPanel = null!;
    private Label _endTitle = null!, _endScore = null!, _endRecord = null!, _endStats = null!;
    protected ScreenFade Fade = null!;

    private readonly Dictionary<Pad, char> _letters = new();
    protected string Answer = "";
    protected string Plain = "";
    protected int Index;

    protected abstract GameInfo Game { get; }
    protected abstract string ScenePath { get; }
    protected abstract string ScoreTitle { get; }

    /// <summary>Monta lo propio del juego (marcadores de la derecha) y elige la primera palabra.</summary>
    protected abstract void Begin();

    protected virtual void Tick(double delta) { }
    protected abstract void WordDone();
    protected abstract void Mistake(Pad pad);

    public override void _Ready()
    {
        Audio.Music("concentracion");
        AddChild(new StageBackdrop { Focus = new Vector2(0, -0.3f), BackdropPosition = new Vector3(0, 4, -16) });
        var camera = new Camera3D { Fov = 48 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 6.6f, 3.9f), new Vector3(0, 0, -0.35f), Vector3.Up);
        Mat = new Mat3D();
        AddChild(Mat);

        BuildUi();
        MatInput.Instance.Pressed += OnPressed;
        MatInput.Instance.Released += OnReleased;
        Begin();
    }

    public override void _ExitTree()
    {
        MatInput.Instance.Pressed -= OnPressed;
        MatInput.Instance.Released -= OnReleased;
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        Ui = Root(this);

        var scoreBox = new VBoxContainer();
        scoreBox.AddThemeConstantOverride("separation", -6);
        scoreBox.AddChild(Label(ScoreTitle, "Muted"));
        ScoreLabel = Label("0", "Huge");
        scoreBox.AddChild(ScoreLabel);
        Ui.AddChild(Anchor(scoreBox, Control.LayoutPreset.TopLeft, new Vector2(44, 26)));

        var center = new VBoxContainer { CustomMinimumSize = new Vector2(900, 0) };
        center.AddThemeConstantOverride("separation", 10);
        Clue = Label("", "Heading", HorizontalAlignment.Center);
        Clue.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Slots = new LetterSlots { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        center.AddChild(Clue);
        center.AddChild(Slots);
        Ui.AddChild(Anchor(center, Control.LayoutPreset.CenterTop, new Vector2(0, 24)));

        RightBox = new VBoxContainer();
        RightBox.AddThemeConstantOverride("separation", 4);
        int? record = Records.Get(Game.Id);
        RightBox.AddChild(Label($"Récord: {(record?.ToString() ?? "—")}", "Muted", HorizontalAlignment.Right));
        Ui.AddChild(Anchor(RightBox, Control.LayoutPreset.TopRight, new Vector2(-44, 30)));

        Ui.AddChild(Anchor(Hints(("SELECT", "Pausa")), Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        BigText = Label("", "Huge", HorizontalAlignment.Center);
        BigText.AddThemeFontSizeOverride("font_size", 96);
        BigText.CustomMinimumSize = new Vector2(1000, 140);
        BigText.VerticalAlignment = VerticalAlignment.Center;
        Ui.AddChild(Anchor(BigText, Control.LayoutPreset.Center, new Vector2(0, 40)));

        _pausePanel = Panel(Ui, "PAUSA", out _, ("START", "Continuar"), ("SELECT", "Salir"));
        _endPanel = Panel(Ui, "", out var endBox, ("START", "Otra vez"), ("SELECT", "Volver"));
        _endTitle = (Label)endBox.GetChild(0);
        _endScore = Label("", "HugeAccent", HorizontalAlignment.Center);
        _endRecord = Label("", "Heading", HorizontalAlignment.Center);
        _endStats = Label("", "Muted", HorizontalAlignment.Center);
        _endStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        foreach (var (label, i) in new[] { (_endScore, 1), (_endRecord, 2), (_endStats, 3) })
        {
            endBox.AddChild(label);
            endBox.MoveChild(label, i);
        }

        Fade = new ScreenFade();
        Ui.AddChild(Fade);
    }

    protected void Big(string text, Color color, double hold = 0.5)
     => FlashText(BigText, text, color, hold);

    // ---------------------------------------------------------------- palabras

    /// <summary>Prepara una palabra: reparte sus letras (y relleno) por las casillas y vacía las casillas de respuesta.</summary>
    protected void SetWord(string answer, bool showWord)
    {
        Answer = answer;
        Plain = WordBank.Plain(answer);
        Index = 0;
        Mat.ClearAll();
        var letters = WordBank.TileLetters(answer, Rng);
        _letters.Clear();
        for (int i = 0; i < Ring.Length; i++)
        {
            _letters[Ring[i]] = letters[i];
            Mat.SetLetter(Ring[i], letters[i]);
        }
        Slots.Setup(answer, showWord);
        Pop(Slots, 0.7f);
        State = Phase.Play;
    }

    /// <summary>La casilla donde está la siguiente letra (para las pistas).</summary>
    protected Pad NextPad => _letters.First(kv => kv.Value == Plain[Index]).Key;

    protected Vector3 TileWorld(Pad pad) => Mat.ToGlobal(Mat.TileSurface(pad));

    private void OnPressed(Pad pad)
    {
        Mat.SetPressed(pad, true);
        switch (State)
        {
            case Phase.Play:
                if (pad == Pad.Select) Pause();
                else if (_letters.ContainsKey(pad)) Step(pad);
                break;
            case Phase.Wait:
                if (pad == Pad.Select) Pause();
                break;
            case Phase.Paused:
                if (pad is Pad.Start or Pad.Circle) Resume();
                else if (pad is Pad.Select or Pad.Cross) Fade.GoTo(HubScreen.ScenePath);
                break;
            case Phase.Over when _endPanel.Visible:
                if (pad is Pad.Start or Pad.Circle) Fade.GoTo(ScenePath);
                else if (pad is Pad.Select or Pad.Cross) Fade.GoTo(HubScreen.ScenePath);
                break;
        }
    }

    private void OnReleased(Pad pad) => Mat.SetPressed(pad, false);

    private void Step(Pad pad)
    {
        if (_letters[pad] != Plain[Index])
        {
            Audio.Play("miss");
            Audio.Impact("impact_soft_medium", -6, 0.8f);
            Slots.Shake();
            Mistake(pad);
            return;
        }
        Index++;
        Slots.Filled = Index;
        Audio.Play("pop", 0.9f + Index * 0.05f);
        Audio.Impact("impact_wood_light", -8, 1.1f + Index * 0.04f);
        Fx.Burst(this, TileWorld(pad) + Vector3.Up * 0.3f, Themes.Current.Accent, 10, 2f);
        if (Index >= Plain.Length)
        {
            State = Phase.Wait;
            Mat.ClearAll();
            WordDone();
        }
    }

    public override void _Process(double delta)
    {
        if (State is Phase.Play or Phase.Wait) Tick(delta);
    }

    private void Pause()
    {
        _beforePause = State;
        State = Phase.Paused;
        Audio.Play("pop");
        ShowPanel(_pausePanel);
    }

    private void Resume()
    {
        _pausePanel.Visible = false;
        Audio.Play("click");
        State = _beforePause;
    }

    /// <summary>Fin de la partida: guarda el récord y enseña el panel final.</summary>
    protected void Finish(string title, int score, string scoreText, string stats)
    {
        State = Phase.Over;
        Mat.ClearAll();
        bool record = Records.Submit(Game, score);
        GetTree().CreateTimer(1.1).Timeout += () =>
        {
            _endTitle.Text = title;
            _endScore.Text = scoreText;
            _endRecord.Text = record ? "¡NUEVO RÉCORD!" : $"Récord: {Records.Get(Game.Id)?.ToString() ?? "—"}";
            _endRecord.ThemeTypeVariation = record ? "Heading" : "Muted";
            _endStats.Text = stats;
            ShowPanel(_endPanel);
            Audio.Play(record ? "stars3" : "sparkle");
            if (record) Fx.Burst(this, new Vector3(0, 1.5f, 0), Themes.Current.Accent, 60, 6);
        };
    }
}

/// <summary>Casillas de la respuesta: rellenas las acertadas, marcada la siguiente y, si se pide, la palabra a la vista.</summary>
public partial class LetterSlots : Control
{
    private const float MaxBox = 64, Gap = 10, MaxWidth = 860;
    private float Box = MaxBox;
    private string _word = "";
    private bool _show;
    private int _filled;
    private float _shake;

    public int Filled
    {
        get => _filled;
        set
        {
            _filled = value;
            QueueRedraw();
        }
    }

    public void Setup(string word, bool showWord)
    {
        _word = word;
        _show = showWord;
        _filled = 0;
        // Las palabras largas usan casillas más estrechas para no chocar con los marcadores.
        Box = Mathf.Min(MaxBox, (MaxWidth - (word.Length - 1) * Gap) / word.Length);
        CustomMinimumSize = new Vector2(word.Length * Box + (word.Length - 1) * Gap, Box + 12);
        QueueRedraw();
    }

    public void Reveal()
    {
        _show = true;
        QueueRedraw();
    }

    public void Shake() => _shake = 1;

    public override void _Process(double delta)
    {
        if (_shake <= 0) return;
        _shake = Mathf.Max(0, _shake - (float)delta * 3);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var t = Themes.Current;
        float dx = _shake > 0 ? Mathf.Sin(_shake * 40) * 10 * _shake : 0;
        for (int i = 0; i < _word.Length; i++)
        {
            var rect = new Rect2(dx + i * (Box + Gap), 6, Box, Box);
            bool done = i < _filled, next = i == _filled;
            var sb = new StyleBoxFlat
            {
                BgColor = done ? t.Accent : new Color(t.Text, 0.08f),
                BorderColor = next ? t.AccentText : new Color(t.Text, 0.25f),
            };
            sb.SetBorderWidthAll(next ? 4 : 2);
            sb.SetCornerRadiusAll(12);
            DrawStyleBox(sb, rect);

            if (!done && !_show) continue;
            var color = done ? t.Ink : new Color(t.Text, 0.45f);
            int size = (int)(Box * 0.69f);
            var font = Fonts.Display;
            float baseline = rect.GetCenter().Y + (font.GetAscent(size) - font.GetDescent(size)) / 2;
            DrawString(font, new Vector2(rect.Position.X, baseline), _word[i].ToString(), HorizontalAlignment.Center, Box, size, color);
        }
    }
}
