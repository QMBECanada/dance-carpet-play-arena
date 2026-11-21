using System.Collections.Generic;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// La alfombra en 3D, tumbada en el suelo: casillas que se hunden y brillan al pisarlas.
/// Se usa al calibrar y servirá de reflejo en los juegos.
/// </summary>
public partial class Mat3D : Node3D
{
    private const float Spacing = 1.15f;
    private const float Thickness = 0.2f;
    private const float PressDepth = 0.11f;

    private sealed class Tile
    {
        public Node3D Node = null!;
        public ShaderMaterial Face = null!;
        public StandardMaterial3D Side = null!;
        public float BaseY;
        public float Press, PressTarget;
        public bool Blink;
        public float Dim = 1, DimTarget = 1;
        public float Glow, GlowTarget;
        public Label3D Key = null!;
        public Label3D Letter = null!;
    }

    private readonly Dictionary<Pad, Tile> _tiles = new();
    private StandardMaterial3D _baseMat = null!;
    private double _time;

    /// <summary>Solo las 8 casillas exteriores, sin base, centro ni Select/Start (zonas de la pista del rival en Tenis).</summary>
    public bool ZonesOnly { get; init; }

    public override void _Ready()
    {
        _baseMat = new StandardMaterial3D { Roughness = 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        if (!ZonesOnly) AddBase();
        BuildTiles();
        ApplyTheme(Themes.Current);
        UpdateKeys();
        MatInput.Instance.DeviceChanged += UpdateKeys;
    }

    public override void _ExitTree() => MatInput.Instance.DeviceChanged -= UpdateKeys;

    /// <summary>Las letras del teclado solo se ven cuando no hay alfombra conectada.</summary>
    private void UpdateKeys()
    {
        foreach (var tile in _tiles.Values) tile.Key.Visible = MatInput.UsingKeyboard;
    }

    private void AddBase()
    {
        var baseMesh = SlabMesh.Build(3.75f, 4.55f, 0.12f, 0.22f);
        baseMesh.SurfaceSetMaterial(0, _baseMat);
        baseMesh.SurfaceSetMaterial(1, _baseMat);
        AddChild(new MeshInstance3D
        {
            Mesh = baseMesh,
            RotationDegrees = new Vector3(-90, 0, 0),
            Position = new Vector3(0, 0, -0.32f),
        });
    }

    private void BuildTiles()
    {
        var shader = GD.Load<Shader>("res://shaders/tile.gdshader");
        foreach (var pad in PadInfo.CalibrationOrder)
        {
            if (ZonesOnly && pad is Pad.Select or Pad.Start or Pad.Center) continue;
            var cell = PadInfo.Cell(pad);
            bool pill = cell.Y < 0;
            float w = pill ? 0.8f : 1.0f, h = pill ? 0.34f : 1.0f;
            var face = new ShaderMaterial { Shader = shader };
            face.SetShaderParameter("glyph", (int)PadInfo.GlyphOf(pad));
            face.SetShaderParameter("angle", Mathf.DegToRad(PadInfo.ArrowAngle(pad)));
            var side = new StandardMaterial3D { Roughness = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            var mesh = SlabMesh.Build(w, h, Thickness, pill ? 0.17f : 0.12f);
            mesh.SurfaceSetMaterial(0, face);
            mesh.SurfaceSetMaterial(1, side);

            float z = pill ? -Spacing - 0.78f : (cell.Y - 1) * Spacing;
            var node = new Node3D { Position = new Vector3((cell.X - 1) * Spacing, 0.06f + Thickness / 2, z) };
            node.AddChild(new MeshInstance3D { Mesh = mesh, RotationDegrees = new Vector3(-90, 0, 0) });
            AddChild(node);

            // Letra del teclado en la esquina cercana de la casilla (en Select/Start, centrada).
            var key = new Label3D
            {
                Text = MatInput.KeyName(pad),
                Font = Fonts.Bold,
                FontSize = pill ? 56 : 72,
                PixelSize = 0.004f,
                OutlineSize = 0,
                RotationDegrees = new Vector3(-90, 0, 0),
                Position = pill ? new Vector3(0, Thickness / 2 + 0.006f, 0)
                    : new Vector3(w / 2 - 0.17f, Thickness / 2 + 0.006f, h / 2 - 0.15f),
            };
            node.AddChild(key);

            // Letra grande para Deletrear y Adivinanza; oculta hasta que un juego la pide.
            var letter = new Label3D
            {
                Font = Fonts.Display,
                FontSize = 190,
                PixelSize = 0.004f,
                OutlineSize = 0,
                Visible = false,
                RotationDegrees = new Vector3(-90, 0, 0),
                Position = new Vector3(0, Thickness / 2 + 0.007f, -0.02f),
            };
            node.AddChild(letter);
            _tiles[pad] = new Tile
            {
                Node = node, Face = face, Side = side, BaseY = node.Position.Y, Key = key, Letter = letter,
            };
        }
    }

    public void ApplyTheme(GameTheme theme)
    {
        _baseMat.AlbedoColor = theme.Light ? theme.Floor.Darkened(0.12f) : theme.Floor.Darkened(0.35f);
        foreach (var (pad, tile) in _tiles)
        {
            var color = pad == Pad.Center ? theme.Floor.Lerp(theme.Text, 0.22f) : theme.Role(PadInfo.Role(pad));
            var ink = pad == Pad.Center ? theme.Text : theme.Ink;
            tile.Face.SetShaderParameter("base", color);
            tile.Face.SetShaderParameter("ink", ink);
            tile.Side.AlbedoColor = color.Darkened(0.45f);
            tile.Key.Modulate = new Color(ink, 0.9f);
            tile.Letter.Modulate = ink;
        }
    }

    /// <summary>Centro de la cara superior de una casilla en reposo, en coordenadas de la alfombra.</summary>
    public Vector3 TileSurface(Pad pad)
    {
        var tile = _tiles[pad];
        var pos = tile.Node.Position;
        return new Vector3(pos.X, tile.BaseY + Thickness / 2, pos.Z);
    }

    public void SetPressed(Pad pad, bool pressed)
    {
        if (_tiles.TryGetValue(pad, out var tile)) tile.PressTarget = pressed ? 1 : 0;
    }

    public void SetBlink(Pad pad, bool blink)
    {
        if (_tiles.TryGetValue(pad, out var tile)) tile.Blink = blink;
    }

    /// <summary>Oscurece las casillas que no tocan (1 = normal).</summary>
    public void SetDim(Pad pad, float dim)
    {
        if (_tiles.TryGetValue(pad, out var tile)) tile.DimTarget = dim;
    }

    /// <summary>Muestra una letra grande en la casilla en lugar de su símbolo; null vuelve al símbolo.</summary>
    public void SetLetter(Pad pad, char? letter)
    {
        if (!_tiles.TryGetValue(pad, out var tile)) return;
        tile.Letter.Visible = letter is not null;
        tile.Letter.Text = letter?.ToString() ?? "";
        tile.Face.SetShaderParameter("glyph", letter is null ? (int)PadInfo.GlyphOf(pad) : (int)Glyph.Pill);
    }

    /// <summary>Intercambia la fila de delante y la de detrás (las zonas del rival vistas desde su lado).</summary>
    public void SetFlipRows(bool flip)
    {
        foreach (var (pad, tile) in _tiles)
        {
            var cell = PadInfo.Cell(pad);
            if (cell.Y < 0) continue;
            int row = flip ? 2 - cell.Y : cell.Y;
            var pos = tile.Node.Position;
            tile.Node.Position = new Vector3(pos.X, pos.Y, (row - 1) * Spacing);
        }
    }

    /// <summary>Brillo sin hundir la casilla (0..1).</summary>
    public void SetGlow(Pad pad, float glow)
    {
        if (_tiles.TryGetValue(pad, out var tile)) tile.GlowTarget = glow;
    }

    public void ClearAll()
    {
        foreach (var tile in _tiles.Values)
        {
            tile.PressTarget = 0;
            tile.Blink = false;
            tile.DimTarget = 1;
            tile.GlowTarget = 0;
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        float k = 1 - Mathf.Exp(-(float)delta * 22);
        foreach (var tile in _tiles.Values)
        {
            tile.Press = Mathf.Lerp(tile.Press, tile.PressTarget, k);
            tile.Dim = Mathf.Lerp(tile.Dim, tile.DimTarget, k * 0.5f);
            tile.Glow = Mathf.Lerp(tile.Glow, tile.GlowTarget, k * 0.6f);
            float blink = tile.Blink ? 0.5f + 0.5f * Mathf.Sin((float)_time * 7) : 0;
            var pos = tile.Node.Position;
            pos.Y = tile.BaseY - tile.Press * PressDepth + (tile.Blink ? blink * 0.05f : 0);
            tile.Node.Position = pos;
            tile.Face.SetShaderParameter("pressed", Mathf.Max(tile.Press, tile.Glow));
            tile.Face.SetShaderParameter("blink", blink);
            tile.Face.SetShaderParameter("dim", Mathf.Max(tile.Dim, blink));
        }
    }
}
