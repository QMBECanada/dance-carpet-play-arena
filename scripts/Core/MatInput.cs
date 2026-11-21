using System;
using System.Collections.Generic;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Lee la alfombra (un mando USB genérico) y el teclado, y avisa de cada casilla pisada o soltada.
/// Vive como autoload para que las pisadas lleguen igual en cualquier pantalla.
/// Teclado de reserva, con la misma forma que la alfombra: Q W E / A S D / Z X C, flechas, Intro = Start, Esc = Select.
/// </summary>
public partial class MatInput : Node
{
    public static MatInput Instance { get; private set; } = null!;

    /// <summary>Ejes que se miran al calibrar y para el centro.</summary>
    public const int AxisCount = 8;
    private const int ButtonCount = 64;
    private const float AxisThreshold = 0.5f;

    private static readonly Dictionary<Pad, Key[]> KeyMap = new()
    {
        [Pad.Up] = new[] { Key.Up, Key.W },
        [Pad.Down] = new[] { Key.Down, Key.X },
        [Pad.Left] = new[] { Key.Left, Key.A },
        [Pad.Right] = new[] { Key.Right, Key.D },
        [Pad.Cross] = new[] { Key.Q },
        [Pad.Circle] = new[] { Key.E },
        [Pad.Triangle] = new[] { Key.Z },
        [Pad.Square] = new[] { Key.C },
        [Pad.Select] = new[] { Key.Escape, Key.Backspace },
        [Pad.Start] = new[] { Key.Enter, Key.KpEnter },
        [Pad.Center] = new[] { Key.S },
    };

    private readonly bool[] _down = new bool[PadInfo.Count];
    private double _scanTimer;

    public event Action<Pad>? Pressed;
    public event Action<Pad>? Released;
    public event Action? DeviceChanged;

    /// <summary>Índice del mando en uso, o -1 si no hay alfombra.</summary>
    public int Device { get; private set; } = -1;

    public bool HasMat => Device >= 0;
    public string DeviceName => HasMat ? Input.GetJoyName(Device) : "";

    /// <summary>Mientras se calibra, la alfombra no genera pisadas (el teclado sí).</summary>
    public bool MatEnabled { get; set; } = true;

