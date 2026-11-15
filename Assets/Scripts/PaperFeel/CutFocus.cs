using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(1)]
public class CutFocus : MonoBehaviour
{
    public float zoom2D = 0.82f;
    public float zoom3D = 0.86f;
    public float centerPull = 0.5f;
    public float darkness = 0.3f;
    public float focusTime = 0.22f;
    public float holePadding = 80f;
    public float minHole = 150f;

    CutController cut;
    CameraRig rig;
    Canvas canvas;
    RectTransform canvasRect;
    Image hole;
    readonly Image[] sides = new Image[4];
    float focus;
    Vector2 holeCenter;
    Vector2 holeRadius;
    bool holePlaced;

    void Start()
    {
        cut = FindFirstObjectByType<CutController>();
        rig = FindFirstObjectByType<CameraRig>();
        Build();
    }

    void LateUpdate()
    {
        if (cut == null || rig == null || rig.cam == null) return;
        float deltaTime = Time.unscaledDeltaTime;
        bool cutting = cut.state != CutController.State.Normal;
        focus = Mathf.MoveTowards(focus, cutting ? 1f : 0f, deltaTime / focusTime);
        float amount = Mathf.SmoothStep(0f, 1f, focus);
        if (amount <= 0f)
        {
            canvas.enabled = false;
            holePlaced = false;
            return;
        }

        Bounds target;
        if (!TryGetTarget(out target))
        {
            canvas.enabled = false;
            return;
        }

        Camera cam = rig.cam;
        if (cam.orthographic)
        {
            cam.orthographicSize *= Mathf.Lerp(1f, zoom2D, amount);
            Vector3 toTarget = target.center - cam.transform.position;
            Vector3 forward = cam.transform.forward;
            Vector3 across = toTarget - forward * Vector3.Dot(toTarget, forward);
            cam.transform.position += across * centerPull * amount;
        }
        else
        {
            cam.fieldOfView *= Mathf.Lerp(1f, zoom3D, amount);
        }

        canvas.enabled = true;
        canvas.worldCamera = cam;
        canvas.planeDistance = cam.nearClipPlane + 1f;
        Layout(cam, target, amount, deltaTime);
    }

    bool TryGetTarget(out Bounds target)
    {
        if (cut.TryGetChoiceBounds(out target)) return true;
        target = new Bounds();
        if (cut.player == null) return false;
        Renderer body = cut.player.GetComponent<Renderer>();
        if (body == null) return false;
        target = body.bounds;
        return true;
    }

    void Layout(Camera cam, Bounds target, float amount, float deltaTime)
    {
        Vector2 low = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 high = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? target.min.x : target.max.x,
                (i & 2) == 0 ? target.min.y : target.max.y,
                (i & 4) == 0 ? target.min.z : target.max.z);
            Vector3 screen = cam.WorldToScreenPoint(corner);
            low = Vector2.Min(low, screen);
            high = Vector2.Max(high, screen);
        }
        float scale = canvas.scaleFactor;
        Vector2 center = (low + high) * 0.5f / scale;
        Vector2 radius = (high - low) * 0.5f / scale * 1.2f + Vector2.one * holePadding;
        radius = Vector2.Max(radius, Vector2.one * minHole);
        if (!holePlaced)
        {
            holeCenter = center;
            holeRadius = radius;
            holePlaced = true;
        }
        else
        {
            float follow = 1f - Mathf.Exp(-14f * deltaTime);
            holeCenter = Vector2.Lerp(holeCenter, center, follow);
            holeRadius = Vector2.Lerp(holeRadius, radius, follow);
        }

        Vector2 size = canvasRect.rect.size;
        Color shade = new Color(PaperFeel.Ink.r, PaperFeel.Ink.g, PaperFeel.Ink.b, darkness * amount);
        hole.color = shade;
        Place(hole.rectTransform, holeCenter - holeRadius, holeRadius * 2f);

        float left = holeCenter.x - holeRadius.x;
        float right = holeCenter.x + holeRadius.x;
        float bottom = holeCenter.y - holeRadius.y;
        float top = holeCenter.y + holeRadius.y;
        Place(sides[0].rectTransform, new Vector2(0f, 0f), new Vector2(Mathf.Max(0f, left), size.y));
        Place(sides[1].rectTransform, new Vector2(right, 0f), new Vector2(Mathf.Max(0f, size.x - right), size.y));
        Place(sides[2].rectTransform, new Vector2(left, top), new Vector2(holeRadius.x * 2f, Mathf.Max(0f, size.y - top)));
        Place(sides[3].rectTransform, new Vector2(left, 0f), new Vector2(holeRadius.x * 2f, Mathf.Max(0f, bottom)));
        foreach (Image side in sides) side.color = shade;
    }

    static void Place(RectTransform rect, Vector2 corner, Vector2 size)
    {
        rect.anchoredPosition = corner;
        rect.sizeDelta = size;
    }

    void Build()
    {
        GameObject host = new GameObject("Cut Focus", typeof(RectTransform));
        host.transform.SetParent(transform, false);
        canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = rig != null ? rig.cam : Camera.main;
        canvas.sortingOrder = -100;
        canvasRect = host.GetComponent<RectTransform>();

        hole = UiShapes.NewGraphic<Image>("Hole", host.transform, Vector2.zero);
        hole.sprite = PaperFeel.PaintSoftHole();
        for (int i = 0; i < sides.Length; i++)
        {
            sides[i] = UiShapes.NewGraphic<Image>("Side", host.transform, Vector2.zero);
        }
        canvas.enabled = false;
    }
}
