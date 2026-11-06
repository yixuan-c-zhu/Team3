using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Scene-local controls and goal, sharing gameplay actions with keyboard input.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HUD))]
public sealed class TutorialHudLayout : MonoBehaviour
{
    [Header("Feedback")]
    [Tooltip("Briefly highlight skill cards when their keyboard shortcut or button is pressed.")]
    public bool enableHighlights = false;

    [Header("Appearance")]
    public Color cardFill = new Color(0.10f, 0.12f, 0.14f, 0.76f);
    public Color cardEdge = new Color(0.80f, 0.83f, 0.85f, 0.48f);
    public Color accent = new Color(1f, 0.83f, 0.22f, 1f);
    public Color blue = new Color(0.30f, 0.48f, 1f, 1f);
    public Color orange = new Color(1f, 0.47f, 0.20f, 1f);

    HUD hud;
    CheckpointManager checkpoints;
    RectTransform root, footer, objective;
    TMP_Text objectiveText;
    TMP_FontAsset font;
    bool statsEnabled, tipsEnabled;
    Vector2 messagePosition;
    int shownRoom = -1;
    CutController cut;
    CameraRig cameraRig;
    PieceControlManager colors;
    CardFeedback cutCard, viewCard, colorCard, switchCard;

    sealed class CardFeedback
    {
        public TutorialHudLayout owner;
        public TutorialHudGraphic panel, keycap;
        public TMP_Text keyText;
        float until;
        bool lit;

        public void Flash()
        {
            if (!owner.enableHighlights)
            {
                if (lit) SetLit(false);
                return;
            }
            until = Time.unscaledTime + 0.22f;
            SetLit(true);
        }

        public void Tick()
        {
            if (lit && (!owner.enableHighlights || Time.unscaledTime >= until)) SetLit(false);
        }

        void SetLit(bool value)
        {
            lit = value;
            panel.color = value ? Color.Lerp(owner.cardFill, owner.orange, 0.24f) : owner.cardFill;
            panel.edge = value ? owner.orange : owner.cardEdge;
            panel.SetVerticesDirty();
            keycap.color = value ? owner.orange : new Color(0, 0, 0, 0.10f);
            keycap.edge = value ? owner.orange : Color.white;
            keycap.SetVerticesDirty();
            keyText.color = value ? new Color(0.12f, 0.10f, 0.08f) : Color.white;
        }
    }

    void OnEnable()
    {
        hud = GetComponent<HUD>();
        checkpoints = FindFirstObjectByType<CheckpointManager>();
        if (checkpoints == null || hud == null) return;
        font = hud.modeText != null ? hud.modeText.font : TMP_Settings.defaultFontAsset;

        // Keep the existing counters and room tips intact; replace only their presentation.
        if (hud.statsText != null)
        {
            statsEnabled = hud.statsText.enabled;
            hud.statsText.enabled = false;
        }
        if (checkpoints.tipText != null)
        {
            tipsEnabled = checkpoints.tipText.enabled;
            checkpoints.tipText.enabled = false;
        }
        if (hud.messageText != null)
        {
            messagePosition = hud.messageText.rectTransform.anchoredPosition;
            hud.messageText.rectTransform.anchoredPosition = new Vector2(messagePosition.x, 330f);
        }

        root = Rect("Tutorial controls", transform, Vector2.zero, Vector2.zero, Vector2.zero);
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        objective = Rect("Room goal", root, new Vector2(0, 1), new Vector2(30, -72), new Vector2(350, 44));
        Panel(objective, cardFill, Color.clear, 10, 0);
        var marker = Rect("Checkpoint marker", objective, new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(26, 26));
        marker.pivot = new Vector2(0, 0.5f);
        Shape(marker, TutorialHudGraphic.Shape.Diamond, new Color(0.24f, 1f, 0.50f));
        objectiveText = Label("Goal", objective, new Vector2(55, 0), new Vector2(320, 44), "", 27);

        footer = Rect("Controls", root, Vector2.zero, new Vector2(44, 30), new Vector2(848, 212));
        footer.pivot = Vector2.zero;
        cut = FindFirstObjectByType<CutController>();
        cameraRig = FindFirstObjectByType<CameraRig>();
        colors = FindFirstObjectByType<PieceControlManager>();
        cutCard = Card("Cut", "C", TutorialHudGraphic.Shape.Cut, new Vector2(0, 114), 402,
            () => { if (cut != null) { GameplayUiInput.ReportControl(KeyCode.C); cut.ToggleCut("ui_cut"); } });
        viewCard = Card("Change view", "Tab", TutorialHudGraphic.Shape.View, new Vector2(420, 114), 428,
            () => { if (cameraRig != null) { GameplayUiInput.ReportControl(KeyCode.Tab); cameraRig.SwitchViewFromInput("ui_view"); } });
        colorCard = Card("Change color", "Shift", TutorialHudGraphic.Shape.Color, new Vector2(0, 0), 402,
            () => { if (colors != null) { GameplayUiInput.ReportControl(colors.colorKey); colors.ChangeControlledPieceColor("ui_color"); } });
        switchCard = Card("Switch piece", "F", TutorialHudGraphic.Shape.Switch, new Vector2(420, 0), 428,
            () => { if (cut != null) { GameplayUiInput.ReportControl(cut.switchKey); cut.SwitchPiece("ui_piece"); } });
        if (cut != null)
        {
            cut.CutInput += cutCard.Flash;
            cut.PieceSwitchInput += switchCard.Flash;
        }
        if (cameraRig != null) cameraRig.ViewInput += viewCard.Flash;
        if (colors != null) colors.ColorInput += colorCard.Flash;
        UpdateGoal();
    }

