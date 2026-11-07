using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the speech bubble for the HintBubble that matters most right now: icons and text in a dark bubble with a
/// gold edge, with a tail pointing at the player. It pops in, follows the player and fades out when the hint is done.
/// Bubbles hide while a menu pauses the game and while the player picks a piece after a cut. While the player draws
/// a cut, only bubbles marked Show While Cutting stay, raised clear of the cut indicator so it stands out.
/// It builds its own overlay canvas, just under the cut indicator's. To change the look, add a HintBubbleView to the
/// scene and edit its fields.
/// </summary>
[DefaultExecutionOrder(1000)]
public class HintBubbleView : MonoBehaviour
{
    [Header("Look")]
    public Color fill = new Color(0.063f, 0.082f, 0.11f, 1f);
    public Color edge = new Color(1f, 0.835f, 0.29f, 1f);
    public Color headlineColor = Color.white;
    public Color messageColor = new Color(0.84f, 0.87f, 0.92f, 1f);
    [Tooltip("Color of highlighted [keys] and the step numbers.")]
    public Color accent = new Color(1f, 0.835f, 0.29f, 1f);
    public Color shadowColor = new Color(0f, 0f, 0f, 0.35f);
    public float headlineSize = 30f;
    public float messageSize = 25f;
    [Tooltip("The longest a line of text gets before it wraps, in pixels at 1080p. Lines are then evened out, so no word sits alone on the last line.")]
    public float maxTextWidth = 520f;
    public float iconHeight = 64f;
    [Tooltip("Space between the top of the player and the tip of the tail, in pixels at 1080p.")]
    public float gap = 14f;
    [Tooltip("While the player draws a cut, the bubble rises this many world units, so it stays clear of the cut indicator's line and cursor.")]
    public float liftWhileCutting = 1f;

    [Header("Motion")]
    public float popTime = 0.32f;
    public float hideTime = 0.15f;
    [Tooltip("How quickly the bubble catches up with the player.")]
    public float followSpeed = 20f;

    const float padX = 22f;
    const float padY = 16f;
    const float iconGap = 14f;
    const float iconTextGap = 18f;
    const float corner = 16f;
    const float edgeWidth = 2.5f;
    const float tailWidth = 30f;
    const float tailHeight = 15f;
    const float shadowBlur = 14f;
    const float shadowDrop = 5f;
    const float badgeSize = 26f;
    const float lineGap = 4f;
    const float margin = 16f;
    const float topMargin = 130f;
    const float pointerGap = 8f;
    const float density = 2f;
    const int superSample = 4;

    static readonly Regex keyPattern = new Regex(@"\[([^\]]+)\]");
    static readonly Vector2 arrowSize = new Vector2(34f, 42f);
    static readonly Vector2[] arrowShape =
    {
        new Vector2(17f, 1.5f), new Vector2(32.5f, 18f), new Vector2(23f, 18f), new Vector2(23f, 40.5f),
        new Vector2(11f, 40.5f), new Vector2(11f, 18f), new Vector2(1.5f, 18f)
    };

    static HintBubbleView instance;

    class Slot
    {
        public RectTransform box;
        public Image image;
        public Image badge;
        public TextMeshProUGUI number;
    }

    private CutController cutController;
    private CameraRig camRig;
    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform bubble;
    private CanvasGroup group;
    private RectTransform body;
    private Image shadow;
    private Image tailFill;
    private Image tailEdge;
    private TextMeshProUGUI headlineText;
    private TextMeshProUGUI messageText;
    private Image pointer;
    private Sprite badgeSprite;
    private readonly List<Slot> slots = new List<Slot>();
    private readonly List<HintBubble.Icon> shownIcons = new List<HintBubble.Icon>();

    private HintBubble shown;
    private string shownText;
    private Vector2 bodySize;
    private float alpha;
    private float popClock;
    private float clock;
    private Vector2 tip;
    private bool placed;

    public static void Ensure()
    {
        if (instance != null) return;
        instance = FindFirstObjectByType<HintBubbleView>();
        if (instance == null)
        {
            instance = new GameObject("HintBubbleView").AddComponent<HintBubbleView>();
        }
    }

