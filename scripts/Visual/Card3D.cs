using Godot;

namespace AlfombraBaile;

/// <summary>Una ficha 3D del carrusel: placa con grosor, cara dibujada en un SubViewport y halo cuando está elegida.</summary>
public partial class Card3D : Node3D
{
    public const float Width = 1.5f, Height = 2.0f, Depth = 0.14f, Radius = 0.14f;

    private readonly GameInfo _game;
    private CardFace _face = null!;
    private ShaderMaterial _faceMat = null!;
    private StandardMaterial3D _sideMat = null!;
    private ShaderMaterial _glowMat = null!;

    public Card3D() : this(GameCatalog.All[0]) { }

    public Card3D(GameInfo game) => _game = game;

    public GameInfo Game => _game;

    public override void _Ready()
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(420, 560),
            Disable3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _face = new CardFace(_game);
        viewport.AddChild(_face);
        AddChild(viewport);

        _faceMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/card_face.gdshader") };
        _faceMat.SetShaderParameter("face_tex", viewport.GetTexture());
        _sideMat = new StandardMaterial3D { Roughness = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

        var mesh = SlabMesh.Build(Width, Height, Depth, Radius);
        mesh.SurfaceSetMaterial(0, _faceMat);
        mesh.SurfaceSetMaterial(1, _sideMat);
        AddChild(new MeshInstance3D { Mesh = mesh });

        var glowSize = new Vector2(Width + 1.0f, Height + 1.0f);
        _glowMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/glow_plate.gdshader") };
        _glowMat.SetShaderParameter("size", glowSize);
        _glowMat.SetShaderParameter("inner", new Vector2(Width, Height));
        _glowMat.SetShaderParameter("radius", Radius);
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = glowSize, Material = _glowMat },
            Position = new Vector3(0, 0, -Depth / 2 - 0.01f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        ApplyTheme(Themes.Current);
    }

    public void ApplyTheme(GameTheme theme)
    {
        _sideMat.AlbedoColor = theme.Light ? theme.Card.Darkened(0.12f) : theme.Card.Darkened(0.35f);
        _glowMat.SetShaderParameter("color", theme.Accent);
        _face.QueueRedraw();
        _face.ApplyTheme(theme);
    }

    /// <summary>dim: 1 = brillo completo; glow: 0..1 intensidad del halo de selección.</summary>
    public void SetLook(float dim, float glow)
    {
        _faceMat.SetShaderParameter("dim", dim);
        _glowMat.SetShaderParameter("intensity", glow);
    }
}

/// <summary>Lo que se ve en la cara de la ficha: fondo degradado, franja de puntos del color del juego, icono y nombre.</summary>
public partial class CardFace : Control
{
    private readonly GameInfo _game;
    private Label _name = null!;

    public CardFace() : this(GameCatalog.All[0]) { }

    public CardFace(GameInfo game) => _game = game;

    public override void _Ready()
    {
        Size = new Vector2(420, 560);
        _name = Fonts.Label(_game.Name.ToUpperInvariant(), Fonts.Display, 44, HorizontalAlignment.Center);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.Position = new Vector2(24, 390);
        _name.Size = new Vector2(372, 140);
        AddChild(_name);
        ApplyTheme(Themes.Current);
    }

    public void ApplyTheme(GameTheme theme)
    {
        _name?.AddThemeColorOverride("font_color", theme.Text);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var theme = Themes.Current;
        var size = Size;
        var top = theme.Card.Lerp(theme.Text, theme.Light ? 0.02f : 0.12f);
        var bottom = theme.Card;
        DrawPolygon(new[] { Vector2.Zero, new Vector2(size.X, 0), size, new Vector2(0, size.Y) },
            new[] { top, top, bottom, bottom });

        var c = theme.Role(_game.Color);
        float band = size.Y * 0.42f;
        DrawPolygon(new[] { Vector2.Zero, new Vector2(size.X, 0), new Vector2(size.X, band), new Vector2(0, band) },
            new[] { new Color(c, 0.32f), new Color(c, 0.32f), new Color(c, 0), new Color(c, 0) });
        const float step = 18;
        for (float y = step / 2; y < band; y += step)
        {
            float fade = 1 - y / band;
            for (float x = step / 2; x < size.X; x += step)
                DrawCircle(new Vector2(x, y), 3.4f, new Color(c, 0.75f * fade));
        }

        // Borde fino para que la ficha se recorte bien sobre el fondo.
        DrawRect(new Rect2(Vector2.Zero, size), new Color(theme.Text, 0.12f), false, 6);

        var iconCenter = new Vector2(size.X / 2, size.Y * 0.4f);
        DrawCircle(iconCenter, 120, new Color(c, 0.10f));
        var iconColor = theme.Light ? c.Darkened(0.25f) : c;
        Painter.Icon(this, _game.Icon, iconCenter, 200, iconColor);
    }
}
