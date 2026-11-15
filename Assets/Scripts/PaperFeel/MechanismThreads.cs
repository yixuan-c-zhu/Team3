using System.Collections.Generic;
using UnityEngine;

public class MechanismThreads : MonoBehaviour
{
    public float width = 0.08f;
    public float gap = 0.03f;
    public float height = 0.6f;
    public float inset = 0.15f;
    public float litTime = 0.2f;
    public float travelTime = 0.45f;

    class Thread
    {
        public System.Func<bool> pressed;
        public LineRenderer line;
        public SpriteRenderer knot;
        public Vector3 end;
        public Vector3 normal;
        public float lit;
        public float travel = -1f;
        public float pop;
        public bool wasPressed;
    }

    readonly List<Thread> threads = new List<Thread>();
    readonly Gradient gradient = new Gradient();
    readonly GradientColorKey[] colorKeys = new GradientColorKey[5];
    readonly GradientAlphaKey[] alphaKeys = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
    Material material;
    Sprite knotSprite;

    void Start()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null) material = new Material(shader);
        knotSprite = PaperFeel.PaintKnot(PaperFeel.Mechanism);

        foreach (PressureButton button in FindObjectsByType<PressureButton>(FindObjectsSortMode.None))
        {
            if (button.movingWall == null) continue;
            Transform wall = button.movingWall;
            float rest = wall.position.y;
            Add(button.GetComponent<Renderer>(), wall.GetComponent<Renderer>(), () => wall.position.y > rest + 0.05f);
        }
        foreach (PressurePlate plate in FindObjectsByType<PressurePlate>(FindObjectsSortMode.None))
        {
            if (plate.door == null) continue;
            PressurePlate source = plate;
            Add(plate.GetComponent<Renderer>(), plate.door.GetComponent<Renderer>(), () => source.isPressed);
        }
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    void Add(Renderer from, Renderer to, System.Func<bool> pressed)
    {
        if (from == null || to == null) return;
        Vector3 normal;
        List<Vector3> path = Route(from.bounds, to, out normal);
        if (path.Count < 2) return;

        Thread thread = new Thread { pressed = pressed, end = path[path.Count - 1], normal = normal };
        thread.line = new GameObject("Thread").AddComponent<LineRenderer>();
        thread.line.transform.SetParent(transform, false);
        thread.line.sharedMaterial = material;
        thread.line.useWorldSpace = true;
        thread.line.numCapVertices = 3;
        thread.line.numCornerVertices = 3;
        thread.line.widthMultiplier = width;
        thread.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        thread.line.receiveShadows = false;
        thread.line.positionCount = path.Count;
        thread.line.SetPositions(path.ToArray());

        thread.knot = new GameObject("Knot").AddComponent<SpriteRenderer>();
        thread.knot.transform.SetParent(transform, false);
        thread.knot.sprite = knotSprite;
        threads.Add(thread);
    }

    List<Vector3> Route(Bounds button, Renderer door, out Vector3 normal)
    {
        float lift = width * 0.5f + gap;
        Collider doorCollider = door.GetComponent<Collider>();
        Bounds doorBounds = door.bounds;
        float floor = FloorAt(button.center, button.min.y, doorCollider);

        bool onDoor;
        Bounds wall = AnchorWall(doorCollider, doorBounds, out onDoor);

        int axis = 0;
        float sign = 1f;
        float best = float.MaxValue;
        for (int a = 0; a <= 2; a += 2)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float plane = s > 0 ? wall.max[a] : wall.min[a];
                float ahead = (button.center[a] - plane) * s;
                if (ahead <= 0f) continue;
                int side = a == 0 ? 2 : 0;
                Vector3 probe = wall.center;
                probe[a] = plane + s * 0.12f;
                probe[side] = Mathf.Clamp(button.center[side], wall.min[side], wall.max[side]);
                if (Physics.CheckSphere(probe, 0.05f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float outside = Mathf.Max(0f, Mathf.Abs(button.center[side] - wall.center[side]) - wall.extents[side]);
                float distance = ahead + outside;
                if (distance < best)
                {
                    best = distance;
                    axis = a;
                    sign = s;
                }
            }
        }
        if (best == float.MaxValue)
        {
            Vector3 away = button.center - wall.center;
            axis = Mathf.Abs(away.x) >= Mathf.Abs(away.z) ? 0 : 2;
            sign = Mathf.Sign(away[axis]);
        }

        int lateral = axis == 0 ? 2 : 0;
        float low = wall.min[lateral] + inset;
        float high = wall.max[lateral] - inset;
        if (low > high) low = high = wall.center[lateral];
        float startSide = Mathf.Clamp(button.center[lateral], low, high);
        float endSide = onDoor ? startSide : Mathf.Clamp(doorBounds.center[lateral], low, high);
        float face = (sign > 0f ? wall.max[axis] : wall.min[axis]) + sign * lift;
        float top = Mathf.Min(wall.max.y, doorBounds.max.y);
        float rise = onDoor ? floor + lift : Mathf.Lerp(floor, top, height);

        normal = Vector3.zero;
        normal[axis] = sign;

        List<Vector3> path = new List<Vector3>();
        Vector3 start = button.center;
        start[axis] = button.center[axis] - sign * (button.extents[axis] + 0.02f);
        start.y = floor + lift;
        path.Add(start);
        Vector3 corner = start;
        corner[lateral] = startSide;
        Push(path, corner);
        Vector3 foot = corner;
        foot[axis] = face;
        Push(path, foot);
        Vector3 up = foot;
        up.y = rise;
        Push(path, up);
        Vector3 end = up;
        end[lateral] = endSide;
        Push(path, end);
        return path;
    }

    static void Push(List<Vector3> path, Vector3 point)
    {
        if (Vector3.Distance(path[path.Count - 1], point) > 0.01f) path.Add(point);
    }

    static float FloorAt(Vector3 position, float fallback, Collider ignore)
    {
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 0.5f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        float found = float.MinValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == ignore) continue;
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body != null && !body.isKinematic) continue;
            if (hit.point.y > found) found = hit.point.y;
        }
        return found > float.MinValue ? found : fallback;
    }

    static Bounds AnchorWall(Collider door, Bounds doorBounds, out bool onDoor)
    {
        onDoor = true;
        Bounds anchor = doorBounds;
        float most = doorBounds.size.y * 0.5f;
        foreach (Collider other in Physics.OverlapBox(doorBounds.center, doorBounds.extents + Vector3.one * 0.08f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (other == door) continue;
            Rigidbody body = other.attachedRigidbody;
            if (body != null && !body.isKinematic) continue;
            Bounds bounds = other.bounds;
            float overlap = Mathf.Min(bounds.max.y, doorBounds.max.y) - Mathf.Max(bounds.min.y, doorBounds.min.y);
            if (overlap < most) continue;
            most = overlap;
            anchor = bounds;
            onDoor = false;
        }
        return anchor;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float deltaTime = Time.deltaTime;
        Color idle = PaperFeel.Mechanism;
        Color bright = Color.Lerp(PaperFeel.Mechanism, Color.white, 0.75f);
        foreach (Thread thread in threads)
        {
            bool pressed = thread.pressed();
            if (pressed && !thread.wasPressed) thread.travel = 0f;
            thread.wasPressed = pressed;
            thread.lit = Mathf.MoveTowards(thread.lit, pressed ? 1f : 0f, deltaTime / litTime);
            if (thread.travel >= 0f)
            {
                thread.travel += deltaTime / travelTime;
                if (thread.travel > 1f)
                {
                    thread.travel = -1f;
                    thread.pop = 1f;
                }
            }
            thread.pop = Mathf.MoveTowards(thread.pop, 0f, deltaTime / 0.3f);

            Color body = Color.Lerp(idle, Color.Lerp(idle, Color.white, 0.3f), thread.lit);
            float t = thread.travel;
            Color head = t >= 0f ? bright : body;
            float k1 = Mathf.Clamp(t - 0.12f, 0.001f, 0.997f);
            float k2 = Mathf.Clamp(t, k1 + 0.001f, 0.998f);
            float k3 = Mathf.Clamp(t + 0.12f, k2 + 0.001f, 0.999f);
            colorKeys[0] = new GradientColorKey(body, 0f);
            colorKeys[1] = new GradientColorKey(body, k1);
            colorKeys[2] = new GradientColorKey(head, k2);
            colorKeys[3] = new GradientColorKey(body, k3);
            colorKeys[4] = new GradientColorKey(body, 1f);
            gradient.SetKeys(colorKeys, alphaKeys);
            thread.line.colorGradient = gradient;

            Transform knot = thread.knot.transform;
            knot.position = thread.end + thread.normal * 0.1f - cam.transform.forward * 0.05f;
            knot.rotation = cam.transform.rotation;
            knot.localScale = Vector3.one * 1.4f * (1f + 0.35f * Mathf.Sin(thread.pop * Mathf.PI));
        }
    }
}
