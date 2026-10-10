using UnityEngine;

public class PieceColor : MonoBehaviour
{
    public enum PieceColorType
    {
        Blue,
        Orange
    }

    public PieceColorType currentColor = PieceColorType.Blue;

    private Renderer pieceRenderer;

    void Awake()
    {
        pieceRenderer = GetComponent<Renderer>();
    }

    public void SetColor(
        PieceColorType newColor,
        Material blueMaterial,
        Material orangeMaterial)
    {
        currentColor = newColor;

        if (pieceRenderer == null)
        {
            pieceRenderer = GetComponent<Renderer>();
        }

        if (currentColor == PieceColorType.Blue)
        {
            pieceRenderer.sharedMaterial = blueMaterial;
        }
        else
        {
            pieceRenderer.sharedMaterial = orangeMaterial;
        }

        ColorObstacle[] obstacles =
        FindObjectsByType<ColorObstacle>(
        FindObjectsSortMode.None);

        foreach (ColorObstacle obstacle in obstacles)
        {
        obstacle.UpdateCollision(this);
        }

    }

    public void ToggleColor(
        Material blueMaterial,
        Material orangeMaterial)
    {
        if (currentColor == PieceColorType.Blue)
        {
            SetColor(
                PieceColorType.Orange,
                blueMaterial,
                orangeMaterial);
        }
        else
        {
            SetColor(
                PieceColorType.Blue,
                blueMaterial,
                orangeMaterial);
        }
    }
}
