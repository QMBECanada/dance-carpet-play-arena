using Godot;

namespace AlfombraBaile;

/// <summary>
/// El escenario común a todas las pantallas: ambiente con niebla y resplandor, luz, suelo con rejilla, trama de puntos
/// al fondo y motas flotando. Sigue al tema actual.
/// </summary>
public partial class StageBackdrop : Node3D
{
    private Environment _env = null!;
    private ShaderMaterial _floorMat = null!;
    private ShaderMaterial _dotsMat = null!;
    private StandardMaterial3D _moteMat = null!;
    private OmniLight3D _spot = null!;

    /// <summary>Punto del suelo (x, z) donde se concentra el brillo.</summary>
    public Vector2 Focus { get; set; } = Vector2.Zero;

    /// <summary>Niebla de fondo; el pinball la quita para que la mesa se vea nítida.</summary>
    public bool Fog { get; init; } = true;

    /// <summary>
    /// Imagen de fondo a pantalla completa (por ejemplo, la del menú). Con ella no se dibujan el suelo ni la trama:
    /// la escena 3D se ve sobre la imagen, con un velo del color del tema para que el texto se lea en cualquier tema.
    /// </summary>
    public string? Image { get; init; }

    private ColorRect? _veil;

    /// <summary>Posición de la trama de fondo.</summary>
    public Vector3 BackdropPosition { get; set; } = new(0, 6, -14);

    public override void _Ready()
    {
        _env = new Environment
        {
            BackgroundMode = Environment.BGMode.Color,
            AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightEnergy = 0.6f,
            TonemapMode = Environment.ToneMapper.Linear,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowBloom = 0.04f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Environment.GlowBlendModeEnum.Screen,
            FogEnabled = true,
            FogDensity = 0.03f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48, -22, 0),
            LightEnergy = 0.85f,
            ShadowEnabled = true,
        });

        _floorMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/floor.gdshader") };
        _floorMat.SetShaderParameter("focus", Focus);
        _dotsMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/halftone.gdshader") };
        if (Image is { } image)
        {
            // La imagen va en una capa por detrás de la escena 3D.
            _env.BackgroundMode = Environment.BGMode.Canvas;
            var layer = new CanvasLayer { Layer = -1 };
            AddChild(layer);
            var picture = new TextureRect
            {
                Texture = GD.Load<Texture2D>(image), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(picture);
            _veil = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
            _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(_veil);
        }
        else
        {
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(80, 80), Material = _floorMat } });
            AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(44, 22), Material = _dotsMat },
                Position = BackdropPosition,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        _spot = new OmniLight3D { Position = new Vector3(Focus.X, 0.4f, Focus.Y), OmniRange = 3.5f, LightEnergy = 1.1f, LightSpecular = 0 };
        AddChild(_spot);

        _moteMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        AddChild(new CpuParticles3D
        {
            Amount = 70,
            Lifetime = 9,
            Preprocess = 9,
            Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 6, Rings = 3, Material = _moteMat },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(9, 2.5f, 5),
            Position = new Vector3(0, 2.5f, -3),
            Direction = Vector3.Up,
            Spread = 25,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 0.08f,
            InitialVelocityMax = 0.25f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.6f,
        });

        ApplyTheme(Themes.Current);
        Themes.Changed += OnThemeChanged;
    }

    public override void _ExitTree() => Themes.Changed -= OnThemeChanged;

    private void OnThemeChanged() => ApplyTheme(Themes.Current);

    private void ApplyTheme(GameTheme theme)
    {
        _env.BackgroundColor = theme.Bg;
        _env.AmbientLightColor = theme.Light ? Colors.White : theme.Text.Lerp(theme.Accent, 0.15f);
        _env.FogLightColor = theme.Bg;
        _env.GlowIntensity = theme.Light ? 0.45f : 0.9f;
        _env.FogDensity = theme.Light ? 0.014f : 0.03f; // sobre fondo claro la niebla lava los colores
        _env.FogEnabled = Fog && Image is null;
        // Velo sobre la imagen: casi opaco en los temas claros (texto oscuro), ligero en los oscuros.
        if (_veil is not null) _veil.Color = new Color(theme.Bg, theme.Light ? 0.72f : 0.25f);

        _floorMat.SetShaderParameter("floor_color", theme.Floor);
        _floorMat.SetShaderParameter("bg_color", theme.Bg);
        _floorMat.SetShaderParameter("line_color", theme.Light ? theme.AccentText : theme.Accent);
        _dotsMat.SetShaderParameter("dot_color", new Color(theme.Text, theme.Light ? 0.10f : 0.13f));
        _moteMat.AlbedoColor = new Color(theme.Accent, theme.Light ? 0.55f : 0.7f);
        _spot.LightColor = theme.Accent;
    }
}
