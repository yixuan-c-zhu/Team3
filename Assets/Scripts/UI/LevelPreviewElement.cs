using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Resolution-independent puzzle dioramas; no downloaded art or runtime textures.</summary>
public sealed class LevelPreviewElement : VisualElement
{
    public int Kind { get; set; }
    Painter2D painter;
    float scale;
    Vector2 offset;
    static readonly Color Blue = Hex(0x536CE6), Orange = Hex(0xED9966), Ink = Hex(0x253C46);

    public LevelPreviewElement(int kind)
    {
        Kind = kind;
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    static Color Hex(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    Vector2 P(float x, float y) => new Vector2(x, y) * scale + offset;

    void Polygon(Color color, params Vector2[] points)
    {
        painter.fillColor = color;
        painter.BeginPath();
        painter.MoveTo(P(points[0].x, points[0].y));
        for (int i = 1; i < points.Length; i++) painter.LineTo(P(points[i].x, points[i].y));
        painter.ClosePath();
        painter.Fill();
    }

    void Line(Color color, float width, params Vector2[] points)
    {
        painter.strokeColor = color;
        painter.lineWidth = width * scale;
        painter.BeginPath();
        painter.MoveTo(P(points[0].x, points[0].y));
        for (int i = 1; i < points.Length; i++) painter.LineTo(P(points[i].x, points[i].y));
        painter.Stroke();
    }

    void Box(float x, float y, float w, float d, float h, Color color)
    {
        var a = new Vector2(x, y - h);
        var b = new Vector2(x + w, y - h + w * .48f);
        var c = new Vector2(x + w - d, y - h + (w + d) * .48f);
        var e = new Vector2(x - d, y - h + d * .48f);
        var down = Vector2.up * h;
        Polygon(Color.Lerp(color, Ink, .23f), e, c, c + down, e + down);
        Polygon(Color.Lerp(color, Ink, .42f), b, c, c + down, b + down);
        Polygon(Color.Lerp(color, Color.white, .22f), a, b, c, e);
    }

    void Dashed(Vector2 from, Vector2 to, Color color)
    {
        float length = Vector2.Distance(from, to);
        for (float t = 0; t < length; t += 11)
            Line(color, 1.5f, Vector2.Lerp(from, to, t / length), Vector2.Lerp(from, to, Mathf.Min(t + 5, length) / length));
    }

    void Draw(MeshGenerationContext context)
    {
        if (contentRect.width < 1 || contentRect.height < 1) return;
        painter = context.painter2D;
        scale = Mathf.Min(contentRect.width / 360f, contentRect.height / 270f);
        offset = new Vector2((contentRect.width - 360 * scale) / 2, (contentRect.height - 270 * scale) / 2);

        // Light construction grid keeps the illustration close to a spatial puzzle blueprint.
        var grid = new Color(.24f, .36f, .36f, .09f);
        for (int i = -3; i <= 3; i++)
        {
            Line(grid, 1, new Vector2(180 + i * 27, 108), new Vector2(312 + i * 8, 206));
            Line(grid, 1, new Vector2(180 + i * 27, 108), new Vector2(48 + i * 8, 206));
        }
        var shadow = new Color(.1f, .22f, .26f, .07f);
        Polygon(shadow, new Vector2(55, 200), new Vector2(180, 145), new Vector2(314, 205), new Vector2(186, 261));

        switch (Kind)
        {
            case 0: // A body separated by a crisp cut.
                Box(170, 155, 113, 107, 12, Hex(0xD3DDD8));
                Box(162, 137, 61, 57, 34, Blue);
                Box(178, 83, 61, 57, 28, Orange);
                Dashed(new Vector2(71, 123), new Vector2(282, 168), Hex(0x758D88));
                Line(Hex(0xECF5EC), 3, new Vector2(139, 78), new Vector2(198, 106));
                break;
            case 1: // Piece on a pressure plate and an open doorway.
                Box(168, 164, 123, 112, 12, Hex(0xD3DDD8));
                Box(122, 170, 44, 38, 6, Orange);
                Box(122, 154, 32, 30, 27, Blue);
                Box(230, 137, 15, 60, 93, Hex(0xA6B9B2));
                Box(229, 36, 16, 60, 16, Hex(0xD3DDD8));
                Dashed(new Vector2(157, 196), new Vector2(234, 162), Orange);
                break;
            case 2: // Thin shape between two walls.
                Box(170, 165, 115, 110, 12, Hex(0xD3DDD8));
                Box(162, 141, 18, 76, 96, Hex(0xB6C9C0));
                Box(208, 156, 17, 46, 58, Blue);
                Box(245, 154, 18, 76, 96, Hex(0xB6C9C0));
                Dashed(new Vector2(99, 220), new Vector2(262, 141), Orange);
                break;
            case 3: // Combined pressure plate and low ceiling.
                Box(170, 165, 122, 112, 12, Hex(0xD3DDD8));
                Box(114, 167, 38, 37, 6, Orange);
                Box(114, 157, 31, 31, 42, Blue);
                Box(227, 128, 58, 46, 54, Hex(0xB6C9C0));
                Box(225, 171, 43, 36, 22, Orange);
                break;
            case 4: // Two bodies and an interrupted upward trajectory.
                Box(130, 168, 66, 78, 13, Hex(0xADC8B8));
                Box(268, 132, 65, 63, 13, Hex(0xADC8B8));
                Box(128, 147, 42, 43, 38, Blue);
                Box(177, 66, 40, 41, 34, Orange);
                Dashed(new Vector2(135, 107), new Vector2(159, 76), Hex(0x638A77));
                Dashed(new Vector2(207, 73), new Vector2(260, 102), Hex(0x638A77));
                Line(Hex(0x638A77), 1.5f, new Vector2(251, 91), new Vector2(260, 102), new Vector2(247, 105));
                Box(209, 173, 48, 19, 4, new Color(.36f, .53f, .81f, .55f));
                break;
            default: // Button, route, and raised orange gate.
                Box(165, 162, 132, 109, 12, Hex(0xD9C6AD));
                Box(123, 169, 47, 40, 7, Orange);
                Box(125, 149, 29, 29, 32, Blue);
                Box(249, 141, 13, 61, 101, Hex(0xAEAA9A));
                Box(217, 118, 13, 45, 65, Orange);
                Box(226, 145, 13, 61, 17, Hex(0xC2BBA9));
                Dashed(new Vector2(169, 191), new Vector2(236, 159), Hex(0xB18054));
                Line(Hex(0xB18054), 1.5f, new Vector2(207, 82), new Vector2(207, 45), new Vector2(201, 52));
                break;
        }
        // Small drafting marks, outside the playable silhouette.
        Line(new Color(.2f, .3f, .3f, .25f), 1, new Vector2(36, 69), new Vector2(36, 57), new Vector2(48, 57));
        Line(new Color(.2f, .3f, .3f, .25f), 1, new Vector2(314, 217), new Vector2(326, 217), new Vector2(326, 205));
    }
}
