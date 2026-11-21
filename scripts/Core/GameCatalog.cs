using Godot;

namespace AlfombraBaile;

/// <summary>Una ficha del carrusel. Sin escena, el juego aparece como «Próximamente».</summary>
public sealed record GameInfo(
    string Id, string Name, string Icon, ColorRole Color, string Blurb, string Unit = "puntos", bool LowerIsBetter = false,
    string? Scene = null);

public static class GameCatalog
{
    public static readonly GameInfo[] All =
    {
        new("moles", "Topos", "mole", ColorRole.H, "Pisa los topos antes de que se escondan. ¡Ojo con las bombas!",
            Scene: "res://scenes/Moles.tscn"),
        new("simon", "Simón dice", "simon", ColorRole.V, "Repite la secuencia, cada vez más larga.", "pasos",
            Scene: "res://scenes/Simon.tscn"),
        new("arrows", "Flechas que caen", "arrows", ColorRole.Accent, "Pisa cada flecha justo cuando llega a su sitio, al ritmo de la música.",
            Scene: "res://scenes/Arrows.tscn"),
        new("maze", "Laberinto", "maze", ColorRole.Corner, "Inclina el tablero pisando y lleva la bola hasta la meta.", "s (los 4 niveles)",
            LowerIsBetter: true, Scene: "res://scenes/Maze.tscn"),
        new("workout", "Ejercicio", "fit", ColorRole.H, "Series de pasos al ritmo de la música: sigue las flechas con cada pie.",
            "% de precisión", Scene: "res://scenes/Workout.tscn"),
        new("circuit", "Circuito", "circuit", ColorRole.Accent, "Seis juegos seguidos de unos minutos, con descansos: ¡a sudar!",
            "circuitos", Scene: "res://scenes/Circuit.tscn"),
        new("tennis", "Tenis", "tennis", ColorRole.Corner, "Golpea, apunta a la zona débil del rival y gana el torneo.",
            "partidos ganados", Scene: "res://scenes/Tennis.tscn"),
        new("pinball", "Pinball", "pinball", ColorRole.H, "Palancas con tres fuerzas, setas, dianas y STAY COOL.",
            Scene: "res://scenes/Pinball.tscn"),
        new("curling", "Curling", "curling", ColorRole.V, "Lanza la piedra y barre para dejarla en la casa. Solo o contra un rival.",
            Scene: "res://scenes/Curling.tscn"),
        new("spell", "Deletrear", "spell", ColorRole.V, "Pisa las letras en orden para formar la palabra.",
            Scene: "res://scenes/Spell.tscn"),
        new("riddle", "Adivinanza", "quiz", ColorRole.Accent, "Lee la pista y pisa solo la respuesta.", "aciertos",
            Scene: "res://scenes/Riddle.tscn"),
    };
}

/// <summary>
/// Récords en user://records.cfg. La clave es el id del juego o una propia (por canción, nivel…).
/// Un 0 o menos nunca es récord: una partida sin puntos o un tiempo sin medir no cuentan.
/// </summary>
public static class Records
{
    private const string Path = "user://records.cfg";
    private const string Section = "records";

    public static int? Get(string key)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok || !cfg.HasSectionKey(Section, key)) return null;
        return (int)cfg.GetValue(Section, key);
    }

    /// <summary>Guarda <paramref name="value"/> si mejora el récord de la clave. Devuelve true si es récord nuevo.</summary>
    public static bool Submit(string key, int value, bool lowerIsBetter = false)
    {
        if (value <= 0) return false;
        int? best = Get(key);
        bool better = best is null || (lowerIsBetter ? value < best : value > best);
        if (!better) return false;

        var cfg = new ConfigFile();
        cfg.Load(Path); // si aún no existe, se crea al guardar
        cfg.SetValue(Section, key, value);
        cfg.Save(Path);
        return true;
    }

    public static bool Submit(GameInfo game, int value) => Submit(game.Id, value, game.LowerIsBetter);
}
