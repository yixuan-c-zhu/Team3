using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent rounded panels and line icons for the tutorial HUD.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TutorialHudGraphic : MaskableGraphic
{
    public enum Shape { Panel, Diamond, Cut, View, Color, Switch }
    public Shape shape;
    public Color edge = Color.white;
    public Color blue = new Color(0.30f, 0.48f, 1f);
    public Color orange = new Color(1f, 0.47f, 0.20f);
    public float radius = 14f;
    public float border = 2f;
    const int CornerSteps = 8;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (shape == Shape.Panel)
        {
            RoundedPanel(mesh, rectTransform.rect, radius, color, edge, border);
            return;
        }
        switch (shape)
        {
            case Shape.Diamond:
                Polygon(mesh, color, P(50, 0), P(100, 50), P(50, 100), P(0, 50));
                break;
            case Shape.Cut:
                Cube(mesh, 0, 0, 1);
                DashedStroke(mesh, 11, 88, 91, 4, 3.2f);
                break;
            case Shape.View:
                Cube(mesh, 15, -1, 0.78f);
                Arc(mesh, 51, 71, 40, 17, 15, 164);
                Arc(mesh, 51, 71, 40, 17, 195, 344);
                Arrow(mesh, 13, 76, 19, 68, 23, 79);
                Arrow(mesh, 89, 66, 83, 74, 79, 63);
                break;
            case Shape.Color:
                IconBox(mesh, 14, 32, 29, 31, blue, Color.clear, 0);
                IconBox(mesh, 57, 32, 29, 31, orange, Color.clear, 0);
                Arc(mesh, 50, 40, 29, 27, 218, 316);
                Arrow(mesh, 65, 16, 73, 23, 62, 25);
                Arc(mesh, 50, 55, 29, 27, 38, 136);
                Arrow(mesh, 35, 79, 27, 72, 38, 70);
                break;
            case Shape.Switch:
                IconBox(mesh, 10, 17, 34, 28, Color.clear, color, 2.8f);
                IconBox(mesh, 56, 54, 34, 28, Color.clear, color, 2.8f);
                Stroke(mesh, 51, 25, 66, 25);
                Stroke(mesh, 66, 25, 80, 40);
                Arrow(mesh, 79, 29, 80, 40, 69, 39);
                Stroke(mesh, 49, 74, 34, 74);
                Stroke(mesh, 34, 74, 20, 59);
                Arrow(mesh, 21, 70, 20, 59, 31, 60);
                break;
        }
    }

    void Cube(VertexHelper mesh, float x, float y, float scale)
    {
        Vector2[] points = { new Vector2(24, 29), new Vector2(61, 29), new Vector2(61, 70),
            new Vector2(24, 70), new Vector2(43, 12), new Vector2(80, 12), new Vector2(80, 53) };
        for (int i = 0; i < points.Length; i++) points[i] = P(x + points[i].x * scale, y + points[i].y * scale);
        int[] pairs = { 0, 1, 1, 2, 2, 3, 3, 0, 0, 4, 4, 5, 5, 6, 6, 2, 1, 5 };
        for (int i = 0; i < pairs.Length; i += 2) Line(mesh, points[pairs[i]], points[pairs[i + 1]], 2.8f, color);
    }

    Vector2 P(float x, float y)
    {
        Rect r = rectTransform.rect;
        return new Vector2(r.xMin + r.width * x / 100f, r.yMax - r.height * y / 100f);
    }

    void Stroke(VertexHelper mesh, float x1, float y1, float x2, float y2, float width = 2.8f)
    {
        Line(mesh, P(x1, y1), P(x2, y2), width, color);
    }

    void Arrow(VertexHelper mesh, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        Stroke(mesh, x1, y1, x2, y2);
        Stroke(mesh, x2, y2, x3, y3);
    }

    void DashedStroke(VertexHelper mesh, float x1, float y1, float x2, float y2, float width)
    {
        Vector2 start = P(x1, y1), end = P(x2, y2);
        const int dashes = 9;
        for (int i = 0; i < dashes; i++)
        {
            Vector2 a = Vector2.Lerp(start, end, i / (float)dashes);
            Vector2 b = Vector2.Lerp(start, end, (i + 0.6f) / dashes);
            Line(mesh, a, b, width, color);
        }
    }

    void Arc(VertexHelper mesh, float x, float y, float rx, float ry, float from, float to)
    {
        Vector2 previous = P(x + Mathf.Cos(from * Mathf.Deg2Rad) * rx, y + Mathf.Sin(from * Mathf.Deg2Rad) * ry);
        for (int i = 1; i <= 24; i++)
        {
            float angle = Mathf.Lerp(from, to, i / 24f) * Mathf.Deg2Rad;
            Vector2 next = P(x + Mathf.Cos(angle) * rx, y + Mathf.Sin(angle) * ry);
            Line(mesh, previous, next, 2.8f, color);
            previous = next;
        }
    }

    void IconBox(VertexHelper mesh, float x, float y, float width, float height, Color fill, Color outline, float stroke)
    {
        Vector2 topLeft = P(x, y), bottomRight = P(x + width, y + height);
        RoundedPanel(mesh, Rect.MinMaxRect(topLeft.x, bottomRight.y, bottomRight.x, topLeft.y), 4, fill, outline, stroke);
    }

    static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized;
        Vector2 inner = normal * (width * 0.5f);
        Vector2 outer = normal * (width * 0.5f + 0.8f);
        Polygon(mesh, tint, a - inner, a + inner, b + inner, b - inner);
        Color clear = new Color(tint.r, tint.g, tint.b, 0);
        Quad(mesh, a + inner, a + outer, b + outer, b + inner, tint, clear, clear, tint);
        Quad(mesh, a - outer, a - inner, b - inner, b - outer, clear, tint, tint, clear);
    }

    static void RoundedPanel(VertexHelper mesh, Rect rect, float corner, Color fill, Color outline, float stroke)
    {
        var outer = RoundedPath(rect, corner);
        var innerRect = new Rect(rect.x + stroke, rect.y + stroke, Mathf.Max(0, rect.width - stroke * 2), Mathf.Max(0, rect.height - stroke * 2));
        var inner = stroke > 0 ? RoundedPath(innerRect, Mathf.Max(0, corner - stroke)) : outer;
        if (fill.a > 0) Polygon(mesh, fill, inner);
        if (stroke > 0 && outline.a > 0) Ring(mesh, inner, outer, outline, outline);
        var featherRect = new Rect(rect.x - 0.8f, rect.y - 0.8f, rect.width + 1.6f, rect.height + 1.6f);
        Color edgeColor = stroke > 0 ? outline : fill;
        Ring(mesh, outer, RoundedPath(featherRect, corner + 0.8f), edgeColor, new Color(edgeColor.r, edgeColor.g, edgeColor.b, 0));
    }

    static Vector2[] RoundedPath(Rect rect, float corner)
    {
        float r = Mathf.Clamp(corner, 0, Mathf.Min(rect.width, rect.height) * 0.5f);
        Vector2[] centers = { new Vector2(rect.xMin + r, rect.yMin + r), new Vector2(rect.xMax - r, rect.yMin + r),
            new Vector2(rect.xMax - r, rect.yMax - r), new Vector2(rect.xMin + r, rect.yMax - r) };
        var path = new Vector2[4 * (CornerSteps + 1)];
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= CornerSteps; i++)
            {
                float angle = (180 + c * 90 + i * 90f / CornerSteps) * Mathf.Deg2Rad;
                path[c * (CornerSteps + 1) + i] = centers[c] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
            }
        return path;
    }

    static void Ring(VertexHelper mesh, Vector2[] inner, Vector2[] outer, Color inside, Color outside)
    {
        for (int i = 0; i < inner.Length; i++)
        {
            int next = (i + 1) % inner.Length;
            Quad(mesh, inner[i], outer[i], outer[next], inner[next], inside, outside, outside, inside);
        }
    }

    static void Polygon(VertexHelper mesh, Color tint, params Vector2[] points)
    {
        int start = mesh.currentVertCount;
        Vector2 center = Vector2.zero;
        foreach (var p in points) center += p;
        mesh.AddVert(center / points.Length, tint, Vector2.zero);
        foreach (var p in points) mesh.AddVert(p, tint, Vector2.zero);
        for (int i = 0; i < points.Length; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points.Length);
    }

    static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(a, ca, Vector2.zero); mesh.AddVert(b, cb, Vector2.zero);
        mesh.AddVert(c, cc, Vector2.zero); mesh.AddVert(d, cd, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