    void Awake()
    {
        instance = this;
        cutController = FindFirstObjectByType<CutController>();
        camRig = FindFirstObjectByType<CameraRig>();
        Build();
        HideAll();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void LateUpdate()
    {
        float deltaTime = Time.unscaledDeltaTime;
        clock += deltaTime;
        if (shown == null) shown = null; // forget a bubble that was destroyed
        HintBubble want = Pick();

        if (want != shown)
        {
            // Fade the old bubble out before the new one pops in.
            alpha = Mathf.MoveTowards(alpha, 0f, deltaTime / Mathf.Max(0.01f, hideTime));
            if (alpha <= 0f)
            {
                shown = want;
                popClock = 0f;
                placed = false;
                if (shown != null) Fill(shown);
            }
        }
        else if (shown != null)
        {
            popClock += deltaTime;
            alpha = Mathf.Clamp01(popClock / Mathf.Max(0.01f, popTime * 0.4f));
            if (Compose(shown) != shownText) Fill(shown);
            shown.WhileShown(deltaTime);
        }

        if (shown == null || !Place(deltaTime))
        {
            HideAll();
            return;
        }

        float grow = want == shown
            ? Mathf.LerpUnclamped(0.6f, 1f, BackOut(Mathf.Clamp01(popClock / Mathf.Max(0.01f, popTime))))
            : Mathf.Lerp(0.9f, 1f, alpha);
        bubble.localScale = new Vector3(grow, grow, 1f);
        group.alpha = alpha;
        AnimateIcons();
        UpdatePointer();
    }

    HintBubble Pick()
    {
        if (cutController == null || camRig == null || camRig.cam == null || cutController.player == null) return null;
        HintBubble best = null;
        foreach (HintBubble hint in HintBubble.all)
        {
            if (!hint.IsReady || !Allowed(hint)) continue;
            if (best == null || hint.priority > best.priority || (hint.priority == best.priority && hint == shown))
            {
                best = hint;
            }
        }
        return best;
    }

    bool Allowed(HintBubble hint)
    {
        if (cutController.state == CutController.State.Choosing) return hint.WhileChoosing;
        if (cutController.state == CutController.State.Drawing) return hint.showWhileCutting;
        if (Time.timeScale == 0f) return false;
        if (!camRig.canControl) return hint.InAimView;
        return true;
    }

    // Lays out the icons and text for a bubble and sizes it to fit.
    void Fill(HintBubble hint)
    {
        shownText = Compose(hint);

        shownIcons.Clear();
        if (hint.icons != null)
        {
            foreach (HintBubble.Icon icon in hint.icons)
            {
                if (icon != null && icon.sprite != null) shownIcons.Add(icon);
            }
        }
        float iconsWidth = 0f;
        for (int i = 0; i < shownIcons.Count; i++)
        {
            iconsWidth += IconWidth(shownIcons[i]) + (i > 0 ? iconGap : 0f);
        }

        // The headline and the message wrap separately, each evened out within its own width.
        headlineText.fontSize = headlineSize;
        headlineText.color = headlineColor;
        messageText.fontSize = messageSize;
        messageText.color = messageColor;
        string head = HeadlinePart(hint);
        string more = MessagePart(hint);
        float headWidth = BalancedWidth(headlineText, head);
        float moreWidth = BalancedWidth(messageText, more);
        float headHeight = Measure(headlineText, head, headWidth);
        float moreHeight = Measure(messageText, more, moreWidth);
        float spacing = headHeight > 0f && moreHeight > 0f ? lineGap : 0f;
        float textWidth = Mathf.Max(headWidth, moreWidth);
        float textHeight = headHeight + spacing + moreHeight;

        float innerHeight = Mathf.Max(shownIcons.Count > 0 ? iconHeight : 0f, textHeight);
        float between = shownIcons.Count > 0 && textWidth > 0f ? iconTextGap : 0f;
        bodySize = new Vector2(padX * 2f + iconsWidth + between + textWidth, padY * 2f + innerHeight);
        body.sizeDelta = bodySize;
        shadow.rectTransform.sizeDelta = bodySize + Vector2.one * shadowBlur * 2f;

        bool numbered = hint.numberIcons && shownIcons.Count > 1;
        float x = padX;
        for (int i = 0; i < shownIcons.Count; i++)
        {
            Slot slot = GetSlot(i);
            float width = IconWidth(shownIcons[i]);
            slot.box.gameObject.SetActive(true);
            slot.box.anchoredPosition = new Vector2(x, padY + (innerHeight - iconHeight) * 0.5f);
            slot.box.sizeDelta = new Vector2(width, iconHeight);
            slot.image.sprite = shownIcons[i].sprite;
            slot.image.rectTransform.sizeDelta = new Vector2(width, iconHeight);
            slot.badge.gameObject.SetActive(numbered);
            slot.number.text = (i + 1).ToString(CultureInfo.InvariantCulture);
            x += width + iconGap;
        }
        for (int i = shownIcons.Count; i < slots.Count; i++)
        {
            slots[i].box.gameObject.SetActive(false);
        }

        float textX = padX + iconsWidth + between;
        float textTop = padY + (innerHeight + textHeight) * 0.5f;
        PlaceText(headlineText, head, textX, textTop, headWidth, headHeight);
        PlaceText(messageText, more, textX, textTop - headHeight - spacing, moreWidth, moreHeight);
    }

    // The narrowest width that still fits the text in as few lines as Max Text Width does, so the lines
    // come out even instead of leaving one word alone at the end.
    float BalancedWidth(TextMeshProUGUI label, string value)
    {
        if (value.Length == 0)
        {
            return 0f;
        }
        Vector2 preferred = label.GetPreferredValues(value, maxTextWidth, Mathf.Infinity);
        float high = Mathf.Min(preferred.x, maxTextWidth);
        float low = high * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            float middle = (low + high) * 0.5f;
            if (label.GetPreferredValues(value, middle, Mathf.Infinity).y > preferred.y + 0.5f)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }
        return Mathf.Ceil(high) + 2f;
    }

