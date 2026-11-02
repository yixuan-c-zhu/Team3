using UnityEngine;
using UnityEngine.UI;

public class CutHintView : MonoBehaviour
{
    public Color fallbackLineColor = new Color(0.878f, 0.463f, 0.227f);
    public Color cursorColor = new Color(1f, 0.835f, 0.29f);
    public Color outlineColor = new Color(0.13f, 0.13f, 0.13f);
    public Color mouseColor = new Color(0.96f, 0.96f, 0.96f);
    public float lineWidth = 6f;
    public float dashLength = 16f;
    public float dashGap = 10f;
    public float dashSpeed = 30f;
    public float overhang = 0.6f;
    public float cursorHeight = 46f;
    public float pressScale = 0.88f;
    public float appearTime = 0.25f;
    public float pressTime = 0.25f;
    public float dragTime = 1f;
    public float releaseTime = 0.2f;
    public float restTime = 0.6f;

    const float facingLimit = 0.1f;
    const float outline = 1.2f;
    const float pad = 1.7f;
    const float texelsPerUnit = 5f;
    const float clickTime = 0.08f;

    static readonly Vector2[] arrowShape =
    {
        new Vector2(0f, 0f), new Vector2(0f, 16f), new Vector2(3.9f, 12.3f), new Vector2(6.4f, 18.2f),
        new Vector2(8.8f, 17.2f), new Vector2(6.4f, 11.4f), new Vector2(11.4f, 11.4f)
    };

    static CutHintView instance;

    private CutController cutController;
    private CameraRig camRig;
    private Canvas canvas;
    private Color lineColor;
    private RawImage dashes;
    private Image trail;
    private Image dot;
    private RectTransform cursor;
    private CanvasGroup cursorGroup;
    private Image leftButton;
    private float clock;
    private float dashOffset;

    public static void Ensure()
    {
        if (instance != null) return;
        instance = FindFirstObjectByType<CutHintView>();
        if (instance == null)
        {
            instance = new GameObject("CutHintView").AddComponent<CutHintView>();
        }
    }

    void Awake()
    {
        instance = this;
        cutController = FindFirstObjectByType<CutController>();
        camRig = FindFirstObjectByType<CameraRig>();
        lineColor = fallbackLineColor;
        if (cutController != null && cutController.lineRenderer != null && cutController.lineRenderer.sharedMaterial != null)
        {
            lineColor = cutController.lineRenderer.sharedMaterial.color;
        }
        lineColor.a = 1f;
        Build();
        Hide();
    }

    void LateUpdate()
    {
        Vector2 start;
        Vector2 end;
        if (!TryGetLine(out start, out end))
        {
            Hide();
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        float period = dashes.texture.width;
        dashOffset = (dashOffset + dashSpeed * deltaTime) % period;
        dashes.enabled = true;
        PlaceSegment(dashes.rectTransform, start, end);
        dashes.uvRect = new Rect(-dashOffset / period, 0f, Vector2.Distance(start, end) / period, 1f);

        if (Input.GetMouseButton(0))
        {
            clock = 0f;
            ShowDemo(false);
            return;
        }
        clock += deltaTime;
        Animate(start, end);
    }

    bool TryGetLine(out Vector2 start, out Vector2 end)
    {
        start = Vector2.zero;
        end = Vector2.zero;
        if (cutController == null || camRig == null || cutController.player == null) return false;
        if (cutController.state != CutController.State.Drawing || !camRig.is2D || camRig.IsTurning()) return false;

        Camera cam = camRig.cam;
        Vector3 forward = cam.transform.forward;
        Bounds body = cutController.player.GetComponent<Collider>().bounds;
        foreach (CutHint hint in CutHint.all)
        {
            Vector3 normal;
            Vector3 point;
            if (!hint.TryGetCut(body, out normal, out point)) continue;
            if (Mathf.Abs(Vector3.Dot(normal, forward)) > facingLimit) continue;

            Vector3 along = Vector3.Cross(normal, forward).normalized;
            Vector3 center = body.center + normal * Vector3.Dot(point - body.center, normal);
            float half = CutHint.Extent(body, along) + overhang;
            Vector2 a = ToCanvas(cam.WorldToScreenPoint(center - along * half));
            Vector2 b = ToCanvas(cam.WorldToScreenPoint(center + along * half));
            bool flat = Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.y - a.y);
            bool swap = flat ? a.x > b.x : a.y < b.y;
            start = swap ? b : a;
            end = swap ? a : b;
            return true;
        }
        return false;
    }

