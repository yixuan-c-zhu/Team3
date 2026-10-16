using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    public LevelManager levelManager;

    public Material inactiveMaterial;
    public Material activeMaterial;

    private Renderer checkpointRenderer;
    private bool activated;

    void Awake()
    {
        checkpointRenderer = GetComponent<Renderer>();

        Collider checkpointCollider =
            GetComponent<Collider>();

        checkpointCollider.isTrigger = true;

        if (checkpointRenderer != null &&
            inactiveMaterial != null)
        {
            checkpointRenderer.sharedMaterial =
                inactiveMaterial;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (activated || levelManager == null)
        {
            return;
        }

        CuttablePiece touchedPiece =
            other.GetComponent<CuttablePiece>();

        // Only the currently controlled piece activates it.
        if (touchedPiece != levelManager.player)
        {
            return;
        }

        activated = true;

        levelManager.SetCheckpoint(transform);

        if (checkpointRenderer != null &&
            activeMaterial != null)
        {
            checkpointRenderer.sharedMaterial =
                activeMaterial;
        }
    }
}