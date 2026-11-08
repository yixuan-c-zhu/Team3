using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class LevelSelectMenu : MonoBehaviour
{
    public Font menuFont;
    VisualElement root;
    Label errorLabel;
    bool loading;

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
        var title = new Label("Level Select") { name = "menu-title", pickingMode = PickingMode.Ignore };
        title.AddToClassList("panel-title");
        header.Add(title);
        panel.Add(header);

        var overview = new VisualElement { name = "chapter-overview" };
        overview.AddToClassList("button-row");
        // Every chapter starts at its first lesson, including the tutorial.
        for (int i = 0; i < LevelCatalog.Chapters.Length; i++)
        {
            int chapter = i;
            var button = MakeButton(i == 0 ? "Tutorial" : "Level " + i, "chapter-" + i, () => ShowChapter(chapter));
            button.SetEnabled(LevelCatalog.CanLoad(LevelCatalog.Chapters[i].Lessons[0].Scene));
            overview.Add(button);
        }
        panel.Add(overview);

        errorLabel = new Label { name = "menu-error" };
        errorLabel.AddToClassList("error-message");
        panel.Add(errorLabel);
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

    public void ShowChapter(int index)
    {
        if (loading || index < 0 || index >= LevelCatalog.Chapters.Length) return;
        LoadLesson(index, 0);
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
