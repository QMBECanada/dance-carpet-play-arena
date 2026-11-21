using System;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Cuenta la actividad: pasos (cada pisada en una casilla de la cuadrícula) y tiempo jugando (fuera del carrusel,
/// la portada y la calibración), en total y por día. Se guarda en user://actividad.cfg.
/// </summary>
public partial class Activity : Node
{
    private const string Path = "user://actividad.cfg";
    private const double SaveEvery = 20;

    public static Activity? Instance { get; private set; }

    public long TotalSteps { get; private set; }
    public double TotalSeconds { get; private set; }
    public long TodaySteps { get; private set; }
    public double TodaySeconds { get; private set; }

    private string _today = "";
    private double _sinceSave;
    private bool _dirty;

    private static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        Load();
    }

    public override void _Ready() => MatInput.Instance.Pressed += OnPressed;

    public override void _ExitTree()
    {
        MatInput.Instance.Pressed -= OnPressed;
        Save();
    }

    private void OnPressed(Pad pad)
    {
        if (pad is Pad.Select or Pad.Start) return;
        RollDay();
        TotalSteps++;
        TodaySteps++;
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        string scene = GetTree().CurrentScene?.SceneFilePath ?? "";
        bool playing = scene != "" && !scene.EndsWith("Hub.tscn") && !scene.EndsWith("Splash.tscn") && !scene.EndsWith("Calibration.tscn");
        if (playing)
        {
            RollDay();
            TotalSeconds += delta;
            TodaySeconds += delta;
            _dirty = true;
        }
        _sinceSave += delta;
        if (_sinceSave >= SaveEvery)
        {
            _sinceSave = 0;
            Save();
        }
    }

    /// <summary>Al cambiar de día, el contador de hoy vuelve a empezar.</summary>
    private void RollDay()
    {
        if (_today == Today) return;
        _today = Today;
        TodaySteps = 0;
        TodaySeconds = 0;
    }

    private void Load()
    {
        var cfg = new ConfigFile();
        _today = Today;
        if (cfg.Load(Path) != Error.Ok) return;
        TotalSteps = (long)cfg.GetValue("total", "pasos", 0L);
        TotalSeconds = (double)cfg.GetValue("total", "segundos", 0.0);
        TodaySteps = (long)cfg.GetValue("dias", $"{_today}_pasos", 0L);
        TodaySeconds = (double)cfg.GetValue("dias", $"{_today}_segundos", 0.0);
    }

    private void Save()
    {
        if (!_dirty) return;
        _dirty = false;
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("total", "pasos", TotalSteps);
        cfg.SetValue("total", "segundos", TotalSeconds);
        cfg.SetValue("dias", $"{_today}_pasos", TodaySteps);
        cfg.SetValue("dias", $"{_today}_segundos", TodaySeconds);
        cfg.Save(Path);
    }

    /// <summary>«1 h 05 min» o «12 min».</summary>
    public static string Duration(double seconds)
    {
        int minutes = (int)(seconds / 60);
        return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
    }

    public static string Thousands(long n) => n.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));
}
