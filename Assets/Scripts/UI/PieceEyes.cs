using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1000)]
public class PieceEyes : MonoBehaviour
{
    public Color eyeColor = new Color(0.09f, 0.11f, 0.16f, 1f);
    public Color shineColor = Color.white;
    public float blinkTime = 0.16f;
    public float sleepDelay = 0.25f;
    public float hopTime = 0.16f;
    public float fadeTime = 0.1f;
    public float switchMargin = 0.12f;
    public float flatPenalty = 0.5f;
    public float raise = 0.12f;
    public float fill = 0.6f;
    public float maxScale = 1f;
    public float minScale = 0.3f;
    public float surfaceOffset = 0.015f;
    public float lookReach = 0.7f;
    public float lookRange = 1.5f;
    public float lookSpeed = 10f;

    const float paintDensity = 2f;
    const float openAbove = 0.25f;
    const float flatScale = 0.12f;
    const float minFacing = 0.05f;
    const float shineLift = 0.004f;
    const float probeGap = 0.08f;
    const float probeRadius = 0.05f;
    const int fitSteps = 8;

    static readonly Vector2 pairSize = new Vector2(96f, 56f);
    static readonly Vector2 eyeLeft = new Vector2(24f, 28f);
    static readonly Vector2 eyeRadius = new Vector2(14f, 22f);
    static readonly Vector2 shineOffset = new Vector2(5f, 9f);
    const float shineRadius = 5f;
    const float lidLift = 9f;
    const float lidRadius = 16f;
    const float lidWidth = 5.2f;
    const float lidFrom = -150f;
    const float lidTo = -30f;

    static readonly int colorId = Shader.PropertyToID("_BaseColor");

    struct Face
    {
        public Vector3 center;
        public Vector3 normal;
        public Vector3[] points;
    }

    class Pair
    {
        public Transform root;
        public Transform openEyes;
        public SpriteRenderer eyes;
        public SpriteRenderer shine;
        public SpriteRenderer lids;
        public Object owner;
        public Transform frame;
        public Renderer body;
        public ConvexShape shape;
        public readonly List<Face> faces = new List<Face>();
        public int face = -1;
        public int nextFace = -1;
        public float hop = 1f;
        public float open = 1f;
        public float alpha;
        public bool hasLook;
        public Vector3 lookAt;
        public Vector2 look;
    }

    private CutController cut;
    private CameraRig rig;
    private GameObject host;
    private Pair you;
    private Pair rest;
    private bool paper;
    private readonly List<Sprite> sprites = new List<Sprite>();
    private readonly List<Vector2> flat = new List<Vector2>();
    private readonly Dictionary<GameObject, ConvexShape> previews = new Dictionary<GameObject, ConvexShape>();

    void Awake()
    {
        cut = FindFirstObjectByType<CutController>();
        rig = FindFirstObjectByType<CameraRig>();
        paper = PaperFeel.IsOn;
        host = new GameObject("Piece Eyes");
        Sprite eyes = Keep(PaintEyes());
        Sprite shine = Keep(PaintShine());
        Sprite lids = Keep(PaintLids());
        you = MakePair("You", eyes, shine, lids);
        rest = MakePair("Last Piece", eyes, shine, lids);
    }

    void OnEnable()
    {
        if (cut != null) cut.PieceSwitchInput += OnSwitch;
        if (host != null) host.SetActive(true);
    }

    void OnDisable()
    {
        if (cut != null) cut.PieceSwitchInput -= OnSwitch;
        if (host != null) host.SetActive(false);
    }