    /// <summary>Hay asignación para las diez casillas con botón; el centro es opcional.</summary>
    public bool IsCalibrated
    {
        get
        {
            foreach (var pad in PadInfo.CalibrationOrder)
                if (pad != Pad.Center && !Settings.Bindings.ContainsKey(pad)) return false;
            return true;
        }
    }

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        Settings.Load();
        Settings.ApplyWindowMode();
    }

    public override void _Ready()
    {
        ScanDevices();
        DevShot();
    }

    /// <summary>
    /// Solo desarrollo: «-- --shot=ruta.png [--delay=3] [--theme=id] [--keys=right,start]» guarda una captura y cierra.
    /// Las teclas simuladas se pulsan una por segundo antes de la captura.
    /// </summary>
    private void DevShot()
    {
        string? shot = null, theme = null, keys = null;
        double delay = 3;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shot=")) shot = arg[7..];
            else if (arg.StartsWith("--delay=")) delay = double.Parse(arg[8..], System.Globalization.CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--theme=")) theme = arg[8..];
            else if (arg.StartsWith("--keys=")) keys = arg[7..];
            else if (arg == "--hub") CalibrationScreen.Dismissed = true;
        }
        if (shot is null) return;
        if (theme is not null)
            foreach (var t in Themes.All)
                if (t.Id == theme) Themes.Set(t);

        var tree = GetTree();
        double at = 1.0;
        foreach (string k in (keys ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<Pad>(k, true, out var pad)) continue;
            tree.CreateTimer(at).Timeout += () => Pressed?.Invoke(pad);
            at += 1.0;
        }
        tree.CreateTimer(at + delay).Timeout += () =>
        {
            GetViewport().GetTexture().GetImage().SavePng(shot);
            tree.Quit();
        };
    }

    public override void _Process(double delta)
    {
        _scanTimer += delta;
        if (_scanTimer > 1.0)
        {
            _scanTimer = 0;
            ScanDevices();
        }

        foreach (var pad in PadInfo.CalibrationOrder)
        {
            bool now = ReadKeyboard(pad) || (MatEnabled && ReadMat(pad));
            int i = (int)pad;
            if (now == _down[i]) continue;
            _down[i] = now;
            if (now) Pressed?.Invoke(pad);
            else Released?.Invoke(pad);
        }
    }

    /// <summary>Espacio cierra la aplicación desde cualquier pantalla (la tecla no se usa en ningún juego).</summary>
    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Space })
        {
            Settings.Save();
            GetTree().Quit();
        }
    }

    public bool IsDown(Pad pad) => _down[(int)pad];

    /// <summary>Sin alfombra conectada se juega con el teclado: las indicaciones muestran teclas.</summary>
    public static bool UsingKeyboard => Instance is null || !Instance.HasMat;

    /// <summary>Tecla principal de una casilla, como se muestra en pantalla.</summary>
    public static string KeyName(Pad pad) => pad switch
    {
        Pad.Up => "W",
        Pad.Down => "X",
        Pad.Left => "A",
        Pad.Right => "D",
        Pad.Cross => "Q",
        Pad.Circle => "E",
        Pad.Triangle => "Z",
        Pad.Square => "C",
        Pad.Select => "ESC",
        Pad.Start => "INTRO",
        _ => "S",
    };

    private static bool ReadKeyboard(Pad pad)
    {
        foreach (var key in KeyMap[pad])
            if (Input.IsPhysicalKeyPressed(key)) return true;
        return false;
    }

    private bool ReadMat(Pad pad) =>
        HasMat && Settings.Bindings.TryGetValue(pad, out var binding) && IsActive(binding);

    public bool IsActive(Binding binding)
    {
        if (!HasMat) return false;
        if (!binding.IsAxis) return Input.IsJoyButtonPressed(Device, (JoyButton)binding.Index);
        float value = Input.GetJoyAxis(Device, (JoyAxis)binding.Index);
        return (value - binding.Rest) * binding.Sign > AxisThreshold;
    }

    /// <summary>Valores actuales de los ejes, para tomarlos como reposo antes de calibrar.</summary>
    public float[] ReadAxes()
    {
        var axes = new float[AxisCount];
        if (!HasMat) return axes;
        for (int i = 0; i < AxisCount; i++) axes[i] = Input.GetJoyAxis(Device, (JoyAxis)i);
        return axes;
    }

    /// <summary>La primera señal activa del mando (un botón, o un eje lejos de su reposo), o null si no se pisa nada.</summary>
    public Binding? DetectRaw(float[] rest)
    {
        if (!HasMat) return null;
        for (int b = 0; b < ButtonCount; b++)
            if (Input.IsJoyButtonPressed(Device, (JoyButton)b)) return Binding.Button(b);

        for (int a = 0; a < AxisCount; a++)
        {
            float diff = Input.GetJoyAxis(Device, (JoyAxis)a) - rest[a];
            if (Mathf.Abs(diff) > AxisThreshold) return Binding.Axis(a, Math.Sign(diff), rest[a]);
        }
        return null;
    }

    public void SaveCalibration(Dictionary<Pad, Binding> bindings)
    {
        Settings.Bindings.Clear();
        foreach (var (pad, binding) in bindings) Settings.Bindings[pad] = binding;
        Settings.MatGuid = HasMat ? Input.GetJoyGuid(Device) : "";
        Settings.Save();
    }

    private void ScanDevices()
    {
        var pads = Input.GetConnectedJoypads();
        int chosen = -1;
        foreach (int id in pads)
            if (Settings.MatGuid != "" && Input.GetJoyGuid(id) == Settings.MatGuid) chosen = id;
        if (chosen < 0 && pads.Count > 0) chosen = pads[0];
        if (chosen == Device) return;

        Device = chosen;
        if (HasMat)
            GD.Print($"Alfombra: «{Input.GetJoyName(Device)}» guid={Input.GetJoyGuid(Device)} conocida={Input.IsJoyKnown(Device)}");
        else
            GD.Print("Alfombra: ningún mando conectado");
        DeviceChanged?.Invoke();
    }
}
