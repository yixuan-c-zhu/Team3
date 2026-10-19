using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class LevelSelectMenu : MonoBehaviour
{
    public Font menuFont;
    VisualElement root, overview, tutorial;
    Label title, errorLabel;
    Button backButton;
    bool showingTutorial, loading;

    void OnEnable()
    {
        Time.timeScale = 1f;
        loading = false;
        root = GetComponent<UIDocument>().rootVisualElement;
        root.Clear();
        root.SetEnabled(true);
        root.AddToClassList("menu-root");
        var stylesheet = Resources.Load<StyleSheet>("LevelSelect/LevelSelect");
        if (stylesheet != null && !root.styleSheets.Contains(stylesheet)) root.styleSheets.Add(stylesheet);
        if (menuFont != null) root.style.unityFontDefinition = FontDefinition.FromFont(menuFont);
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

        var panel = new VisualElement { name = "level-select-panel" };
        panel.AddToClassList("menu-panel");
        root.Add(panel);

        var header = new VisualElement();
        header.AddToClassList("panel-header");
        title = new Label { name = "menu-title", pickingMode = PickingMode.Ignore };
        title.AddToClassList("panel-title");
        header.Add(title);
        backButton = new Button(ShowOverview) { name = "back-to-chapters", text = "←", tooltip = "Back" };
        backButton.AddToClassList("back-button");
        header.Add(backButton);
        panel.Add(header);

        overview = new VisualElement { name = "chapter-overview" };
        overview.AddToClassList("button-row");
        // One button per chapter in LevelCatalog: Tutorial, then Level 1, Level 2, ...
        for (int i = 0; i < LevelCatalog.Chapters.Length; i++)
        {
            int chapter = i;
            var button = MakeButton(i == 0 ? "Tutorial" : "Level " + i, "chapter-" + i, () => ShowChapter(chapter));
            if (i > 0) button.SetEnabled(LevelCatalog.CanLoad(LevelCatalog.Chapters[i].Lessons[0].Scene));
            overview.Add(button);
        }
        panel.Add(overview);

        tutorial = new VisualElement { name = "tutorial-lessons" };
        tutorial.AddToClassList("button-row");
        for (int i = 0; i < LevelCatalog.Chapters[0].Lessons.Length; i++)
        {
            int lesson = i;
            var button = MakeButton((i + 1).ToString(), "lesson-" + i, () => PlayTutorial(lesson));
            button.AddToClassList("number-button");
            button.SetEnabled(LevelCatalog.CanLoad(LevelCatalog.Chapters[0].Lessons[i].Scene));
            tutorial.Add(button);
        }
        panel.Add(tutorial);
        errorLabel = new Label { name = "menu-error" };
        errorLabel.AddToClassList("error-message");
        panel.Add(errorLabel);
        ShowOverview();
    }

    static Button MakeButton(string text, string name, System.Action action)
    {
        var button = new Button(action) { name = name, text = text };
        button.AddToClassList("menu-button");
        return button;
    }

    void OnDisable()
    {
        if (root != null) root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    void OnGeometryChanged(GeometryChangedEvent evt)
    {
        root.EnableInClassList("compact", evt.newRect.width < 650);
    }

    void Update()
    {
        if (showingTutorial && Input.GetKeyDown(KeyCode.Escape)) ShowOverview();
    }

    public void ShowOverview()
    {
        if (loading) return;
        showingTutorial = false;
        title.text = "Level Select";
        overview.style.display = DisplayStyle.Flex;
        tutorial.style.display = DisplayStyle.None;
        backButton.style.visibility = Visibility.Hidden;
        errorLabel.text = "";
    }

    public void ShowChapter(int index)
    {
        if (loading || index < 0 || index >= LevelCatalog.Chapters.Length) return;
        if (index > 0)
        {
            LoadLesson(index, 0);
            return;
        }
        showingTutorial = true;
        title.text = "Tutorial";
        overview.style.display = DisplayStyle.None;
        tutorial.style.display = DisplayStyle.Flex;
        backButton.style.visibility = Visibility.Visible;
        errorLabel.text = "";
    }

    public void PlayTutorial(int index)
    {
        if (index >= 0 && index < LevelCatalog.Chapters[0].Lessons.Length) LoadLesson(0, index);
    }

    void LoadLesson(int chapter, int lesson)
    {
        if (loading) return;
        string scene = LevelCatalog.Chapters[chapter].Lessons[lesson].Scene;
        if (!LevelCatalog.CanLoad(scene))
        {
            errorLabel.text = "This level is unavailable.";
            return;
        }
        loading = true;
        root.SetEnabled(false);
        LevelCatalog.Load(LevelCatalog.Chapters[chapter].Lessons[lesson]);
    }
}
