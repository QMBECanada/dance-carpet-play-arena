using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Calibración: pide pisar cada casilla y apunta qué botón (o eje) envía la alfombra. Rechaza repetidos.
/// Al terminar deja probar la alfombra; START vuelve al carrusel. Esc sale sin guardar; Tab salta el centro.
/// </summary>
public partial class CalibrationScreen : Node3D
{
    public const string ScenePath = "res://scenes/Calibration.tscn";

    /// <summary>Se salió sin calibrar: no volver a mandar aquí automáticamente en esta sesión.</summary>
    public static bool Dismissed { get; internal set; }

    private const double RestSeconds = 1.2;
    private const double ReleaseSeconds = 0.12;

    private enum State { NoDevice, Rest, Wait, Release, Test }

    private readonly Dictionary<Pad, Binding> _map = new();
    private readonly float[] _restSum = new float[MatInput.AxisCount];
    private State _state;
    private int _step;
    private int _samples;
    private float[] _rest = new float[MatInput.AxisCount];
    private double _timer;
    private bool _advance;
    private Pad? _flashed;

    private Mat3D _mat = null!;
    private Label _verb = null!, _padName = null!, _message = null!, _device = null!;
    private ProgressDots _dots = null!;
    private ScreenFade _fade = null!;

    private Pad Target => PadInfo.CalibrationOrder[_step];

