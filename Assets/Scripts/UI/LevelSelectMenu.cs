using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Level select as a paper level map: a paper sky with cut-paper hills, the title on a ticket stub, and one
/// cardstock-and-kraft block per chapter, stepping up to the right, each with a sign (Tutorial, Level 1, Level 2...).
/// The blue cube stands on the chosen block. A / D or the arrow keys walk it between blocks, Enter or Space plays,
/// and clicking a sign or block walks there and plays. A green pennant over a sign means every lesson in that
/// chapter is cleared. Everything is built in code; LevelSelect.uss (Resources) styles the signs and cards.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class LevelSelectMenu : MonoBehaviour
{
    public Font menuFont;

    static readonly Color Ink = new Color32(34, 40, 58, 255);
    static readonly Color Card = new Color32(247, 246, 241, 255);
    static readonly Color Kraft = new Color32(176, 141, 96, 255);
    static readonly Color Flute = new Color32(126, 97, 65, 255);
    static readonly Color MatGreen = new Color32(47, 122, 85, 255);
    static readonly Color Blue = new Color32(46, 98, 240, 255);
    static readonly Color BlueStripe = new Color32(91, 134, 245, 255);
    static readonly Color SkyTop = new Color32(196, 212, 218, 255);
    static readonly Color SkyBottom = new Color32(228, 233, 225, 255);
    static readonly Color HillFar = new Color32(211, 222, 216, 255);
    static readonly Color HillMid = new Color32(197, 213, 205, 255);
    static readonly Color HillNear = new Color32(182, 201, 192, 255);
    static readonly Color HillLine = new Color32(240, 243, 238, 255);
    static readonly Color Shade = new Color(34f / 255f, 40f / 255f, 58f / 255f, 0.22f);
    static readonly Color CloudShade = new Color(34f / 255f, 40f / 255f, 58f / 255f, 0.1f);

    const float hopTime = 0.32f;
    const float bandHeight = 14f;

    VisualElement root, scenery, ticket, cube;
    Label errorLabel;
    readonly List<VisualElement> signWraps = new List<VisualElement>();
    readonly List<Button> signs = new List<Button>();
    readonly List<VisualElement> blockHits = new List<VisualElement>();
    Rect[] blocks = new Rect[0];
    Rect[] signRects = new Rect[0];
    float[] postX = new float[0];
    bool[] cleared = new bool[0];
    float cubeSize = 64f;
    int selected;
    bool loading;
    int hopFrom = -1;
    float hopClock;
    int playAfterHop = -1;
    float facing = 1f;

    void OnEnable()
    {
        Time.timeScale = 1f;
        loading = false;
        hopFrom = -1;
        playAfterHop = -1;
        root = GetComponent<UIDocument>().rootVisualElement;
        root.Clear();
        root.SetEnabled(true);
        root.AddToClassList("menu-root");
        var stylesheet = Resources.Load<StyleSheet>("LevelSelect/LevelSelect");
        if (stylesheet != null && !root.styleSheets.Contains(stylesheet)) root.styleSheets.Add(stylesheet);
        if (menuFont != null) root.style.unityFontDefinition = FontDefinition.FromFont(menuFont);

        int count = LevelCatalog.Chapters.Length;
        cleared = new bool[count];
        int lessons = 0;
        int done = 0;
        selected = -1;
        for (int i = 0; i < count; i++)
        {
            var chapter = LevelCatalog.Chapters[i];
            int complete = LevelCatalog.CompletedCount(chapter);
            lessons += chapter.Lessons.Length;
            done += complete;
            cleared[i] = complete >= chapter.Lessons.Length;
            // Start on the first chapter that still has something to do.
            if (selected < 0 && !cleared[i]) selected = i;
        }
        if (selected < 0) selected = count - 1;

        scenery = new VisualElement { name = "scenery", pickingMode = PickingMode.Ignore };
        scenery.AddToClassList("scenery");
        scenery.generateVisualContent += PaintScenery;
        root.Add(scenery);

        blockHits.Clear();
        signWraps.Clear();
        signs.Clear();
        for (int i = 0; i < count; i++)
        {
            int chapter = i;
            var hit = new VisualElement { name = "block-" + i };
            hit.AddToClassList("block-hit");
            hit.RegisterCallback<ClickEvent>(_ => Choose(chapter));
            root.Add(hit);
            blockHits.Add(hit);
        }
        for (int i = 0; i < count; i++)
        {
            int chapter = i;
            var wrap = new VisualElement { name = "sign-" + i };
            wrap.AddToClassList("sign-wrap");
            var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
            shadow.AddToClassList("paper-shadow");
            wrap.Add(shadow);
            var sign = new Button(() => Choose(chapter)) { name = "chapter-" + i, text = i == 0 ? "Tutorial" : "Level " + i };
            sign.AddToClassList("sign");
            sign.focusable = false;
            if (!LevelCatalog.CanLoad(LevelCatalog.Chapters[i].Lessons[0].Scene)) sign.AddToClassList("locked");
            wrap.Add(sign);
            root.Add(wrap);
            signWraps.Add(wrap);
            signs.Add(sign);
        }

        cube = new VisualElement { name = "player-cube", pickingMode = PickingMode.Ignore };
        cube.AddToClassList("cube");
        cube.generateVisualContent += PaintCube;
        root.Add(cube);

        ticket = new VisualElement { name = "title-ticket", pickingMode = PickingMode.Ignore };
        ticket.AddToClassList("ticket");
        ticket.generateVisualContent += PaintTicket;
        var title = new Label("Cross Section") { name = "menu-title" };
        title.AddToClassList("ticket-title");
        ticket.Add(title);
        var progress = new Label(done + " / " + lessons) { name = "menu-progress" };
        progress.AddToClassList("ticket-count");
        ticket.Add(progress);
        var caption = new Label("cleared");
        caption.AddToClassList("ticket-caption");
        ticket.Add(caption);
        root.Add(ticket);

        errorLabel = new Label { name = "menu-error" };
        errorLabel.AddToClassList("error-message");
        root.Add(errorLabel);

        var hint = new VisualElement { name = "menu-hint", pickingMode = PickingMode.Ignore };
        hint.AddToClassList("hint");
        var hintWrap = new VisualElement();
        hintWrap.AddToClassList("hint-wrap");
        var hintShadow = new VisualElement { pickingMode = PickingMode.Ignore };
        hintShadow.AddToClassList("paper-shadow");
        hintWrap.Add(hintShadow);
        var card = new VisualElement();
        card.AddToClassList("hint-card");
        AddKey(card, "A");
        AddKey(card, "D");
        AddText(card, "walk", false);
        AddKey(card, "Enter");
        AddText(card, "play", false);
        AddText(card, "or click a sign", true);
        hintWrap.Add(card);
        hint.Add(hintWrap);
        root.Add(hint);

        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        UpdateSelection();
    }

    void OnDisable()
    {
        if (root != null) root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    static void AddKey(VisualElement parent, string key)
    {
        var label = new Label(key);
        label.AddToClassList("keycap");
        parent.Add(label);
    }

    static void AddText(VisualElement parent, string text, bool last)
    {
        var label = new Label(text);
        label.AddToClassList("hint-text");
        if (last) label.AddToClassList("last");
        parent.Add(label);
    }

    void OnGeometryChanged(GeometryChangedEvent evt)
    {
        Layout();
    }

    void Layout()
    {
        float w = root.resolvedStyle.width;
        float h = root.resolvedStyle.height;
        if (float.IsNaN(w) || float.IsNaN(h) || w < 10f || h < 10f) return;
        int count = signs.Count;
        float margin = w * 0.07f;
        float blockWidth = Mathf.Clamp(w * 0.16f, 150f, 260f);
        float spacing = count > 1 ? (w - 2f * margin - blockWidth) / (count - 1) : 0f;
        float step = Mathf.Min(h * 0.08f, 78f);
        float baseTop = h * 0.7f;
        float signWidth = Mathf.Clamp(blockWidth * 0.78f, 130f, 190f);
        const float signHeight = 56f;
        cubeSize = Mathf.Clamp(blockWidth * 0.3f, 48f, 72f);
        // Tall enough posts that the signs float above the cube's head.
        float post = cubeSize + 20f;

        blocks = new Rect[count];
        signRects = new Rect[count];
        postX = new float[count];
        for (int i = 0; i < count; i++)
        {
            float left = margin + i * spacing;
            float top = baseTop - i * step;
            blocks[i] = new Rect(left, top, blockWidth, h - top + 40f);
            postX[i] = left + blockWidth * 0.66f;
            signRects[i] = new Rect(postX[i] - signWidth * 0.5f, top - post - signHeight, signWidth, signHeight);
            Place(signWraps[i], signRects[i]);
            Place(blockHits[i], new Rect(left, top - 12f, blockWidth, h - top + 12f));
        }
        cube.style.width = cubeSize;
        cube.style.height = cubeSize;
        PlaceCube();
        scenery.MarkDirtyRepaint();
        ticket.MarkDirtyRepaint();
    }

    static void Place(VisualElement element, Rect rect)
    {
        element.style.left = rect.x;
        element.style.top = rect.y;
        element.style.width = rect.width;
        element.style.height = rect.height;
    }

    void Update()
    {
        if (root == null || blocks.Length == 0) return;
        if (!loading && hopFrom < 0) ReadKeys();
        if (hopFrom >= 0)
        {
            hopClock += Time.unscaledDeltaTime;
            if (hopClock >= HopDuration())
            {
                hopFrom = -1;
                if (playAfterHop == selected)
                {
                    playAfterHop = -1;
                    Play(selected);
                }
            }
        }
        PlaceCube();
        scenery.MarkDirtyRepaint();
        cube.MarkDirtyRepaint();
    }

    void ReadKeys()
    {
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) Walk(selected - 1);
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) Walk(selected + 1);
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) Play(selected);
    }

    /// <summary>Plays a chapter from the start (kept for anything that called the old menu).</summary>
    public void ShowChapter(int index)
    {
        if (index >= 0 && index < LevelCatalog.Chapters.Length) Play(index);
    }

    void Choose(int index)
    {
        if (loading || hopFrom >= 0) return;
        if (index == selected)
        {
            Play(index);
            return;
        }
        playAfterHop = index;
        Walk(index);
    }

    void Walk(int index)
    {
        if (loading || hopFrom >= 0) return;
        index = Mathf.Clamp(index, 0, signs.Count - 1);
        if (index == selected) return;
        facing = index > selected ? 1f : -1f;
        hopFrom = selected;
        selected = index;
        hopClock = 0f;
        errorLabel.text = "";
        UpdateSelection();
    }

    void UpdateSelection()
    {
        for (int i = 0; i < signs.Count; i++) signs[i].EnableInClassList("selected", i == selected);
    }

    void Play(int index)
    {
        if (loading) return;
        var lesson = LevelCatalog.Chapters[index].Lessons[0];
        if (!LevelCatalog.CanLoad(lesson.Scene))
        {
            errorLabel.text = "This level is unavailable.";
            return;
        }
        loading = true;
        LevelCatalog.Load(lesson);
    }

    float HopDuration()
    {
        return hopFrom < 0 ? hopTime : hopTime + 0.1f * (Mathf.Abs(selected - hopFrom) - 1);
    }

    Vector2 CubeSpot(int index)
    {
        Rect block = blocks[index];
        return new Vector2(block.x + block.width * 0.28f - cubeSize * 0.5f, block.y - cubeSize + 1f);
    }

    void PlaceCube()
    {
        if (blocks.Length == 0) return;
        Vector2 spot = CubeSpot(selected);
        if (hopFrom >= 0)
        {
            float t = Mathf.Clamp01(hopClock / HopDuration());
            float eased = t * t * (3f - 2f * t);
            spot = Vector2.Lerp(CubeSpot(hopFrom), spot, eased);
            spot.y -= Mathf.Sin(t * Mathf.PI) * (60f + 20f * Mathf.Abs(selected - hopFrom));
        }
        else
        {
            spot.y -= (1f - Mathf.Cos(Time.unscaledTime * 2.4f)) * 1.5f;
        }
        cube.style.left = spot.x;
        cube.style.top = spot.y;
    }

    Vector2 PathPoint(int index)
    {
        Rect block = blocks[index];
        return new Vector2(block.x + block.width * 0.28f, block.y - 8f);
    }

    // ---- Drawing ----

    void PaintScenery(MeshGenerationContext context)
    {
        var p = context.painter2D;
        Rect area = scenery.contentRect;
        float w = area.width;
        float h = area.height;
        if (w < 1f || h < 1f) return;

        const int bands = 32;
        for (int i = 0; i < bands; i++)
        {
            float y0 = h * i / bands;
            Box(p, new Rect(0f, y0, w, h / bands + 1f), Color.Lerp(SkyTop, SkyBottom, i / (bands - 1f)));
        }
        Hill(p, w, h, 0.5f, 0.035f, 0.9f, 0.4f, HillFar);
        Hill(p, w, h, 0.6f, 0.045f, 1.4f, 2.1f, HillMid);
        Hill(p, w, h, 0.73f, 0.03f, 2.1f, 4.2f, HillNear);

        float drift = Time.unscaledTime * 8f;
        Cloud(p, Wrap(0.12f * w + drift, w), h * 0.17f, 190f, 40f);
        Cloud(p, Wrap(0.55f * w + drift * 0.7f, w), h * 0.1f, 150f, 34f);
        Cloud(p, Wrap(0.8f * w + drift * 0.85f, w), h * 0.3f, 120f, 30f);

        for (int i = 0; i + 1 < blocks.Length; i++) DashedHop(p, PathPoint(i), PathPoint(i + 1));
        for (int i = 0; i < blocks.Length; i++) Block(p, blocks[i]);
        for (int i = 0; i < blocks.Length; i++)
        {
            float top = cleared[i] ? signRects[i].y - 34f : signRects[i].yMax;
            Line(p, new Vector2(postX[i], blocks[i].y), new Vector2(postX[i], top), 4f, Ink);
            if (cleared[i]) Pennant(p, new Vector2(postX[i] + 2f, top + 2f));
        }
    }

    static float Wrap(float x, float width)
    {
        float span = width + 320f;
        return (x % span + span) % span - 160f;
    }

    static void Hill(Painter2D p, float w, float h, float level, float height, float waves, float phase, Color color)
    {
        p.BeginPath();
        p.MoveTo(new Vector2(0f, h));
        for (float x = 0f; x <= w + 24f; x += 24f)
        {
            p.LineTo(new Vector2(x, HillY(x, w, h, level, height, waves, phase)));
        }
        p.LineTo(new Vector2(w + 24f, h));
        p.ClosePath();
        p.fillColor = color;
        p.Fill();

        p.BeginPath();
        p.MoveTo(new Vector2(0f, HillY(0f, w, h, level, height, waves, phase)));
        for (float x = 24f; x <= w + 24f; x += 24f)
        {
            p.LineTo(new Vector2(x, HillY(x, w, h, level, height, waves, phase)));
        }
        p.lineWidth = 2f;
        p.strokeColor = HillLine;
        p.Stroke();
    }

    static float HillY(float x, float w, float h, float level, float height, float waves, float phase)
    {
        float u = x / Mathf.Max(1f, w);
        return h * level + Mathf.Sin(u * Mathf.PI * 2f * waves + phase) * h * height
            + Mathf.Sin(u * Mathf.PI * 2f * waves * 2.3f + phase * 1.7f) * h * height * 0.35f;
    }

    static void Cloud(Painter2D p, float x, float y, float width, float height)
    {
        RoundRect(p, new Rect(x + 6f, y + 6f, width, height), height * 0.5f, CloudShade);
        RoundRect(p, new Rect(x, y, width, height), height * 0.5f, Card);
    }

    static void Block(Painter2D p, Rect block)
    {
        Box(p, new Rect(block.x + 8f, block.y + 8f, block.width, block.height), Shade);
        Box(p, block, Kraft);
        for (float y = block.y + bandHeight + 22f; y < block.yMax; y += 34f)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(block.x + 4f, y));
            for (float x = block.x + 4f; x <= block.xMax - 4f; x += 3f)
            {
                p.LineTo(new Vector2(x, y + Mathf.Sin((x - block.x) / 28f * Mathf.PI * 2f) * 5f));
            }
            p.lineWidth = 2f;
            p.strokeColor = Flute;
            p.Stroke();
        }
        StrokeBox(p, block, 3f, Ink);
        Rect band = new Rect(block.x, block.y, block.width, bandHeight);
        Box(p, band, Card);
        StrokeBox(p, band, 3f, Ink);
    }

    static void DashedHop(Painter2D p, Vector2 from, Vector2 to)
    {
        Vector2 control = (from + to) * 0.5f + Vector2.down * (20f + Mathf.Abs(from.y - to.y) * 0.5f);
        const int samples = 48;
        const float dash = 12f;
        const float gap = 9f;
        float walked = 0f;
        Vector2 previous = from;
        p.lineWidth = 3f;
        p.strokeColor = Ink;
        p.lineCap = LineCap.Round;
        for (int i = 1; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector2 point = (1f - t) * (1f - t) * from + 2f * (1f - t) * t * control + t * t * to;
            float length = Vector2.Distance(previous, point);
            if (walked % (dash + gap) < dash)
            {
                p.BeginPath();
                p.MoveTo(previous);
                p.LineTo(point);
                p.Stroke();
            }
            walked += length;
            previous = point;
        }
        p.lineCap = LineCap.Butt;
    }

    static void Pennant(Painter2D p, Vector2 at)
    {
        p.BeginPath();
        p.MoveTo(at);
        p.LineTo(at + new Vector2(30f, 9f));
        p.LineTo(at + new Vector2(0f, 18f));
        p.ClosePath();
        p.fillColor = MatGreen;
        p.Fill();
        p.lineWidth = 2f;
        p.strokeColor = Ink;
        p.Stroke();
    }

    void PaintCube(MeshGenerationContext context)
    {
        var p = context.painter2D;
        float s = cubeSize;
        Box(p, new Rect(6f, 6f, s, s), Shade);
        Box(p, new Rect(0f, 0f, s, s), Blue);
        for (float x = s / 6f; x < s - 2f; x += s / 6f)
        {
            Line(p, new Vector2(x, 2f), new Vector2(x, s - 2f), 3f, BlueStripe);
        }
        StrokeBox(p, new Rect(0f, 0f, s, s), 3f, Ink);
        float look = facing * s * 0.05f;
        Eye(p, new Vector2(s * 0.4f + look, s * 0.45f), s);
        Eye(p, new Vector2(s * 0.62f + look, s * 0.45f), s);
    }

    static void Eye(Painter2D p, Vector2 center, float size)
    {
        Ellipse(p, center, size * 0.065f, size * 0.105f, Ink);
        Ellipse(p, center + new Vector2(size * 0.02f, -size * 0.04f), size * 0.025f, size * 0.025f, Color.white);
    }

    void PaintTicket(MeshGenerationContext context)
    {
        var p = context.painter2D;
        Rect area = ticket.contentRect;
        float w = area.width;
        float h = area.height;
        if (w < 1f) return;
        RoundRect(p, new Rect(6f, 6f, w, h), 6f, Shade);
        RoundRect(p, new Rect(0f, 0f, w, h), 6f, Card);
        StrokeRoundRect(p, new Rect(0f, 0f, w, h), 6f, 3f, Ink);

        // Notches at both ends, the colour of the sky behind them.
        float rootHeight = Mathf.Max(1f, root.resolvedStyle.height);
        Color sky = Color.Lerp(SkyTop, SkyBottom, Mathf.Clamp01((ticket.layout.y + h * 0.5f) / rootHeight));
        const float notch = 11f;
        Notch(p, new Vector2(0f, h * 0.5f), notch, sky, -90f, 90f);
        Notch(p, new Vector2(w, h * 0.5f), notch, sky, 90f, 270f);

        float perforation = w - 86f;
        for (float y = 10f; y < h - 10f; y += 11f)
        {
            Line(p, new Vector2(perforation, y), new Vector2(perforation, Mathf.Min(y + 6f, h - 10f)), 2f, Ink);
        }
    }

    static void Notch(Painter2D p, Vector2 center, float radius, Color fill, float from, float to)
    {
        p.BeginPath();
        p.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
        p.ClosePath();
        p.fillColor = fill;
        p.Fill();
        p.BeginPath();
        p.Arc(center, radius, new Angle(from, AngleUnit.Degree), new Angle(to, AngleUnit.Degree));
        p.lineWidth = 3f;
        p.strokeColor = Ink;
        p.Stroke();
    }

    // ---- Painter helpers ----

    static void Box(Painter2D p, Rect rect, Color color)
    {
        p.BeginPath();
        p.MoveTo(new Vector2(rect.xMin, rect.yMin));
        p.LineTo(new Vector2(rect.xMax, rect.yMin));
        p.LineTo(new Vector2(rect.xMax, rect.yMax));
        p.LineTo(new Vector2(rect.xMin, rect.yMax));
        p.ClosePath();
        p.fillColor = color;
        p.Fill();
    }

    static void StrokeBox(Painter2D p, Rect rect, float width, Color color)
    {
        p.BeginPath();
        p.MoveTo(new Vector2(rect.xMin, rect.yMin));
        p.LineTo(new Vector2(rect.xMax, rect.yMin));
        p.LineTo(new Vector2(rect.xMax, rect.yMax));
        p.LineTo(new Vector2(rect.xMin, rect.yMax));
        p.ClosePath();
        p.lineWidth = width;
        p.lineJoin = LineJoin.Miter;
        p.strokeColor = color;
        p.Stroke();
    }

    static void Line(Painter2D p, Vector2 from, Vector2 to, float width, Color color)
    {
        p.BeginPath();
        p.MoveTo(from);
        p.LineTo(to);
        p.lineWidth = width;
        p.strokeColor = color;
        p.Stroke();
    }

    static void RoundRectPath(Painter2D p, Rect rect, float radius)
    {
        float r = Mathf.Min(radius, rect.width * 0.5f, rect.height * 0.5f);
        p.BeginPath();
        p.MoveTo(new Vector2(rect.xMin + r, rect.yMin));
        p.LineTo(new Vector2(rect.xMax - r, rect.yMin));
        p.ArcTo(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMin + r), r);
        p.LineTo(new Vector2(rect.xMax, rect.yMax - r));
        p.ArcTo(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax - r, rect.yMax), r);
        p.LineTo(new Vector2(rect.xMin + r, rect.yMax));
        p.ArcTo(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMax - r), r);
        p.LineTo(new Vector2(rect.xMin, rect.yMin + r));
        p.ArcTo(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin + r, rect.yMin), r);
        p.ClosePath();
    }

    static void RoundRect(Painter2D p, Rect rect, float radius, Color color)
    {
        RoundRectPath(p, rect, radius);
        p.fillColor = color;
        p.Fill();
    }

    static void StrokeRoundRect(Painter2D p, Rect rect, float radius, float width, Color color)
    {
        RoundRectPath(p, rect, radius);
        p.lineWidth = width;
        p.strokeColor = color;
        p.Stroke();
    }

    static void Ellipse(Painter2D p, Vector2 center, float rx, float ry, Color color)
    {
        const float k = 0.5523f;
        p.BeginPath();
        p.MoveTo(center + new Vector2(rx, 0f));
        p.BezierCurveTo(center + new Vector2(rx, ry * k), center + new Vector2(rx * k, ry), center + new Vector2(0f, ry));
        p.BezierCurveTo(center + new Vector2(-rx * k, ry), center + new Vector2(-rx, ry * k), center + new Vector2(-rx, 0f));
        p.BezierCurveTo(center + new Vector2(-rx, -ry * k), center + new Vector2(-rx * k, -ry), center + new Vector2(0f, -ry));
        p.BezierCurveTo(center + new Vector2(rx * k, -ry), center + new Vector2(rx, -ry * k), center + new Vector2(rx, 0f));
        p.ClosePath();
        p.fillColor = color;
        p.Fill();
    }
}
