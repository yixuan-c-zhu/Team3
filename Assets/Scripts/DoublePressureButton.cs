
using UnityEngine;

public class DoublePressureButton : MonoBehaviour
{
    [Header("Two Buttons")]
    public Collider button1;
    public Collider button2;

    [Header("Moving Door")]
    public Transform movingDoor;
    public float riseHeight = 4f;
    public float moveSpeed = 2f;

    private Vector3 closedPosition;
    private Vector3 openPosition;

    void Start()
    {
        if (button1 == null || button2 == null || movingDoor == null)
        {
            Debug.LogError("Assign both buttons and the door!");
            enabled = false;
            return;
        }

        closedPosition = movingDoor.position;
        openPosition = closedPosition + Vector3.up * riseHeight;
    }

    void Update()
    {
        bool pressed1 = IsPressed(button1);
        bool pressed2 = IsPressed(button2);

        bool open = pressed1 && pressed2;

        Vector3 target = open ? openPosition : closedPosition;

        movingDoor.position = Vector3.MoveTowards(
            movingDoor.position,
            target,
            moveSpeed * Time.deltaTime
        );
    }

    bool IsPressed(Collider button)
    {
        Bounds bounds = button.bounds;

        Vector3 center = new Vector3(
            bounds.center.x,
            bounds.max.y + 0.15f,
            bounds.center.z
        );

        Vector3 halfExtents = new Vector3(
            bounds.extents.x,
            0.2f,
            bounds.extents.z
        );

        Collider[] hits = Physics.OverlapBox(
            center,
            halfExtents,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            Rigidbody rb = hit.attachedRigidbody;

            if (rb != null && !rb.isKinematic &&
                hit.transform != button.transform)
            {
                return true;
            }
        }

        return false;
    }
}

