using UnityEngine;
using UnityEngine.Rendering;

public class ColorWipe : MonoBehaviour
{
    public float wipeTime = 0.3f;
    public float settleTime = 0.18f;
    public float settleSquash = 0.05f;

    PieceControlManager colors;
    CutController cut;
    Renderer watched;
    Material before;
    int pressedFrame = -1;

    CuttablePiece piece;
    Renderer body;
    Material oldMaterial;
    Material newMaterial;
    float clock = -1f;
    Mesh upper;
    Mesh lower;

    void Start()
    {
        colors = FindFirstObjectByType<PieceControlManager>();
        cut = FindFirstObjectByType<CutController>();
        if (colors != null) colors.ColorInput += OnColorInput;
    }

    void OnDestroy()
    {
        if (colors != null) colors.ColorInput -= OnColorInput;
        Finish();
    }

    void OnColorInput()
    {
        if (cut == null || cut.player == null) return;
        watched = cut.player.GetComponent<Renderer>();
        before = watched != null ? watched.sharedMaterial : null;
        pressedFrame = Time.frameCount;
    }

    void LateUpdate()
    {
        if (pressedFrame >= 0)
        {
            if (watched != null && before != null && watched.sharedMaterial != before)
            {
                Begin(watched, before, watched.sharedMaterial);
                pressedFrame = -1;
            }
            else if (Time.frameCount > pressedFrame + 2)
            {
                pressedFrame = -1;
            }
        }

        if (clock < 0f) return;
        if (piece == null || body == null || piece.shape == null || cut == null
            || cut.state != CutController.State.Normal || !body.enabled)
        {
            Finish();
            return;
        }

        clock += Time.deltaTime;
        if (clock >= wipeTime + settleTime)
        {
            Finish();
            return;
        }

        ConvexShape shape = piece.shape;
        Bounds bounds = shape.GetBounds();
        Matrix4x4 settle = body.localToWorldMatrix;
        if (clock > wipeTime)
        {
            float s = (clock - wipeTime) / settleTime;
            float squash = Mathf.Sin(s * Mathf.PI) * settleSquash;
            Vector3 foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            settle *= Matrix4x4.Translate(foot) * Matrix4x4.Scale(new Vector3(1f + squash, 1f - squash, 1f + squash)) * Matrix4x4.Translate(-foot);
        }

        body.forceRenderingOff = true;
        float covered = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / wipeTime));
        float line = Mathf.Lerp(bounds.max.y, bounds.min.y, covered);
        ConvexShape first;
        ConvexShape second;
        Release();
        if (covered > 0f && covered < 1f && shape.Cut(Vector3.up, line, out first, out second))
        {
            bool firstOnTop = first.GetBounds().center.y > second.GetBounds().center.y;
            upper = (firstOnTop ? first : second).MakeMesh();
            lower = (firstOnTop ? second : first).MakeMesh();
            Draw(upper, newMaterial, settle, bounds);
            Draw(lower, oldMaterial, settle, bounds);
        }
        else
        {
            upper = shape.MakeMesh();
            Draw(upper, covered >= 1f ? newMaterial : oldMaterial, settle, bounds);
        }
    }

    void Draw(Mesh mesh, Material material, Matrix4x4 matrix, Bounds bounds)
    {
        if (mesh == null || material == null) return;
        RenderParams draw = new RenderParams(material);
        draw.layer = body.gameObject.layer;
        draw.shadowCastingMode = ShadowCastingMode.On;
        draw.receiveShadows = true;
        draw.worldBounds = new Bounds(body.bounds.center, body.bounds.size * 2f + Vector3.one);
        Graphics.RenderMesh(draw, mesh, 0, matrix);
    }

    void Begin(Renderer target, Material from, Material to)
    {
        Finish();
        body = target;
        piece = target.GetComponent<CuttablePiece>();
        oldMaterial = from;
        newMaterial = to;
        clock = 0f;
    }

    void Release()
    {
        if (upper != null) Destroy(upper);
        if (lower != null) Destroy(lower);
        upper = null;
        lower = null;
    }

    void Finish()
    {
        if (body != null) body.forceRenderingOff = false;
        clock = -1f;
        Release();
    }
}
