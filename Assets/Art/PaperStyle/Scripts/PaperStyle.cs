using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Turns on the Cross Section paper look for this scene.
/// - Sets _PaperStyleOn, which the "Paper Ink" renderer feature reads (ink outlines + 2D drop shadow).
///   Scenes without a PaperStyle keep their old look.
/// - Hands each camera's near/far and projection to that pass (a full-screen blit can't rely on them).
/// - Draws the paper backdrop (sky, clouds, hills) behind everything for each camera. It is drawn per
///   camera instead of being placed in the level, so it works in the front view, the Q/E side views,
///   the back view, the 3D view and the aim view, and DepthFader never hides it.
/// Added by Tools > Paper Style > Apply to Open Scene.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class PaperStyle : MonoBehaviour
{
    [Tooltip("Sky, clouds and hills drawn behind the level (shader Paper/Backdrop).")]
    public Material backdrop;

    [Range(0f, 1f), Tooltip("Strength of the ink outlines, 2D drop shadow and vignette. 0 turns them off.")]
    public float ink = 1f;

    static readonly int StyleOnId = Shader.PropertyToID("_PaperStyleOn");
    static readonly int CameraParamsId = Shader.PropertyToID("_PaperCameraParams");
    static readonly int CameraForwardId = Shader.PropertyToID("_PaperCameraForward");
    static Mesh quad;

    void OnEnable()
    {
        Shader.SetGlobalFloat(StyleOnId, ink);
        RenderPipelineManager.beginCameraRendering += BeginCamera;
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(StyleOnId, 0f);
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) Shader.SetGlobalFloat(StyleOnId, ink);
    }

    void BeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam == null) return;
        Shader.SetGlobalVector(CameraParamsId, new Vector4(cam.nearClipPlane, cam.farClipPlane, cam.orthographic ? 1f : 0f, 0f));
        Shader.SetGlobalVector(CameraForwardId, cam.transform.forward);
        DrawBackdrop(cam);
    }

    void DrawBackdrop(Camera cam)
    {
        if (backdrop == null) return;
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        if (quad == null) quad = MakeQuad();

        RenderParams rp = new RenderParams(backdrop);
        rp.camera = cam;
        rp.worldBounds = new Bounds(cam.transform.position, Vector3.one * 100000f);
        rp.shadowCastingMode = ShadowCastingMode.Off;
        rp.receiveShadows = false;
        rp.layer = gameObject.layer;
        Graphics.RenderMesh(rp, quad, 0, Matrix4x4.identity);
    }

    static Mesh MakeQuad()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Paper Backdrop";
        mesh.hideFlags = HideFlags.HideAndDontSave;
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return mesh;
    }
}
