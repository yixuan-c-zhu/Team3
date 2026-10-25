using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class LockWall : MonoBehaviour
{
    public enum Corner { TopRight, TopLeft }

    public PieceColor.PieceColorType wallColor = PieceColor.PieceColorType.Orange;
    public Corner keyCorner = Corner.TopRight;
    public Transform lookPoint;
    public Material blueGlass;
    public Material orangeGlass;
    public Material keyholeMaterial;
    public float keyholeDepth = 1.5f;
    public float warnDistance = 2f;

    [TextArea] public string notCornerMessage = "That's not a corner of you.\n<size=70%>{0} and cut off one corner with a single slanted cut.</size>";
    [TextArea] public string wrongCornerMessage = "Wrong corner.\n<size=70%>{0} and cut off your {1} corner instead.</size>";
    [TextArea] public string bigCornerMessage = "This corner is too big for the keyhole.\n<size=70%>{0} and cut closer to the corner.</size>";
    [TextArea] public string smallCornerMessage = "This corner is too small for the keyhole.\n<size=70%>{0} and cut further from the corner.</size>";
    [TextArea] public string unlockMessage = "Unlocked! The wall turned into {0} glass.\n<size=70%>Turn {0} to walk through it.</size>";
    [TextArea] public string blockedMessage = "You're {1}, so this {0} glass still blocks you.\n<size=70%>Press {2} to turn {0}.</size>";
    public string topRightName = "top-right";
    public string topLeftName = "top-left";

    const float keyholeLift = 0.02f;
    const float flatFace = 0.95f;
    const float rightAngle = 0.2f;
    const float cornerReach = 0.15f;
    const float restTolerance = 0.15f;
    const float matchGrow = 1.8f;
    const float depthMargin = 0.25f;
    const float lineInset = 0.07f;

    private Vector3[] slot;
    private Vector3 aimCenter;
    private Vector3 faceNormal;
    private int faceAxis;
    private int faceSide;
    private float slotDepth;
    private float scale = 2f;
    private Vector3 keySize;
    private float sizeTolerance;
    private bool unlocked;
    private Mesh wallMesh;
    private Mesh carvedMesh;
    private Material wallMaterial;
    private CutController cutController;
    private PieceControlManager colorManager;
    private LevelManager levelManager;
    private string shownMessage;

    public float Scale
    {
        get { return scale; }
    }

    public bool IsUnlocked
    {
        get { return unlocked; }
    }

    void Awake()
    {
        cutController = FindFirstObjectByType<CutController>();
        colorManager = FindFirstObjectByType<PieceControlManager>();
        levelManager = FindFirstObjectByType<LevelManager>();
    }

    void Start()
    {
        CarveKeyhole();
    }

    void Update()
    {
        UpdateBlockedWarning();
    }

    void OnDestroy()
    {
        if (carvedMesh != null)
        {
            Destroy(carvedMesh);
        }
    }

    public bool Build(PieceSpot spot, Vector3 size, float tolerance)
    {
        keySize = size;
        sizeTolerance = tolerance;
        slot = null;
        if (spot == null || lookPoint == null)
        {
            return false;
        }

        Vector3[] key = GetKeyPrism(GetExpectedKeyBounds(spot, size));
        Vector3 keyCenter = GetCenter(key);
        Vector3 planePoint;
        float thickness;
        if (!GetFacingPlane(keyCenter, out planePoint, out thickness))
        {
            Debug.LogWarning("LockWall: the line from the look point through the key misses this wall.", this);
            return false;
        }

        Vector3 look = lookPoint.position;
        Vector3 frontCenter = (key[0] + key[1] + key[2]) / 3f;
        scale = Vector3.Dot(planePoint - look, faceNormal) / Vector3.Dot(frontCenter - look, faceNormal);
        aimCenter = look + (keyCenter - look) * scale;

        slotDepth = Mathf.Min(keyholeDepth, thickness - depthMargin);
        slot = new Vector3[6];
        for (int i = 0; i < 3; i++)
        {
            slot[i] = Project(key[i], planePoint, faceNormal);
            slot[i + 3] = slot[i] - faceNormal * slotDepth;
        }
        return true;
    }

    public Bounds GetExpectedKeyBounds(PieceSpot spot, Vector3 size)
    {
        Vector3 center = spot.GetFloorCenter() + Vector3.up * (size.y / 2f);
        return new Bounds(center, size);
    }

    public CuttablePiece FindKey(PieceSpot spot, CuttablePiece player)
    {
        Bounds area = GetKeyArea(spot);
        CuttablePiece best = null;
        foreach (PieceCarry carry in PieceCarry.allPieces)
        {
            CuttablePiece piece = carry.GetComponent<CuttablePiece>();
            if (piece == null || piece == player)
            {
                continue;
            }
            Collider pieceCollider = piece.GetComponent<Collider>();
            if (!pieceCollider.enabled)
            {
                continue;
            }
            Bounds bounds = pieceCollider.bounds;
            if (!Covers(area, bounds.center) || Mathf.Abs(bounds.min.y - area.min.y) > restTolerance)
            {
                continue;
            }
            if (best == null || piece.volume < best.volume)
            {
                best = piece;
            }
        }
        return best;
    }

    public bool CoversKey(PieceSpot spot, Vector3 center)
    {
        return Covers(GetKeyArea(spot), center);
    }

    Bounds GetKeyArea(PieceSpot spot)
    {
        Bounds area = spot.GetComponent<BoxCollider>().bounds;
        area.Expand(new Vector3(keySize.x, 0f, 0f));
        return area;
    }

    bool Covers(Bounds area, Vector3 point)
    {
        return point.x > area.min.x && point.x < area.max.x && point.z > area.min.z && point.z < area.max.z;
    }

    public Bounds GetKeyholeBounds()
    {
        if (slot == null)
        {
            return new Bounds(transform.position, Vector3.zero);
        }
        Bounds bounds = new Bounds(slot[0], Vector3.zero);
        foreach (Vector3 point in slot)
        {
            bounds.Encapsulate(point);
        }
        return bounds;
    }

    public Vector3[] GetSlotOutline()
    {
        if (slot == null)
        {
            return null;
        }
        float radius;
        Vector3 center = GetIncenter(slot[0], slot[1], slot[2], out radius);
        float shrink = Mathf.Max(0f, radius - lineInset) / radius;
        Vector3[] corners = new Vector3[6];
        for (int i = 0; i < 3; i++)
        {
            Vector3 inner = center + (slot[i] - center) * shrink;
            corners[i] = inner + faceNormal * keyholeLift;
            corners[i + 3] = inner - faceNormal * (slotDepth - lineInset);
        }

        int[] order = { 0, 1, 2, 0, 3, 4, 5, 3, 4, 1, 2, 5 };
        Vector3[] path = new Vector3[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            path[i] = corners[order[i]];
        }
        return path;
    }

    public Vector3 GetAimSpot(Bounds keyBounds)
    {
        if (lookPoint == null || scale <= 1.01f)
        {
            return keyBounds.center - Vector3.forward * 5f;
        }
        Vector3 keyCenter = GetCenter(GetKeyPrism(keyBounds));
        return (keyCenter * scale - aimCenter) / (scale - 1f);
    }

    public string GetKeyProblem(ConvexShape shape, string action)
    {
        Vector3 normal;
        List<Vector3> face = FindKeyFace(shape, out normal);
        if (face == null || face.Count != 3 || Mathf.Abs(normal.z) < flatFace)
        {
            return string.Format(notCornerMessage, action);
        }

        Bounds bounds = shape.GetBounds();
        if (Mathf.Abs(bounds.size.z - keySize.z) > sizeTolerance)
        {
            return string.Format(notCornerMessage, action);
        }

        int corner = GetRightAngle(face);
        if (corner < 0)
        {
            return string.Format(notCornerMessage, action);
        }
        if (!IsExpectedCorner(face[corner], bounds))
        {
            return string.Format(wrongCornerMessage, action, GetCornerName());
        }

        float wide = bounds.size.x - keySize.x;
        float tall = bounds.size.y - keySize.y;
        if (wide > sizeTolerance || tall > sizeTolerance)
        {
            return string.Format(bigCornerMessage, action);
        }
        if (wide < -sizeTolerance || tall < -sizeTolerance)
        {
            return string.Format(smallCornerMessage, action);
        }
        return null;
    }

    public float GetMatch(ConvexShape shape, Camera cam)
    {
        if (slot == null)
        {
            return 0f;
        }
        Vector3 normal;
        List<Vector3> face = FindKeyFace(shape, out normal);
        if (face == null)
        {
            return 0f;
        }

        List<Vector2> keyPoints;
        List<Vector2> slotPoints;
        List<Vector3> slotFace = new List<Vector3> { slot[0], slot[1], slot[2] };
        if (!ToViewport(face, cam, out keyPoints) || !ToViewport(slotFace, cam, out slotPoints))
        {
            return 0f;
        }
        return GetOverlap(Grow(keyPoints), Grow(slotPoints));
    }

    List<Vector2> Grow(List<Vector2> points)
    {
        Vector2 center = Vector2.zero;
        foreach (Vector2 point in points)
        {
            center += point;
        }
        center /= points.Count;

        List<Vector2> grown = new List<Vector2>();
        foreach (Vector2 point in points)
        {
            grown.Add(center + (point - center) * matchGrow);
        }
        return grown;
    }

    public void Unlock()
    {
        if (unlocked)
        {
            return;
        }
        unlocked = true;
        RestoreWall();

        ColorObstacle glass = gameObject.AddComponent<ColorObstacle>();
        glass.obstacleColor = wallColor;
        glass.blueMaterial = blueGlass;
        glass.orangeMaterial = orangeGlass;
    }

    public string GetUnlockMessage()
    {
        return string.Format(unlockMessage, GetColorName(wallColor));
    }

    public Vector3[] GetGuideLine(Bounds body)
    {
        float margin = 0.15f;
        Vector3 top;
        Vector3 side;
        if (keyCorner == Corner.TopRight)
        {
            top = new Vector3(body.max.x - keySize.x, body.max.y, 0f);
            side = new Vector3(body.max.x, body.max.y - keySize.y, 0f);
        }
        else
        {
            top = new Vector3(body.min.x + keySize.x, body.max.y, 0f);
            side = new Vector3(body.min.x, body.max.y - keySize.y, 0f);
        }
        Vector3 along = (side - top).normalized * margin;
        top = top - along;
        side = side + along;

        float front = body.min.z - keyholeLift;
        float back = body.max.z + keyholeLift;
        Vector3[] line =
        {
            new Vector3(top.x, top.y, front),
            new Vector3(side.x, side.y, front),
            new Vector3(side.x, side.y, back),
            new Vector3(top.x, top.y, back),
            new Vector3(top.x, top.y, front)
        };
        return line;
    }

    void CarveKeyhole()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        MeshRenderer wallRenderer = GetComponent<MeshRenderer>();
        if (slot == null || keyholeMaterial == null || filter == null || wallRenderer == null)
        {
            return;
        }

        BoxCollider box = GetComponent<BoxCollider>();
        Vector3 min = box.center - box.size / 2f;
        Vector3 max = box.center + box.size / 2f;
        Vector3[] hole = new Vector3[slot.Length];
        for (int i = 0; i < slot.Length; i++)
        {
            hole[i] = transform.InverseTransformPoint(slot[i]);
        }

        int u = (faceAxis + 1) % 3;
        int v = (faceAxis + 2) % 3;
        for (int i = 0; i < 3; i++)
        {
            if (hole[i][u] <= min[u] || hole[i][u] >= max[u] || hole[i][v] <= min[v] || hole[i][v] >= max[v])
            {
                Debug.LogWarning("LockWall: the keyhole does not fit on this wall.", this);
                return;
            }
        }

        List<Vector3> vertices = new List<Vector3>();
        List<int> wallTriangles = new List<int>();
        List<int> holeTriangles = new List<int>();
        for (int axis = 0; axis < 3; axis++)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                if (axis == faceAxis && side == faceSide)
                {
                    AddHoledFace(vertices, wallTriangles, min, max, axis, side, hole);
                }
                else
                {
                    AddBoxFace(vertices, wallTriangles, min, max, axis, side);
                }
            }
        }

        Vector3 middle = GetCenter(hole);
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            Vector3 facing = middle - (hole[i] + hole[j] + hole[i + 3] + hole[j + 3]) / 4f;
            AddQuad(vertices, holeTriangles, hole[i], hole[j], hole[j + 3], hole[i + 3], facing);
        }
        Vector3 outward = Vector3.zero;
        outward[faceAxis] = faceSide;
        AddTriangle(vertices, holeTriangles, hole[3], hole[4], hole[5], outward);

        carvedMesh = new Mesh();
        carvedMesh.name = "CarvedWall";
        carvedMesh.SetVertices(vertices);
        carvedMesh.subMeshCount = 2;
        carvedMesh.SetTriangles(wallTriangles, 0);
        carvedMesh.SetTriangles(holeTriangles, 1);
        carvedMesh.RecalculateNormals();
        carvedMesh.RecalculateBounds();

        wallMesh = filter.sharedMesh;
        wallMaterial = wallRenderer.sharedMaterial;
        filter.sharedMesh = carvedMesh;
        wallRenderer.sharedMaterials = new Material[] { wallMaterial, keyholeMaterial };
    }

    void RestoreWall()
    {
        if (carvedMesh == null)
        {
            return;
        }
        GetComponent<MeshFilter>().sharedMesh = wallMesh;
        GetComponent<MeshRenderer>().sharedMaterials = new Material[] { wallMaterial };
        Destroy(carvedMesh);
        carvedMesh = null;
    }

    void AddBoxFace(List<Vector3> vertices, List<int> triangles, Vector3 min, Vector3 max, int axis, int side)
    {
        Vector2 low = new Vector2(min[(axis + 1) % 3], min[(axis + 2) % 3]);
        Vector2 high = new Vector2(max[(axis + 1) % 3], max[(axis + 2) % 3]);
        AddFaceQuad(vertices, triangles, min, max, axis, side, low, new Vector2(high.x, low.y), high, new Vector2(low.x, high.y));
    }

    void AddHoledFace(List<Vector3> vertices, List<int> triangles, Vector3 min, Vector3 max, int axis, int side, Vector3[] hole)
    {
        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;
        Vector2[] points = new Vector2[3];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(hole[i][u], hole[i][v]);
        }
        System.Array.Sort(points, (a, b) => a.y.CompareTo(b.y));
        Vector2 bottom = points[0];
        Vector2 middle = points[1];
        Vector2 top = points[2];
        Vector2 across = Vector2.Lerp(bottom, top, (middle.y - bottom.y) / (top.y - bottom.y));
        Vector2 left = middle.x < across.x ? middle : across;
        Vector2 right = middle.x < across.x ? across : middle;

        float minU = min[u];
        float maxU = max[u];
        AddFaceQuad(vertices, triangles, min, max, axis, side, new Vector2(minU, min[v]), new Vector2(maxU, min[v]), new Vector2(maxU, bottom.y), new Vector2(minU, bottom.y));
        AddFaceQuad(vertices, triangles, min, max, axis, side, new Vector2(minU, bottom.y), bottom, left, new Vector2(minU, middle.y));
        AddFaceQuad(vertices, triangles, min, max, axis, side, bottom, new Vector2(maxU, bottom.y), new Vector2(maxU, middle.y), right);
        AddFaceQuad(vertices, triangles, min, max, axis, side, new Vector2(minU, middle.y), left, top, new Vector2(minU, top.y));
        AddFaceQuad(vertices, triangles, min, max, axis, side, right, new Vector2(maxU, middle.y), new Vector2(maxU, top.y), top);
        AddFaceQuad(vertices, triangles, min, max, axis, side, new Vector2(minU, top.y), new Vector2(maxU, top.y), new Vector2(maxU, max[v]), new Vector2(minU, max[v]));
    }

    void AddFaceQuad(List<Vector3> vertices, List<int> triangles, Vector3 min, Vector3 max, int axis, int side, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float plane = side < 0 ? min[axis] : max[axis];
        Vector3 facing = Vector3.zero;
        facing[axis] = side;
        AddQuad(vertices, triangles, OnFace(axis, plane, a), OnFace(axis, plane, b), OnFace(axis, plane, c), OnFace(axis, plane, d), facing);
    }

    Vector3 OnFace(int axis, float plane, Vector2 point)
    {
        Vector3 result = Vector3.zero;
        result[axis] = plane;
        result[(axis + 1) % 3] = point.x;
        result[(axis + 2) % 3] = point.y;
        return result;
    }

    void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
    {
        AddTriangle(vertices, triangles, a, b, c, facing);
        AddTriangle(vertices, triangles, a, c, d, facing);
    }

    void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.sqrMagnitude < 1e-10f)
        {
            return;
        }
        if (Vector3.Dot(normal, facing) < 0f)
        {
            Vector3 swap = b;
            b = c;
            c = swap;
        }
        triangles.Add(vertices.Count);
        vertices.Add(a);
        triangles.Add(vertices.Count);
        vertices.Add(b);
        triangles.Add(vertices.Count);
        vertices.Add(c);
    }

    void UpdateBlockedWarning()
    {
        if (!unlocked || cutController == null || levelManager == null || cutController.state != CutController.State.Normal)
        {
            return;
        }

        string message = GetBlockedMessage();
        if (message != null)
        {
            if (message != shownMessage || IsShowing(message))
            {
                levelManager.ShowMessage(message);
            }
        }
        else if (shownMessage != null && IsShowing(shownMessage))
        {
            levelManager.ShowMessage("");
        }
        shownMessage = message;
    }

    string GetBlockedMessage()
    {
        PieceColor playerColor = cutController.player.GetComponent<PieceColor>();
        if (playerColor == null || playerColor.currentColor == wallColor)
        {
            return null;
        }

        Bounds nearPlayer = cutController.player.GetComponent<Collider>().bounds;
        nearPlayer.Expand(warnDistance * 2f);
        if (!nearPlayer.Intersects(GetComponent<Collider>().bounds))
        {
            return null;
        }
        return string.Format(blockedMessage, GetColorName(wallColor), GetColorName(playerColor.currentColor), GetColorKeyName());
    }

    bool IsShowing(string message)
    {
        HUD hud = levelManager.hud;
        if (hud == null || hud.messageText == null)
        {
            return false;
        }
        return hud.messageText.text == message;
    }

    Vector3[] GetKeyPrism(Bounds bounds)
    {
        float near = bounds.max.z;
        float far = bounds.min.z;
        if (lookPoint != null && lookPoint.position.z < bounds.center.z)
        {
            near = bounds.min.z;
            far = bounds.max.z;
        }
        Vector3[] front = GetKeyTriangle(bounds, near);
        Vector3[] back = GetKeyTriangle(bounds, far);
        return new Vector3[] { front[0], front[1], front[2], back[0], back[1], back[2] };
    }

    Vector3[] GetKeyTriangle(Bounds bounds, float z)
    {
        if (keyCorner == Corner.TopRight)
        {
            return new Vector3[]
            {
                new Vector3(bounds.max.x, bounds.max.y, z),
                new Vector3(bounds.min.x, bounds.max.y, z),
                new Vector3(bounds.max.x, bounds.min.y, z)
            };
        }
        return new Vector3[]
        {
            new Vector3(bounds.min.x, bounds.max.y, z),
            new Vector3(bounds.max.x, bounds.max.y, z),
            new Vector3(bounds.min.x, bounds.min.y, z)
        };
    }

    bool GetFacingPlane(Vector3 keyCenter, out Vector3 point, out float thickness)
    {
        point = Vector3.zero;
        thickness = 0f;
        BoxCollider box = GetComponent<BoxCollider>();
        Bounds wall = new Bounds(box.center, box.size);
        Vector3 lookLocal = transform.InverseTransformPoint(lookPoint.position);
        Vector3 keyLocal = transform.InverseTransformPoint(keyCenter);
        Ray ray = new Ray(lookLocal, keyLocal - lookLocal);
        float distance;
        if (wall.Contains(lookLocal) || !wall.IntersectRay(ray, out distance))
        {
            return false;
        }

        Vector3 hit = ray.GetPoint(distance);
        float closest = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float toMin = Mathf.Abs(hit[axis] - wall.min[axis]) * transform.lossyScale[axis];
            float toMax = Mathf.Abs(hit[axis] - wall.max[axis]) * transform.lossyScale[axis];
            if (toMin < closest)
            {
                closest = toMin;
                faceAxis = axis;
                faceSide = -1;
            }
            if (toMax < closest)
            {
                closest = toMax;
                faceAxis = axis;
                faceSide = 1;
            }
        }

        Vector3 localNormal = Vector3.zero;
        localNormal[faceAxis] = faceSide;
        Vector3 across = Vector3.zero;
        across[faceAxis] = box.size[faceAxis];
        point = transform.TransformPoint(hit);
        faceNormal = transform.TransformDirection(localNormal).normalized;
        thickness = Mathf.Abs(Vector3.Dot(transform.TransformVector(across), faceNormal));
        return true;
    }

    Vector3 Project(Vector3 point, Vector3 planePoint, Vector3 planeNormal)
    {
        Vector3 look = lookPoint.position;
        Vector3 direction = point - look;
        float along = Vector3.Dot(planePoint - look, planeNormal) / Vector3.Dot(direction, planeNormal);
        return look + direction * along;
    }

    List<Vector3> FindKeyFace(ConvexShape shape, out Vector3 bestNormal)
    {
        bestNormal = Vector3.zero;
        if (shape == null)
        {
            return null;
        }

        Vector3 center = shape.GetCenter();
        float side = 1f;
        if (lookPoint != null && lookPoint.position.z < center.z)
        {
            side = -1f;
        }

        List<Vector3> best = null;
        float bestFacing = float.MinValue;
        foreach (Vector3[] face in shape.faces)
        {
            List<Vector3> points = RemoveRepeats(face);
            if (points.Count < 3)
            {
                continue;
            }
            Vector3 normal = GetOutwardNormal(points, center);
            float facing = normal.z * side;
            if (facing > bestFacing)
            {
                bestFacing = facing;
                best = points;
                bestNormal = normal;
            }
        }
        return best;
    }

    List<Vector3> RemoveRepeats(Vector3[] face)
    {
        List<Vector3> points = new List<Vector3>();
        foreach (Vector3 point in face)
        {
            if (points.Count == 0 || (points[points.Count - 1] - point).sqrMagnitude > 1e-6f)
            {
                points.Add(point);
            }
        }
        while (points.Count > 1 && (points[0] - points[points.Count - 1]).sqrMagnitude <= 1e-6f)
        {
            points.RemoveAt(points.Count - 1);
        }
        return points;
    }

    Vector3 GetOutwardNormal(List<Vector3> points, Vector3 shapeCenter)
    {
        Vector3 normal = Vector3.zero;
        Vector3 faceCenter = Vector3.zero;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[(i + 1) % points.Count];
            normal.x += (a.y - b.y) * (a.z + b.z);
            normal.y += (a.z - b.z) * (a.x + b.x);
            normal.z += (a.x - b.x) * (a.y + b.y);
            faceCenter += a;
        }
        faceCenter /= points.Count;
        normal = normal.normalized;
        if (Vector3.Dot(normal, faceCenter - shapeCenter) < 0f)
        {
            normal = -normal;
        }
        return normal;
    }

    int GetRightAngle(List<Vector3> face)
    {
        int best = -1;
        float bestCos = rightAngle;
        for (int i = 0; i < face.Count; i++)
        {
            Vector3 toPrevious = (face[(i + face.Count - 1) % face.Count] - face[i]).normalized;
            Vector3 toNext = (face[(i + 1) % face.Count] - face[i]).normalized;
            float cos = Mathf.Abs(Vector3.Dot(toPrevious, toNext));
            if (cos < bestCos)
            {
                bestCos = cos;
                best = i;
            }
        }
        return best;
    }

    bool IsExpectedCorner(Vector3 corner, Bounds bounds)
    {
        bool atTop = Mathf.Abs(corner.y - bounds.max.y) < cornerReach;
        if (keyCorner == Corner.TopRight)
        {
            return atTop && Mathf.Abs(corner.x - bounds.max.x) < cornerReach;
        }
        return atTop && Mathf.Abs(corner.x - bounds.min.x) < cornerReach;
    }

    string GetCornerName()
    {
        if (keyCorner == Corner.TopRight)
        {
            return topRightName;
        }
        return topLeftName;
    }

    bool ToViewport(List<Vector3> points, Camera cam, out List<Vector2> result)
    {
        result = new List<Vector2>();
        foreach (Vector3 point in points)
        {
            Vector3 view = cam.WorldToViewportPoint(point);
            if (view.z <= 0f)
            {
                return false;
            }
            result.Add(new Vector2(view.x, view.y));
        }
        if (GetSignedArea(result) < 0f)
        {
            result.Reverse();
        }
        return true;
    }

    float GetOverlap(List<Vector2> first, List<Vector2> second)
    {
        float firstArea = GetSignedArea(first);
        float secondArea = GetSignedArea(second);
        if (firstArea <= 0f || secondArea <= 0f)
        {
            return 0f;
        }

        List<Vector2> overlap = first;
        for (int i = 0; i < second.Count && overlap.Count > 0; i++)
        {
            overlap = ClipSide(overlap, second[i], second[(i + 1) % second.Count]);
        }
        float overlapArea = Mathf.Max(0f, GetSignedArea(overlap));
        return overlapArea / (firstArea + secondArea - overlapArea);
    }

    List<Vector2> ClipSide(List<Vector2> points, Vector2 edgeStart, Vector2 edgeEnd)
    {
        List<Vector2> kept = new List<Vector2>();
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 current = points[i];
            Vector2 next = points[(i + 1) % points.Count];
            float currentSide = Cross(edgeEnd - edgeStart, current - edgeStart);
            float nextSide = Cross(edgeEnd - edgeStart, next - edgeStart);
            if (currentSide >= 0f)
            {
                kept.Add(current);
            }
            if ((currentSide >= 0f) != (nextSide >= 0f))
            {
                kept.Add(Vector2.Lerp(current, next, currentSide / (currentSide - nextSide)));
            }
        }
        return kept;
    }

    float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    float GetSignedArea(List<Vector2> points)
    {
        float area = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return area / 2f;
    }

    Vector3 GetCenter(Vector3[] points)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 point in points)
        {
            sum += point;
        }
        return sum / points.Length;
    }

    Vector3 GetIncenter(Vector3 a, Vector3 b, Vector3 c, out float radius)
    {
        float sideA = Vector3.Distance(b, c);
        float sideB = Vector3.Distance(c, a);
        float sideC = Vector3.Distance(a, b);
        float perimeter = sideA + sideB + sideC;
        radius = Vector3.Cross(b - a, c - a).magnitude / perimeter;
        return (a * sideA + b * sideB + c * sideC) / perimeter;
    }

    string GetColorName(PieceColor.PieceColorType color)
    {
        if (color == PieceColor.PieceColorType.Blue)
        {
            return "<color=#5B9BFF>blue</color>";
        }
        return "<color=#FF9A33>orange</color>";
    }

    string GetColorKeyName()
    {
        if (colorManager == null)
        {
            return "<color=#FFD54A>Shift</color>";
        }
        KeyCode key = colorManager.colorKey;
        string keyName = key.ToString();
        if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
        {
            keyName = "Shift";
        }
        return "<color=#FFD54A>" + keyName + "</color>";
    }

    void OnDrawGizmosSelected()
    {
        AimZone zone = null;
        foreach (AimZone candidate in FindObjectsByType<AimZone>(FindObjectsSortMode.None))
        {
            if (candidate.lockWall == this)
            {
                zone = candidate;
            }
        }
        if (zone == null || !Build(zone.pieceSpot, zone.pieceSize, zone.sizeTolerance))
        {
            return;
        }

        Vector3[] key = GetKeyPrism(GetExpectedKeyBounds(zone.pieceSpot, zone.pieceSize));
        Vector3[] outline = GetSlotOutline();
        Gizmos.color = Color.yellow;
        for (int i = 0; i + 1 < outline.Length; i++)
        {
            Gizmos.DrawLine(outline[i], outline[i + 1]);
        }
        for (int i = 0; i < 3; i++)
        {
            Gizmos.DrawLine(key[i], key[(i + 1) % 3]);
            Gizmos.DrawLine(key[i + 3], key[(i + 1) % 3 + 3]);
            Gizmos.DrawLine(lookPoint.position, slot[i]);
        }
        Gizmos.DrawSphere(lookPoint.position, 0.3f);
    }
}