    public override void _Ready()
    {
        Audio.Music("menu");
        AddChild(new StageBackdrop { Focus = new Vector2(0, -0.3f), BackdropPosition = new Vector3(0, 4, -16) });
        var camera = new Camera3D { Fov = 48 };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 6.4f, 4.3f), new Vector3(0, 0, -1.0f), Vector3.Up);

        _mat = new Mat3D();
        AddChild(_mat);
        BuildUi();

        var input = MatInput.Instance;
        input.MatEnabled = false;
        input.DeviceChanged += OnDeviceChanged;
        input.Pressed += OnPad;
        Begin();
    }

    public override void _ExitTree()
    {
        var input = MatInput.Instance;
        input.MatEnabled = true;
        input.DeviceChanged -= OnDeviceChanged;
        input.Pressed -= OnPad;
    }

    private void BuildUi()
    {
        var ui = Ui.Root(this);

        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 14);
        title.AddChild(Ui.Label("CALIBRAR", "TitleAccent"));
        title.AddChild(Ui.Label("LA ALFOMBRA", "Title"));
        ui.AddChild(Ui.Anchor(title, Control.LayoutPreset.TopLeft, new Vector2(44, 30)));

        var chip = new PanelContainer { ThemeTypeVariation = "Status" };
        _device = Ui.Label("", "Muted");
        chip.AddChild(_device);
        ui.AddChild(Ui.Anchor(chip, Control.LayoutPreset.TopRight, new Vector2(-44, 36)));

        var center = new VBoxContainer { CustomMinimumSize = new Vector2(900, 0), Alignment = BoxContainer.AlignmentMode.Center };
        center.AddThemeConstantOverride("separation", 8);
        _dots = new ProgressDots { Total = PadInfo.CalibrationOrder.Length };
        center.AddChild(_dots);
        var prompt = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        prompt.AddThemeConstantOverride("separation", 22);
        _verb = Ui.Label("", "Huge");
        _padName = Ui.Label("", "HugeAccent");
        prompt.AddChild(_verb);
        prompt.AddChild(_padName);
        center.AddChild(prompt);
        _message = Ui.Label("", "Muted", HorizontalAlignment.Center);
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        center.AddChild(_message);
        ui.AddChild(Ui.Anchor(center, Control.LayoutPreset.CenterTop, new Vector2(0, 92)));

        var hints = Ui.Hints(("ESC", "Salir sin guardar"), ("TAB", "Saltar el centro"));
        ui.AddChild(Ui.Anchor(hints, Control.LayoutPreset.BottomLeft, new Vector2(44, -36)));

        _fade = new ScreenFade();
        ui.AddChild(_fade);
    }

    // ---------------------------------------------------------------- flujo

    private void Begin()
    {
        _map.Clear();
        _step = 0;
        _mat.ClearAll();
        _dots.Done = 0;
        _dots.Current = -1;
        UpdateDevice();

        if (!MatInput.Instance.HasMat)
        {
            _state = State.NoDevice;
            Show("CONECTA", "LA ALFOMBRA", "No se detecta ningún mando. Enchufa la alfombra por USB y espera un momento.");
            return;
        }
        StartRest();
    }

    private void StartRest()
    {
        _state = State.Rest;
        _timer = 0;
        _samples = 0;
        System.Array.Clear(_restSum);
        Show("NO PISES", "NADA", "Un segundo… estoy viendo cómo está la alfombra en reposo.");
    }

    private void StartStep()
    {
        _state = State.Wait;
        var target = Target;
        foreach (var pad in PadInfo.CalibrationOrder)
        {
            _mat.SetBlink(pad, pad == target);
            _mat.SetDim(pad, pad == target || _map.ContainsKey(pad) ? 1 : 0.4f);
        }
        _dots.Current = _step;
        string hint = target switch
        {
            Pad.Select or Pad.Start => "Es uno de los botones pequeños de arriba.",
            Pad.Center => "Si tu alfombra no detecta el centro, pulsa Tab para saltarlo.",
            _ => "Pisa y levanta el pie.",
        };
        Show("PISA", PadInfo.Label(target).ToUpperInvariant(), hint);
    }

    private void Assign(Binding binding)
    {
        var target = Target;
        _map[target] = binding;
        _mat.SetBlink(target, false);
        _mat.SetPressed(target, true);
        _flashed = target;
        _dots.Done = _step + 1;
        Audio.Play("pop", 0.9f + _step * 0.04f);
        _message.Text = $"{PadInfo.Label(target)} = {binding.Describe()}";
        _advance = true;
        _state = State.Release;
        _timer = 0;
    }

    private void Next()
    {
        if (_flashed is { } flashed) _mat.SetPressed(flashed, false);
        _flashed = null;
        _step++;
        if (_step >= PadInfo.CalibrationOrder.Length) Finish();
        else StartStep();
    }

    private void Finish()
    {
        MatInput.Instance.SaveCalibration(_map);
        MatInput.Instance.MatEnabled = true;
        Dismissed = false;
        _state = State.Test;
        _mat.ClearAll();
        _dots.Current = -1;
        _dots.Done = _dots.Total;
        Audio.Play("stars3");
        Show("¡LISTA!", "", "Pisa las casillas para probarlas. Cuando quieras, START para ir a los juegos.");
    }

    // ---------------------------------------------------------------- cada fotograma

    public override void _Process(double delta)
    {
        var input = MatInput.Instance;
        switch (_state)
        {
            case State.Rest:
            {
                if (input.DetectRaw(input.ReadAxes()) is not null)
                {
                    StartRest();
                    _message.Text = "Bájate de la alfombra un momento, por favor.";
                    return;
                }
                var axes = input.ReadAxes();
                for (int i = 0; i < axes.Length; i++) _restSum[i] += axes[i];
                _samples++;
                _timer += delta;
                if (_timer >= RestSeconds)
                {
                    _rest = _restSum.Select(v => v / _samples).ToArray();
                    _step = 0;
                    StartStep();
                }
                break;
            }
            case State.Wait:
            {
                if (input.DetectRaw(_rest) is not { } raw) break;
                Pad? owner = null;
                foreach (var (pad, binding) in _map)
                    if (binding.SameInput(raw)) owner = pad;
                if (owner is { } taken)
                {
                    _message.Text = $"Esa casilla ya es «{PadInfo.Label(taken)}». Ahora pisa «{PadInfo.Label(Target)}».";
                    Audio.Play("miss");
                    _mat.SetPressed(taken, true);
                    _flashed = taken;
                    _advance = false;
                    _state = State.Release;
                    _timer = 0;
                }
                else
                {
                    Assign(raw);
                }
                break;
            }
            case State.Release:
            {
                if (input.DetectRaw(_rest) is not null)
                {
                    _timer = 0;
                    break;
                }
                _timer += delta;
                if (_timer < ReleaseSeconds) break;
                if (_advance)
                {
                    Next();
                }
                else
                {
                    if (_flashed is { } f) _mat.SetPressed(f, false);
                    _flashed = null;
                    _state = State.Wait;
                }
                break;
            }
            case State.Test:
                foreach (var pad in PadInfo.CalibrationOrder) _mat.SetPressed(pad, input.IsDown(pad));
                break;
        }
    }

    private void OnPad(Pad pad)
    {
        if (_state == State.Test && pad is Pad.Start)
        {
            Audio.Play("start");
            _fade.GoTo(HubScreen.ScenePath);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode == Key.Escape && _state != State.Test)
        {
            Dismissed = true;
            _fade.GoTo(HubScreen.ScenePath);
        }
        else if (key.PhysicalKeycode == Key.Tab && _state == State.Wait && Target == Pad.Center)
        {
            _message.Text = "Centro saltado.";
            Next();
        }
    }

    private void OnDeviceChanged()
    {
        if (_state is State.Test) return;
        Begin();
    }

    private void UpdateDevice()
    {
        var input = MatInput.Instance;
        _device.Text = input.HasMat ? input.DeviceName : "Sin alfombra";
    }

    private void Show(string verb, string name, string message)
    {
        _verb.Text = verb;
        _padName.Text = name;
        _padName.Visible = name != "";
        _message.Text = message;
    }
}

/// <summary>Fila de puntos de progreso: hechos, el actual (anillo) y los que faltan.</summary>
public partial class ProgressDots : Control
{
    private int _done, _current = -1;
    public int Total { get; set; }

    public int Done
    {
        get => _done;
        set
        {
            _done = value;
            QueueRedraw();
        }
    }

    public int Current
    {
        get => _current;
        set
        {
            _current = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 24);
        Themes.Changed += QueueRedraw;
    }

    public override void _ExitTree() => Themes.Changed -= QueueRedraw;

    public override void _Draw()
    {
        var t = Themes.Current;
        const float gap = 26;
        float x0 = Size.X / 2 - (Total - 1) * gap / 2;
        for (int i = 0; i < Total; i++)
        {
            var c = new Vector2(x0 + i * gap, Size.Y / 2);
            if (i < _done) DrawCircle(c, 7, t.AccentText);
            else DrawCircle(c, 5, new Color(t.Text, 0.25f));
            if (i == _current) DrawArc(c, 11, 0, Mathf.Tau, 24, t.AccentText, 2.5f, true);
        }
    }
}
