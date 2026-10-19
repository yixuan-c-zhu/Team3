using UnityEngine;

public class PlacedPieceColorSetup : MonoBehaviour
{
    [Header("Obstacle Color")]
    public PieceColor.PieceColorType obstacleColor =
        PieceColor.PieceColorType.Blue;

    [Header("Materials")]
    public Material blueMaterial;
    public Material orangeMaterial;

    private GameObject configuredPiece;

    void Update()
    {
        GameObject placedPiece = GameObject.Find("Placed Piece");

        if (placedPiece == null)
            return;

        if (placedPiece == configuredPiece)
            return;

        // Wait until the perspective transformation is complete.
        if (placedPiece.GetComponent<CuttablePiece>() != null)
            return;

        Collider pieceCollider =
            placedPiece.GetComponent<Collider>();

        if (pieceCollider == null)
            return;

        // Add or retrieve the color obstacle component.
        ColorObstacle obstacle =
            placedPiece.GetComponent<ColorObstacle>();

        if (obstacle == null)
        {
            obstacle = placedPiece.AddComponent<ColorObstacle>();
        }

        obstacle.obstacleColor = obstacleColor;
        obstacle.blueMaterial = blueMaterial;
        obstacle.orangeMaterial = orangeMaterial;

        // Explicitly assign the material to prevent pink objects.
        Renderer pieceRenderer =
            placedPiece.GetComponent<Renderer>();

        if (pieceRenderer != null)
        {
            Material selectedMaterial =
                obstacleColor == PieceColor.PieceColorType.Blue
                ? blueMaterial
                : orangeMaterial;

            if (selectedMaterial != null)
            {
                pieceRenderer.sharedMaterial = selectedMaterial;
            }
            else
            {
                Debug.LogWarning(
                    "PlacedPieceColorSetup: Material is missing."
                );
            }
        }

        // Initialize collision rules for existing pieces.
        PieceColor[] players =
            FindObjectsByType<PieceColor>(
                FindObjectsSortMode.None);

        foreach (PieceColor player in players)
        {
            obstacle.UpdateCollision(player);
        }

        configuredPiece = placedPiece;

        Debug.Log(
            "Placed Piece color obstacle configured: "
            + obstacleColor
        );
    }
}
