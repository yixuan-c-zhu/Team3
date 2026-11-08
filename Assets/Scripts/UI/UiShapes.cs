using UnityEngine;
using UnityEngine.UI;

public static class UiShapes
{
    const int superSample = 4;

    public static Canvas MakeOverlay(GameObject host, int order)
    {
        Canvas canvas = host.GetComponent<Canvas>();
        if (canvas == null) canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        CanvasScaler scaler = host.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = host.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        return canvas;
    }

    public static RectTransform NewRect(string name, Transform parent, Vector2 pivot)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = pivot;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    public static T NewGraphic<T>(string name, Transform parent, Vector2 pivot) where T : Graphic
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = pivot;
        T graphic = obj.GetComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    public static Sprite RoundBox(float radius, float ring)
    {
        float side = radius * 2f + 4f;
        Vector2 size = new Vector2(side, side);
        Vector2 center = size * 0.5f;
        return Paint(size, 2f, Vector4.one * (radius + 1f), p =>
        {
            float d = RoundBoxDistance(p - center, center, radius);
            if (d > 0f || (ring > 0f && d < -ring)) return Color.clear;
            return Color.white;
        });
    }

    public static Sprite Paint(Vector2 size, float density, Vector4 border, System.Func<Vector2, Color> shade)
    {
        int width = Mathf.CeilToInt(size.x * density);
        int height = Mathf.CeilToInt(size.y * density);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[width * height];
        Color clear = new Color(1f, 1f, 1f, 0f);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                for (int i = 0; i < superSample; i++)
                {
                    for (int j = 0; j < superSample; j++)
                    {
                        Vector2 p = new Vector2(x + (i + 0.5f) / superSample, y + (j + 0.5f) / superSample) / density;
                        Color c = shade(p);
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                }
                sum /= superSample * superSample;
                pixels[y * width + x] = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : clear;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f * density, 0,
            SpriteMeshType.FullRect, border * density);
    }

    public static float RoundBoxDistance(Vector2 p, Vector2 half, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * radius;
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    public static float TaperDistance(Vector2 p, Vector2 a, Vector2 b, float radiusA, float radiusB)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
        return Vector2.Distance(p, a + ab * t) - Mathf.Lerp(radiusA, radiusB, t);
    }

    public static float BackOut(float t)
    {
        const float overshoot = 1.70158f;
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }
}