    static float Measure(TextMeshProUGUI label, string value, float width)
    {
        return value.Length > 0 ? Mathf.Ceil(label.GetPreferredValues(value, width, Mathf.Infinity).y) : 0f;
    }

    static void PlaceText(TextMeshProUGUI label, string value, float x, float top, float width, float height)
    {
        label.text = value;
        label.gameObject.SetActive(value.Length > 0);
        label.rectTransform.anchoredPosition = new Vector2(x, top - height);
        label.rectTransform.sizeDelta = new Vector2(width, height);
    }

    float IconWidth(HintBubble.Icon icon)
    {
        Rect rect = icon.sprite.rect;
        return iconHeight * rect.width / Mathf.Max(1f, rect.height);
    }

    // Everything the bubble says, used to notice when the text changes (for example {color}).
    string Compose(HintBubble hint)
    {
        return HeadlinePart(hint) + "\n" + MessagePart(hint);
    }

    string HeadlinePart(HintBubble hint)
    {
        string head = hint.GetHeadline();
        return string.IsNullOrEmpty(head) ? "" : Keys(head);
    }

    string MessagePart(HintBubble hint)
    {
        string more = hint.GetMessage();
        return string.IsNullOrEmpty(more) ? "" : Keys(more);
    }

    string Keys(string value)
    {
        string hex = ColorUtility.ToHtmlStringRGB(accent);
        return keyPattern.Replace(value, "<mark=#" + hex + "33><color=#" + hex + "><b>\u00A0$1\u00A0</b></color></mark>");
    }

