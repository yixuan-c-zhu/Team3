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
