using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(1000)]
public class PieceChoiceView : MonoBehaviour
{
    public Sprite clickIcon;
    public string keepText = "Click to keep";
    public string leaveText = "Stays behind";
    public Color gold = new Color(1f, 0.835f, 0.29f, 1f);
    public Color rim = new Color(0.063f, 0.082f, 0.11f, 0.9f);
    public Color dashColor = new Color(1f, 1f, 1f, 0.85f);
    public Color tagFill = new Color(0.063f, 0.082f, 0.11f, 1f);
    public Color leaveTextColor = new Color(0.84f, 0.87f, 0.92f, 1f);
    public float pieceGap = 0.4f;
    public float splitTime = 0.18f;
    public float fadeTime = 0.15f;
    public float strokeWidth = 7f;
    public float strokeBreath = 1.5f;
    public float breathTime = 1.4f;
    public float popSize = 12f;
    public float popTime = 0.22f;
    public float dashSpeed = 0.6f;
    public float dimBrightness = 0.6f;
    public float dimAlpha = 0.5f;
    public float tagGap = 28f;
    public float tagHeight = 54f;

    const float screenMargin = 16f;
    const float tagPadding = 16f;
    const float iconGap = 10f;
    const float tagCorner = 14f;
    const float tagEdge = 2.5f;
    const float keepFontSize = 26f;
    const float leaveFontSize = 22f;
    const float pressPeriod = 1.2f;

    class Tag
    {
        public RectTransform box;
        public Image fill;
        public Image edge;
        public TextMeshProUGUI label;
        public Image icon;
        public float width;
    }

    private CutController cut;
    private CameraRig rig;
    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform root;
    private CanvasGroup group;
    private PieceOutline selectedLine;
    private PieceOutline otherLine;
    private Tag keepTag;
    private Tag leaveTag;
    private Sprite tagFillSprite;
    private Sprite tagEdgeSprite;

    private bool running;
    private float clock;
    private float popClock;
    private int shownSelected = -1;
    private Material dimMaterial;
    private readonly GameObject[] pieces = new GameObject[2];
    private readonly Vector3[][] vertices = new Vector3[2][];
    private readonly Vector3[] apart = new Vector3[2];
    private readonly List<Vector2>[] hulls = { new List<Vector2>(), new List<Vector2>() };
    private readonly Rect[] areas = new Rect[2];
    private readonly List<Vector2> projected = new List<Vector2>();

    void Awake()
    {
        cut = FindFirstObjectByType<CutController>();
        rig = FindFirstObjectByType<CameraRig>();
        Build();
        root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (dimMaterial != null) Destroy(dimMaterial);
    }

    void LateUpdate()
    {
        GameObject first = cut != null ? cut.GetChoicePiece(0) : null;
        GameObject second = cut != null ? cut.GetChoicePiece(1) : null;
        if (first == null || second == null || rig == null || rig.cam == null)
        {
            Stop();
            return;
        }
        if (!running)
        {
            Begin(first, second);
        }

        float deltaTime = Time.unscaledDeltaTime;
        clock += deltaTime;
        Split();

        int selected = cut.HoveredPiece;
        if (selected != 0 && selected != 1) return;
        if (selected != shownSelected)
        {
            shownSelected = selected;
            popClock = 0f;
        }
        popClock += deltaTime;

        Dim(1 - selected);
        Measure(0);
        Measure(1);
        DrawLines(selected);
        PlaceTags(selected);
        group.alpha = Mathf.Clamp01(clock / fadeTime);
    }

    void Begin(GameObject first, GameObject second)
    {
        running = true;
        clock = 0f;
        popClock = popTime;
        shownSelected = -1;
        pieces[0] = first;
        pieces[1] = second;
        vertices[0] = first.GetComponent<MeshFilter>().sharedMesh.vertices;
        vertices[1] = second.GetComponent<MeshFilter>().sharedMesh.vertices;

        Vector3 slide = cut.CutNormal * pieceGap * 0.5f;
        Vector3 lift = Vector3.up * Mathf.Abs(slide.y);
        apart[0] = slide + lift;
        apart[1] = -slide + lift;

        int other = 1 - cut.HoveredPiece;
        Material clear = pieces[Mathf.Clamp(other, 0, 1)].GetComponent<MeshRenderer>().sharedMaterial;
        if (dimMaterial != null) Destroy(dimMaterial);
        dimMaterial = new Material(clear);
        Color tint = clear.color;
        dimMaterial.color = new Color(tint.r * dimBrightness, tint.g * dimBrightness, tint.b * dimBrightness, dimAlpha);

        group.alpha = 0f;
        root.gameObject.SetActive(true);
    }

