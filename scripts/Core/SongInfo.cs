using System;
using System.Text.Json;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// El ritmo medido de una canción (assets/musica/{id}.json, generado con Bastidor/scripts/analizar_ritmo.py):
/// tempo, momento del primer golpe, duración y fuerza de cada corchea.
/// </summary>
public sealed class SongInfo
{
    public double Bpm { get; set; }
    public double Offset { get; set; }
    public double Duration { get; set; }
    public float[] Eighths { get; set; } = Array.Empty<float>();

    /// <summary>Momento medido de cada pulso (Suno no mantiene el tempo constante); vacío en análisis antiguos.</summary>
    public double[] Beats { get; set; } = Array.Empty<double>();

    public double Beat => 60.0 / Bpm;

    /// <summary>Momento (segundos) del pulso número <paramref name="index"/>.</summary>
    public double BeatTime(double index)
    {
        if (Beats.Length < 2) return Offset + index * Beat;
        if (index <= 0) return Beats[0] + index * (Beats[1] - Beats[0]);
        int last = Beats.Length - 1;
        if (index >= last) return Beats[last] + (index - last) * (Beats[last] - Beats[last - 1]);
        int k = (int)index;
        return Beats[k] + (index - k) * (Beats[k + 1] - Beats[k]);
    }

    public static SongInfo Load(string id)
    {
        string json = FileAccess.GetFileAsString($"res://assets/musica/{id}.json");
        return JsonSerializer.Deserialize<SongInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SongInfo();
    }

    /// <summary>Si el reloj propio se separa más que esto del reproductor, salta a su posición en vez de suavizar.</summary>
    private const double ResyncThreshold = 0.06;

    /// <summary>Peso con que el reloj propio se acerca cada fotograma a la posición medida (suaviza los saltos de audio).</summary>
    private const double ResyncBlend = 0.08;

    /// <summary>Tiempo de reproducción compensando la latencia de audio (para sincronizar con la imagen).</summary>
    public static double PlaybackTime(AudioStreamPlayer player) =>
        player.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();

    /// <summary>
    /// Avanza un reloj de canción: suma el tiempo del fotograma y lo acerca suavemente a la posición real del audio,
    /// que llega a saltos. Así las notas se mueven sin tirones y sin desfasarse de la música.
    /// </summary>
    public static double Advance(double clock, double delta, AudioStreamPlayer player)
    {
        clock += delta;
        double measured = PlaybackTime(player);
        return Math.Abs(measured - clock) > ResyncThreshold ? measured : clock + (measured - clock) * ResyncBlend;
    }
}
