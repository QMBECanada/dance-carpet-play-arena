using System.Collections.Generic;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Lo que asoma por una casilla en Topos: un topillo, el topillo dorado o una bomba, con los dibujos de GPT
/// (assets/topos). Sale de un montículo de tierra en dos piezas: el agujero va detrás y el borde de delante tapa la
/// parte de abajo. «Lift» (0..1) lo saca; la casilla tapa lo que queda por debajo de su superficie.
/// </summary>
public partial class Critter : Node3D
{
    public enum State { Rising, Up, Hiding, Hit }
    public enum Kind { Mole, Golden, Bomb }

    public Pad Pad { get; init; }
    public Kind Type { get; init; }
    public bool IsBomb => Type == Kind.Bomb;
    public bool IsGolden => Type == Kind.Golden;
    public double UpTime { get; init; }
    public State Phase { get; set; } = State.Rising;
    public double T { get; set; }

    /// <summary>Se esconde asustado (por una explosión o porque se acaba la partida) en vez de burlándose.</summary>
    public bool Scared { get; set; }

    /// <summary>Tamaño general: 1 = el topillo algo más ancho que la casilla; 0,75 queda bien dentro de ella.</summary>
    private const float Size = 0.75f;

    /// <summary>Escala de los dibujos del topillo (250 px de ancho).</summary>
    private const float Pixel = 1.05f * Size / 250f;

    private static readonly Dictionary<string, Texture2D> Cache = new();

    private Node3D _lift = null!, _mound = null!;
    private Sprite3D _body = null!;
    private string _pose = "";
    private float _seed;

    public static Texture2D Tex(string name)
    {
        if (!Cache.TryGetValue(name, out var tex))
        {
            tex = GD.Load<Texture2D>($"res://assets/topos/{name}.png");
            Cache[name] = tex;
        }
        return tex;
    }

    /// <summary>Dibujo que siempre mira a la cámara (girando solo en vertical) y se ordena bien con el resto.</summary>
    public static Sprite3D Sprite(string name, float pixel, BaseMaterial3D.BillboardModeEnum billboard = BaseMaterial3D.BillboardModeEnum.FixedY) => new()
    {
        Texture = Tex(name),
        PixelSize = pixel,
        Billboard = billboard,
        Shaded = false,
        AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
    };

    public override void _Ready()
    {
        _seed = GD.Randf() * 10;

        _mound = new Node3D { Scale = Vector3.One * 0.01f };
        AddChild(_mound);
        var back = Sprite("monticulo_detras", 1.3f * Size / 312f);
        back.Position = new Vector3(0, 175 * back.PixelSize / 2 - 0.06f * Size, -0.16f * Size);
        _mound.AddChild(back);

        _lift = new Node3D();
        AddChild(_lift);
        _body = Sprite(BasePose, Pixel);
        _lift.AddChild(_body);
        SetPose(BasePose);

        var front = Sprite("monticulo_delante", 1.32f * Size / 313f);
        front.Position = new Vector3(0, 97 * front.PixelSize / 2 - 0.05f * Size, 0.2f * Size);
        _mound.AddChild(front);

        _mound.CreateTween().TweenProperty(_mound, "scale", Vector3.One, 0.18)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        SetLift(0);
    }

    private string BasePose => IsBomb ? "bomba" : IsGolden ? "topo_dorado" : "topo";

    private void SetPose(string name)
    {
        if (name == _pose) return;
        _pose = name;
        var tex = Tex(name);
        _body.Texture = tex;
        _body.Position = new Vector3(0, tex.GetHeight() * Pixel / 2, 0);
    }

    public void SetLift(float k) => _lift.Position = new Vector3(0, Mathf.Lerp(-1.2f, 0.02f, k), 0);

    /// <summary>Aplastado: 1 = normal; menos de 1 = chafado y ensanchado.</summary>
    public void SetSquash(float sy) => _lift.Scale = new Vector3(1 + (1 - sy) * 0.4f, sy, 1);

