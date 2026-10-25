using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Player-facing chapters are independent of legacy scene names/build indices.</summary>
public static class LevelCatalog
{
    public const string MenuScene = "MainMenu";
    public const string TutorialScene = "Tutorial";
    const string CompletionPrefix = "CrossSection.Completed.";

    public sealed class Lesson
    {
        public readonly string Scene, Title, Description, Mechanic;
        public readonly int Illustration;
        /// <summary>The checkpoint (room) the lesson starts at inside its scene. The tutorial lessons share one scene.</summary>
        public readonly int Checkpoint;
        /// <summary>Key for the completion save. Rooms 1, 2 and 4 keep their old IDs, so earlier progress still counts.</summary>
        public readonly string Id;
        public Lesson(string scene, string title, string description, string mechanic, int illustration)
            : this(scene, 0, scene, title, description, mechanic, illustration) { }
        public Lesson(string scene, int checkpoint, string id, string title, string description, string mechanic, int illustration)
        {
            Scene = scene; Checkpoint = checkpoint; Id = id;
            Title = title; Description = description;
            Mechanic = mechanic; Illustration = illustration;
        }
    }

    public sealed class Chapter
    {
        public readonly string Label, Title, Description, Tag, ColorClass;
        public readonly Lesson[] Lessons;
        public Chapter(string label, string title, string description, string tag, string colorClass, params Lesson[] lessons)
        {
            Label = label; Title = title; Description = description;
            Tag = tag; ColorClass = colorClass; Lessons = lessons;
        }
    }

    public static readonly Chapter[] Chapters =
    {
        new Chapter("START HERE", "Tutorial", "A small cut. A whole new possibility.", "THE FUNDAMENTALS", "tutorial",
            new Lesson(TutorialScene, 0, "Level1", "Cut to fit", "A low ceiling, a wall that hides a gap, and a road that ends. Change your shape and your view to get through.", "CUTTING + PERSPECTIVE", 0),
            new Lesson(TutorialScene, 1, "Level2", "Leave a little behind", "A gold plate sits under blue glass by a locked door. Leave a piece of yourself where you can't go.", "WEIGHT + GLASS + SWITCHING", 1),
            new Lesson(TutorialScene, 2, "FirstBridge", "First bridge", "Leave a thin slice on the gold ledge and line it up with the gap. Your bridge is made of your color.", "AIM + COLOR", 6),
            new Lesson(TutorialScene, 3, "BuildingBridge", "Building Bridge", "The road ends in 2D. Find the hidden path, leave a slice of yourself on the ledge, and line it up with the gap from the lookout.", "HIDDEN PATHS + CUTTING + AIM", 6)),
        new Chapter("LEVEL 01", "New Level", "Explore a new puzzle with color and perspective.", "COLOR + PERSPECTIVE", "jump",
            new Lesson("NewLevel", "New Level", "Use color switching and perspective to overcome obstacles and reach the exit.", "COLOR + PERSPECTIVE", 4)),
        new Chapter("LEVEL 02", "Glass Paradox", "Blue can't get in. Orange can't stand.", "PERSPECTIVE + COLOR", "glass",
            new Lesson("GlassParadox", "Glass Paradox", "Whatever you line up turns into glass of your slice's color. Pick the color, then find the one floor that holds you.", "AIM + COLOR + SWITCHING", 8))
    };

    public static bool IsComplete(string id) => PlayerPrefs.GetInt(CompletionPrefix + id, 0) == 1;
    public static int CompletedCount(Chapter chapter)
    {
        int count = 0;
        foreach (var lesson in chapter.Lessons) if (IsComplete(lesson.Id)) count++;
        return count;
    }
    public static void MarkComplete(string scene, int checkpoint = 0)
    {
        var lesson = Find(scene, checkpoint);
        if (lesson == null) return;
        PlayerPrefs.SetInt(CompletionPrefix + lesson.Id, 1);
        PlayerPrefs.Save();
    }
    public static Lesson Find(string scene, int checkpoint = 0)
    {
        foreach (var chapter in Chapters)
            foreach (var lesson in chapter.Lessons)
                if (lesson.Scene == scene && lesson.Checkpoint == checkpoint) return lesson;
        return null;
    }
    // Each chapter ends at chapter select. Lessons that share a scene (the tutorial's rooms) move on after the last one.
    public static string NextScene(string scene)
    {
        foreach (var chapter in Chapters)
            for (int i = chapter.Lessons.Length - 1; i >= 0; i--)
                if (chapter.Lessons[i].Scene == scene)
                    return i + 1 < chapter.Lessons.Length ? chapter.Lessons[i + 1].Scene : MenuScene;
        return MenuScene;
    }
    public static bool CanLoad(string scene) => Application.CanStreamedLevelBeLoaded(scene);
    public static void Load(string scene) => Load(scene, 0);
    /// <summary>Loads the lesson's scene, starting at the lesson's checkpoint.</summary>
    public static void Load(Lesson lesson) => Load(lesson.Scene, lesson.Checkpoint);
    public static void Load(string scene, int checkpoint)
    {
        if (!CanLoad(scene))
        {
            Debug.LogError("Scene is missing from the build scene list: " + scene);
            return;
        }
        SetStartCheckpoint(scene, checkpoint);
        HelpPanel.CloseAll();
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }

    // The checkpoint the next load of a scene starts at. Load and restarts set it; the scene's CheckpointManager takes it.
    static string startScene;
    static int startCheckpoint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearStartCheckpoint() => startScene = null;

    public static void SetStartCheckpoint(string scene, int checkpoint)
    {
        startScene = scene;
        startCheckpoint = checkpoint;
    }

    /// <summary>The checkpoint this scene was asked to start at, or -1 if none. Clears it.</summary>
    public static int TakeStartCheckpoint(string scene)
    {
        if (startScene != scene) return -1;
        startScene = null;
        return startCheckpoint;
    }
}
