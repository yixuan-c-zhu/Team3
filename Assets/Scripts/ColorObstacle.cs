using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ColorObstacle : MonoBehaviour
{
    public PieceColor.PieceColorType obstacleColor =
        PieceColor.PieceColorType.Blue;

    public Material blueMaterial;
    public Material orangeMaterial;

    private Collider obstacleCollider;
    private Renderer obstacleRenderer;

    void Awake()
    {
        obstacleCollider = GetComponent<Collider>();
        obstacleRenderer = GetComponent<Renderer>();
    }

    void Start()
    {
        UpdateAppearance();

        PieceColor[] pieces =
            FindObjectsByType<PieceColor>(
                FindObjectsSortMode.None);

        foreach (PieceColor piece in pieces)
        {
            UpdateCollision(piece);
        }
    }

    public void UpdateCollision(PieceColor piece)
    {
        if (piece == null || obstacleCollider == null)
        {
            return;
        }

        Collider pieceCollider =
            piece.GetComponent<Collider>();

        if (pieceCollider == null)
        {
            return;
        }

        bool colorsMatch =
            piece.currentColor == obstacleColor;

        Physics.IgnoreCollision(
            obstacleCollider,
            pieceCollider,
            colorsMatch);
    }

    void UpdateAppearance()
    {
        if (obstacleRenderer == null)
        {
            return;
        }

        if (obstacleColor ==
            PieceColor.PieceColorType.Blue)
        {
            obstacleRenderer.sharedMaterial = blueMaterial;
        }
        else
        {
            obstacleRenderer.sharedMaterial = orangeMaterial;
        }
    }
}