    void OnDestroy()
    {
        if (host != null) Destroy(host);
        foreach (Sprite sprite in sprites)
        {
            if (sprite == null) continue;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
    }

    void OnSwitch()
    {
        if (cut.state != CutController.State.Normal || cut.LastLeftover == null) return;
        you.open = 0f;
        rest.open = 1f;
    }

    void LateUpdate()
    {
        Camera cam = rig != null ? rig.cam : null;
        float deltaTime = Time.unscaledDeltaTime;
        GameObject hovered = paper ? HoveredPreview() : null;
        if (hovered != null)
        {
            Track(you, hovered, hovered.transform, hovered.GetComponent<Renderer>(), PreviewShape(hovered), 1f, cam, deltaTime);
        }
        else
        {
            if (previews.Count > 0) previews.Clear();
            CuttablePiece player = cut != null ? cut.player : null;
            TrackPiece(you, player, 1f, cam, deltaTime);
        }
        TrackPiece(rest, cut != null ? cut.LastLeftover : null, 0f, cam, deltaTime);
    }

    void TrackPiece(Pair pair, CuttablePiece piece, float target, Camera cam, float deltaTime)
    {
        if (piece == null)
        {
            Track(pair, null, null, null, null, target, cam, deltaTime);
            return;
        }
        Renderer body = ReferenceEquals(piece, pair.owner) ? pair.body : piece.GetComponent<Renderer>();
        Track(pair, piece, piece.transform, body, piece.shape, target, cam, deltaTime);
    }

    void Track(Pair pair, Object owner, Transform frame, Renderer body, ConvexShape shape, float target, Camera cam, float deltaTime)
    {
        if (!ReferenceEquals(owner, pair.owner))
        {
            pair.owner = owner;
            pair.frame = frame;
            pair.body = body;
            pair.shape = null;
            pair.alpha = 0f;
            pair.look = Vector2.zero;
            pair.open = target > 0f ? 1f : 1f + sleepDelay / blinkTime;
        }

        bool wasShown = pair.root.gameObject.activeSelf;
        int best = -1;
        if (cam != null && owner != null && pair.frame != null && pair.body != null && pair.body.enabled && pair.frame.gameObject.activeInHierarchy)
        {
            if (shape != pair.shape) ReadFaces(pair, shape);
            best = PickFace(pair, cam);
        }
        pair.open = Mathf.MoveTowards(pair.open, target, deltaTime / blinkTime);
        if (best < 0)
        {
            pair.alpha = 0f;
            if (wasShown) pair.root.gameObject.SetActive(false);
            return;
        }

        if (!wasShown) pair.root.gameObject.SetActive(true);
        if (!wasShown || pair.face < 0)
        {
            pair.face = best;
            pair.nextFace = best;
            pair.hop = 1f;
        }
        else if (pair.hop >= 1f && best != pair.face)
        {
            pair.nextFace = best;
            pair.hop = 0f;
        }
        if (pair.hop < 1f)
        {
            pair.hop = Mathf.Min(1f, pair.hop + deltaTime / hopTime);
            if (pair.hop >= 0.5f) pair.face = pair.nextFace;
        }

        pair.alpha = Mathf.MoveTowards(pair.alpha, BodyAlpha(pair.body), deltaTime / fadeTime);
        pair.hasLook = paper && target > 0f && FindLook(pair, cam, out pair.lookAt);
        Place(pair, cam, Mathf.Abs(1f - 2f * pair.hop), deltaTime);
        Show(pair);
    }

    GameObject HoveredPreview()
    {
        if (cut == null || cut.state != CutController.State.Choosing) return null;
        int index = cut.HoveredPiece;
        if (index != 0 && index != 1) return null;
        return cut.GetChoicePiece(index);
    }

    ConvexShape PreviewShape(GameObject preview)
    {
        ConvexShape shape;
        if (previews.TryGetValue(preview, out shape)) return shape;
        List<Vector3[]> faces = new List<Vector3[]>();
        MeshFilter filter = preview.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh != null)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            List<Vector3> current = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (current.Count > 0 && normals[i] != normals[i - 1])
                {
                    if (current.Count >= 3) faces.Add(current.ToArray());
                    current.Clear();
                }
                current.Add(vertices[i]);
            }
            if (current.Count >= 3) faces.Add(current.ToArray());
        }
        shape = new ConvexShape(faces);
        previews[preview] = shape;
        return shape;
    }

    bool FindLook(Pair pair, Camera cam, out Vector3 point)
    {
        point = Vector3.zero;
        if (cut == null || cam == null || pair.body == null || cut.state == CutController.State.Normal) return false;
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Plane plane = new Plane(-cam.transform.forward, pair.body.bounds.center);
        float distance;
        if (!plane.Raycast(ray, out distance)) return false;
        point = ray.GetPoint(distance);
        return true;
    }

    void ReadFaces(Pair pair, ConvexShape shape)
    {
        pair.shape = shape;
        pair.faces.Clear();
        pair.face = -1;
        pair.hop = 1f;
        if (pair.shape == null || pair.shape.faces == null || pair.shape.faces.Count == 0) return;

        Vector3 middle = pair.shape.GetCenter();
        foreach (Vector3[] points in pair.shape.faces)
        {
            if (points == null || points.Length < 3) continue;
            Vector3 center = Vector3.zero;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < points.Length; i++)
            {
                center += points[i];
                normal += Vector3.Cross(points[i], points[(i + 1) % points.Length]);
            }
            if (normal.sqrMagnitude < 1e-8f) continue;
            center /= points.Length;
            normal.Normalize();
            if (Vector3.Dot(normal, center - middle) < 0f) normal = -normal;

            Face face;
            face.center = center;
            face.normal = normal;
            face.points = points;
            pair.faces.Add(face);
        }
    }

    int PickFace(Pair pair, Camera cam)
    {
        Transform body = pair.frame;
        int best = -1;
        int fallback = -1;
        float bestScore = minFacing;
        float fallbackScore = minFacing;
        float keepScore = -1f;
        for (int i = 0; i < pair.faces.Count; i++)
        {
            float score = Facing(pair.faces[i], body, cam);
            if (score <= minFacing) continue;
            if (score > fallbackScore)
            {
                fallbackScore = score;
                fallback = i;
            }
            if (Covered(pair.faces[i], body)) continue;
            if (i == pair.face) keepScore = score;
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        if (best < 0) return fallback;
        if (pair.face >= 0 && keepScore > minFacing && keepScore >= bestScore - switchMargin) return pair.face;
        return best;
    }

    static bool Covered(Face face, Transform body)
    {
        Vector3 probe = body.TransformPoint(face.center) + body.TransformDirection(face.normal).normalized * probeGap;
        return Physics.CheckSphere(probe, probeRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    float Facing(Face face, Transform body, Camera cam)
    {
        Vector3 normal = body.TransformDirection(face.normal);
        Vector3 toCamera = cam.orthographic
            ? -cam.transform.forward
            : (cam.transform.position - body.TransformPoint(face.center)).normalized;
        return Vector3.Dot(normal, toCamera) * (1f - flatPenalty * Mathf.Abs(normal.y));
    }

    void Place(Pair pair, Camera cam, float squash, float deltaTime)
    {
        Face face = pair.faces[pair.face];
        Transform body = pair.frame;
        Vector3 normal = body.TransformDirection(face.normal).normalized;
        Vector3 center = body.TransformPoint(face.center);
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (up.sqrMagnitude < 0.04f) up = Vector3.ProjectOnPlane(cam.transform.up, normal);
        up.Normalize();
        Vector3 right = Vector3.Cross(normal, up);

        flat.Clear();
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 point in face.points)
        {
            Vector3 offset = body.TransformPoint(point) - center;
            Vector2 local = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, up));
            flat.Add(local);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }

        Vector2 span = max - min;
        Vector2 size = pairSize / 100f;
        float scale = Mathf.Min(maxScale, Mathf.Min(fill * span.x / size.x, fill * span.y / size.y));
        Vector2 spot = (min + max) * 0.5f;
        float highest = max.y - size.y * scale * 0.5f - span.y * 0.05f;
        spot.y = Mathf.Max(spot.y, Mathf.Min(spot.y + raise * span.y, highest));
        for (int i = 0; i < fitSteps && !Fits(spot, size * scale * 0.5f); i++)
        {
            scale *= 0.88f;
            spot *= 0.7f;
        }
        scale = Mathf.Max(scale, minScale);

        Vector2 wanted = Vector2.zero;
        if (pair.hasLook)
        {
            Vector3 toward = pair.lookAt - (center + right * spot.x + up * spot.y);
            Vector2 local = new Vector2(Vector3.Dot(toward, right), Vector3.Dot(toward, up));
            float distance = local.magnitude;
            if (distance > 0.001f) wanted = local / distance * Mathf.Clamp01(distance / lookRange);
        }
        pair.look = Vector2.Lerp(pair.look, wanted, 1f - Mathf.Exp(-lookSpeed * deltaTime));
        Vector2 half = size * scale * 0.5f;
        Vector2 shift = pair.look * size.y * scale * lookReach;
        for (int i = 0; i < 4 && shift.sqrMagnitude > 1e-6f && !Fits(spot + shift, half); i++) shift *= 0.5f;
        if (shift.sqrMagnitude > 1e-6f && Fits(spot + shift, half)) spot += shift;

        Vector3 position = center + right * spot.x + up * spot.y + normal * surfaceOffset;
        pair.root.SetPositionAndRotation(position, Quaternion.LookRotation(-normal, up));
        pair.root.localScale = new Vector3(scale, scale * squash, scale);
    }

    bool Fits(Vector2 spot, Vector2 half)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = spot + new Vector2((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y);
            if (!Inside(corner)) return false;
        }
        return true;
    }

    bool Inside(Vector2 point)
    {
        int side = 0;
        for (int i = 0; i < flat.Count; i++)
        {
            Vector2 a = flat[i];
            Vector2 b = flat[(i + 1) % flat.Count];
            float cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
            int sign = cross > 0f ? 1 : (cross < 0f ? -1 : 0);
            if (sign == 0) continue;
            if (side == 0) side = sign;
            else if (sign != side) return false;
        }
        return true;
    }

    void Show(Pair pair)
    {
        float open = Mathf.Clamp01(pair.open);
        bool isOpen = open > openAbove;
        pair.eyes.enabled = isOpen;
        pair.shine.enabled = isOpen;
        pair.lids.enabled = !isOpen;
        float height = isOpen ? Mathf.Lerp(flatScale, 1f, Mathf.SmoothStep(0f, 1f, (open - openAbove) / (1f - openAbove))) : 1f;
        pair.openEyes.localScale = new Vector3(1f, height, 1f);

        Color tint = eyeColor;
        tint.a *= pair.alpha;
        pair.eyes.color = tint;
        pair.lids.color = tint;
        Color glint = shineColor;
        glint.a *= pair.alpha;
        pair.shine.color = glint;
    }

    static float BodyAlpha(Renderer body)
    {
        Material material = body.sharedMaterial;
        if (material == null || !material.HasProperty(colorId)) return 1f;
        return material.GetColor(colorId).a;
    }

    Pair MakePair(string name, Sprite eyes, Sprite shine, Sprite lids)
    {
        Pair pair = new Pair();
        pair.root = new GameObject(name).transform;
        pair.root.SetParent(host.transform, false);
        pair.eyes = NewSprite("Open", pair.root, eyes);
        pair.openEyes = pair.eyes.transform;
        pair.shine = NewSprite("Shine", pair.openEyes, shine);
        pair.shine.transform.localPosition = new Vector3(0f, 0f, -shineLift);
        pair.lids = NewSprite("Closed", pair.root, lids);
        pair.root.gameObject.SetActive(false);
        return pair;
    }

    static SpriteRenderer NewSprite(string name, Transform parent, Sprite sprite)
    {
        SpriteRenderer renderer = new GameObject(name).AddComponent<SpriteRenderer>();
        renderer.transform.SetParent(parent, false);
        renderer.sprite = sprite;
        return renderer;
    }

    Sprite Keep(Sprite sprite)
    {
        sprites.Add(sprite);
        return sprite;
    }

    static Vector2 Mirror(Vector2 p)
    {
        return new Vector2(Mathf.Min(p.x, pairSize.x - p.x), p.y);
    }

    static Sprite PaintEyes()
    {
        return UiShapes.Paint(pairSize, paintDensity, Vector4.zero, p =>
        {
            Vector2 q = Mirror(p) - eyeLeft;
            float x = q.x / eyeRadius.x;
            float y = q.y / eyeRadius.y;
            return x * x + y * y <= 1f ? Color.white : Color.clear;
        }, true);
    }

    static Sprite PaintShine()
    {
        Vector2 eyeRight = new Vector2(pairSize.x - eyeLeft.x, eyeLeft.y);
        return UiShapes.Paint(pairSize, paintDensity, Vector4.zero, p =>
        {
            float d = Mathf.Min(Vector2.Distance(p, eyeLeft + shineOffset), Vector2.Distance(p, eyeRight + shineOffset));
            return d <= shineRadius ? Color.white : Color.clear;
        }, true);
    }

    static Sprite PaintLids()
    {
        Vector2 center = eyeLeft + Vector2.up * lidLift;
        Vector2 from = center + Turn(lidFrom) * lidRadius;
        Vector2 to = center + Turn(lidTo) * lidRadius;
        return UiShapes.Paint(pairSize, paintDensity, Vector4.zero, p =>
        {
            Vector2 q = Mirror(p);
            Vector2 offset = q - center;
            float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            float d = angle >= lidFrom && angle <= lidTo
                ? Mathf.Abs(offset.magnitude - lidRadius)
                : Mathf.Min(Vector2.Distance(q, from), Vector2.Distance(q, to));
            return d <= lidWidth * 0.5f ? Color.white : Color.clear;
        }, true);
    }

    static Vector2 Turn(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }
}