    // Puts the tail's tip just above the anchor and keeps the whole bubble on screen.
    bool Place(float deltaTime)
    {
        Vector2 canvasSize = canvasRect.rect.size;
        bool hasTail = shown.anchor != HintBubble.Anchor.Screen;
        Vector2 goal;
        if (hasTail)
        {
            Vector3 world;
            if (!shown.TryGetAnchor(out world)) return false;
            if (shown.anchor == HintBubble.Anchor.Player && cutController.state == CutController.State.Drawing)
            {
                world += Vector3.up * liftWhileCutting;
            }
            Vector3 screen = camRig.cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;
            goal = new Vector2(screen.x, screen.y) / canvas.scaleFactor + Vector2.up * gap;
            float top = goal.y + tailHeight + bodySize.y;
            float highest = canvasSize.y - topMargin;
            if (top > highest) goal.y -= top - highest;
            goal.y = Mathf.Max(goal.y, margin);
            float side = margin + corner + tailWidth * 0.5f;
            goal.x = Mathf.Clamp(goal.x, side, canvasSize.x - side);
        }
        else
        {
            goal = Vector2.Scale(shown.screenPoint, canvasSize) - Vector2.up * (tailHeight + bodySize.y * 0.5f);
        }

        if (!placed)
        {
            tip = goal;
            placed = true;
        }
        else
        {
            tip = Vector2.Lerp(tip, goal, 1f - Mathf.Exp(-followSpeed * deltaTime));
        }
        bubble.anchoredPosition = tip;

        // Slide the body sideways to keep it on screen. The tail keeps pointing at the anchor.
        float half = bodySize.x * 0.5f;
        float shift = 0f;
        if (tip.x - half < margin)
        {
            shift = margin - (tip.x - half);
        }
        else if (tip.x + half > canvasSize.x - margin)
        {
            shift = canvasSize.x - margin - (tip.x + half);
        }
        float most = Mathf.Max(0f, half - corner - tailWidth * 0.5f);
        shift = Mathf.Clamp(shift, -most, most);
        body.anchoredPosition = new Vector2(shift, tailHeight);
        shadow.rectTransform.anchoredPosition = new Vector2(shift, tailHeight - shadowBlur - shadowDrop);
        tailFill.enabled = hasTail;
        tailEdge.enabled = hasTail;
        return true;
    }

    void AnimateIcons()
    {
        bool cutting = cutController.state == CutController.State.Drawing;
        bool numbered = shown.numberIcons && shownIcons.Count > 1;
        for (int i = 0; i < shownIcons.Count; i++)
        {
            Slot slot = slots[i];
            float time = clock - i * 0.6f; // icons take turns
            Vector2 offset = Vector2.zero;
            Vector3 scale = Vector3.one;
            switch (shownIcons[i].motion)
            {
                case HintBubble.Motion.Press:
                {
                    float press = PressAmount(Mathf.Repeat(time, 1.5f));
                    offset.y = -4f * press;
                    scale = new Vector3(1f + 0.03f * press, 1f - 0.08f * press, 1f);
                    break;
                }
                case HintBubble.Motion.Drag:
                    offset.x = Mathf.Sin(time * Mathf.PI * 2f / 1.6f) * 8f;
                    break;
                case HintBubble.Motion.Orbit:
                {
                    float angle = time * Mathf.PI * 2f / 1.5f;
                    offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 5f;
                    break;
                }
            }
            slot.image.rectTransform.anchoredPosition = offset;
            slot.image.rectTransform.localScale = scale;

            // While cutting, step 1 (press C) is done, so it dims.
            float shade = numbered && cutting && i == 0 ? 0.35f : 1f;
            slot.image.color = new Color(1f, 1f, 1f, shade);
            slot.badge.color = new Color(accent.r, accent.g, accent.b, shade);
            slot.number.alpha = shade;
        }
    }

    static float PressAmount(float time)
    {
        if (time < 0.12f) return Mathf.SmoothStep(0f, 1f, time / 0.12f);
        if (time < 0.34f) return 1f - Mathf.SmoothStep(0f, 1f, (time - 0.12f) / 0.22f);
        return 0f;
    }

