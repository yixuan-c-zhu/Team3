using UnityEngine;
using UnityEngine.SceneManagement;

public static class PaperFeel
{
    public static readonly Color Ink = new Color(0.133f, 0.157f, 0.227f, 1f);
    public static readonly Color Cardstock = new Color(1f, 0.992f, 0.965f, 1f);
    public static readonly Color Kraft = new Color(0.69f, 0.553f, 0.376f, 1f);
    public static readonly Color Blue = new Color(0.180f, 0.384f, 0.941f, 1f);
    public static readonly Color Orange = new Color(1f, 0.541f, 0.165f, 1f);
    public static readonly Color Gold = new Color(1f, 0.835f, 0.29f, 1f);
    public static readonly Color Mechanism = new Color(0.710f, 0.200f, 0.541f, 1f);
    public static readonly Color Shadow = new Color(0.133f, 0.157f, 0.227f, 0.3f);
    public static readonly Vector2 ShadowOffset = new Vector2(5f, -5f);

    const string RigName = "Paper Feel";
    static GameObject rig;

    public static bool IsOn
    {
        get { return Object.FindAnyObjectByType<PaperStyle>() != null; }
    }

    public static Color ColorOf(PieceColor.PieceColorType type)
    {
        return type == PieceColor.PieceColorType.Blue ? Blue : Orange;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        rig = null;
        SceneManager.sceneLoaded -= OnLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AttachFirst()
    {
        Attach(SceneManager.GetActiveScene());
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        Attach(scene);
    }

    static void Attach(Scene scene)
    {
        if (rig != null || !scene.isLoaded || !IsOn) return;
        if (Object.FindAnyObjectByType<CutController>() == null) return;
        rig = new GameObject(RigName);
        SceneManager.MoveGameObjectToScene(rig, scene);
        rig.AddComponent<PaperBits>();
        rig.AddComponent<CutFocus>();
        rig.AddComponent<PaperTrail>();
        rig.AddComponent<CutScraps>();
        rig.AddComponent<ColorWipe>();
        rig.AddComponent<PopUpPaths>();
        rig.AddComponent<GlassRipple>();
        rig.AddComponent<MechanismThreads>();
    }

    public static Sprite PaintScrap(int seed, Color fill)
    {
        System.Random random = new System.Random(seed);
        Vector2 size = new Vector2(30f, 24f);
        Vector2[] corners =
        {
            new Vector2(Jitter(random, 3f, 7f), Jitter(random, 3f, 7f)),
            new Vector2(Jitter(random, 23f, 27f), Jitter(random, 2f, 6f)),
            new Vector2(Jitter(random, 24f, 28f), Jitter(random, 17f, 21f)),
            new Vector2(Jitter(random, 2f, 6f), Jitter(random, 18f, 22f))
        };
        Vector2[] fibres = new Vector2[14];
        for (int i = 0; i < fibres.Length; i += 2)
        {
            Vector2 start = new Vector2(Jitter(random, 4f, 26f), Jitter(random, 4f, 20f));
            float angle = Jitter(random, -0.6f, 0.6f) + (random.NextDouble() < 0.3 ? Mathf.PI * 0.5f : 0f);
            fibres[i] = start;
            fibres[i + 1] = start + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Jitter(random, 4f, 9f);
        }
        Vector2 speck = new Vector2(Jitter(random, 8f, 22f), Jitter(random, 7f, 17f));
        Color light = Color.Lerp(fill, new Color(1f, 0.97f, 0.9f, 1f), 0.55f);
        Color dark = new Color(fill.r * 0.42f, fill.g * 0.38f, fill.b * 0.36f, 1f);
        return UiShapes.Paint(size, 3f, Vector4.zero, p =>
        {
            float d = PolygonDistance(p, corners);
            if (d > 0f) return Color.clear;
            if (d > -1.6f) return Ink;
            Color c = fill;
            for (int i = 0; i < fibres.Length; i += 2)
            {
                if (SegmentDistance(p, fibres[i], fibres[i + 1]) < 0.45f) c = Color.Lerp(c, i < 10 ? light : dark, 0.6f);
            }
            if (Vector2.Distance(p, speck) < 0.8f) c = Color.Lerp(c, dark, 0.7f);
            return c;
        });
    }

    public static Sprite PaintRing()
    {
        Vector2 size = new Vector2(64f, 64f);
        Vector2 center = size * 0.5f;
        return UiShapes.Paint(size, 2f, Vector4.zero, p =>
        {
            float d = Mathf.Abs(Vector2.Distance(p, center) - 27f);
            return d < 3.6f ? Color.white : Color.clear;
        }, true);
    }

    public static Sprite PaintKnot(Color fill)
    {
        Vector2 size = new Vector2(20f, 20f);
        Vector2 center = size * 0.5f;
        return UiShapes.Paint(size, 3f, Vector4.zero, p =>
        {
            float d = Vector2.Distance(p, center);
            if (d > 8.5f) return Color.clear;
            return d > 6.3f ? Ink : fill;
        });
    }

    public static Sprite PaintSoftHole()
    {
        Vector2 size = new Vector2(128f, 128f);
        Vector2 center = size * 0.5f;
        return UiShapes.Paint(size, 1f, Vector4.zero, p =>
        {
            float r = Vector2.Distance(p, center) / 64f;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 1f, r));
            return new Color(1f, 1f, 1f, a);
        });
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    static float Jitter(System.Random random, float low, float high)
    {
        return low + (float)random.NextDouble() * (high - low);
    }

    public static float PolygonDistance(Vector2 p, Vector2[] v)
    {
        float d = (p - v[0]).sqrMagnitude;
        float s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i];
            Vector2 w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, b.sqrMagnitude);
            bool c1 = p.y >= v[i].y;
            bool c2 = p.y < v[j].y;
            bool c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }

    public static float BackOut(float t, float overshoot = 1.70158f)
    {
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }
}
