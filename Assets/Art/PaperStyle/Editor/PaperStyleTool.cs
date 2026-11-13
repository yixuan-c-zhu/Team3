using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Cross Section paper style (art direction: "纸艺剖面", adjusted to the game's mechanics).
///
/// Tools > Paper Style > Apply to Open Scene
///     Backs the scene up to Backups/before-paper-style/, then swaps every level material for its paper
///     version by role, points the gameplay scripts (PieceControlManager, CutController, AimZone,
///     ColorObstacle, PlacedPieceColorSetup, Fadeable) at the paper materials, rewrites hint texts that
///     name the old colours, sets the lighting and adds a "Paper Style" object.
/// Tools > Paper Style > Restore Open Scene From Backup
///     Puts the backed-up scene file back.
/// Tools > Paper Style > Create Missing Assets
///     Creates any missing material, the grain texture and the "Paper Ink" outline feature on both
///     URP renderers (PC for the editor, Mobile for WebGL builds). Never overwrites existing materials,
///     so tweaks made in the Inspector are kept.
/// </summary>
public static class PaperStyleTool
{
    const string Root = "Assets/Art/PaperStyle/";
    const string MaterialDir = Root + "Materials/";
    const string GrainPath = Root + "Textures/T_PaperGrain.png";
    const string BackupDir = "Backups/before-paper-style/";
    const string InkFeatureName = "Paper Ink";
    static readonly string[] RendererPaths = { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" };

    // Palette. Blue and orange belong to the colour mechanic only; nothing else uses them.
    public static readonly Color MechanicBlue = Hex("#2E62F0");
    public static readonly Color MechanicOrange = Hex("#FF8A2A");
    public static readonly Color Cardstock = Hex("#F7F6F1");
    public static readonly Color Kraft = Hex("#B08D60");          // darker than the PDF's #C9A87A so orange stands out in greyscale
    public static readonly Color Ink = Hex("#22283A");
    public static readonly Color Mechanism = Hex("#B5338A");      // magenta, not the PDF's purple (purple reads as blue for red-green colour blindness)
    public static readonly Color CuttingMat = Hex("#2F7A55");
    public static readonly Color SkyHorizon = Hex("#E2E7DE");

    // Old material name -> paper material name.
    static readonly Dictionary<string, string> MaterialMap = new Dictionary<string, string>
    {
        { "M_Floor", "M_Paper_Ground" },
        { "M_Wall", "M_Paper_Wall" },
        { "M_HiddenPath", "M_Paper_HiddenPath" },
        { "M_HiddenPathGhost", "M_Paper_HiddenPathGhost" },
        { "M_Plate", "M_Paper_CuttingMat" },
        { "M_PieceSpot_Gold", "M_Paper_CuttingMat" },
        { "M_AimPad", "M_Paper_Lookout" },
        { "M_AimPadWhite", "M_Paper_Lookout" },
        { "M_LookoutPad", "M_Paper_Lookout" },
        { "ButtonMaterial", "M_Paper_Mechanism" },
        { "M_Door", "M_Paper_Mechanism" },
        { "M_Player", "M_Paper_Blue" },
        { "M_PlayerBlue", "M_Paper_Blue" },
        { "M_PlayerOrange", "M_Paper_Orange" },
        { "M_PlayerBlueSelect", "M_Paper_BlueSelect" },
        { "M_PlayerOrangeSelect", "M_Paper_OrangeSelect" },
        { "M_PieceA", "M_Paper_Blue" },
        { "M_PieceB", "M_Paper_Orange" },
        { "M_Leftover", "M_Paper_Blue" },
        { "M_GlassBlue", "M_Paper_GlassBlue" },
        { "M_GlassOrange", "M_Paper_GlassOrange" },
        { "M_Exit", "M_Paper_Checkpoint" },
    };

    // Hint texts that name colours the paper style no longer uses. Order matters (longest first).
    static readonly string[][] TextSwaps =
    {
        new[] { "<color=#FFD54A>gold ledge</color>", "<color=#8FD9B0>cutting mat</color>" },
        new[] { "gold ledge", "cutting mat" },
        new[] { "Look at that white pad!", "See that lookout pad?" },
        new[] { "white pad", "lookout pad" },
        new[] { "Teal things only show up in 3D.", "Dotted paths only show up in 3D." },
        new[] { "A teal path", "A dotted path" },
        new[] { "a teal path", "a dotted path" },
        new[] { "teal path", "dotted path" },
        new[] { "orange line", "dashed line" },
    };

    [MenuItem("Tools/Paper Style/Apply to Open Scene")]
    static void ApplyMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Paper Style", "Save the scene first.", "OK");
            return;
        }
        if (scene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string report = Apply(scene);
        Debug.Log("Paper Style applied to " + scene.name + "\n" + report);
    }

    [MenuItem("Tools/Paper Style/Restore Open Scene From Backup")]
    static void RestoreMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        string backup = BackupDir + Path.GetFileName(scene.path);
        if (!File.Exists(backup))
        {
            EditorUtility.DisplayDialog("Paper Style", "No backup for " + scene.name + " in " + BackupDir, "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("Paper Style",
            "Replace " + scene.path + " with the copy saved before the paper style was applied?\nChanges made to the scene since then are lost.",
            "Restore", "Cancel")) return;
        File.Copy(backup, scene.path, true);
        AssetDatabase.ImportAsset(scene.path);
        EditorSceneManager.OpenScene(scene.path);
    }

    [MenuItem("Tools/Paper Style/Create Missing Assets")]
    public static void CreateMissingAssets()
    {
        Texture2D grain = EnsureGrainTexture();
        EnsureMaterials(grain);
        EnsureInkFeature(LoadMaterial("M_Paper_Composite"));
        AssetDatabase.SaveAssets();
    }

    /// <summary>Applies the paper style to an open scene and saves it. Returns a short report.</summary>
    public static string Apply(Scene scene)
    {
        CreateMissingAssets();
        StringBuilder report = new StringBuilder();
        report.AppendLine(BackupScene(scene.path));

        // Objects that are mechanisms by role, whatever material they had.
        HashSet<GameObject> mechanisms = new HashSet<GameObject>();
        foreach (PressureButton button in FindInScene<PressureButton>(scene))
        {
            mechanisms.Add(button.gameObject);
            if (button.movingWall != null) mechanisms.Add(button.movingWall.gameObject);
        }
        foreach (PressurePlate plate in FindInScene<PressurePlate>(scene))
        {
            mechanisms.Add(plate.gameObject);
            if (plate.door != null) mechanisms.Add(plate.door.gameObject);
        }
        foreach (Door door in FindInScene<Door>(scene)) mechanisms.Add(door.gameObject);

        HashSet<LineRenderer> cutLines = new HashSet<LineRenderer>();
        foreach (CutController cut in FindInScene<CutController>(scene))
        {
            if (cut.lineRenderer != null) cutLines.Add(cut.lineRenderer);
        }

        Material cutLine = LoadMaterial("M_Paper_CutLine");
        Material targetLine = LoadMaterial("M_Paper_TargetLine");
        Material mechanism = LoadMaterial("M_Paper_Mechanism");
        int renderers = 0;
        int lines = 0;
        foreach (Renderer renderer in FindInScene<Renderer>(scene))
        {
            LineRenderer line = renderer as LineRenderer;
            if (line != null)
            {
                bool isCut = cutLines.Contains(line);
                bool isOld = line.sharedMaterial != null && line.sharedMaterial.name == "M_CuteLine";
                if (!isCut && !isOld) continue;
                Undo.RecordObject(line, "Paper Style");
                line.sharedMaterial = isCut ? cutLine : targetLine;
                line.textureMode = isCut ? LineTextureMode.Stretch : LineTextureMode.Tile;
                lines++;
                continue;
            }
            if (!(renderer is MeshRenderer)) continue;

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                Material paper = mechanisms.Contains(renderer.gameObject) ? mechanism : MapMaterial(materials[i]);
                if (paper != null && paper != materials[i])
                {
                    materials[i] = paper;
                    changed = true;
                }
            }
            if (changed)
            {
                Undo.RecordObject(renderer, "Paper Style");
                renderer.sharedMaterials = materials;
                renderers++;
            }
        }
        report.AppendLine("Renderers restyled: " + renderers + ", lines: " + lines);

        // Material and text fields on scripts.
        int fields = 0;
        int texts = 0;
        foreach (MonoBehaviour behaviour in FindInScene<MonoBehaviour>(scene))
        {
            if (behaviour == null || behaviour is PaperStyle) continue;
            SerializedObject so = new SerializedObject(behaviour);
            SerializedProperty it = so.GetIterator();
            bool changed = false;
            bool enter = true;
            while (it.Next(enter))
            {
                enter = true;
                if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    Material old = it.objectReferenceValue as Material;
                    Material paper = old != null ? MapMaterial(old) : null;
                    if (paper != null && paper != old)
                    {
                        it.objectReferenceValue = paper;
                        changed = true;
                        fields++;
                    }
                }
                else if (it.propertyType == SerializedPropertyType.String)
                {
                    string before = it.stringValue;
                    string after = RewriteText(before);
                    if (after != before)
                    {
                        it.stringValue = after;
                        changed = true;
                        texts++;
                    }
                }
            }
            if (changed) so.ApplyModifiedProperties();
        }
        report.AppendLine("Script material fields: " + fields + ", texts reworded: " + texts);

        // Lighting: paper sky instead of the default skybox, flat ambient, sun from the upper left
        // (so tops are lightest and right-hand sides darkest).
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.78f);
        RenderSettings.fog = false;
        foreach (Camera cam in FindInScene<Camera>(scene))
        {
            if (!cam.CompareTag("MainCamera") && cam.GetComponent<CameraRig>() == null) continue;
            Undo.RecordObject(cam, "Paper Style");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyHorizon;
        }
        foreach (Light light in FindInScene<Light>(scene))
        {
            if (light.type != LightType.Directional) continue;
            Undo.RecordObject(light, "Paper Style");
            Undo.RecordObject(light.transform, "Paper Style");
            light.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
        }
        report.AppendLine("Lighting set");

        PaperStyle style = null;
        foreach (PaperStyle found in FindInScene<PaperStyle>(scene)) style = found;
        if (style == null)
        {
            GameObject go = new GameObject("Paper Style");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Paper Style");
            style = go.AddComponent<PaperStyle>();
        }
        style.backdrop = LoadMaterial("M_Paper_Backdrop");
        style.ink = 1f;
        EditorUtility.SetDirty(style);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.AppendLine("Saved " + scene.path);
        return report.ToString();
    }

    static string BackupScene(string scenePath)
    {
        Directory.CreateDirectory(BackupDir);
        string backup = BackupDir + Path.GetFileName(scenePath);
        if (File.Exists(backup)) return "Backup already exists: " + backup;
        File.Copy(scenePath, backup);
        return "Backed up to " + backup;
    }

    static Material MapMaterial(Material old)
    {
        string paperName;
        if (old == null || !MaterialMap.TryGetValue(old.name, out paperName)) return null;
        return LoadMaterial(paperName);
    }

    static string RewriteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (string[] swap in TextSwaps)
        {
            text = text.Replace(swap[0], swap[1]);
        }
        return text;
    }

    static List<T> FindInScene<T>(Scene scene) where T : Component
    {
        List<T> found = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            found.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return found;
    }

    static Material LoadMaterial(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + name + ".mat");
    }

    // ---------------------------------------------------------------- assets

    static void EnsureMaterials(Texture2D grain)
    {
        Make("M_Paper_Ground", "Paper/Solid", grain, m => { });
        Make("M_Paper_Wall", "Paper/Solid", grain, m => { });
        Make("M_Paper_CuttingMat", "Paper/Solid", grain, m =>
        {
            m.SetColor("_TopColor", CuttingMat);
            m.SetColor("_SideColor", Hex("#2A6E4C"));
            m.SetColor("_BottomColor", Hex("#22593D"));
            m.SetColor("_LipColor", Hex("#4FA37A"));
            m.SetFloat("_LipWidth", 0.06f);
            m.SetFloat("_FluteStrength", 0f);
            m.SetFloat("_TopStyle", 1f);
            m.SetColor("_MarkColor", Hex("#6FB08F"));
            m.SetFloat("_MarkScale", 0.25f);
        });
        Make("M_Paper_HiddenPath", "Paper/Solid", grain, m => SetHiddenPath(m, 1f));
        Make("M_Paper_HiddenPathGhost", "Paper/Solid", grain, m => SetHiddenPath(m, 0.35f));
        Make("M_Paper_Lookout", "Paper/Solid", grain, m =>
        {
            Color plate = Hex("#2B3247");
            m.SetColor("_TopColor", plate);
            m.SetColor("_SideColor", plate);
            m.SetColor("_BottomColor", Ink);
            m.SetColor("_LipColor", plate);
            m.SetFloat("_FluteStrength", 0f);
            m.SetFloat("_TopStyle", 3f);
            m.SetColor("_MarkColor", Cardstock);
        });
        Make("M_Paper_Mechanism", "Paper/Solid", grain, m =>
        {
            m.SetColor("_TopColor", Mechanism);
            m.SetColor("_SideColor", Hex("#8E2A6D"));
            m.SetColor("_BottomColor", Hex("#6E2054"));
            m.SetColor("_LipColor", Hex("#D46AAE"));
            m.SetFloat("_LipWidth", 0.06f);
            m.SetFloat("_FluteStrength", 0f);
        });

        Make("M_Paper_Blue", "Paper/Piece", grain, m => SetPiece(m, MechanicBlue, 1f, 0.14f, 0.25f, 1f));
        Make("M_Paper_Orange", "Paper/Piece", grain, m => SetPiece(m, MechanicOrange, 2f, 0.24f, 0.3f, 1f));
        Make("M_Paper_BlueSelect", "Paper/Piece", grain, m => SetPiece(m, MechanicBlue, 1f, 0.14f, 0.25f, 0.4f));
        Make("M_Paper_OrangeSelect", "Paper/Piece", grain, m => SetPiece(m, MechanicOrange, 2f, 0.24f, 0.3f, 0.4f));

        Make("M_Paper_GlassBlue", "Paper/Glass", null, m => SetGlass(m, MechanicBlue, 0.26f, 1f, Hex("#1E4FD6")));
        Make("M_Paper_GlassOrange", "Paper/Glass", null, m => SetGlass(m, MechanicOrange, 0.28f, 2f, Hex("#E36A10")));
        Make("M_Paper_Checkpoint", "Paper/Glass", null, m =>
        {
            SetGlass(m, CuttingMat, 0.1f, 0f, CuttingMat);
            m.SetFloat("_EdgeWidth", 0.05f);
            m.SetFloat("_GlintStrength", 0.25f);
        });

        Make("M_Paper_CutLine", "Paper/InkLine", null, m =>
        {
            m.SetColor("_BaseColor", Ink);
            m.SetFloat("_DashSpace", 1f);
            m.SetFloat("_DashLength", 10f);
            m.SetFloat("_GapLength", 7f);
        });
        Make("M_Paper_TargetLine", "Paper/InkLine", null, m =>
        {
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_RimColor", Ink);
            m.SetFloat("_RimWidth", 0.28f);
            m.SetFloat("_DashSpace", 0f);
            m.SetFloat("_DashLength", 0.5f);
            m.SetFloat("_GapLength", 0.3f);
            m.SetFloat("_DashScroll", 0.4f);
        });

        Make("M_Paper_Backdrop", "Paper/Backdrop", grain, m =>
        {
            m.SetColor("_Hill1", Hex("#D3DED8"));
            m.SetColor("_Hill2", Hex("#C5D5CD"));
            m.SetColor("_Hill3", Hex("#B6C9C0"));
            m.SetColor("_EdgeColor", new Color(1f, 1f, 1f, 0.35f));
            m.SetFloat("_Horizon", 0.36f);
        });
        Make("M_Paper_Composite", "Hidden/Paper/Composite", null, m => { });
    }

    static void SetHiddenPath(Material m, float alpha)
    {
        m.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
        m.SetColor("_SideColor", Hex("#CFE3DA"));
        m.SetColor("_BottomColor", Hex("#B6CCC3"));
        m.SetFloat("_FluteStrength", 0f);
        m.SetFloat("_TopStyle", 2f);
        m.SetColor("_MarkColor", Ink);
        m.SetFloat("_MarkScale", 0.5f);
    }

    static void SetPiece(Material m, Color color, float pattern, float strength, float spacing, float alpha)
    {
        color.a = alpha;
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Pattern", pattern);
        m.SetFloat("_PatternStrength", strength);
        m.SetFloat("_PatternScale", spacing);
    }

    static void SetGlass(Material m, Color color, float alpha, float pattern, Color edge)
    {
        color.a = alpha;
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Pattern", pattern);
        m.SetFloat("_PatternAlpha", 0.45f);
        m.SetColor("_EdgeColor", edge);
    }

    static void Make(string name, string shaderName, Texture2D grain, System.Action<Material> setup)
    {
        string path = MaterialDir + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("Paper Style: shader not found: " + shaderName);
            return;
        }
        Material material = new Material(shader);
        material.name = name;
        if (grain != null && material.HasProperty("_GrainTex")) material.SetTexture("_GrainTex", grain);
        if (material.HasProperty("_GrainScale")) material.SetFloat("_GrainScale", 1.2f);
        if (material.HasProperty("_GrainStrength")) material.SetFloat("_GrainStrength", shaderName == "Paper/Piece" ? 0.08f : 0.1f);
        if (material.HasProperty("_FluteStrength")) material.SetFloat("_FluteStrength", 0.65f);
        setup(material);
        AssetDatabase.CreateAsset(material, path);
    }

    static void EnsureInkFeature(Material composite)
    {
        if (composite == null) return;
        foreach (string path in RendererPaths)
        {
            UniversalRendererData data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) continue;
            bool present = false;
            foreach (ScriptableRendererFeature existing in data.rendererFeatures)
            {
                if (existing != null && existing.name == InkFeatureName) present = true;
            }
            if (present) continue;

            FullScreenPassRendererFeature feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = InkFeatureName;
            feature.passMaterial = composite;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
            feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
            feature.fetchColorBuffer = true;
            feature.bindDepthStencilAttachment = false;
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);

            // Keep the feature map in sync, as the renderer's Inspector does when adding a feature.
            string guid;
            long localId;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out guid, out localId);
            SerializedObject so = new SerializedObject(data);
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            if (map != null)
            {
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            data.SetDirty();
            EditorUtility.SetDirty(data);
        }
    }

    static Texture2D EnsureGrainTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GrainPath);
        if (existing != null) return existing;

        // Tileable paper grain: value noise at four scales, short fibres and a little speckle.
        const int size = 256;
        System.Random rng = new System.Random(526);
        float[] value = new float[size * size];
        int[] cells = { 8, 16, 32, 64 };
        float[] weights = { 0.45f, 0.28f, 0.17f, 0.10f };
        for (int octave = 0; octave < cells.Length; octave++)
        {
            int c = cells[octave];
            float[] grid = new float[c * c];
            for (int k = 0; k < grid.Length; k++) grid[k] = (float)rng.NextDouble();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = (float)x * c / size;
                    float fy = (float)y * c / size;
                    int x0 = (int)fx;
                    int y0 = (int)fy;
                    float tx = fx - x0;
                    float ty = fy - y0;
                    int x1 = (x0 + 1) % c;
                    int y1 = (y0 + 1) % c;
                    tx = tx * tx * (3f - 2f * tx);
                    ty = ty * ty * (3f - 2f * ty);
                    float a = Mathf.Lerp(grid[y0 * c + x0], grid[y0 * c + x1], tx);
                    float b = Mathf.Lerp(grid[y1 * c + x0], grid[y1 * c + x1], tx);
                    value[y * size + x] += Mathf.Lerp(a, b, ty) * weights[octave];
                }
            }
        }
        for (int fibre = 0; fibre < 260; fibre++)
        {
            float x = rng.Next(size);
            float y = rng.Next(size);
            float angle = (float)(rng.NextDouble() * Mathf.PI);
            int length = 5 + rng.Next(16);
            float strength = (rng.NextDouble() < 0.5 ? -1f : 1f) * (0.04f + 0.06f * (float)rng.NextDouble());
            for (int t = 0; t < length; t++)
            {
                int px = (((int)(x + Mathf.Cos(angle) * t)) % size + size) % size;
                int py = (((int)(y + Mathf.Sin(angle) * t)) % size + size) % size;
                value[py * size + px] += strength;
            }
        }
        for (int k = 0; k < value.Length; k++) value[k] += ((float)rng.NextDouble() - 0.5f) * 0.05f;

        double mean = 0;
        foreach (float v in value) mean += v;
        mean /= value.Length;
        double variance = 0;
        foreach (float v in value) variance += (v - mean) * (v - mean);
        float deviation = (float)System.Math.Sqrt(variance / value.Length);

        Color32[] pixels = new Color32[value.Length];
        for (int k = 0; k < value.Length; k++)
        {
            float n = 0.5f + (value[k] - (float)mean) / (5f * deviation);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(n * 255f), 0, 255);
            pixels[k] = new Color32(b, b, b, 255);
        }
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        texture.SetPixels32(pixels);
        texture.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(GrainPath));
        File.WriteAllBytes(GrainPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(GrainPath);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(GrainPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(GrainPath);
    }

    static Color Hex(string hex)
    {
        Color color;
        ColorUtility.TryParseHtmlString(hex, out color);
        return color;
    }
}
