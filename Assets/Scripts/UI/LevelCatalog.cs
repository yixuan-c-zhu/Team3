using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Player-facing chapters are independent of legacy scene names/build indices.</summary>
public static class LevelCatalog
{
    public const string MenuScene = "MainMenu";
    const string CompletionPrefix = "CrossSection.Completed.";

    public sealed class Lesson
    {
        public readonly string Scene, Title, Description, Mechanic;
        public readonly int Illustration;
        public Lesson(string scene, string title, string description, string mechanic, int illustration)
        {
            Scene = scene; Title = title; Description = description;
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
            new Lesson("Level1", "Cut to fit", "A low ceiling stands between you and the exit. Change your shape to find a way through.", "CUTTING + MOVEMENT", 0),
            new Lesson("Level2", "Leave a little behind", "One piece holds the pressure plate. The other walks through the door. Make both pieces count.", "WEIGHT + SWITCHING", 1),
            new Lesson("Level3", "A different angle", "A narrow gap hides in plain sight. Turn your perspective, then make the cut that fits.", "PERSPECTIVE + PRECISION", 2),
            new Lesson("Level4", "Put it all together", "Keep enough weight on the plate and enough room under the ceiling. Find the balance in one shape.", "YOUR FIRST COMBINED PUZZLE", 3)),
        new Chapter("LEVEL 01", "New Level", "Explore a new puzzle with color and perspective.", "COLOR + PERSPECTIVE", "jump",
            new Lesson("NewLevel", "New Level", "Use color switching and perspective to overcome obstacles and reach the exit.", "COLOR + PERSPECTIVE", 4)),
        new Chapter("LEVEL 02", "Glass Paradox", "Blue can't get in. Orange can't stand.", "PERSPECTIVE + COLOR", "glass",
            new Lesson("GlassParadox", "Glass Paradox", "Whatever you line up turns into glass of your slice's color. Pick the color, then find the one floor that holds you.", "AIM + COLOR + SWITCHING", 8)),
        new Chapter("LEVEL 03", "Building Bridge", "Find the angle. Make it real.", "PERSPECTIVE + AIM", "bridge",
            new Lesson("BuildingBridge", "Building Bridge", "The road ends in 2D. Find the hidden path, leave a slice of yourself on the ledge, and line it up with the gap from the lookout.", "HIDDEN PATHS + CUTTING + AIM", 6)),
        new Chapter("LEVEL 04", "Level 4", "Coming soon.", "PERSPECTIVE + COLOR", "level4",
            new Lesson("Level4Placeholder", "Level 4", "Coming soon.", "PERSPECTIVE + COLOR", 7)),
        new Chapter("LEVEL 05", "Pushing Button", "Leave your mark. Open a new path.", "WEIGHT + COLOR", "button",
            new Lesson("PushingButton", "Pushing Button", "Navigate colored barriers and leave a piece on the button. Hold the gate open and find your way to the exit.", "SWITCHING + COLOR + MECHANISMS", 5))
    };

    public static bool IsComplete(string scene) => PlayerPrefs.GetInt(CompletionPrefix + scene, 0) == 1;
    public static int CompletedCount(Chapter chapter)
    {
        int count = 0;
        foreach (var lesson in chapter.Lessons) if (IsComplete(lesson.Scene)) count++;
        return count;
    }
    public static void MarkComplete(string scene)
    {
        if (Find(scene) == null) return;
        PlayerPrefs.SetInt(CompletionPrefix + scene, 1);
        PlayerPrefs.Save();
    }
    public static Lesson Find(string scene)
    {
        foreach (var chapter in Chapters)
            foreach (var lesson in chapter.Lessons)
                if (lesson.Scene == scene) return lesson;
        return null;
    }
    // Tutorials advance through four lessons. Each chapter ends at chapter select.
    public static string NextScene(string scene)
    {
        foreach (var chapter in Chapters)
            for (int i = 0; i < chapter.Lessons.Length; i++)
                if (chapter.Lessons[i].Scene == scene)
                    return i + 1 < chapter.Lessons.Length ? chapter.Lessons[i + 1].Scene : MenuScene;
        return MenuScene;
    }
    public static bool CanLoad(string scene) => Application.CanStreamedLevelBeLoaded(scene);
    public static void Load(string scene)
    {
        if (!CanLoad(scene))
        {
            Debug.LogError("Scene is missing from the build scene list: " + scene);
            return;
        }
        HelpPanel.CloseAll();
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene);
    }
}
