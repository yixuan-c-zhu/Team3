using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class PieceSpot : MonoBehaviour
{
    const float heightTolerance = 0.15f;

    private CutController cutController;

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        cutController = FindFirstObjectByType<CutController>();
    }

    public Vector3 GetFloorCenter()
    {
        Bounds area = GetComponent<BoxCollider>().bounds;
        return new Vector3(area.center.x, area.min.y, area.center.z);
    }

    public bool Holds(Bounds bounds)
    {
        Bounds area = GetComponent<BoxCollider>().bounds;
        bool insideX = bounds.center.x > area.min.x && bounds.center.x < area.max.x;
        bool insideZ = bounds.center.z > area.min.z && bounds.center.z < area.max.z;
        bool onFloor = Mathf.Abs(bounds.min.y - area.min.y) < heightTolerance;
        return insideX && insideZ && onFloor;
    }

    public CuttablePiece GetPiece()
    {
        foreach (PieceCarry carry in PieceCarry.allPieces)
        {
            CuttablePiece piece = carry.GetComponent<CuttablePiece>();
            if (piece == null || piece == cutController.player)
            {
                continue;
            }

            Collider pieceCollider = piece.GetComponent<Collider>();
            if (!pieceCollider.enabled)
            {
                continue;
            }

            if (Holds(pieceCollider.bounds))
            {
                return piece;
            }
        }
        return null;
    }
}