    void Animate(Vector2 start, Vector2 end)
    {
        float pressAt = appearTime;
        float dragAt = pressAt + pressTime;
        float releaseAt = dragAt + dragTime;
        float restAt = releaseAt + releaseTime;
        float t = clock % (restAt + restTime);

        float drag = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - dragAt) / dragTime));
        Vector2 tip = Vector2.Lerp(start, end, drag);
        bool pressed = t >= pressAt && t < releaseAt;
        float held = Mathf.Clamp01((t - pressAt) / clickTime) - Mathf.Clamp01((t - releaseAt) / clickTime);
        float fade = 1f - Mathf.Clamp01((t - restAt) / restTime);

        ShowDemo(true);
        cursor.anchoredPosition = tip;
        cursor.localScale = Vector3.one * Mathf.Lerp(1f, pressScale, held);
        cursorGroup.alpha = Mathf.Min(Mathf.Clamp01(t / appearTime), fade);
        leftButton.enabled = pressed;

        dot.enabled = t >= pressAt;
        dot.rectTransform.anchoredPosition = start;
        dot.rectTransform.localScale = Vector3.one * Mathf.Clamp01((t - pressAt) / (clickTime * 1.5f));
        dot.color = new Color(1f, 1f, 1f, fade);

        trail.enabled = t >= dragAt;
        PlaceSegment(trail.rectTransform, start, tip);
        trail.color = new Color(lineColor.r, lineColor.g, lineColor.b, fade);
    }

    void Hide()
    {
        clock = 0f;
        dashes.enabled = false;
        ShowDemo(false);
    }

    void ShowDemo(bool show)
    {
        trail.enabled = show;
        dot.enabled = show;
        cursor.gameObject.SetActive(show);
    }

    void PlaceSegment(RectTransform rect, Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        rect.anchoredPosition = from;
        rect.sizeDelta = new Vector2(d.magnitude, lineWidth);
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    Vector2 ToCanvas(Vector3 screen)
    {
        return new Vector2(screen.x, screen.y) / canvas.scaleFactor;
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        dashes = MakeGraphic<RawImage>("Dashes", transform, new Vector2(0f, 0.5f));
        dashes.texture = MakeDashTexture();
        dashes.color = lineColor;

        trail = MakeGraphic<Image>("Trail", transform, new Vector2(0f, 0.5f));

        dot = MakeGraphic<Image>("Dot", transform, new Vector2(0.5f, 0.5f));
        dot.sprite = MakeDotSprite();
        dot.rectTransform.sizeDelta = Vector2.one * lineWidth * 3f;

        cursor = new GameObject("Cursor", typeof(RectTransform)).GetComponent<RectTransform>();
        cursor.SetParent(transform, false);
        cursor.anchorMin = Vector2.zero;
        cursor.anchorMax = Vector2.zero;
        cursor.sizeDelta = Vector2.zero;
        cursorGroup = cursor.gameObject.AddComponent<CanvasGroup>();
        cursorGroup.blocksRaycasts = false;
        cursorGroup.interactable = false;

        Vector2 arrowSize = new Vector2(11.4f + pad * 2f, 18.2f + pad * 2f);
        float unit = cursorHeight / arrowSize.y;
        Image arrow = MakeGraphic<Image>("Arrow", cursor, new Vector2(pad / arrowSize.x, 1f - pad / arrowSize.y));
        arrow.sprite = MakeArrowSprite(arrowSize);
        arrow.rectTransform.sizeDelta = arrowSize * unit;

        Vector2 mouseSize = new Vector2(10f + pad * 2f, 15f + pad * 2f);
        Image mouse = MakeGraphic<Image>("Mouse", cursor, new Vector2(0f, 1f));
        mouse.sprite = MakeMouseSprite(mouseSize, false);
        mouse.rectTransform.sizeDelta = mouseSize * unit * 0.85f;
        mouse.rectTransform.anchoredPosition = new Vector2(12f, -9f) * unit;

        leftButton = MakeGraphic<Image>("Left Button", mouse.transform, new Vector2(0.5f, 0.5f));
        leftButton.sprite = MakeMouseSprite(mouseSize, true);
        leftButton.rectTransform.anchorMax = Vector2.one;
        leftButton.rectTransform.sizeDelta = Vector2.zero;
    }

    static T MakeGraphic<T>(string name, Transform parent, Vector2 pivot) where T : Graphic
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

    Texture2D MakeDashTexture()
    {
        int width = Mathf.RoundToInt(dashLength + dashGap);
        Texture2D texture = new Texture2D(width, 1, TextureFormat.RGBA32, false);
        texture.wrapModeU = TextureWrapMode.Repeat;
        texture.wrapModeV = TextureWrapMode.Clamp;
        for (int x = 0; x < width; x++)
        {
            texture.SetPixel(x, 0, x < dashLength ? Color.white : Color.clear);
        }
        texture.Apply();
        return texture;
    }

    Sprite MakeArrowSprite(Vector2 size)
    {
        Vector2 offset = new Vector2(pad, pad);
        return Paint(size, p =>
        {
            float d = PolygonDistance(p - offset, arrowShape);
            if (d < 0f) return cursorColor;
            if (d < outline) return outlineColor;
            return Color.clear;
        });
    }

    Sprite MakeMouseSprite(Vector2 size, bool button)
    {
        Vector2 center = size / 2f;
        float split = pad + 6f;
        return Paint(size, p =>
        {
            float d = RoundBoxDistance(p - center, new Vector2(5f, 7.5f), 4.5f);
            bool line = (Mathf.Abs(p.x - center.x) < 0.45f && p.y < split) || Mathf.Abs(p.y - split) < 0.45f;
            if (button)
            {
                return d < 0f && !line && p.x < center.x && p.y < split ? lineColor : Color.clear;
            }
            if (d < 0f) return line ? outlineColor : mouseColor;
            if (d < outline) return outlineColor;
            return Color.clear;
        });
    }

    Sprite MakeDotSprite()
    {
        float radius = 4f;
        Vector2 size = Vector2.one * (radius + outline + 0.5f) * 2f;
        Vector2 center = size / 2f;
        return Paint(size, p =>
        {
            float d = Vector2.Distance(p, center) - radius;
            if (d < 0f) return lineColor;
            if (d < outline) return Color.white;
            return Color.clear;
        });
    }

    static Sprite Paint(Vector2 size, System.Func<Vector2, Color> shade)
    {
        int width = Mathf.CeilToInt(size.x * texelsPerUnit);
        int height = Mathf.CeilToInt(size.y * texelsPerUnit);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        Vector2 p = new Vector2(x + (i + 0.5f) / 4f, height - y - (j + 0.5f) / 4f) / texelsPerUnit;
                        Color c = shade(p);
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                }
                sum /= 16f;
                pixels[y * width + x] = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), texelsPerUnit);
    }

    static float PolygonDistance(Vector2 p, Vector2[] v)
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

    static float RoundBoxDistance(Vector2 p, Vector2 half, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * radius;
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }
}
