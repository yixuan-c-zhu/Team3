using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Explicit authoring command; never rewrites scenes automatically on editor load.</summary>
public static class LevelSelectSceneSetup
{
    [MenuItem("Tools/Cross Section/Create Level Select Scene")]
    public static void CreateScene()
    {
        const string scenePath = "Assets/Scenes/MainMenu.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
        {
            Debug.LogWarning("MainMenu already exists. Open it to edit; it will not be overwritten.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1440, 900);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 0.5f;
        panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/LevelSelect/LevelSelectTheme.tss");
        AssetDatabase.CreateAsset(panel, "Assets/UI/LevelSelect/LevelSelectPanel.asset");

        var cameraObject = new GameObject("Menu Camera", typeof(Camera), typeof(AudioListener));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(245, 243, 235, 255);
        camera.cullingMask = 0;
        cameraObject.tag = "MainCamera";
        var menuObject = new GameObject("Level Select", typeof(UIDocument));
        menuObject.GetComponent<UIDocument>().panelSettings = panel;
        menuObject.AddComponent<LevelSelectMenu>().menuFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
        EditorSceneManager.SaveScene(scene, scenePath);

        var buildScenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(scenePath, true) };
        foreach (var chapter in LevelCatalog.Chapters)
            foreach (var lesson in chapter.Lessons)
            {
                // The tutorial's four lessons share one scene, so add each scene once.
                string lessonPath = "Assets/Scenes/" + lesson.Scene + ".unity";
                if (!buildScenes.Exists(item => item.path == lessonPath)) buildScenes.Add(new EditorBuildSettingsScene(lessonPath, true));
            }
        // Retain other project scenes (e.g. the disabled template) without duplicate entries.
        foreach (var existing in EditorBuildSettings.scenes)
            if (!buildScenes.Exists(item => item.path == existing.path)) buildScenes.Add(existing);
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("Level select scene created with Tutorial, Level 1, and Level 2.");
    }
}