    // A bouncing arrow over the object the bubble talks about. Off screen, it waits at the edge and points the way.
    void UpdatePointer()
    {
        if (shown.pointAt == null || alpha <= 0f)
        {
            pointer.enabled = false;
            return;
        }

        Vector2 canvasSize = canvasRect.rect.size;
        Vector3 screen = camRig.cam.WorldToScreenPoint(HintBubble.TopOf(shown.pointAt));
        Vector2 point = new Vector2(screen.x, screen.y) / canvas.scaleFactor;
        float bob = Mathf.Abs(Mathf.Sin(clock * Mathf.PI * 1.4f)) * 12f;
        float inset = margin + arrowSize.y;
        Rect safe = Rect.MinMaxRect(inset, inset, canvasSize.x - inset, canvasSize.y - topMargin);
        RectTransform rect = pointer.rectTransform;
        pointer.enabled = true;
        pointer.color = new Color(1f, 1f, 1f, alpha);

        if (screen.z > 0f && safe.Contains(point))
        {
            rect.anchoredPosition = point + Vector2.up * (pointerGap + bob);
            rect.localRotation = Quaternion.identity;
            return;
        }

        Vector2 center = safe.center;
        Vector2 direction = screen.z > 0f ? point - center : center - point;
        if (direction.sqrMagnitude < 1f) direction = Vector2.down;
        float reachX = direction.x != 0f ? safe.width * 0.5f / Mathf.Abs(direction.x) : float.MaxValue;
        float reachY = direction.y != 0f ? safe.height * 0.5f / Mathf.Abs(direction.y) : float.MaxValue;
        Vector2 way = direction.normalized;
        rect.anchoredPosition = center + direction * Mathf.Min(reachX, reachY) - way * bob;
        rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(way.y, way.x) * Mathf.Rad2Deg + 90f);
    }

    void HideAll()
    {
        if (group != null) group.alpha = 0f;
        if (pointer != null) pointer.enabled = false;
    }

    static float BackOut(float t)
    {
        const float overshoot = 1.70158f;
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }

    // ------------------------------------------------------------------
    // Building the UI
    // ------------------------------------------------------------------

    void Build()
    {
        canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        canvasRect = GetComponent<RectTransform>();

        badgeSprite = MakeCircleSprite();

        bubble = NewRect("Bubble", transform, new Vector2(0.5f, 0f));
        group = bubble.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        shadow = NewImage("Shadow", bubble, new Vector2(0.5f, 0f), MakeShadowSprite(), shadowColor, true);

        Image bodyFill = NewImage("Body", bubble, new Vector2(0.5f, 0f), MakeRoundSprite(false), fill, true);
        body = bodyFill.rectTransform;
        Image bodyEdge = NewImage("Edge", body, new Vector2(0.5f, 0.5f), MakeRoundSprite(true), edge, true);
        Stretch(bodyEdge.rectTransform);

        // The tail is drawn over the body so it covers the body's edge where they meet.
        Vector2 tailSize = new Vector2(tailWidth, tailHeight + edgeWidth);
        tailFill = NewImage("Tail", bubble, new Vector2(0.5f, 0f), MakeTailSprite(false), fill, false);
        tailFill.rectTransform.sizeDelta = tailSize;
        tailEdge = NewImage("Tail Edge", bubble, new Vector2(0.5f, 0f), MakeTailSprite(true), edge, false);
        tailEdge.rectTransform.sizeDelta = tailSize;

        headlineText = NewText("Headline", FontStyles.Bold);
        messageText = NewText("Message", FontStyles.Normal);

        pointer = NewImage("Pointer", transform, new Vector2(0.5f, 0f), MakeArrowSprite(), Color.white, false);
        pointer.rectTransform.sizeDelta = arrowSize;
    }

    Slot GetSlot(int index)
    {
        while (slots.Count <= index)
        {
            Slot slot = new Slot();
            slot.box = NewRect("Icon " + (slots.Count + 1), body, Vector2.zero);

            slot.image = NewImage("Image", slot.box, new Vector2(0.5f, 0f), null, Color.white, false);
            slot.image.preserveAspect = true;
            slot.image.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            slot.image.rectTransform.anchorMax = new Vector2(0.5f, 0f);

            slot.badge = NewImage("Step", slot.box, new Vector2(0.5f, 0.5f), badgeSprite, accent, false);
            RectTransform badgeRect = slot.badge.rectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(4f, -4f);
            badgeRect.sizeDelta = Vector2.one * badgeSize;

            slot.number = NewGraphic<TextMeshProUGUI>("Number", badgeRect, new Vector2(0.5f, 0.5f));
            Stretch(slot.number.rectTransform);
            slot.number.fontSize = 17f;
            slot.number.fontStyle = FontStyles.Bold;
            slot.number.color = new Color(fill.r, fill.g, fill.b, 1f);
            slot.number.alignment = TextAlignmentOptions.Center;
            slot.number.textWrappingMode = TextWrappingModes.NoWrap;

            slots.Add(slot);
        }
        return slots[index];
    }

    TextMeshProUGUI NewText(string name, FontStyles style)
    {
        TextMeshProUGUI label = NewGraphic<TextMeshProUGUI>(name, body, Vector2.zero);
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = true;
        return label;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 pivot)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = pivot;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    static T NewGraphic<T>(string name, Transform parent, Vector2 pivot) where T : Graphic
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

    static Image NewImage(string name, Transform parent, Vector2 pivot, Sprite sprite, Color color, bool sliced)
    {
        Image image = NewGraphic<Image>(name, parent, pivot);
        image.sprite = sprite;
        image.color = color;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        return image;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------------
    // Sprites, drawn once at start (white ones are tinted by their Image)
    // ------------------------------------------------------------------

    static Sprite MakeRoundSprite(bool ring)
    {
        float side = corner * 2f + 4f;
        Vector2 size = new Vector2(side, side);
        Vector2 center = size * 0.5f;
        float border = corner + 1f;
        return MakeSprite(size, Vector4.one * border, Color.white, p =>
        {
            float d = RoundBoxDistance(p - center, center, corner);
            if (d > 0f || (ring && d < -edgeWidth)) return Color.clear;
            return Color.white;
        });
    }

    static Sprite MakeShadowSprite()
    {
        float side = (corner + shadowBlur) * 2f + 4f;
        Vector2 size = new Vector2(side, side);
        Vector2 center = size * 0.5f;
        Vector2 half = center - Vector2.one * shadowBlur;
        float border = corner + shadowBlur + 1f;
        return MakeSprite(size, Vector4.one * border, Color.white, p =>
        {
            float d = RoundBoxDistance(p - center, half, corner);
            float a = Mathf.Clamp01(1f - (d + 3f) / (shadowBlur + 3f));
            return new Color(1f, 1f, 1f, a * a);
        });
    }

    static Sprite MakeTailSprite(bool edgeOnly)
    {
        Vector2 size = new Vector2(tailWidth, tailHeight + edgeWidth);
        float halfWidth = tailWidth * 0.5f;
        Vector2 left = new Vector2(0f, tailHeight);
        Vector2 right = new Vector2(tailWidth, tailHeight);
        Vector2 point = new Vector2(halfWidth, 0f);
        return MakeSprite(size, Vector4.zero, Color.white, p =>
        {
            bool inside;
            float toSide;
            if (p.y >= tailHeight)
            {
                // The strip that overlaps the body's edge.
                inside = p.x >= 0f && p.x <= tailWidth;
                toSide = Mathf.Min(p.x, tailWidth - p.x);
            }
            else
            {
                inside = Mathf.Abs(p.x - halfWidth) <= halfWidth * p.y / tailHeight;
                toSide = Mathf.Min(SegmentDistance(p, left, point), SegmentDistance(p, right, point));
            }
            if (!inside || (edgeOnly && toSide > edgeWidth)) return Color.clear;
            return Color.white;
        });
    }

    static Sprite MakeCircleSprite()
    {
        Vector2 size = Vector2.one * badgeSize;
        Vector2 center = size * 0.5f;
        float radius = badgeSize * 0.5f - 0.5f;
        return MakeSprite(size, Vector4.zero, Color.white, p => Vector2.Distance(p, center) <= radius ? Color.white : Color.clear);
    }

    Sprite MakeArrowSprite()
    {
        Color dark = new Color(fill.r, fill.g, fill.b, 1f);
        Color bright = accent;
        return MakeSprite(arrowSize, Vector4.zero, dark, p =>
        {
            float d = PolygonDistance(p, arrowShape);
            if (d < -2.5f) return bright;
            if (d < 0f) return dark;
            return Color.clear;
        });
    }

    // Supersampled, so edges are smooth. bleed is the color given to see-through pixels, so edges don't darken when filtered.
    static Sprite MakeSprite(Vector2 size, Vector4 border, Color bleed, System.Func<Vector2, Color> shade)
    {
        int width = Mathf.CeilToInt(size.x * density);
        int height = Mathf.CeilToInt(size.y * density);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color[] pixels = new Color[width * height];
        Color clear = new Color(bleed.r, bleed.g, bleed.b, 0f);
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

    static float RoundBoxDistance(Vector2 p, Vector2 half, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * radius;
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
        return Vector2.Distance(p, a + ab * t);
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
}
