using System.Collections.Generic;
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

    [Header("Level layout")]
    [Tooltip("Show the WASD and Space keys under the control cards.")]
    public bool showMoveKeys = false;
    [Tooltip("Put a small restart button left of this button instead of the restart card.")]
    public RectTransform restartBeside;

    const float cardHeight = 98f;
    const float footerHeight = 212f;
    const float moveKeyHeight = 38f;
    const float moveKeyGap = 18f;
    const float compactHeight = 56f;
    const float compactWidth = 232f;
    const float compactGap = 12f;
    const float slotWidth = 415f;
    const float slotGap = 18f;
    const float rowStep = 114f;
    const float flyTime = 0.55f;
    const float flyArc = 120f;
    const float flyStartScale = 0.35f;
    const float glowTime = 0.45f;
    const string learnedPrefix = "CrossSection.Learned.";

    static readonly Vector3[] corners = new Vector3[4];

    Color textColor = Color.white;
    Color iconColor = Color.white;
    Color keyFill = new Color(0, 0, 0, 0.10f);
    Color keyEdge = Color.white;
    Color keyText = Color.white;
    Color dividerColor = new Color(1, 1, 1, 0.34f);
    Color shadowColor = Color.clear;
    float cardBorder = 2f;

    HUD hud;
    CheckpointManager checkpoints;
    RectTransform root, footer, objective, restartArea;
    TMP_Text objectiveText;
    TMP_FontAsset font;
    bool statsEnabled, tipsEnabled;
    int shownRoom = -1;
    CutController cut;
    CameraRig cameraRig;
    PieceControlManager colors;
    CardFeedback cutCard, viewCard, colorCard, switchCard;
    GameObject moveKeys;
    GameObject[] aimCards;
    AimZone aimingZone;
    CardFeedback[] skillCards;
    readonly List<CardFeedback> shownCards = new List<CardFeedback>();
    float cardsBase;
    Canvas hudCanvas;

    sealed class CardFeedback
    {
        public TutorialHudLayout owner;
        public GameObject card;
        public TutorialHudGraphic panel, keycap;
        public TMP_Text keyText;
        public string skill;
        public bool learned;
        public Vector2 slot;
        public Vector2 flyFrom;
        public float flyClock = -1f;
        public float glow;
        float until;
        bool lit;

        public void ShowGlow(float amount)
        {
            panel.color = Color.Lerp(owner.cardFill, Color.Lerp(owner.cardFill, owner.accent, 0.35f), amount);
            panel.edge = Color.Lerp(owner.cardEdge, owner.accent, amount);
            panel.SetVerticesDirty();
        }

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
            keycap.color = value ? owner.orange : owner.keyFill;
            keycap.edge = value ? owner.orange : owner.keyEdge;
            keycap.SetVerticesDirty();
            if (keyText != null) keyText.color = value ? new Color(0.12f, 0.10f, 0.08f) : owner.keyText;
        }
    }

    void OnEnable()
    {
        hud = GetComponent<HUD>();
        hudCanvas = GetComponentInParent<Canvas>();
        checkpoints = FindFirstObjectByType<CheckpointManager>();
        if (hud == null) return;
        font = hud.modeText != null ? hud.modeText.font : TMP_Settings.defaultFontAsset;
        if (PaperFeel.IsOn) UsePaperLook();

        // Keep the existing counters and room tips intact; replace only their presentation.
        if (hud.statsText != null)
        {
            statsEnabled = hud.statsText.enabled;
            hud.statsText.enabled = false;
        }
        if (checkpoints != null && checkpoints.tipText != null)
        {
            tipsEnabled = checkpoints.tipText.enabled;
            checkpoints.tipText.enabled = false;
        }

        root = Rect("Tutorial controls", transform, Vector2.zero, Vector2.zero, Vector2.zero);
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        objective = Rect("Room goal", root, new Vector2(0, 1), new Vector2(30, -72), new Vector2(350, 44));
        var goalPanel = Panel(objective, cardFill, shadowColor.a > 0f ? cardEdge : Color.clear, 10, shadowColor.a > 0f ? cardBorder : 0);
        goalPanel.shadow = shadowColor;
        var marker = Rect("Checkpoint marker", objective, new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(26, 26));
        marker.pivot = new Vector2(0, 0.5f);
        Shape(marker, TutorialHudGraphic.Shape.Diamond, new Color(0.24f, 1f, 0.50f));
        objectiveText = Label("Goal", objective, new Vector2(55, 0), new Vector2(320, 44), "", 27);

        float keysRow = showMoveKeys ? moveKeyHeight + moveKeyGap : 0f;
        footer = Rect("Controls", root, Vector2.zero, new Vector2(44, 30), new Vector2(848, footerHeight + keysRow));
        footer.pivot = Vector2.zero;
        cut = FindFirstObjectByType<CutController>();
        cameraRig = FindFirstObjectByType<CameraRig>();
        colors = FindFirstObjectByType<PieceControlManager>();
        cutCard = Card("Cut", "C", TutorialHudGraphic.Shape.Cut, Vector2.zero, slotWidth,
            () => { if (cut != null) { GameplayUiInput.ReportControl(KeyCode.C); cut.ToggleCut("ui_cut"); } });
        viewCard = Card("Change view", "", TutorialHudGraphic.Shape.View, Vector2.zero, slotWidth,
            () => { }, null, cardHeight, true);
        colorCard = Card("Change color", "Shift", TutorialHudGraphic.Shape.Color, Vector2.zero, slotWidth,
            () => { if (colors != null) { GameplayUiInput.ReportControl(colors.colorKey); colors.ChangeControlledPieceColor("ui_color"); } });
        switchCard = Card("Switch piece", "F", TutorialHudGraphic.Shape.Switch, Vector2.zero, slotWidth,
            () => { if (cut != null) { GameplayUiInput.ReportControl(cut.switchKey); cut.SwitchPiece("ui_piece"); } });
        SetUpSkills(keysRow);
        if (showMoveKeys) MoveKeys();
        AimCards();
        var levelManager = FindFirstObjectByType<LevelManager>();
        UnityEngine.Events.UnityAction restart =
            () => { if (levelManager != null) { GameplayUiInput.ReportControl(KeyCode.R); levelManager.RestartLevel("ui_restart"); } };
        if (restartBeside != null)
        {
            restartArea = Rect("Restart", root, new Vector2(1, 0), Vector2.zero, new Vector2(compactWidth, compactHeight));
            restartArea.pivot = new Vector2(1, 0);
            Card("Restart", "R", TutorialHudGraphic.Shape.Restart, Vector2.zero, compactWidth, restart, restartArea, compactHeight);
        }
        else
        {
            // Restart sits apart from the other controls, in the bottom-right corner.
            restartArea = Rect("Restart", root, new Vector2(1, 0), new Vector2(-44, 30), new Vector2(402, 98));
            restartArea.pivot = new Vector2(1, 0);
            Card("Restart room", "R", TutorialHudGraphic.Shape.Restart, Vector2.zero, 402, restart, restartArea);
        }
        if (cut != null)
        {
            cut.CutInput += cutCard.Flash;
            cut.PieceSwitchInput += switchCard.Flash;
        }
        if (cameraRig != null) cameraRig.ViewInput += viewCard.Flash;
        if (colors != null) colors.ColorInput += colorCard.Flash;
        if (cut != null)
        {
            cut.CutDone += LearnCut;
            cut.PieceSwitched += LearnSwitch;
        }
        if (cameraRig != null) cameraRig.ViewInput += LearnView;
        if (colors != null) colors.ColorChanged += LearnColor;
        objective.gameObject.SetActive(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == LevelCatalog.TutorialScene);
        UpdateGoal();
    }

    void UsePaperLook()
    {
        cardFill = PaperFeel.Cardstock;
        cardEdge = PaperFeel.Ink;
        blue = PaperFeel.Blue;
        orange = PaperFeel.Orange;
        textColor = PaperFeel.Ink;
        iconColor = PaperFeel.Ink;
        keyFill = PaperFeel.Gold;
        keyEdge = PaperFeel.Ink;
        keyText = PaperFeel.Ink;
        dividerColor = new Color(PaperFeel.Ink.r, PaperFeel.Ink.g, PaperFeel.Ink.b, 0.25f);
        shadowColor = PaperFeel.Shadow;
        cardBorder = 3f;
    }

    void LateUpdate()
    {
        if (root == null) return;
        cutCard.Tick();
        viewCard.Tick();
        colorCard.Tick();
        switchCard.Tick();
        AnimateCards(Time.unscaledDeltaTime);
        UpdateGoal();
        if (aimCards != null) ShowAimCards(FindAimingZone());
        // The parent canvas scales with width. Limit the footer on short, ultrawide windows too.
        float scale = Mathf.Min(0.9f, root.rect.height * 0.24f / footerHeight);
        footer.localScale = Vector3.one * scale;
        if (restartArea == null) return;
        if (restartBeside != null) PlaceRestart();
        else restartArea.localScale = footer.localScale;
    }

    void PlaceRestart()
    {
        restartBeside.GetWorldCorners(corners);
        float height = Vector3.Distance(corners[0], corners[1]);
        Vector3 left = (corners[0] - corners[3]).normalized;
        float unit = height / compactHeight;
        restartArea.localScale = Vector3.one * (unit / root.lossyScale.y);
        restartArea.position = corners[0] + left * compactGap * unit;
    }

    void UpdateGoal()
    {
        int room = checkpoints != null ? checkpoints.CurrentRoom : 0;
        if (shownRoom == room) return;
        shownRoom = room;
        bool last = checkpoints == null || checkpoints.rooms == null || room == checkpoints.rooms.Length - 1;
        objectiveText.text = last ? "Reach the exit" : "Reach the checkpoint";
        objective.sizeDelta = new Vector2(objectiveText.preferredWidth + 76f, 44f);
    }

    void AimCards()
    {
        var zone = FindFirstObjectByType<AimZone>();
        if (zone == null) return;
        aimCards = new[]
        {
            Card("Drag to aim", "", TutorialHudGraphic.Shape.Drag, new Vector2(0, 114), 428, () => { }).card,
            Card("Step back", "WASD", TutorialHudGraphic.Shape.Back, Vector2.zero, 428,
                () => { if (aimingZone != null) aimingZone.StepBack(); }).card
        };
        foreach (var card in aimCards) card.SetActive(false);
    }

    static AimZone FindAimingZone()
    {
        foreach (var zone in AimZone.allZones)
            if (zone.IsAiming) return zone;
        return null;
    }

    void ShowAimCards(AimZone zone)
    {
        aimingZone = zone;
        bool aiming = zone != null;
        if (aimCards[0].activeSelf == aiming) return;
        foreach (var card in aimCards) card.SetActive(aiming);
        foreach (var card in shownCards) card.card.SetActive(!aiming);
        if (moveKeys != null) moveKeys.SetActive(!aiming);
    }

    void SetUpSkills(float keysRow)
    {
        cardsBase = keysRow;
        skillCards = new[] { cutCard, viewCard, colorCard, switchCard };
        string[] names = { "Cut", "View", "Color", "Switch" };
        bool gradual = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == LevelCatalog.TutorialScene;
        var order = new List<KeyValuePair<int, CardFeedback>>();
        for (int i = 0; i < skillCards.Length; i++)
        {
            CardFeedback card = skillCards[i];
            card.skill = names[i];
            ((RectTransform)card.card.transform).pivot = new Vector2(0.5f, 0.5f);
            card.card.SetActive(false);
            int learnedAt = gradual ? PlayerPrefs.GetInt(learnedPrefix + card.skill, 0) : i + 1;
            card.learned = learnedAt > 0;
            if (card.learned) order.Add(new KeyValuePair<int, CardFeedback>(learnedAt, card));
        }
        order.Sort((a, b) => a.Key.CompareTo(b.Key));
        shownCards.Clear();
        foreach (var pair in order)
        {
            ShowInSlot(pair.Value);
            Place(pair.Value, pair.Value.slot, 1f);
        }
    }

    void ShowInSlot(CardFeedback card)
    {
        int index = shownCards.Count;
        Vector2 corner = new Vector2((index % 2) * (slotWidth + slotGap), cardsBase + (index / 2) * rowStep);
        card.slot = corner + new Vector2(slotWidth, cardHeight) * 0.5f;
        shownCards.Add(card);
        card.card.SetActive(aimingZone == null);
    }

    static void Place(CardFeedback card, Vector2 center, float scale)
    {
        var rect = (RectTransform)card.card.transform;
        rect.anchoredPosition = center;
        rect.localScale = Vector3.one * scale;
    }

    void LearnCut() { Learn(cutCard); }
    void LearnView() { Learn(viewCard); }
    void LearnColor() { Learn(colorCard); }
    void LearnSwitch() { Learn(switchCard); }

    void Learn(CardFeedback card)
    {
        if (card == null || card.learned || skillCards == null) return;
        card.learned = true;
        PlayerPrefs.SetInt(learnedPrefix + card.skill, shownCards.Count + 1);
        PlayerPrefs.Save();
        ShowInSlot(card);
        card.flyFrom = PlayerOnFooter(card.slot);
        card.flyClock = 0f;
        Place(card, card.flyFrom, flyStartScale);
    }

    Vector2 PlayerOnFooter(Vector2 fallback)
    {
        if (cut == null || cut.player == null || cameraRig == null || cameraRig.cam == null) return fallback;
        var body = cut.player.GetComponent<Renderer>();
        Vector3 world = body != null ? body.bounds.center : cut.player.transform.position;
        Vector3 screen = cameraRig.cam.WorldToScreenPoint(world);
        if (screen.z <= 0f) return fallback;
        Camera uiCamera = hudCanvas != null && hudCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? hudCanvas.worldCamera : null;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(footer, screen, uiCamera, out local)) return fallback;
        return local;
    }

    void AnimateCards(float deltaTime)
    {
        foreach (var card in shownCards)
        {
            if (card.flyClock >= 0f)
            {
                card.flyClock += deltaTime;
                float t = Mathf.Clamp01(card.flyClock / flyTime);
                float move = 1f - Mathf.Pow(1f - t, 3f);
                Vector2 position = Vector2.Lerp(card.flyFrom, card.slot, move) + Vector2.up * flyArc * Mathf.Sin(t * Mathf.PI);
                Place(card, position, Mathf.LerpUnclamped(flyStartScale, 1f, BackOut(t)));
                if (t >= 1f)
                {
                    card.flyClock = -1f;
                    card.glow = 1f;
                    Place(card, card.slot, 1f);
                }
            }
            if (card.flyClock < 0f && card.glow > 0f)
            {
                card.glow = Mathf.Max(0f, card.glow - deltaTime / glowTime);
                card.ShowGlow(card.glow);
            }
        }
    }

    static float BackOut(float t)
    {
        float back = t - 1f;
        return 1f + 2.70158f * back * back * back + 1.70158f * back * back;
    }

    void MoveKeys()
    {
        var row = Rect("Move keys", footer, Vector2.zero, Vector2.zero, new Vector2(848, moveKeyHeight));
        row.pivot = Vector2.zero;
        moveKeys = row.gameObject;
        float x = KeyHint(row, "WASD", "Move", 0f);
        KeyHint(row, "Space", "Jump", x + 34f);
    }

    float KeyHint(RectTransform row, string key, string action, float x)
    {
        float keyWidth = key.Length * 15f + 26f;
        Keycap(row, key, new Vector2(x, 0), new Vector2(keyWidth, moveKeyHeight), 20);
        var label = Label(action, row, new Vector2(x + keyWidth + 14f, 0), new Vector2(120, moveKeyHeight), action, 22);
        return x + keyWidth + 14f + label.preferredWidth;
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
        if (cut != null)
        {
            cut.CutDone -= LearnCut;
            cut.PieceSwitched -= LearnSwitch;
        }
        if (cameraRig != null) cameraRig.ViewInput -= LearnView;
        if (colors != null) colors.ColorChanged -= LearnColor;
        shownCards.Clear();
        skillCards = null;
        if (hud != null)
        {
            if (hud.statsText != null) hud.statsText.enabled = statsEnabled;
        }
        if (checkpoints != null && checkpoints.tipText != null) checkpoints.tipText.enabled = tipsEnabled;
        if (root != null) Destroy(root.gameObject);
        shownRoom = -1;
        aimCards = null;
        moveKeys = null;
        aimingZone = null;
    }

    CardFeedback Card(string action, string key, TutorialHudGraphic.Shape icon, Vector2 position, float width,
        UnityEngine.Events.UnityAction onClick, RectTransform parent = null, float height = cardHeight, bool mouseKey = false)
    {
        float k = height / cardHeight;
        var card = Rect(action + " card", parent != null ? parent : footer, Vector2.zero, position, new Vector2(width, height));
        card.pivot = Vector2.zero;
        var panel = Panel(card, cardFill, cardEdge, 18 * k, cardBorder);
        panel.shadow = shadowColor;
        panel.raycastTarget = true;
        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = panel;
        button.transition = Selectable.Transition.None;
        // A clicked card must not consume Space (jump) or keyboard navigation.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);

        var picture = Rect("Action icon", card, new Vector2(0, 1), new Vector2(20, -10) * k, new Vector2(106, 78) * k);
        var graphic = Shape(picture, icon, iconColor);
        graphic.blue = blue;
        graphic.orange = orange;
        var divider = Rect("Divider", card, new Vector2(0, 1), new Vector2(142, -24) * k, new Vector2(2, 50 * k));
        divider.gameObject.AddComponent<Image>().color = dividerColor;
        divider.GetComponent<Image>().raycastTarget = false;

        TutorialHudGraphic keycap = null;
        float textLeft = 160 * k;
        if (mouseKey)
        {
            float keyWidth = 46 * k;
            keycap = Keycap(card, "", new Vector2(160, -29) * k, new Vector2(keyWidth, 40 * k), 25 * k);
            var mouse = Rect("Mouse", keycap.rectTransform, new Vector2(0, 1), new Vector2(-12, 0) * k, new Vector2(70, 40) * k);
            Shape(mouse, TutorialHudGraphic.Shape.MouseLeft, keyText);
            textLeft += keyWidth + 14;
        }
        else if (!string.IsNullOrEmpty(key))
        {
            float keyWidth = (key == "Shift" ? 78 : key == "Tab" ? 68 : key == "WASD" ? 86 : 50) * k;
            keycap = Keycap(card, key, new Vector2(160, -29) * k, new Vector2(keyWidth, 40 * k), 25 * k);
            textLeft += keyWidth + 14;
        }
        var description = Label("Action", card, new Vector2(textLeft, 0),
            new Vector2(width - textLeft - 16, height), action, 24);
        description.enableAutoSizing = true;
        description.fontSizeMin = 21;
        description.fontSizeMax = 24;
        return new CardFeedback { owner = this, card = card.gameObject, panel = panel, keycap = keycap,
            keyText = keycap != null ? keycap.GetComponentInChildren<TMP_Text>() : null };
    }

    TutorialHudGraphic Keycap(RectTransform parent, string key, Vector2 position, Vector2 size, float fontSize = 25)
    {
        var rect = Rect(key + " key", parent, new Vector2(0, 1), position, size);
        var panel = Panel(rect, keyFill, keyEdge, 10, 2f);
        var text = Label("Key", rect, Vector2.zero, size, key, fontSize);
        text.color = keyText;
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
        text.color = textColor;
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
