using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AlfombraBaile;

/// <summary>Efectos de sonido (Kenney, CC0). Autoload con varias voces para que puedan sonar a la vez.</summary>
public partial class Audio : Node
{
    private const int Voices = 10;

    private static readonly Dictionary<string, float> Volumes = new()
    {
        ["click"] = -10, ["pop"] = -9, ["start"] = -9, ["sparkle"] = -12, ["combo"] = -12, ["miss"] = -11, ["bonk"] = -9,
        ["stars3"] = -8,
    };

    private static Audio? _instance;
    private readonly Dictionary<string, AudioStream?> _cache = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private int _next;

    // Música de fondo: dos reproductores para encadenar con fundido.
    private const double Fade = 0.8;
    private static readonly Dictionary<string, float> MusicTrim = new()
    {
        ["arcade"] = 0, ["concentracion"] = 0, ["estadio"] = 0, ["menu"] = -2,
        ["folk"] = 0, ["chillout"] = 0, ["pinball"] = 0,
    };
    private readonly AudioStreamPlayer[] _music = new AudioStreamPlayer[2];
    private readonly Tween?[] _musicFades = new Tween?[2];
    private int _musicSlot;
    private string? _musicId;

    /// <summary>Volumen en dB de cada nivel del ajuste (0 = apagada).</summary>
    private static float LevelDb(int level) => level switch { 1 => -24, 2 => -16, 3 => -10, 4 => -5, _ => -80 };

    public override void _EnterTree()
    {
        _instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        for (int i = 0; i < Voices; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _players.Add(player);
        }
        for (int i = 0; i < _music.Length; i++)
        {
            _music[i] = new AudioStreamPlayer { VolumeDb = -80 };
            AddChild(_music[i]);
        }
    }

    public static void Play(string name, float pitch = 1f) => _instance?.PlayInternal(name, pitch);

    /// <summary>
    /// Música de fondo en bucle (assets/musica/fondo/{id}.mp3) con fundido; null la apaga. Si ya suena esa canción, sigue.
    /// </summary>
    public static void Music(string? id) => _instance?.MusicInternal(id);

    /// <summary>Aplica un cambio del ajuste de volumen a la música que suena.</summary>
    public static void RefreshMusicVolume()
    {
        if (_instance is null) return;
        var player = _instance._music[_instance._musicSlot];
        if (_instance._musicId is not { } id || !player.Playing) return;
        _instance._musicFades[_instance._musicSlot]?.Kill();
        player.VolumeDb = TargetDb(id);
    }

    private static float TargetDb(string id) => LevelDb(Settings.MusicLevel) + MusicTrim.GetValueOrDefault(id);

    private void MusicInternal(string? id)
    {
        if (id == _musicId) return;
        _musicId = id;
        var old = _music[_musicSlot];
        if (old.Playing)
        {
            var fadeOut = FadeMusic(_musicSlot, SilentDb);
            fadeOut.TweenCallback(Callable.From(old.Stop));
        }
        if (id is null) return;

        var stream = GD.Load<AudioStream>($"res://assets/musica/fondo/{id}.mp3");
        if (stream is AudioStreamMP3 mp3) mp3.Loop = true;
        _musicSlot = 1 - _musicSlot;
        var player = _music[_musicSlot];
        player.Stream = stream;
        player.VolumeDb = SilentDb;
        player.Play();
        FadeMusic(_musicSlot, TargetDb(id));
    }

    private const float SilentDb = -60;

    /// <summary>Lleva un reproductor de música a un volumen, cancelando el fundido que tuviera en marcha.</summary>
    private Tween FadeMusic(int slot, float db)
    {
        _musicFades[slot]?.Kill();
        var tween = CreateTween();
        tween.TweenProperty(_music[slot], "volume_db", (double)db, Fade);
        _musicFades[slot] = tween;
        return tween;
    }

    // ---------------------------------------------------------------- efectos de los juegos

    private static readonly System.Random Pick = new();
    private static readonly Dictionary<string, AudioStream[]> Impacts = new();

    /// <summary>
    /// Efecto de impacto de Kenney (assets/sounds/impact/{name}_00N.ogg): elige una de sus 5 variantes al azar y varía
    /// un poco el tono, para que golpes repetidos no suenen idénticos.
    /// </summary>
    public static void Impact(string name, float db = -6, float pitch = 1f)
    {
        if (_instance is null) return;
        if (!Impacts.TryGetValue(name, out var variants))
        {
            variants = Enumerable.Range(0, 5)
                .Select(i => GD.Load<AudioStream>($"res://assets/sounds/impact/{name}_{i:000}.ogg"))
                .Where(s => s is not null).ToArray();
            Impacts[name] = variants;
        }
        if (variants.Length == 0) return;
        float jitter = 0.94f + (float)Pick.NextDouble() * 0.12f;
        _instance.PlayOn(variants[Pick.Next(variants.Length)], db, pitch * jitter);
    }

    /// <summary>Efecto generado por código (explosión, cepillado…), ver <see cref="Synth"/>.</summary>
    public static void Synth(string name, float db = -6, float pitch = 1f)
    {
        if (_instance is null) return;
        _instance.PlayOn(AlfombraBaile.Synth.Get(name), db, pitch);
    }

    /// <summary>Volumen en dB que debe llevar un sonido continuo (de 0 a 1 de intensidad) con el ajuste de efectos.</summary>
    public static float LoopDb(float intensity, float baseDb) =>
        Settings.SfxLevel == 0 || intensity <= 0.001f ? -80 : baseDb + SfxOffset + Mathf.LinearToDb(Mathf.Clamp(intensity, 0, 1));

    /// <summary>Reproduce un sonido ya creado (por ejemplo, un tono generado).</summary>
    public static void PlayStream(AudioStream stream, float db = -8, float pitch = 1f) => _instance?.PlayOn(stream, db, pitch);

    /// <summary>
    /// Un tono musical sintetizado: fundamental con dos armónicos y caída suave, como una campanita.
    /// Así cada casilla de Simón tiene su nota sin necesitar archivos.
    /// </summary>
    public static AudioStreamWav Tone(float frequency, float seconds = 0.45f)
    {
        const int rate = 44100;
        int samples = (int)(rate * seconds);
        var data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Min(1, t / 0.008f) * Mathf.Exp(-t * 5.5f);
            float w = Mathf.Tau * frequency * t;
            float s = Mathf.Sin(w) * 0.62f + Mathf.Sin(2 * w) * 0.24f + Mathf.Sin(3 * w) * 0.09f;
            short v = (short)(Mathf.Clamp(s * env * 0.55f, -1, 1) * short.MaxValue);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }

    private void PlayInternal(string name, float pitch)
    {
        if (!Volumes.TryGetValue(name, out float db)) return;
        if (!_cache.TryGetValue(name, out var stream))
        {
            stream = GD.Load<AudioStream>($"res://assets/sounds/{name}.wav");
            _cache[name] = stream;
        }
        if (stream is null) return;
        PlayOn(stream, db, pitch);
    }

    /// <summary>Ajuste de los efectos según el volumen elegido (nivel 3 = tal cual).</summary>
    private static float SfxOffset => Settings.SfxLevel switch { 0 => -80, 1 => -14, 2 => -7, 4 => 4, _ => 0 };

    private void PlayOn(AudioStream stream, float db, float pitch)
    {
        if (Settings.SfxLevel == 0) return;
        var player = _players.FirstOrDefault(p => !p.Playing) ?? _players[_next++ % _players.Count];
        player.Stream = stream;
        player.VolumeDb = db + SfxOffset;
        player.PitchScale = pitch;
        player.Play();
    }
}
