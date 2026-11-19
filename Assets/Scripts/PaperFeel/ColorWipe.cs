using UnityEngine;
using UnityEngine.Rendering;

public class ColorWipe : MonoBehaviour
{
    public float wipeTime = 0.3f;
    public float settleTime = 0.18f;
    public float settleSquash = 0.05f;
    public float jamTime = 0.42f;
    public float jamDepth = 0.25f;
    public float jamShiver = 0.03f;

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
    bool jammed;
    Mesh upper;
    Mesh lower;

    void Start()
    {
        colors = FindFirstObjectByType<PieceControlManager>();
        cut = FindFirstObjectByType<CutController>();
        if (colors != null)
        {
            colors.ColorInput += OnColorInput;
            colors.ColorRefused += OnColorRefused;
        }
    }

    void OnDestroy()
    {
        if (colors != null)
        {
            colors.ColorInput -= OnColorInput;
            colors.ColorRefused -= OnColorRefused;
        }
        Finish();
    }

    void OnColorRefused(CuttablePiece target, ColorObstacle glass)
    {
        pressedFrame = -1;
        Renderer rend = target.GetComponent<Renderer>();
        PieceColor color = target.GetComponent<PieceColor>();
        if (rend == null || color == null) return;
        Material other = color.currentColor == PieceColor.PieceColorType.Blue ? colors.orangeMaterial : colors.blueMaterial;
        if (other == null) return;
        Begin(rend, rend.sharedMaterial, other);
        jammed = true;
    }

    float JamCover(float t)
    {
        if (t < 0.35f)
        {
            float e = t / 0.35f;
            return jamDepth * (1f - (1f - e) * (1f - e));
        }
        if (t < 0.6f)
        {
            float s = (t - 0.35f) / 0.25f;
            return jamDepth + jamShiver * Mathf.Sin(s * Mathf.PI * 4f) * (1f - s);
        }
        float back = (t - 0.6f) / 0.4f;
        return jamDepth * (1f - back * back);
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
        if (clock >= (jammed ? jamTime : wipeTime + settleTime))
        {
            Finish();
            return;
        }

        ConvexShape shape = piece.shape;
        Bounds bounds = shape.GetBounds();
        Matrix4x4 settle = body.localToWorldMatrix;
        if (!jammed && clock > wipeTime)
        {
            float s = (clock - wipeTime) / settleTime;
            float squash = Mathf.Sin(s * Mathf.PI) * settleSquash;
            Vector3 foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            settle *= Matrix4x4.Translate(foot) * Matrix4x4.Scale(new Vector3(1f + squash, 1f - squash, 1f + squash)) * Matrix4x4.Translate(-foot);
        }

        body.forceRenderingOff = true;
        float covered = jammed
            ? Mathf.Clamp01(JamCover(clock / jamTime))
            : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / wipeTime));
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
        jammed = false;
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