    void LateUpdate()
    {
        if (root == null) return;
        cutCard.Tick();
        viewCard.Tick();
        colorCard.Tick();
        switchCard.Tick();
        UpdateGoal();
        // The parent canvas scales with width. Limit the footer on short, ultrawide windows too.
        float scale = Mathf.Min(0.9f, root.rect.height * 0.24f / 212f);
        footer.localScale = Vector3.one * scale;
        if (hud.messageText != null)
            hud.messageText.rectTransform.anchoredPosition = new Vector2(messagePosition.x, 30 + 234 * scale);
    }

    void UpdateGoal()
    {
        if (checkpoints == null || checkpoints.rooms == null || shownRoom == checkpoints.CurrentRoom) return;
        shownRoom = checkpoints.CurrentRoom;
        objectiveText.text = shownRoom == checkpoints.rooms.Length - 1 ? "Reach the exit" : "Reach the checkpoint";
        objective.sizeDelta = new Vector2(objectiveText.preferredWidth + 76f, 44f);
    }

    void OnDisable()
    {
        if (cut != null)
        {
            if (cutCard != null) cut.CutInput -= cutCard.Flash;
            if (switchCard != null) cut.PieceSwitchInput -= switchCard.Flash;
        }
        if (cameraRig != null && viewCard != null) cameraRig.ViewInput -= viewCard.Flash;
        if (colors != null && colorCard != null) colors.ColorInput -= colorCard.Flash;
        if (hud != null)
        {
            if (hud.statsText != null) hud.statsText.enabled = statsEnabled;
            if (hud.messageText != null) hud.messageText.rectTransform.anchoredPosition = messagePosition;
        }
        if (checkpoints != null && checkpoints.tipText != null) checkpoints.tipText.enabled = tipsEnabled;
        if (root != null) Destroy(root.gameObject);
        shownRoom = -1;
    }

    CardFeedback Card(string action, string key, TutorialHudGraphic.Shape icon, Vector2 position, float width,
        UnityEngine.Events.UnityAction onClick)
    {
        var card = Rect(action + " card", footer, Vector2.zero, position, new Vector2(width, 98));
        card.pivot = Vector2.zero;
        var panel = Panel(card, cardFill, cardEdge, 18, 2f);
        panel.raycastTarget = true;
        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = panel;
        button.transition = Selectable.Transition.None;
        // A clicked card must not consume Space (jump) or keyboard navigation.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);

        var picture = Rect("Action icon", card, new Vector2(0, 1), new Vector2(20, -10), new Vector2(106, 78));
        var graphic = Shape(picture, icon, Color.white);
        graphic.blue = blue;
        graphic.orange = orange;
        var divider = Rect("Divider", card, new Vector2(0, 1), new Vector2(142, -24), new Vector2(2, 50));
        divider.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.34f);
        divider.GetComponent<Image>().raycastTarget = false;

        float keyWidth = key == "Shift" ? 78 : key == "Tab" ? 68 : 50;
        var keycap = Keycap(card, key, new Vector2(160, -29), new Vector2(keyWidth, 40));
        var description = Label("Action", card, new Vector2(174 + keyWidth, 0),
            new Vector2(width - keyWidth - 190, 98), action, 24);
        description.enableAutoSizing = true;
        description.fontSizeMin = 21;
        description.fontSizeMax = 24;
        return new CardFeedback { owner = this, panel = panel, keycap = keycap,
            keyText = keycap.GetComponentInChildren<TMP_Text>() };
    }

    TutorialHudGraphic Keycap(RectTransform parent, string key, Vector2 position, Vector2 size)
    {
        var rect = Rect(key + " key", parent, new Vector2(0, 1), position, size);
        var panel = Panel(rect, new Color(0, 0, 0, 0.10f), Color.white, 10, 2f);
        var text = Label("Key", rect, Vector2.zero, size, key, 25);
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Midline;
        return panel;
    }

    TMP_Text Label(string name, RectTransform parent, Vector2 position, Vector2 size, string value, float fontSize)
    {
        var rect = Rect(name, parent, new Vector2(0, 1), position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.text = value;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static TutorialHudGraphic Shape(RectTransform rect, TutorialHudGraphic.Shape shape, Color color)
    {
        var graphic = rect.gameObject.AddComponent<TutorialHudGraphic>();
        graphic.shape = shape;
        graphic.color = color;
        graphic.raycastTarget = false;
        return graphic;
    }

    static TutorialHudGraphic Panel(RectTransform rect, Color fill, Color edge, float radius, float border)
    {
        var graphic = Shape(rect, TutorialHudGraphic.Shape.Panel, fill);
        graphic.edge = edge;
        graphic.radius = radius;
        graphic.border = border;
        return graphic;
    }
}
