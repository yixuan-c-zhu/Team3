using UnityEngine;

// The same spot guide AimZone draws when Show Spot Guide is on (it frames the player standing on the
// spot, follows them around the spot, and falls back to the expected slice or the slice left there),
// but drawn as a wedge (a ramp) instead of a box.
// Use: turn off Show Spot Guide on the AimZone, put this on its own object, and give it a LineRenderer
// on another object to draw with (this script switches that object on and off).
public class WedgeSpotGuide : MonoBehaviour
{
    [Tooltip("The AimZone whose spot this guide sits on. Turn off its Show Spot Guide.")]
    public AimZone aimZone;
    [Tooltip("The dashed line to draw the wedge with. Keep it on a different object from this script.")]
    public LineRenderer line;
    [Tooltip("World direction the wedge rises toward. (1, 0, 0) means its tall side faces +X.")]
    public Vector3 highSide = Vector3.right;

    // Same numbers as AimZone's own guide.
    const float guideMargin = 0.04f;
    const float guideLift = 0.02f;

    private CutController cutController;

    void Awake()
    {
        cutController = FindFirstObjectByType<CutController>();
        if (line == null)
        {
            return;
        }
        line.useWorldSpace = true;
        line.loop = false;

        // Like AimZone's guide: hidden in 2D unless it overlaps the player in depth.
        Fadeable fade = line.GetComponent<Fadeable>();
        if (fade == null)
        {
            fade = line.gameObject.AddComponent<Fadeable>();
        }
        fade.hideIn2D = true;
    }

    void Update()
    {
        if (aimZone == null || line == null || aimZone.pieceSpot == null)
        {
            return;
        }
        if (cutController == null || cutController.player == null || cutController.state != CutController.State.Normal)
        {
            return;
        }

        bool canStart = !aimZone.IsBusy && !aimZone.IsBuilt;
        line.gameObject.SetActive(canStart);
        if (!canStart)
        {
            return;
        }

        PieceSpot spot = aimZone.pieceSpot;
        Bounds expected = GetExpectedPieceBounds(spot);
        Bounds footprint = expected;
        Bounds playerBounds = cutController.player.GetComponent<Collider>().bounds;
        CuttablePiece spotPiece = spot.GetPiece();
        if (spot.Holds(playerBounds))
        {
            footprint = playerBounds;
        }
        else if (spotPiece != null)
        {
            footprint = spotPiece.GetComponent<Collider>().bounds;
        }
        SetWedgeLines(GetGuideBox(expected, footprint));
    }

    Bounds GetExpectedPieceBounds(PieceSpot spot)
    {
        Vector3 center = spot.GetFloorCenter() + Vector3.up * (aimZone.pieceSize.y / 2f);
        return new Bounds(center, aimZone.pieceSize);
    }

    // Same as AimZone.GetGuideBox: the height comes from the expected slice, the sides from the footprint.
    Bounds GetGuideBox(Bounds expected, Bounds footprint)
    {
        int thinSide = GetThinSide(aimZone.pieceSize);
        Vector3 min = expected.min;
        Vector3 max = expected.max;
        for (int axis = 0; axis < 3; axis += 2)
        {
            if (axis != thinSide)
            {
                min[axis] = footprint.min[axis];
                max[axis] = footprint.max[axis];
            }
            min[axis] = min[axis] - guideMargin;
            max[axis] = max[axis] + guideMargin;
        }
        min.y = min.y + guideLift;

        Bounds box = new Bounds();
        box.SetMinMax(min, max);
        return box;
    }

    static int GetThinSide(Vector3 size)
    {
        int thinSide = 0;
        if (size.y < size[thinSide])
        {
            thinSide = 1;
        }
        if (size.z < size[thinSide])
        {
            thinSide = 2;
        }
        return thinSide;
    }

    // A wedge filling the box: flat on the bottom, full height on the high side, zero height on the low side.
    void SetWedgeLines(Bounds box)
    {
        int rise = Mathf.Abs(highSide.x) >= Mathf.Abs(highSide.z) ? 0 : 2;
        int across = rise == 0 ? 2 : 0;
        bool positive = highSide[rise] >= 0f;
        float low = positive ? box.min[rise] : box.max[rise];
        float high = positive ? box.max[rise] : box.min[rise];
        float near = box.min[across];
        float far = box.max[across];
        float bottom = box.min.y;
        float top = box.max.y;

        Vector3 b0 = Point(rise, across, low, near, bottom);
        Vector3 b1 = Point(rise, across, high, near, bottom);
        Vector3 b2 = Point(rise, across, high, far, bottom);
        Vector3 b3 = Point(rise, across, low, far, bottom);
        Vector3 t1 = Point(rise, across, high, near, top);
        Vector3 t2 = Point(rise, across, high, far, top);

        // One unbroken path over all 9 edges (two edges are drawn twice).
        Vector3[] path = { b0, b1, b2, b3, b0, t1, t2, b3, b2, t2, t1, b1 };
        line.positionCount = path.Length;
        line.SetPositions(path);
    }

    static Vector3 Point(int rise, int across, float riseValue, float acrossValue, float y)
    {
        Vector3 point = Vector3.zero;
        point[rise] = riseValue;
        point[across] = acrossValue;
        point.y = y;
        return point;
    }
}
