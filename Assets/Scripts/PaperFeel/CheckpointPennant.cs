using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The paper pennant that marks a checkpoint or exit in paper-style scenes: a checkered ink-and-cardstock base on the floor,
/// an ink pole at the back of the path and a cardstock pennant with a green stripe. When the player reaches it,
/// the pennant flips over to its green side with a white check and throws a few paper scraps.
/// ExitZone puts one up in every scene with a PaperStyle object, and hides its old glass box and label.
/// The pennant has no colliders: the ExitZone's trigger box still decides when the player gets there.
/// </summary>
public class CheckpointPennant : MonoBehaviour
{
    [Header("Materials")]
    public Material cardstock;
    public Material ink;
    public Material green;
    [Tooltip("The light squares of the checkered base (the dark squares and rim use Ink). With Checker Size 0, the whole base in this material.")]
    [UnityEngine.Serialization.FormerlySerializedAs("cuttingMat")]
    public Material baseTile;

    [Header("Shape (world units)")]
    public float poleHeight = 3.9f;
    public float poleWidth = 0.12f;
    public float flagLength = 1.9f;
    public float flagHeight = 1.25f;
    public float stripeWidth = 0.34f;
    [Tooltip("How far in from the back edge of the checkpoint the pole stands. The back is the side away from the 2D camera.")]
    public float poleInset = 0.3f;

    [Header("Base")]
    [Tooltip("Size of the checker squares on the base, in world units. 0 makes a plain tile.")]
    public float checkerSize = 0.42f;
    [Tooltip("Width of the ink rim around the checkered base.")]
    public float baseRim = 0.08f;

    [Header("Motion")]
    public float swayAngle = 15f;
    public float swaySpeed = 1.7f;
    public float flipTime = 0.6f;
    public int scraps = 12;

    const string PrefabPath = "Paper/CheckpointPennant";
    const float clothThickness = 0.05f;
    static readonly Color fallbackGreen = new Color(0.184f, 0.478f, 0.333f, 1f);

    Renderer box;
    Transform swing;
    MeshRenderer cloth, stripe;
    GameObject check;
    readonly List<Renderer> parts = new List<Renderer>();
    bool reached, hidden;
    float flipClock = -1f;
    float phase;
    int layer;

    /// <summary>Puts a pennant on an exit. Returns null if the prefab is missing.</summary>
    public static CheckpointPennant Attach(ExitZone zone)
    {
        CheckpointPennant prefab = Resources.Load<CheckpointPennant>(PrefabPath);
        if (prefab == null) return null;
        CheckpointPennant pennant = Instantiate(prefab, zone.transform.parent);
        pennant.name = zone.name + " Pennant";
        pennant.Build(zone);
        return pennant;
    }

    /// <summary>Turns the pennant green. With celebrate, it flips over and throws paper scraps first.</summary>
    public void Reach(bool celebrate)
    {
        if (reached) return;
        reached = true;
        if (!celebrate || swing == null)
        {
            ShowGreenSide();
            return;
        }
        flipClock = 0f;
        ThrowScraps();
    }

    void Build(ExitZone zone)
    {
        layer = zone.gameObject.layer;
        box = zone.GetComponent<Renderer>();
        if (box != null) box.enabled = false;
        Bounds area = zone.GetComponent<Collider>().bounds;

        // Face the way the player walks: from the room's spawn to this exit, or +x outside checkpoint rooms.
        Vector3 ahead = Vector3.right;
        int room = -1;
        CheckpointManager checkpoints = zone.levelManager != null ? zone.levelManager.checkpoints : null;
        if (checkpoints != null && checkpoints.rooms != null)
        {
            room = System.Array.FindIndex(checkpoints.rooms, r => r.exit == zone);
            if (room >= 0 && checkpoints.rooms[room].spawn != null)
            {
                Vector3 toExit = zone.transform.position - checkpoints.rooms[room].spawn.position;
                toExit.y = 0f;
                if (toExit.sqrMagnitude > 0.01f) ahead = toExit.normalized;
            }
        }
        Vector3 back = Vector3.Cross(ahead, Vector3.up);
        transform.SetPositionAndRotation(new Vector3(area.center.x, area.min.y, area.center.z), Quaternion.LookRotation(back, Vector3.up));
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);

