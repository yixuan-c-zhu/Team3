using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(1000)]
public class CutCountView : MonoBehaviour
{
    public Color readyColor = new Color(1f, 0.835f, 0.29f, 1f);
    public Color usedColor = new Color(0.56f, 0.58f, 0.61f, 1f);
    public Color panelColor = new Color(0.063f, 0.082f, 0.11f, 0.85f);
    public Color emptyColor = new Color(0.86f, 0.22f, 0.2f, 0.9f);
    public float iconSize = 40f;
    public float iconGap = 14f;
    public float sidePadding = 20f;
    public float panelHeight = 54f;
    public float gapAbovePlayer = 12f;
    public float gapWhileChoosing = 30f;
    public float gapBelowBubble = 6f;
    public float liftWhileCutting = 1f;
    public float followSpeed = 20f;
    public float fadeTime = 0.12f;
    public float snipTime = 0.38f;
    public float shakeTime = 0.4f;
    public float shakeSize = 9f;

    const float paintSize = 40f;
    const float paintDensity = 3f;
    const float snipGrow = 0.4f;
    const float snipTurn = 16f;

    static readonly Vector2 pivot = new Vector2(20f, 21f);
    static readonly Vector2 tipRight = new Vector2(30f, 39f);
    static readonly Vector2 ringLeft = new Vector2(10.5f, 8.5f);

    static CutCountView instance;

    class Slot
    {
        public RectTransform rect;
        public Image image;
        public bool used;
        public float snip = 1f;
    }

    private CutController cut;
    private CameraRig rig;
    private LevelManager level;
    private Canvas canvas;
    private RectTransform panel;
    private CanvasGroup group;
    private Image panelImage;
    private Sprite scissors;
    private readonly List<Slot> slots = new List<Slot>();

    private Vector2 spot;
    private Vector2 wanted;
    private bool placed;
    private bool showing;
    private float alpha;
    private float shake = 1f;
    private int shownMax = -1;

    public static float ClearHeight
    {
        get
        {
            if (instance == null || !instance.showing) return float.MinValue;
            return instance.wanted.y + instance.panelHeight + instance.gapBelowBubble;
        }
    }

    void Awake()
    {
        instance = this;
        cut = FindFirstObjectByType<CutController>();
        rig = FindFirstObjectByType<CameraRig>();
        level = FindFirstObjectByType<LevelManager>();
        Build();
        group.alpha = 0f;
    }

    void OnEnable()
    {
        if (cut != null) cut.CutInput += OnCutInput;
    }

