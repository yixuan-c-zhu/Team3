using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class LevelSelectMenu : MonoBehaviour
{
    public Font menuFont;
    VisualElement root, overview, detail, lessonList, previewHost;
    Label detailTitle, detailDescription, chapterLabel, detailMechanic, completionLabel, lessonNumber, errorLabel;
    Button playButton, backButton;
    int selectedChapter, selectedLesson;
    bool loading;

    static VisualElement Element(string css)
    {
        var element = new VisualElement();
        if (!string.IsNullOrEmpty(css)) element.AddToClassList(css);
        return element;
    }
    static Label Text(string text, string css)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(css);
        return label;
    }
    static Button Action(string text, string css, System.Action action)
    {
        var button = new Button(action) { text = text };
        button.AddToClassList(css);
        return button;
    }

    void OnEnable()
    {
        Time.timeScale = 1f;
        loading = false;
        root = GetComponent<UIDocument>().rootVisualElement;
        root.Clear();
        root.AddToClassList("menu-root");
        var stylesheet = Resources.Load<StyleSheet>("LevelSelect/LevelSelect");
        if (stylesheet != null && !root.styleSheets.Contains(stylesheet)) root.styleSheets.Add(stylesheet);
        if (menuFont != null) root.style.unityFontDefinition = FontDefinition.FromFont(menuFont);
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "menu-scroll" };
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.AddToClassList("menu-scroll");
        root.Add(scroll);
        var shell = Element("shell");
        scroll.Add(shell);
        BuildHeader(shell);
        BuildOverview(shell);
        BuildDetail(shell);
        var footer = Element("footer");
        footer.Add(Text("CUT. SWITCH. SEE THINGS DIFFERENTLY.", "footer-motto"));
        footer.Add(Text("YOUR NEXT PERSPECTIVE STARTS HERE.", "footer-keys"));
        shell.Add(footer);
        ShowOverview();
    }

    void OnDisable()
    {
        if (root != null) root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }
    void OnGeometryChanged(GeometryChangedEvent evt)
    {
        root.EnableInClassList("compact", evt.newRect.width < 1000);
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && detail != null && detail.style.display != DisplayStyle.None)
            ShowOverview();
    }

    void BuildHeader(VisualElement shell)
    {
        var header = Element("header");
        var brand = Element("brand");
        var mark = Element("brand-mark");
        mark.Add(Element("mark-blue")); mark.Add(Element("mark-orange"));
        brand.Add(mark);
        brand.Add(Text("CROSS SECTION", "brand-name"));
        header.Add(brand);
        header.Add(Text("A PUZZLE OF PERSPECTIVE", "brand-caption"));
        shell.Add(header);
    }

    void BuildOverview(VisualElement shell)
    {
        overview = Element("overview"); overview.name = "chapter-overview";
        var hero = Element("hero");
        var intro = Element("intro");
        intro.Add(Text("CHOOSE YOUR NEXT PERSPECTIVE", "eyebrow"));
        intro.Add(Text("Every cut opens\na new possibility.", "hero-title"));
        hero.Add(intro);
        var note = Element("hero-note");
        note.Add(Text("One shape. More than one way through.", "hero-subtitle"));
        note.Add(Text("Start with the basics, or step into a new challenge.", "hero-description"));
        hero.Add(note);
        overview.Add(hero);

        var cards = Element("chapter-cards");
        for (int i = 0; i < LevelCatalog.Chapters.Length; i++)
        {
            int chapterIndex = i;
            var chapter = LevelCatalog.Chapters[i];
            var card = Action("", "chapter-card", () => ShowChapter(chapterIndex));
            card.name = "chapter-" + i;
            card.AddToClassList(chapter.ColorClass);
            var top = Element("card-top");
            top.Add(Text(chapter.Label, "card-kicker"));
            top.Add(Text(i == 0 ? "04 LESSONS" : "01 CHALLENGE", "card-count"));
            card.Add(top);
            var art = new LevelPreviewElement(chapter.Lessons[0].Illustration);
            art.AddToClassList("card-art"); card.Add(art);
            var body = Element("card-body");
            body.Add(Text(chapter.Tag, "card-tag"));
            body.Add(Text(chapter.Title, "card-title"));
            body.Add(Text(chapter.Description, "card-description"));
            card.Add(body);
            var bottom = Element("card-bottom");
            bottom.Add(Text(i == 0 ? "Explore lessons" : "Explore level", "card-action"));
            bottom.Add(Text("↗", "card-arrow"));
            card.Add(bottom);
            cards.Add(card);
        }
        overview.Add(cards);
        var below = Element("overview-bottom");
        below.Add(Text("A GOOD PLACE TO BEGIN", "section-note"));
        below.Add(Text("All chapters are open. Find your own way through.", "availability-note"));
        overview.Add(below);
        shell.Add(overview);
    }

    void BuildDetail(VisualElement shell)
    {
        detail = Element("detail"); detail.name = "chapter-detail";
        var breadcrumb = Element("detail-breadcrumb");
        backButton = Action("←  All chapters", "back-button", ShowOverview);
        backButton.name = "back-to-chapters";
        breadcrumb.Add(backButton);
        chapterLabel = Text("", "eyebrow"); breadcrumb.Add(chapterLabel);
        detail.Add(breadcrumb);

        var layout = Element("detail-layout");
        var sidebar = Element("lesson-sidebar");
        sidebar.Add(Text("YOUR PATH", "eyebrow"));
        completionLabel = Text("", "completion-note"); sidebar.Add(completionLabel);
        lessonList = Element("lesson-list"); lessonList.name = "lesson-list"; sidebar.Add(lessonList);
        var hint = Element("sidebar-hint");
        hint.Add(Text("SMALL CUTS. BIG IDEAS.", "eyebrow"));
        hint.Add(Text("Take your time. There is more than\none way to solve a puzzle.", "sidebar-description"));
        sidebar.Add(hint);
        layout.Add(sidebar);

        var stage = Element("lesson-stage");
        var artwork = Element("detail-artwork");
        lessonNumber = Text("", "lesson-number"); artwork.Add(lessonNumber);
        previewHost = Element("preview-host"); artwork.Add(previewHost);
        artwork.Add(Text("CHANGE YOUR SHAPE. CHANGE YOUR PATH.", "art-caption"));
        stage.Add(artwork);
        var description = Element("lesson-content");
        detailMechanic = Text("", "eyebrow"); description.Add(detailMechanic);
        detailTitle = Text("", "detail-title"); description.Add(detailTitle);
        detailDescription = Text("", "detail-description"); description.Add(detailDescription);
        var playRow = Element("play-row");
        playButton = Action("Play lesson  →", "play-button", PlaySelected);
        playButton.name = "play-selected";
        playRow.Add(playButton);
        playRow.Add(Text("2 cuts. Make them count.", "cut-note"));
        description.Add(playRow);
        errorLabel = Text("", "error-message"); description.Add(errorLabel);
        stage.Add(description);
        layout.Add(stage);
        detail.Add(layout);
        shell.Add(detail);
    }

    public void ShowOverview()
    {
        if (loading) return;
        overview.style.display = DisplayStyle.Flex;
        detail.style.display = DisplayStyle.None;
        root.Q<Button>("chapter-" + selectedChapter)?.Focus();
    }

    public void ShowChapter(int index)
    {
        if (loading || index < 0 || index >= LevelCatalog.Chapters.Length) return;
        selectedChapter = index;
        var chapter = LevelCatalog.Chapters[index];
        overview.style.display = DisplayStyle.None;
        detail.style.display = DisplayStyle.Flex;
        foreach (var c in LevelCatalog.Chapters) detail.RemoveFromClassList(c.ColorClass);
        detail.AddToClassList(chapter.ColorClass);
        chapterLabel.text = index == 0 ? "TUTORIAL  /  " + chapter.Lessons.Length + " LESSONS"
            : chapter.Label + "  /  " + chapter.Title.ToUpperInvariant();
        completionLabel.text = LevelCatalog.CompletedCount(chapter) + " / " + chapter.Lessons.Length + (index == 0 ? " lessons completed" : " challenge completed");
        lessonList.Clear();
        int firstIncomplete = 0;
        for (int i = 0; i < chapter.Lessons.Length; i++)
        {
            int lessonIndex = i;
            var lesson = chapter.Lessons[i];
            var row = Action("", "lesson-row", () => SelectLesson(lessonIndex));
            row.name = "lesson-" + i;
            row.Add(Text((i + 1).ToString("00"), "row-number"));
            var text = Element("row-copy");
            text.Add(Text(lesson.Title, "row-title"));
            text.Add(Text(LevelCatalog.IsComplete(lesson.Scene) ? "COMPLETED" : "READY TO EXPLORE", "row-state"));
            row.Add(text);
            row.Add(Text(LevelCatalog.IsComplete(lesson.Scene) ? "✓" : "→", "row-arrow"));
            lessonList.Add(row);
        }
        while (firstIncomplete < chapter.Lessons.Length - 1 && LevelCatalog.IsComplete(chapter.Lessons[firstIncomplete].Scene)) firstIncomplete++;
        SelectLesson(firstIncomplete);
    }

    public void SelectLesson(int index)
    {
        var chapter = LevelCatalog.Chapters[selectedChapter];
        if (loading || index < 0 || index >= chapter.Lessons.Length) return;
        selectedLesson = index;
        var lesson = chapter.Lessons[index];
        for (int i = 0; i < chapter.Lessons.Length; i++)
            lessonList.Q<Button>("lesson-" + i).EnableInClassList("selected", i == index);
        detailTitle.text = lesson.Title;
        detailDescription.text = lesson.Description;
        detailMechanic.text = lesson.Mechanic;
        lessonNumber.text = selectedChapter == 0 ? "LESSON  " + (index + 1).ToString("00") : chapter.Label;
        previewHost.Clear();
        var art = new LevelPreviewElement(lesson.Illustration);
        art.AddToClassList("detail-art"); previewHost.Add(art);
        bool available = LevelCatalog.CanLoad(lesson.Scene);
        playButton.SetEnabled(available);
        playButton.text = (LevelCatalog.IsComplete(lesson.Scene) ? "Play again" : selectedChapter == 0 ? "Play lesson" : "Play level") + "  →";
        errorLabel.text = available ? "" : "This level is unavailable in this build.";
        lessonList.Q<Button>("lesson-" + index)?.Focus();
    }

    public void PlaySelected()
    {
        if (loading) return;
        string scene = LevelCatalog.Chapters[selectedChapter].Lessons[selectedLesson].Scene;
        if (!LevelCatalog.CanLoad(scene)) return;
        loading = true;
        playButton.SetEnabled(false);
        playButton.text = "Opening level…";
        LevelCatalog.Load(scene);
    }
}
