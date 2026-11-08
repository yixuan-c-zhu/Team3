using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PieceOutline : MaskableGraphic
{
    public struct Ring
    {
        public float radius;
        public Color32 color;

        public Ring(float radius, Color color)
        {
            this.radius = radius;
            this.color = color;
        }
    }

    const float turnStep = Mathf.PI / 8f;
    const int dashTexels = 24;
    const int dashOn = 14;

    static Texture2D dashTexture;

    public bool dashed;
    public float dashPeriod = 24f;
    public float dashOffset;

    private readonly List<Vector2> hull = new List<Vector2>();
    private readonly List<Vector2> points = new List<Vector2>();
    private readonly List<Vector2> normals = new List<Vector2>();
    private Ring[] rings = new Ring[0];

    public override Texture mainTexture
    {
        get { return dashed ? DashTexture : s_WhiteTexture; }
    }

    static Texture2D DashTexture
    {
        get
        {
            if (dashTexture == null)
            {
                dashTexture = new Texture2D(dashTexels, 1, TextureFormat.RGBA32, false);
                dashTexture.wrapModeU = TextureWrapMode.Repeat;
                dashTexture.wrapModeV = TextureWrapMode.Clamp;
                for (int x = 0; x < dashTexels; x++)
                {
                    dashTexture.SetPixel(x, 0, x < dashOn ? Color.white : new Color(1f, 1f, 1f, 0f));
                }
                dashTexture.Apply();
            }
            return dashTexture;
        }
    }

    public void SetShape(List<Vector2> newHull, Ring[] newRings)
    {
        hull.Clear();
        hull.AddRange(newHull);
        rings = newRings;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (hull.Count < 3 || rings.Length < 2) return;

        BuildContour();
        float reach = rings[rings.Length / 2].radius;
        int along = points.Count;
        int across = rings.Length;

        float total = 0f;
        for (int i = 1; i < along; i++)
        {
            total += Vector2.Distance(points[i] + normals[i] * reach, points[i - 1] + normals[i - 1] * reach);
        }
        float scale = dashed && total > 0f ? Mathf.Max(1f, Mathf.Round(total / dashPeriod)) / total : 0f;

        float arc = 0f;
        for (int i = 0; i < along; i++)
        {
            if (i > 0)
            {
                arc += Vector2.Distance(points[i] + normals[i] * reach, points[i - 1] + normals[i - 1] * reach);
            }
            Vector4 uv = new Vector4(arc * scale + dashOffset, 0.5f, 0f, 0f);
            for (int j = 0; j < across; j++)
            {
                vh.AddVert(points[i] + normals[i] * rings[j].radius, rings[j].color, uv);
            }
        }

        for (int i = 0; i < along - 1; i++)
        {
            for (int j = 0; j < across - 1; j++)
            {
                int a = i * across + j;
                int b = a + 1;
                int c = a + across;
                int d = c + 1;
                vh.AddTriangle(a, b, d);
                vh.AddTriangle(a, d, c);
            }
        }
    }

    void BuildContour()
    {
        points.Clear();
        normals.Clear();
        int count = hull.Count;
        for (int i = 0; i < count; i++)
        {
            Vector2 here = hull[i];
            Vector2 inward = (here - hull[(i + count - 1) % count]).normalized;
            Vector2 outward = (hull[(i + 1) % count] - here).normalized;
            float from = Mathf.Atan2(-inward.x, inward.y);
            float to = Mathf.Atan2(-outward.x, outward.y);
            float turn = Mathf.Repeat(to - from, Mathf.PI * 2f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(turn / turnStep));
            for (int k = 0; k <= steps; k++)
            {
                float angle = from + turn * k / steps;
                points.Add(here);
                normals.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }
        }
        points.Add(points[0]);
        normals.Add(normals[0]);
    }

    public static void ConvexHull(List<Vector2> input, List<Vector2> output)
    {
        output.Clear();
        input.Sort(CompareByX);
        int count = input.Count;
        for (int i = 0; i < count; i++)
        {
            while (output.Count >= 2 && Turn(output[output.Count - 2], output[output.Count - 1], input[i]) <= 0.01f)
            {
                output.RemoveAt(output.Count - 1);
            }
            output.Add(input[i]);
        }
        int lower = output.Count + 1;
        for (int i = count - 2; i >= 0; i--)
        {
            while (output.Count >= lower && Turn(output[output.Count - 2], output[output.Count - 1], input[i]) <= 0.01f)
            {
                output.RemoveAt(output.Count - 1);
            }
            output.Add(input[i]);
        }
        if (output.Count > 1)
        {
            output.RemoveAt(output.Count - 1);
        }
    }

    static int CompareByX(Vector2 a, Vector2 b)
    {
        int result = a.x.CompareTo(b.x);
        return result != 0 ? result : a.y.CompareTo(b.y);
    }

    static float Turn(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }
}
