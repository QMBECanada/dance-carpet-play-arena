using System.Collections.Generic;
using Godot;

namespace AlfombraBaile;

/// <summary>
/// Placa rectangular con esquinas redondeadas y grosor (fichas y casillas). Superficie 0: cara frontal (+Z), con UV que
/// cubre toda la cara; superficie 1: trasera y cantos. Los materiales no recortan caras, así que el orden de los
/// vértices no importa.
/// </summary>
public static class SlabMesh
{
    public static ArrayMesh Build(float width, float height, float depth, float radius, int segments = 6)
    {
        var outline = Outline(width, height, radius, segments);
        float zf = depth / 2, zb = -depth / 2;

        var front = new SurfaceTool();
        front.Begin(Mesh.PrimitiveType.Triangles);
        Cap(front, outline, zf, Vector3.Back, width, height);
        var mesh = front.Commit();

        var rest = new SurfaceTool();
        rest.Begin(Mesh.PrimitiveType.Triangles);
        Cap(rest, outline, zb, Vector3.Forward, width, height);
        for (int k = 0; k < outline.Count; k++)
        {
            Vector2 a = outline[k], b = outline[(k + 1) % outline.Count];
            var edge = b - a;
            var normal = new Vector3(edge.Y, -edge.X, 0).Normalized();
            var quad = new[]
            {
                new Vector3(a.X, a.Y, zf), new Vector3(b.X, b.Y, zf), new Vector3(b.X, b.Y, zb),
                new Vector3(a.X, a.Y, zf), new Vector3(b.X, b.Y, zb), new Vector3(a.X, a.Y, zb),
            };
            foreach (var v in quad)
            {
                rest.SetNormal(normal);
                rest.SetUV(Vector2.Zero);
                rest.AddVertex(v);
            }
        }
        rest.Commit(mesh);
        return mesh;
    }

    private static void Cap(SurfaceTool st, List<Vector2> outline, float z, Vector3 normal, float w, float h)
    {
        for (int k = 0; k < outline.Count; k++)
        {
            Vector2 a = outline[k], b = outline[(k + 1) % outline.Count];
            foreach (var p in new[] { Vector2.Zero, b, a })
            {
                st.SetNormal(normal);
                st.SetUV(new Vector2(p.X / w + 0.5f, 0.5f - p.Y / h));
                st.AddVertex(new Vector3(p.X, p.Y, z));
            }
        }
    }

    private static List<Vector2> Outline(float w, float h, float r, int segments)
    {
        float hx = w / 2 - r, hy = h / 2 - r;
        var corners = new (Vector2 Center, float Start)[]
        {
            (new(hx, hy), 0), (new(-hx, hy), 90), (new(-hx, -hy), 180), (new(hx, -hy), 270),
        };
        var points = new List<Vector2>();
        foreach (var (center, start) in corners)
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.DegToRad(start + 90f * i / segments);
                points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        return points;
    }
}
