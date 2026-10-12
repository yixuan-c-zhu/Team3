using UnityEngine;
using System.Collections.Generic;

public class PressureButton : MonoBehaviour
{
    public Transform movingWall;
    public float riseHeight = 4f;
    public float moveSpeed = 2f;

    private Vector3 closedPosition;
    private Vector3 openPosition;

    private HashSet<Rigidbody> pressingBodies = new HashSet<Rigidbody>();

    void Start()
    {
        if (movingWall == null)
        {
            Debug.LogError("MovingWall not assigned!");
            enabled = false;
            return;
        }

        closedPosition = movingWall.position;
        openPosition = closedPosition + Vector3.up * riseHeight;
    }

    void Update()
    {
        pressingBodies.RemoveWhere(rb => rb == null);

        bool isPressed = pressingBodies.Count > 0;

        Vector3 target = isPressed ? openPosition : closedPosition;

        movingWall.position = Vector3.MoveTowards(
            movingWall.position,
            target,
            moveSpeed * Time.deltaTime
        );
    }

    void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;

        if (rb != null && !rb.isKinematic)
        {
            pressingBodies.Add(rb);
        }
    }

    void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;

        if (rb != null)
        {
            pressingBodies.Remove(rb);
        }
    }
}