        float along = Mathf.Abs(ahead.x) * area.size.x + Mathf.Abs(ahead.z) * area.size.z;
        float depth = Mathf.Abs(back.x) * area.size.x + Mathf.Abs(back.z) * area.size.z;
        float poleZ = depth * 0.5f - poleInset;

        BuildBase(along, depth);
        Part("Pole", ink, ConvexShape.MakeBox(new Vector3(poleWidth, poleHeight, poleWidth)).MoveBy(new Vector3(0f, poleHeight * 0.5f, poleZ)), transform);
        Part("Knob", cardstock, ConvexShape.MakeBox(Vector3.one * 0.28f).MoveBy(new Vector3(0f, poleHeight + 0.12f, poleZ)), transform);

        swing = new GameObject("Flag").transform;
        swing.SetParent(transform, false);
        swing.localPosition = new Vector3(0f, poleHeight - 0.1f, poleZ);
        swing.gameObject.layer = layer;

        float x0 = poleWidth * 0.5f;
        Vector2 top = new Vector2(x0, 0f);
        Vector2 tip = new Vector2(x0 + flagLength, -flagHeight * 0.5f);
        Vector2 bottom = new Vector2(x0, -flagHeight);
        cloth = Part("Cloth", cardstock, Prism(clothThickness, top, tip, bottom), swing);
        float sx = x0 + stripeWidth;
        stripe = Part("Stripe", green, Prism(clothThickness * 1.6f, top, new Vector2(sx, EdgeY(top, tip, sx)),
            new Vector2(sx, EdgeY(bottom, tip, sx)), bottom), swing);

        check = new GameObject("Check");
        check.transform.SetParent(swing, false);
        check.layer = layer;
        float cx = x0 + flagLength * 0.42f;
        float cy = -flagHeight * 0.5f;
        float s = flagHeight;
        Vector2 a = new Vector2(cx - 0.17f * s, cy + 0.02f * s);
        Vector2 b = new Vector2(cx - 0.04f * s, cy - 0.12f * s);
        Vector2 c = new Vector2(cx + 0.22f * s, cy + 0.17f * s);
        Part("Check A", cardstock, Stroke(a, b, 0.09f * s), check.transform);
        Part("Check B", cardstock, Stroke(b, c, 0.09f * s), check.transform);
        check.SetActive(false);

