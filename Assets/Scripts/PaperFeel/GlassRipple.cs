using System.Collections.Generic;
using UnityEngine;

public class GlassRipple : MonoBehaviour
{
    public float rippleTime = 0.6f;
    public float startSize = 0.6f;
    public float endSize = 2.4f;
    public float ringGap = 0.12f;

    class Ring
    {
        public SpriteRenderer renderer;
        public Vector3 position;
        public Vector3 normal;
        public float clock;
        public float delay;
        public float size;
        public bool alive;
    }

    CutController cut;
    Sprite ringSprite;
    readonly List<Ring> rings = new List<Ring>();
    readonly Collider[] hits = new Collider[24];
    HashSet<ColorObstacle> inside = new HashSet<ColorObstacle>();
    HashSet<ColorObstacle> now = new HashSet<ColorObstacle>();
    readonly Dictionary<ColorObstacle, Collider> colliders = new Dictionary<ColorObstacle, Collider>();
    int next;

    void Start()
    {
        cut = FindFirstObjectByType<CutController>();
        ringSprite = PaperFeel.PaintRing();
        for (int i = 0; i < 12; i++)
        {
            SpriteRenderer renderer = new GameObject("Ripple").AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(transform, false);
            renderer.sprite = ringSprite;
            renderer.enabled = false;
            rings.Add(new Ring { renderer = renderer });
        }
    }

    void LateUpdate()
    {
        Watch();
        Animate();
    }

    void Watch()
    {
        if (cut == null || cut.player == null) return;
        CuttablePiece piece = cut.player;
        Collider body = piece.GetComponent<Collider>();
        PieceColor color = piece.GetComponent<PieceColor>();
        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (body == null || color == null || !body.enabled) return;

        Bounds bounds = body.bounds;
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents * 0.96f, hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        now.Clear();
        for (int i = 0; i < count; i++)
        {
            ColorObstacle glass = hits[i].GetComponent<ColorObstacle>();
            if (glass == null || glass.obstacleColor != color.currentColor) continue;
            now.Add(glass);
            colliders[glass] = hits[i];
            if (!inside.Contains(glass))
            {
                Vector3 velocity = rb != null ? rb.linearVelocity : Vector3.zero;
                Splash(glass, hits[i], bounds, velocity, 1f);
            }
        }
        foreach (ColorObstacle glass in inside)
        {
            Collider wall;
            if (glass == null || now.Contains(glass) || !colliders.TryGetValue(glass, out wall) || wall == null) continue;
            Vector3 velocity = rb != null ? rb.linearVelocity : Vector3.zero;
            Splash(glass, wall, bounds, -velocity, 0.7f);
        }
        HashSet<ColorObstacle> swap = inside;
        inside = now;
        now = swap;
    }

    void Splash(ColorObstacle glass, Collider wall, Bounds piece, Vector3 velocity, float strength)
    {
        Bounds box = wall.bounds;
        Vector3 relative = piece.center - box.center;
        Vector3 normal = Vector3.up;
        if (velocity.sqrMagnitude > 1f)
        {
            Vector3 way = -velocity;
            Vector3 size = box.extents + piece.extents;
            Vector3 scaled = new Vector3(way.x / size.x, way.y / size.y, way.z / size.z);
            normal = Dominant(scaled);
        }
        else
        {
            Vector3 scaled = new Vector3(relative.x / box.extents.x, relative.y / box.extents.y, relative.z / box.extents.z);
            normal = Dominant(scaled);
        }

        Vector3 point = box.ClosestPoint(piece.center);
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(normal[axis]) > 0.5f) point[axis] = normal[axis] > 0f ? box.max[axis] : box.min[axis];
        }

        Color tint = PaperFeel.ColorOf(glass.obstacleColor);
        float size2 = Mathf.Max(piece.size.x, piece.size.z) * strength;
        for (int i = 0; i < 2; i++)
        {
            Ring ring = rings[next];
            next = (next + 1) % rings.Count;
            ring.position = point + normal * 0.02f;
            ring.normal = normal;
            ring.clock = 0f;
            ring.delay = i * ringGap;
            ring.size = size2;
            ring.alive = true;
            ring.renderer.color = new Color(tint.r, tint.g, tint.b, 0f);
        }
    }

    static Vector3 Dominant(Vector3 v)
    {
        Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (a.y >= a.x && a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
        if (a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
        return new Vector3(0f, 0f, Mathf.Sign(v.z));
    }

    void Animate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float deltaTime = Time.deltaTime;
        foreach (Ring ring in rings)
        {
            if (!ring.alive) continue;
            ring.clock += deltaTime;
            float t = (ring.clock - ring.delay) / rippleTime;
            if (t >= 1f)
            {
                ring.alive = false;
                ring.renderer.enabled = false;
                continue;
            }
            if (t < 0f)
            {
                ring.renderer.enabled = false;
                continue;
            }

            float ease = 1f - (1f - t) * (1f - t);
            float size = Mathf.Lerp(startSize, endSize, ease) * ring.size;
            Transform transformRing = ring.renderer.transform;
            transformRing.position = ring.position;
            Vector2 squash = Vector2.one;
            if (cam.orthographic)
            {
                transformRing.rotation = cam.transform.rotation;
                Vector3 normalOnScreen = cam.transform.InverseTransformDirection(ring.normal);
                if (Mathf.Abs(normalOnScreen.y) > 0.5f) squash = new Vector2(1f, 0.32f);
                else if (Mathf.Abs(normalOnScreen.x) > 0.5f) squash = new Vector2(0.32f, 1f);
            }
            else
            {
                transformRing.rotation = Quaternion.LookRotation(ring.normal);
            }
            transformRing.localScale = new Vector3(size * squash.x, size * squash.y, 1f);
            Color c = ring.renderer.color;
            c.a = 1f - t * t;
            ring.renderer.color = c;
            ring.renderer.enabled = true;
        }
    }
}
