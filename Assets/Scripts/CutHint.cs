using System.Collections.Generic;
using UnityEngine;

public class CutHint : MonoBehaviour
{
    public enum Kind { UnderCeiling, ThinnerThanGap, OverGlass, BridgeSlice }

    public Kind kind;
    public Vector3 size = new Vector3(8f, 10f, 8f);
    public Collider obstacle;
    public float gapWidth = 1.4f;
    public Vector3 gapAxis = Vector3.forward;
    public Door door;
    public AimZone aimZone;
    public float margin = 0.2f;

    const float minPiece = 0.1f;
    const float minKeep = 0.12f;
    const int searchSteps = 14;
    const int scanSteps = 12;

    public static readonly List<CutHint> all = new List<CutHint>();

    void OnEnable()
    {
        all.Add(this);
        CutHintView.Ensure();
    }

    void OnDisable()
    {
        all.Remove(this);
    }

    public bool TryGetCut(Bounds body, out Vector3 normal, out Vector3 point)
    {
        normal = Vector3.up;
        point = body.center;
        if (kind == Kind.BridgeSlice)
        {
            return TryGetSlice(body, ref point);
        }
        if (!Contains(body.center))
        {
            return false;
        }

        if (kind == Kind.UnderCeiling)
        {
            if (obstacle == null) return false;
            float ceiling = obstacle.bounds.min.y;
            if (body.max.y <= ceiling) return false;
            point.y = ceiling - margin;
        }
        else if (kind == Kind.ThinnerThanGap)
        {
            normal = gapAxis.normalized;
            float thickness = Extent(body, normal) * 2f;
            if (thickness <= gapWidth) return false;
            float offset = Mathf.Min(thickness / 2f, gapWidth - margin) - thickness / 2f;
            point = body.center + normal * offset;
        }
        else
        {
            if (obstacle == null || door == null || door.isOpen) return false;
            Bounds glass = obstacle.bounds;
            Vector3 toward = glass.center - body.center;
            normal = Mathf.Abs(toward.x) >= Mathf.Abs(toward.z)
                ? new Vector3(Mathf.Sign(toward.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(toward.z));
            float middle = Vector3.Dot(body.center, normal);
            float edge = Vector3.Dot(glass.center, normal) - Extent(glass, normal);
            point = body.center + normal * (Mathf.Max(middle, edge + margin) - middle);
        }
        return Splits(body, normal, point);
    }

    public bool TryGetCut3D(ConvexShape body, Bounds bounds, Vector3 eye, Vector3 right, float minVolume, out Vector3[] line)
    {
        line = null;
        if (!Contains(bounds.center)) return false;
        float smallest = Mathf.Max(minVolume, body.GetVolume() * minKeep);
        if (kind == Kind.UnderCeiling)
        {
            return SolveUnderCeiling(body, bounds, eye, right, minVolume, smallest, out line);
        }
        if (kind == Kind.ThinnerThanGap)
        {
            return SolveThinnerThanGap(body, bounds, eye, right, minVolume, smallest, out line);
        }
        return false;
    }

    bool SolveUnderCeiling(ConvexShape body, Bounds bounds, Vector3 eye, Vector3 right, float minVolume, float smallest, out Vector3[] line)
    {
        line = null;
        if (obstacle == null) return false;
        float ceiling = obstacle.bounds.min.y;
        if (bounds.max.y <= ceiling) return false;

        float limit = ceiling - margin;
        float low = bounds.min.y - bounds.size.y;
        float high = bounds.max.y + bounds.size.y;
        for (int i = 0; i < searchSteps; i++)
        {
            float height = (low + high) * 0.5f;
            Vector3 aim = new Vector3(bounds.center.x, height, bounds.center.z);
            Vector3 normal = Vector3.Cross(right, aim - eye).normalized;
            if (normal.y < 0f) normal = -normal;

            ConvexShape upper;
            ConvexShape lower;
            if (!body.Cut(normal, Vector3.Dot(normal, eye), out upper, out lower))
            {
                if (Vector3.Dot(normal, bounds.center - eye) > 0f) low = height;
                else high = height;
                continue;
            }

            if (lower.GetBounds().max.y <= limit)
            {
                low = height;
                if (lower.GetVolume() >= smallest && upper.GetVolume() >= minVolume)
                {
                    line = CutEdge(lower);
                }
            }
            else
            {
                high = height;
            }
        }
        return line != null;
    }

    bool SolveThinnerThanGap(ConvexShape body, Bounds bounds, Vector3 eye, Vector3 right, float minVolume, float smallest, out Vector3[] line)
    {
        line = null;
        Vector3 axis = gapAxis.normalized;
        if (Extent(bounds, axis) * 2f <= gapWidth) return false;

        float limit = gapWidth - margin;
        Vector3 across = new Vector3(right.x, 0f, right.z).normalized;
        float reach = Extent(bounds, across);
        for (int i = 0; i <= scanSteps; i++)
        {
            float offset = reach * i / scanSteps;
            for (int side = 0; side < 2; side++)
            {
                Vector3 aim = bounds.center + across * (side == 0 ? offset : -offset);
                Vector3 normal = Vector3.Cross(Vector3.up, aim - eye).normalized;
                ConvexShape front;
                ConvexShape back;
                if (!body.Cut(normal, Vector3.Dot(normal, eye), out front, out back)) continue;
                if (front.GetVolume() < minVolume || back.GetVolume() < minVolume) continue;
                if (Fits(front, axis, limit, smallest) || Fits(back, axis, limit, smallest))
                {
                    line = CutEdge(front);
                    return true;
                }
            }
        }
        return false;
    }

    static bool Fits(ConvexShape piece, Vector3 axis, float limit, float smallest)
    {
        return piece.GetVolume() >= smallest && Extent(piece.GetBounds(), axis) * 2f <= limit;
    }

    static Vector3[] CutEdge(ConvexShape piece)
    {
        return piece.faces[piece.faces.Count - 1];
    }

    bool TryGetSlice(Bounds body, ref Vector3 point)
    {
        if (aimZone == null || aimZone.pieceSpot == null) return false;
        PieceSpot spot = aimZone.pieceSpot;
        if (!spot.Holds(body) || spot.GetPiece() != null || BridgeBuilt()) return false;
        point.y = spot.GetFloorCenter().y + aimZone.pieceSize.y;
        return Splits(body, Vector3.up, point);
    }

    bool BridgeBuilt()
    {
        if (aimZone.target == null) return false;
        Bounds goal = new Bounds(aimZone.target.position, aimZone.target.lossyScale);
        foreach (ColorObstacle glass in FindObjectsByType<ColorObstacle>(FindObjectsSortMode.None))
        {
            Collider col = glass.GetComponent<Collider>();
            if (col != null && col.bounds.Intersects(goal)) return true;
        }
        return false;
    }

    static bool Splits(Bounds body, Vector3 normal, Vector3 point)
    {
        float middle = Vector3.Dot(body.center, normal);
        float reach = Extent(body, normal);
        float cut = Vector3.Dot(point, normal);
        return cut > middle - reach + minPiece && cut < middle + reach - minPiece;
    }

    public static float Extent(Bounds bounds, Vector3 direction)
    {
        Vector3 e = bounds.extents;
        return Mathf.Abs(direction.x) * e.x + Mathf.Abs(direction.y) * e.y + Mathf.Abs(direction.z) * e.z;
    }

    public bool Contains(Vector3 point)
    {
        Vector3 d = point - transform.position;
        return Mathf.Abs(d.x) <= size.x * 0.5f && Mathf.Abs(d.y) <= size.y * 0.5f && Mathf.Abs(d.z) <= size.z * 0.5f;
    }

    void OnDrawGizmos()
    {
        if (kind == Kind.BridgeSlice) return;
        Gizmos.color = new Color(0.88f, 0.46f, 0.23f, 0.5f);
        Gizmos.DrawWireCube(transform.position, size);
    }
}