    /// <summary>Elige el dibujo según lo que esté pasando y añade algo de vida (parpadeo de la bomba, brillo del dorado).</summary>
    public void Idle(double time)
    {
        float t = (float)time + _seed;
        if (IsBomb)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 11);
            _body.Modulate = Colors.White.Lerp(new Color(1, 0.55f, 0.5f), pulse);
            _lift.Scale = Vector3.One * (1 + 0.05f * Mathf.Sin(t * 9));
            _body.Visible = Phase != State.Hit;
            return;
        }

        string pose = Phase switch
        {
            State.Hit => IsGolden ? "topo_dorado" : "topo_mareado",
            State.Hiding => Scared ? "topo_asustado" : IsGolden ? "topo_dorado" : "topo_burla",
            State.Up when !IsGolden && T > UpTime * 0.65 => "topo_burla",
            State.Up when !IsGolden && Mathf.Sin(t * 2.6f) > 0.55f => "topo_saluda",
            _ => BasePose,
        };
        SetPose(pose);
        if (IsGolden) _body.Modulate = Colors.White * (1.05f + 0.2f * Mathf.Sin(t * 14));
    }

    /// <summary>Se va: el montículo se encoge y el nodo desaparece.</summary>
    public void Vanish()
    {
        _body.Visible = false;
        var tween = _mound.CreateTween();
        tween.TweenProperty(_mound, "scale", Vector3.One * 0.01f, 0.15);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}

/// <summary>Efectos de un solo uso: chispas, explosión y números flotantes.</summary>
public static class Fx
{
    public static void Burst(Node3D parent, Vector3 at, Color color, int amount = 16, float speed = 3.2f)
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = color * 1.8f,
            VertexColorUseAsAlbedo = false,
        };
        var particles = new CpuParticles3D
        {
            Position = at,
            Emitting = false,
            OneShot = true,
            Explosiveness = 1,
            Amount = amount,
            Lifetime = 0.7f,
            Mesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 8, Rings = 4, Material = mat },
            Direction = Vector3.Up,
            Spread = 70,
            InitialVelocityMin = speed * 0.6f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -9, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
        };
        parent.AddChild(particles);
        particles.Emitting = true;
        particles.GetTree().CreateTimer(1.2).Timeout += particles.QueueFree;
    }

    /// <summary>Un dibujo de un solo uso (golpe, explosión, tierra) que crece y se desvanece.</summary>
    public static void Picture(Node3D parent, Vector3 at, string name, float width, double life, float from = 0.4f)
    {
        var tex = Critter.Tex(name);
        var sprite = Critter.Sprite(name, width / tex.GetWidth(), BaseMaterial3D.BillboardModeEnum.Enabled);
        sprite.AlphaCut = SpriteBase3D.AlphaCutMode.Disabled;
        sprite.NoDepthTest = true;
        sprite.Position = at;
        parent.AddChild(sprite);
        var tween = sprite.CreateTween().SetParallel();
        tween.TweenProperty(sprite, "scale", Vector3.One, life * 0.4).From(Vector3.One * from)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(sprite, "modulate:a", 0.0, life * 0.5).SetDelay(life * 0.5);
        tween.Chain().TweenCallback(Callable.From(sprite.QueueFree));
    }

    public static void Flash(Node3D parent, Vector3 at, Color color)
    {
        var light = new OmniLight3D { Position = at + Vector3.Up * 0.6f, LightColor = color, LightEnergy = 6, OmniRange = 4 };
        parent.AddChild(light);
        var tween = light.CreateTween();
        tween.TweenProperty(light, "light_energy", 0.0, 0.45);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }

    public static void Popup(Node3D parent, Vector3 at, string text, Color color, Color outline, int size = 96)
    {
        var label = new Label3D
        {
            Text = text,
            Font = Fonts.Display,
            FontSize = size,
            PixelSize = 0.004f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Modulate = color,
            OutlineSize = 22,
            OutlineModulate = outline,
            Position = at,
        };
        parent.AddChild(label);
        var tween = label.CreateTween().SetParallel();
        tween.TweenProperty(label, "position", at + Vector3.Up * 0.9f, 0.8).SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "scale", Vector3.One, 0.25).From(Vector3.One * 0.4f).SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.3).SetDelay(0.5);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}
