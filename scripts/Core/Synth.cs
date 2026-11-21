using System;
using System.Collections.Generic;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Efectos de sonido generados por código, para lo que no está en los paquetes de Kenney: ruidos filtrados que
/// suenan a roce, cepillo, mecha o explosión. Los continuos («roce», «rodar», «mecha») se repiten en bucle sin cortes.
/// </summary>
public static class Synth
{
    private const int Rate = 22050;
    private static readonly Dictionary<string, AudioStreamWav> Cache = new();

    public static AudioStreamWav Get(string name)
    {
        if (Cache.TryGetValue(name, out var stream)) return stream;
        var rng = new Random(name.GetHashCode());
        stream = name switch
        {
            "roce" => Loop(Brown(rng, 2.0, 0.985f), 0.9f),            // piedra deslizando sobre el hielo
            "rodar" => Loop(Rumble(rng, 2.0), 0.9f),                    // bola rodando por el laberinto
            "mecha" => Loop(Crackle(rng, 1.5), 0.6f),                   // mecha de la bomba chisporroteando
            "cepillo" => OneShot(Brush(rng, 0.16)),                     // un golpe de cepillo sobre el hielo
            "explosion" => OneShot(Explosion(rng, 1.1)),
            "muelle" => OneShot(Spring(0.35)),                           // lanzador del pinball
            _ => OneShot(new float[1]),
        };
        Cache[name] = stream;
        return stream;
    }

    // ---------------------------------------------------------------- generadores

    private static float[] Brown(Random rng, double seconds, float smooth)
    {
        var s = new float[(int)(seconds * Rate)];
        float y = 0;
        for (int i = 0; i < s.Length; i++)
        {
            y = y * smooth + (float)(rng.NextDouble() * 2 - 1) * (1 - smooth) * 6;
            s[i] = y;
        }
        return Normalize(s, 0.8f);
    }

    private static float[] Rumble(Random rng, double seconds)
    {
        var s = Brown(rng, seconds, 0.97f);
        for (int i = 0; i < s.Length; i++) s[i] *= 0.75f + 0.25f * MathF.Sin(i * MathF.Tau * 7f / Rate);
        return Normalize(s, 0.8f);
    }

    private static float[] Crackle(Random rng, double seconds)
    {
        var s = new float[(int)(seconds * Rate)];
        float prev = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            float hiss = (white - prev) * 0.35f; // paso alto: siseo
            prev = white;
            float pop = rng.NextDouble() < 0.0015 ? (float)(rng.NextDouble() * 2 - 1) : 0;
            s[i] = hiss + pop;
        }
        return Normalize(s, 0.7f);
    }

    private static float[] Brush(Random rng, double seconds)
    {
        var s = new float[(int)(seconds * Rate)];
        float prev = 0, low = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = (float)i / s.Length;
            float white = (float)(rng.NextDouble() * 2 - 1);
            low = low * 0.6f + white * 0.4f;           // quita lo más agudo
            float band = low - prev * 0.9f;              // y lo más grave: «shh»
            prev = low;
            float env = MathF.Sin(t * MathF.PI);         // sube y baja
            s[i] = band * env;
        }
        return Normalize(s, 0.8f);
    }

    private static float[] Explosion(Random rng, double seconds)
    {
        var s = new float[(int)(seconds * Rate)];
        float y = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = (float)i / Rate;
            float smooth = 0.6f + 0.38f * MathF.Min(1, t * 2.5f); // de chasquido a retumbo
            y = y * smooth + (float)(rng.NextDouble() * 2 - 1) * (1 - smooth) * 4;
            float env = MathF.Min(1, t * 200) * MathF.Exp(-t * 3.2f);
            s[i] = y * env;
        }
        return Normalize(s, 0.95f);
    }

    private static float[] Spring(double seconds)
    {
        var s = new float[(int)(seconds * Rate)];
        for (int i = 0; i < s.Length; i++)
        {
            float t = (float)i / Rate;
            float freq = 180 + 260 * MathF.Exp(-t * 9);
            float wob = MathF.Sin(MathF.Tau * 23 * t);
            s[i] = MathF.Sin(MathF.Tau * freq * t + wob * 2) * MathF.Exp(-t * 9);
        }
        return Normalize(s, 0.7f);
    }

    private static float[] Normalize(float[] s, float peak)
    {
        float max = 1e-6f;
        foreach (float v in s) max = MathF.Max(max, MathF.Abs(v));
        for (int i = 0; i < s.Length; i++) s[i] = s[i] / max * peak;
        return s;
    }

    // ---------------------------------------------------------------- a sonido de Godot

    private static AudioStreamWav OneShot(float[] samples) => ToWav(samples, loop: false);

    /// <summary>Bucle sin chasquido: el final se funde con el principio.</summary>
    private static AudioStreamWav Loop(float[] samples, float gain)
    {
        int fade = Rate / 10;
        var s = (float[])samples.Clone();
        for (int i = 0; i < fade; i++)
        {
            float k = (float)i / fade;
            s[i] = s[i] * k + samples[s.Length - fade + i] * (1 - k);
        }
        Array.Resize(ref s, s.Length - fade);
        for (int i = 0; i < s.Length; i++) s[i] *= gain;
        return ToWav(s, loop: true);
    }

    private static AudioStreamWav ToWav(float[] samples, bool loop)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Math.Clamp(samples[i], -1, 1) * short.MaxValue);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = samples.Length;
        }
        return wav;
    }
}

/// <summary>Un sonido continuo (roce, rodar, mecha) cuyo volumen y tono se ajustan cada fotograma.</summary>
public partial class LoopSound : AudioStreamPlayer
{
    private readonly float _baseDb;

    public LoopSound() : this("roce", -10) { }

    public LoopSound(string synth, float baseDb)
    {
        Stream = Synth.Get(synth);
        _baseDb = baseDb;
        VolumeDb = -80;
    }

    public override void _Ready() => Play();

    /// <summary>Intensidad de 0 (silencio) a 1 y tono (1 = normal).</summary>
    public void Set(float intensity, float pitch = 1f)
    {
        float target = Audio.LoopDb(intensity, _baseDb);
        VolumeDb = Mathf.Lerp(VolumeDb, target, 0.25f);
        PitchScale = Mathf.Clamp(pitch, 0.3f, 3f);
    }
}