    void OnDisable()
    {
        if (cut != null) cut.CutInput -= OnCutInput;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void OnCutInput()
    {
        if (cut.state == CutController.State.Normal && level != null && !level.CanCut())
        {
            shake = 0f;
        }
    }

    void LateUpdate()
    {
        float deltaTime = Time.unscaledDeltaTime;
        showing = ShouldShow() && TryGetSpot(out wanted);
        alpha = Mathf.MoveTowards(alpha, showing ? 1f : 0f, deltaTime / fadeTime);
        group.alpha = alpha;
        if (!showing)
        {
            placed = false;
            return;
        }

        if (level.maxCuts != shownMax)
        {
            Rebuild();
        }
        UpdateSlots(deltaTime);

        spot = placed ? Vector2.Lerp(spot, wanted, 1f - Mathf.Exp(-followSpeed * deltaTime)) : wanted;
        placed = true;

        shake = Mathf.Min(1f, shake + deltaTime / shakeTime);
        float wobble = shake < 1f ? Mathf.Sin(shake * Mathf.PI * 7f) * (1f - shake) * shakeSize : 0f;
        panel.anchoredPosition = spot + Vector2.right * wobble;
        panelImage.color = Color.Lerp(emptyColor, panelColor, shake);
    }

    bool ShouldShow()
    {
        if (cut == null || rig == null || rig.cam == null || level == null || cut.player == null) return false;
        if (level.maxCuts <= 0) return false;
        if (cut.state != CutController.State.Normal) return true;
        return Time.timeScale > 0f && rig.canControl;
    }

    bool TryGetSpot(out Vector2 result)
    {
        result = Vector2.zero;
        Bounds bounds;
        if (!cut.TryGetChoiceBounds(out bounds))
        {
            Collider body = cut.player.GetComponent<Collider>();
            if (body == null) return false;
            bounds = body.bounds;
        }

        Vector3 lift = cut.state == CutController.State.Drawing ? Vector3.up * liftWhileCutting : Vector3.zero;
        Camera cam = rig.cam;
        Vector3 center = cam.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + lift);
        if (center.z <= 0f) return false;

        float top = center.y;
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, max.y, (i & 2) == 0 ? min.z : max.z);
            top = Mathf.Max(top, cam.WorldToScreenPoint(corner + lift).y);
        }

        float scale = canvas.scaleFactor;
        float gap = cut.state == CutController.State.Choosing ? gapWhileChoosing : gapAbovePlayer;
        result = new Vector2(center.x / scale, top / scale + gap);
        return true;
    }

    void Rebuild()
    {
        shownMax = level.maxCuts;
        while (slots.Count < shownMax)
        {
            Slot slot = new Slot();
            slot.image = UiShapes.NewGraphic<Image>("Scissors " + (slots.Count + 1), panel, new Vector2(0.5f, 0.5f));
            slot.image.sprite = scissors;
            slot.rect = slot.image.rectTransform;
            slot.rect.sizeDelta = Vector2.one * iconSize;
            slot.rect.anchorMin = new Vector2(0f, 0.5f);
            slot.rect.anchorMax = new Vector2(0f, 0.5f);
            slots.Add(slot);
        }

        int used = shownMax - level.cutsLeft;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            bool active = i < shownMax;
            slot.image.gameObject.SetActive(active);
            slot.rect.anchoredPosition = new Vector2(sidePadding + iconSize * 0.5f + i * (iconSize + iconGap), 0f);
            slot.used = i < used;
            slot.snip = 1f;
        }
        float width = sidePadding * 2f + shownMax * iconSize + (shownMax - 1) * iconGap;
        panel.sizeDelta = new Vector2(width, panelHeight);
    }

    void UpdateSlots(float deltaTime)
    {
        int used = level.maxCuts - level.cutsLeft;
        for (int i = 0; i < shownMax; i++)
        {
            Slot slot = slots[i];
            bool nowUsed = i < used;
            if (nowUsed != slot.used)
            {
                slot.used = nowUsed;
                slot.snip = 0f;
            }

            slot.snip = Mathf.Min(1f, slot.snip + deltaTime / snipTime);
            float bump = Mathf.Sin(slot.snip * Mathf.PI);
            Color from = slot.used ? readyColor : usedColor;
            Color to = slot.used ? usedColor : readyColor;
            slot.image.color = Color.Lerp(from, to, slot.snip);
            slot.rect.localScale = Vector3.one * (1f + bump * snipGrow);
            slot.rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(slot.snip * Mathf.PI * 3f) * bump * snipTurn);
        }
    }

    void Build()
    {
        canvas = UiShapes.MakeOverlay(gameObject, 3);
        scissors = MakeScissors();

        panelImage = UiShapes.NewGraphic<Image>("Cuts Left", transform, new Vector2(0.5f, 0f));
        panelImage.sprite = UiShapes.RoundBox(panelHeight * 0.5f, 0f);
        panelImage.type = Image.Type.Sliced;
        panelImage.color = panelColor;
        panel = panelImage.rectTransform;
        group = panel.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    static Sprite MakeScissors()
    {
        Vector2 size = Vector2.one * paintSize;
        return UiShapes.Paint(size, paintDensity, Vector4.zero, p =>
        {
            Vector2 mirrored = new Vector2(paintSize - p.x, p.y);
            if (Vector2.Distance(p, pivot) < 1f) return Color.clear;
            float d = Mathf.Min(HalfDistance(p), HalfDistance(mirrored));
            return d < 0f ? Color.white : Color.clear;
        });
    }

    static float HalfDistance(Vector2 p)
    {
        Vector2 toPivot = (pivot - ringLeft).normalized;
        float blade = UiShapes.TaperDistance(p, pivot - toPivot * 2f, tipRight, 2.3f, 0.25f);
        float arm = UiShapes.TaperDistance(p, ringLeft + toPivot * 5f, pivot, 1.5f, 2.1f);
        float ring = Mathf.Abs(Vector2.Distance(p, ringLeft) - 5.6f) - 1.5f;
        return Mathf.Min(blade, Mathf.Min(arm, ring));
    }
}