    void Stop()
    {
        if (!running) return;
        running = false;
        pieces[0] = null;
        pieces[1] = null;
        root.gameObject.SetActive(false);
    }

    void Split()
    {
        float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / splitTime));
        pieces[0].transform.position = apart[0] * open;
        pieces[1].transform.position = apart[1] * open;
    }

    void Dim(int other)
    {
        pieces[other].GetComponent<MeshRenderer>().sharedMaterial = dimMaterial;
    }

    void Measure(int index)
    {
        projected.Clear();
        Transform piece = pieces[index].transform;
        Camera cam = rig.cam;
        float scale = canvas.scaleFactor;
        Vector2 low = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 high = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 vertex in vertices[index])
        {
            Vector3 screen = cam.WorldToScreenPoint(piece.TransformPoint(vertex));
            Vector2 point = new Vector2(screen.x, screen.y) / scale;
            projected.Add(point);
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }
        areas[index] = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        PieceOutline.ConvexHull(projected, hulls[index]);
    }

    void DrawLines(int selected)
    {
        float pop = (1f - Mathf.Clamp01(popClock / popTime)) * popSize;
        float breath = (Mathf.Sin(clock * Mathf.PI * 2f / breathTime) + 1f) * 0.5f * strokeBreath;
        float inner = 3f + pop;
        float outer = inner + strokeWidth + breath;
        Color clearRim = new Color(rim.r, rim.g, rim.b, 0f);
        Color glow = new Color(gold.r, gold.g, gold.b, 0.3f);
        Color noGlow = new Color(gold.r, gold.g, gold.b, 0f);
        PieceOutline.Ring[] selectedRings =
        {
            new PieceOutline.Ring(inner - 2.5f, clearRim),
            new PieceOutline.Ring(inner - 1f, rim),
            new PieceOutline.Ring(inner, gold),
            new PieceOutline.Ring(outer, gold),
            new PieceOutline.Ring(outer + 1f, rim),
            new PieceOutline.Ring(outer + 3f, rim),
            new PieceOutline.Ring(outer + 4f, glow),
            new PieceOutline.Ring(outer + 22f, noGlow)
        };
        selectedLine.SetShape(hulls[selected], selectedRings);

        Color clearDash = new Color(dashColor.r, dashColor.g, dashColor.b, 0f);
        PieceOutline.Ring[] otherRings =
        {
            new PieceOutline.Ring(1.5f, clearDash),
            new PieceOutline.Ring(2.5f, dashColor),
            new PieceOutline.Ring(5.5f, dashColor),
            new PieceOutline.Ring(6.5f, clearDash)
        };
        otherLine.dashOffset = clock * dashSpeed;
        otherLine.SetShape(hulls[1 - selected], otherRings);
    }

    void PlaceTags(int selected)
    {
        Vector2 split = areas[0].center - areas[1].center;
        bool sideBySide = Mathf.Abs(split.x) >= Mathf.Abs(split.y);
        float firstSide = (sideBySide ? split.x : split.y) >= 0f ? 1f : -1f;
        float selectedSide = selected == 0 ? firstSide : -firstSide;
        Place(keepTag, areas[selected], selectedSide);
        Place(leaveTag, areas[1 - selected], -selectedSide);

        float grow = popClock < popTime ? Mathf.LerpUnclamped(0.75f, 1f, UiShapes.BackOut(popClock / popTime)) : 1f;
        keepTag.box.localScale = new Vector3(grow, grow, 1f);

        float time = Mathf.Repeat(clock, pressPeriod);
        float press = time < 0.12f ? time / 0.12f : Mathf.Clamp01(1f - (time - 0.12f) / 0.22f);
        keepTag.icon.rectTransform.localScale = new Vector3(1f + 0.05f * press, 1f - 0.12f * press, 1f);
    }

    void Place(Tag tag, Rect area, float side)
    {
        Vector2 size = canvasRect.rect.size;
        bool right = side > 0f;
        tag.box.pivot = new Vector2(right ? 0f : 1f, 0.5f);
        float x = right ? area.xMax + tagGap : area.xMin - tagGap;
        float lowest = right ? screenMargin : screenMargin + tag.width;
        float highest = right ? size.x - screenMargin - tag.width : size.x - screenMargin;
        x = Mathf.Clamp(x, lowest, highest);
        float y = Mathf.Clamp(area.center.y, screenMargin + tagHeight * 0.5f, size.y - screenMargin - tagHeight * 0.5f);
        tag.box.anchoredPosition = new Vector2(x, y);
    }

    void Build()
    {
        canvas = UiShapes.MakeOverlay(gameObject, 3);
        canvasRect = GetComponent<RectTransform>();
        tagFillSprite = UiShapes.RoundBox(tagCorner, 0f);
        tagEdgeSprite = UiShapes.RoundBox(tagCorner, tagEdge);

        root = UiShapes.NewRect("Piece Choice", transform, Vector2.zero);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        otherLine = UiShapes.NewGraphic<PieceOutline>("Other Piece", root, Vector2.zero);
        otherLine.dashed = true;
        selectedLine = UiShapes.NewGraphic<PieceOutline>("Selected Piece", root, Vector2.zero);

        leaveTag = MakeTag("Leave Tag", leaveText, leaveFontSize, FontStyles.Normal, leaveTextColor, false, 0.92f);
        keepTag = MakeTag("Keep Tag", keepText, keepFontSize, FontStyles.Bold, Color.white, true, 1f);
    }

    Tag MakeTag(string name, string text, float fontSize, FontStyles style, Color textColor, bool withIcon, float opacity)
    {
        Tag tag = new Tag();
        tag.box = UiShapes.NewRect(name, root, new Vector2(0f, 0.5f));

        tag.fill = UiShapes.NewGraphic<Image>("Fill", tag.box, new Vector2(0.5f, 0.5f));
        tag.fill.sprite = tagFillSprite;
        tag.fill.type = Image.Type.Sliced;
        tag.fill.color = new Color(tagFill.r, tagFill.g, tagFill.b, opacity);
        Stretch(tag.fill.rectTransform);

        tag.edge = UiShapes.NewGraphic<Image>("Edge", tag.box, new Vector2(0.5f, 0.5f));
        tag.edge.sprite = tagEdgeSprite;
        tag.edge.type = Image.Type.Sliced;
        tag.edge.color = withIcon ? gold : new Color(leaveTextColor.r, leaveTextColor.g, leaveTextColor.b, 0.5f);
        Stretch(tag.edge.rectTransform);

        float x = tagPadding;
        float height = withIcon ? tagHeight : tagHeight - 10f;
        if (withIcon && clickIcon != null)
        {
            float iconHeight = height - 14f;
            float iconWidth = iconHeight * clickIcon.rect.width / Mathf.Max(1f, clickIcon.rect.height);
            tag.icon = UiShapes.NewGraphic<Image>("Icon", tag.box, new Vector2(0.5f, 0.5f));
            tag.icon.sprite = clickIcon;
            tag.icon.preserveAspect = true;
            tag.icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            tag.icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            tag.icon.rectTransform.anchoredPosition = new Vector2(x + iconWidth * 0.5f, 0f);
            tag.icon.rectTransform.sizeDelta = new Vector2(iconWidth, iconHeight);
            x += iconWidth + iconGap;
        }

        tag.label = UiShapes.NewGraphic<TextMeshProUGUI>("Label", tag.box, new Vector2(0f, 0.5f));
        tag.label.text = text;
        tag.label.fontSize = fontSize;
        tag.label.fontStyle = style;
        tag.label.color = textColor;
        tag.label.alignment = TextAlignmentOptions.MidlineLeft;
        tag.label.textWrappingMode = TextWrappingModes.NoWrap;
        float labelWidth = Mathf.Ceil(tag.label.GetPreferredValues(text).x) + 2f;
        tag.label.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        tag.label.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        tag.label.rectTransform.anchoredPosition = new Vector2(x, 0f);
        tag.label.rectTransform.sizeDelta = new Vector2(labelWidth, height);

        tag.width = x + labelWidth + tagPadding;
        tag.box.sizeDelta = new Vector2(tag.width, height);
        return tag;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
