using System.Collections.Generic;
using Godot;

namespace AlfombraBaile;

/// <summary>Preferencias guardadas en user://settings.cfg: tema, pantalla completa y calibración de la alfombra.</summary>
public static class Settings
{
    private const string Path = "user://settings.cfg";
    private static bool _loaded;

    public static string ThemeId { get; set; } = "stay";
    public static bool Fullscreen { get; set; }
    public static string MatGuid { get; set; } = "";

    /// <summary>Tenis: la fila de delante de la alfombra apunta junto a la red (pensando desde el lado del rival).</summary>
    public static bool TennisFlipRows { get; set; }

    /// <summary>Tenis: ritmo rápido (2/3 del tiempo de vuelo para apuntar).</summary>
    public static bool TennisFast { get; set; }

    /// <summary>Flechas que caen: retraso medido de pantalla, altavoces y reflejos, en segundos.</summary>
    public static double RhythmOffset { get; set; }

    /// <summary>Volumen de la música de fondo, de 0 (apagada) a 4 (máximo).</summary>
    public static int MusicLevel { get; set; } = 2;

    /// <summary>Volumen de los efectos de sonido, de 0 (apagados) a 4 (máximo).</summary>
    public static int SfxLevel { get; set; } = 3;

    public static Dictionary<Pad, Binding> Bindings { get; } = new();

    /// <summary>
    /// La app se llamó «Stay Cool! Arcade»; al cambiar de nombre, Godot usa otra carpeta de datos. La primera vez se
    /// copian de la antigua la calibración, los ajustes y los récords.
    /// </summary>
    private static void MigrateOldData()
    {
        string current = OS.GetUserDataDir();
        string? parent = System.IO.Path.GetDirectoryName(current);
        if (parent is null) return;
        string old = System.IO.Path.Combine(parent, "Stay Cool! Arcade");
        if (!System.IO.Directory.Exists(old) || old == current) return;
        foreach (string file in new[] { "settings.cfg", "records.cfg" })
        {
            string from = System.IO.Path.Combine(old, file), to = System.IO.Path.Combine(current, file);
            if (System.IO.File.Exists(from) && !System.IO.File.Exists(to))
            {
                System.IO.Directory.CreateDirectory(current);
                System.IO.File.Copy(from, to);
            }
        }
    }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        MigrateOldData();
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;

        ThemeId = (string)cfg.GetValue("display", "theme", ThemeId);
        Fullscreen = (bool)cfg.GetValue("display", "fullscreen", false);
        MatGuid = (string)cfg.GetValue("mat", "guid", "");
        TennisFlipRows = (bool)cfg.GetValue("tennis", "flip_rows", false);
        TennisFast = (bool)cfg.GetValue("tennis", "fast", false);
        RhythmOffset = (double)cfg.GetValue("arrows", "offset", 0.0);
        MusicLevel = (int)cfg.GetValue("audio", "music", 2);
        SfxLevel = (int)cfg.GetValue("audio", "sfx", 3);
        foreach (var pad in PadInfo.CalibrationOrder)
        {
            string raw = (string)cfg.GetValue("mat", pad.ToString(), "");
            if (Binding.Parse(raw) is { } binding) Bindings[pad] = binding;
        }
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display", "theme", ThemeId);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.SetValue("mat", "guid", MatGuid);
        cfg.SetValue("tennis", "flip_rows", TennisFlipRows);
        cfg.SetValue("tennis", "fast", TennisFast);
        cfg.SetValue("arrows", "offset", RhythmOffset);
        cfg.SetValue("audio", "music", MusicLevel);
        cfg.SetValue("audio", "sfx", SfxLevel);
        foreach (var (pad, binding) in Bindings) cfg.SetValue("mat", pad.ToString(), binding.Serialize());
        cfg.Save(Path);
    }

    public static void ApplyWindowMode()
    {
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Maximized);
    }
}
