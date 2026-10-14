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

    public CuttablePiece GetPiece()
    {
        Bounds area = GetComponent<BoxCollider>().bounds;
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

            Bounds pieceBounds = pieceCollider.bounds;
            bool insideX = pieceBounds.center.x > area.min.x && pieceBounds.center.x < area.max.x;
            bool insideZ = pieceBounds.center.z > area.min.z && pieceBounds.center.z < area.max.z;
            bool onFloor = Mathf.Abs(pieceBounds.min.y - area.min.y) < heightTolerance;
            if (insideX && insideZ && onFloor)
            {
                return piece;
            }
        }
        return null;
    }
}
