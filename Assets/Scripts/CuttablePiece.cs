using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
[RequireComponent(typeof(Rigidbody))]
public class CuttablePiece : MonoBehaviour
{
    public Vector3 boxSize = new Vector3(2f, 2f, 2f);
    public float density = 1f;

    public ConvexShape shape;
    public float volume;

    private MeshFilter meshFilter;
    private MeshCollider meshCol;
    private Rigidbody rb;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCol = GetComponent<MeshCollider>();
        rb = GetComponent<Rigidbody>();

        if (GetComponent<PieceCarry>() == null)
        {
            gameObject.AddComponent<PieceCarry>();
        }

        if (shape == null)
        {
            SetShape(ConvexShape.MakeBox(boxSize));
        }
    }

    public Vector3 GetSize()
    {
        if (shape != null) return shape.GetBounds().size;
        return boxSize;
    }

    public void SetShape(ConvexShape newShape)
    {
        shape = newShape;
        Mesh mesh = newShape.MakeMesh();

        Mesh oldMesh = meshFilter.sharedMesh;
        meshFilter.sharedMesh = mesh;
        meshCol.sharedMesh = null;
        meshCol.convex = true;
        meshCol.sharedMesh = mesh;
        if (oldMesh != null && oldMesh.name == "CutPiece")
        {
            Destroy(oldMesh);
        }

        volume = newShape.GetVolume();
        rb.mass = Mathf.Max(0.05f, volume * density);
    }

    public ConvexShape GetWorldShape()
    {
        return shape.ApplyMatrix(transform.localToWorldMatrix);
    }

    public void SetWorldShape(ConvexShape worldShape)
    {
        Vector3 center = worldShape.GetBounds().center;
        transform.SetPositionAndRotation(center, Quaternion.identity);
        rb.position = center;
        rb.rotation = Quaternion.identity;
        SetShape(worldShape.MoveBy(-center));
        Physics.SyncTransforms();
    }

    void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        Gizmos.color = new Color(0.18f, 0.36f, 0.83f, 1f);
        Gizmos.DrawWireCube(transform.position, boxSize);
    }
}