        phase = Random.value * 10f;
        if (room >= 0 && room < checkpoints.CurrentRoom) Reach(false);
    }

    void Update()
    {
        if (swing == null) return;
        float t = Time.unscaledTime + phase;
        float turn = Mathf.Sin(t * swaySpeed) * swayAngle;
        float droop = Mathf.Sin(t * swaySpeed * 1.37f) * 2f;
        float pop = 1f;
        if (flipClock >= 0f)
        {
            flipClock += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(flipClock / flipTime);
            turn += 360f * (1f - (1f - k) * (1f - k));
            pop = 1f + 0.25f * Mathf.Sin(k * Mathf.PI);
            // Halfway round, the pennant shows its green side.
            if (k >= 0.5f && !check.activeSelf) ShowGreenSide();
            if (k >= 1f) flipClock = -1f;
        }
        swing.localRotation = Quaternion.Euler(0f, turn, droop);
        swing.localScale = Vector3.one * pop;
    }

    void LateUpdate()
    {
        // CheckpointManager hides other rooms in the 2D side views by turning their renderers off,
        // including the hidden box of this exit. Follow it.
        bool hide = box != null && box.forceRenderingOff;
        if (hide == hidden) return;
        hidden = hide;
        foreach (Renderer part in parts)
        {
            if (part != null) part.forceRenderingOff = hide;
        }
    }

    void ShowGreenSide()
    {
        if (cloth != null) cloth.sharedMaterial = green;
        if (stripe != null) stripe.enabled = false;
        if (check != null) check.SetActive(true);
    }

    void ThrowScraps()
    {
        PaperBits bits = FindAnyObjectByType<PaperBits>();
        if (bits == null || scraps <= 0) return;
        Color greenColor = green != null ? green.GetColor("_TopColor") : fallbackGreen;
        Sprite[] pool =
        {
            PaperFeel.PaintScrap(81, PaperFeel.Cardstock), PaperFeel.PaintScrap(82, greenColor),
            PaperFeel.PaintScrap(83, PaperFeel.Cardstock), PaperFeel.PaintScrap(84, greenColor)
        };
        Vector3 from = swing.TransformPoint(new Vector3(flagLength * 0.5f, -flagHeight * 0.5f, 0f));
        float floor = transform.position.y + 0.06f;
        for (int i = 0; i < scraps; i++)
        {
            Vector3 velocity = Random.insideUnitSphere * 1.6f + Vector3.up * Random.Range(2.5f, 4f);
            bits.Spawn(pool[i % pool.Length], from, velocity, Random.Range(1.1f, 1.6f), Random.Range(1f, 1.5f), floor);
        }
    }

    // A checkerboard of ink and cardstock with an ink rim: the finish-line sign, in the paper palette.
    void BuildBase(float along, float depth)
    {
        const float height = 0.05f;
        if (checkerSize <= 0f || ink == null)
        {
            Part("Base", baseTile, ConvexShape.MakeBox(new Vector3(along, height, depth)).MoveBy(new Vector3(0f, height * 0.5f, 0f)), transform);
            return;
        }
        Part("Base", ink, ConvexShape.MakeBox(new Vector3(along, height, depth)).MoveBy(new Vector3(0f, height * 0.5f, 0f)), transform);

        float innerX = along - 2f * baseRim;
        float innerZ = depth - 2f * baseRim;
        int countX = Mathf.Max(2, Mathf.RoundToInt(innerX / checkerSize));
        int countZ = Mathf.Max(2, Mathf.RoundToInt(innerZ / checkerSize));
        float sizeX = innerX / countX;
        float sizeZ = innerZ / countZ;
        float top = height + 0.004f;
        var squares = new List<CombineInstance>();
        for (int x = 0; x < countX; x++)
        {
            for (int z = 0; z < countZ; z++)
            {
                if ((x + z) % 2 != 0) continue;
                Vector3 center = new Vector3(-innerX * 0.5f + (x + 0.5f) * sizeX, top * 0.5f, -innerZ * 0.5f + (z + 0.5f) * sizeZ);
                Mesh square = ConvexShape.MakeBox(new Vector3(sizeX, top, sizeZ)).MoveBy(center).MakeMesh();
                squares.Add(new CombineInstance { mesh = square, transform = Matrix4x4.identity });
            }
        }
        Mesh combined = new Mesh { name = "CheckerSquares" };
        combined.CombineMeshes(squares.ToArray(), true, true);
        foreach (CombineInstance square in squares) Destroy(square.mesh);
        PartMesh("Checker", baseTile, combined, transform);
    }

    MeshRenderer Part(string partName, Material material, ConvexShape shape, Transform parent)
    {
        return PartMesh(partName, material, shape.MakeMesh(), parent);
    }

    MeshRenderer PartMesh(string partName, Material material, Mesh mesh, Transform parent)
    {
        GameObject go = new GameObject(partName);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        parts.Add(meshRenderer);
        return meshRenderer;
    }

    // A flat shape in the flag's plane, given some thickness across it.
    static ConvexShape Prism(float thickness, params Vector2[] outline)
    {
        float half = thickness * 0.5f;
        int count = outline.Length;
        Vector3[] front = new Vector3[count];
        Vector3[] rear = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            front[i] = new Vector3(outline[i].x, outline[i].y, -half);
            rear[count - 1 - i] = new Vector3(outline[i].x, outline[i].y, half);
        }
        List<Vector3[]> faces = new List<Vector3[]> { front, rear };
        for (int i = 0; i < count; i++)
        {
            Vector2 p = outline[i];
            Vector2 q = outline[(i + 1) % count];
            faces.Add(new[] { new Vector3(p.x, p.y, -half), new Vector3(p.x, p.y, half), new Vector3(q.x, q.y, half), new Vector3(q.x, q.y, -half) });
        }
        return new ConvexShape(faces);
    }

    // A check-mark stroke that pokes through both faces of the flag.
    static ConvexShape Stroke(Vector2 from, Vector2 to, float width)
    {
        Vector2 across = new Vector2(-(to - from).y, (to - from).x).normalized * width * 0.5f;
        Vector2 along = (to - from).normalized * width * 0.5f;
        return Prism(clothThickness * 2.2f, from - along - across, to + along - across, to + along + across, from - along + across);
    }

    static float EdgeY(Vector2 from, Vector2 to, float x)
    {
        return Mathf.Lerp(from.y, to.y, Mathf.InverseLerp(from.x, to.x, x));
    }
}
