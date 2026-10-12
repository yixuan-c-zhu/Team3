using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public CameraRig camScript;
    public float speed = 6f;
    public float jumpForce = 8.5f;

    public bool canMove = true;

    public float stepHeight = 0.15f;
    public float stepLift = 0.05f;

    private Rigidbody rb;
    private Collider col;
    private bool isGrounded;
    private bool jumpPressed;
    private bool blockedLow;
    private Vector3 blockedNormal;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void Update()
    {
        if (canMove && Input.GetKeyDown(KeyCode.Space))
        {
            jumpPressed = true;
        }
    }

    void FixedUpdate()
    {
        Vector3 moveDir = Vector3.zero;
        if (canMove)
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            if (camScript.is2D)
            {
                if (!camScript.IsTurning())
                {
                    moveDir = camScript.GetRightDir() * horizontal;
                }
            }
            else
            {
                moveDir = camScript.GetCamForward() * vertical + camScript.GetCamRight() * horizontal;
                if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
            }
        }

        Vector3 velocity = GetVel();
        velocity.x = moveDir.x * speed;
        velocity.z = moveDir.z * speed;
        if (jumpPressed && isGrounded)
        {
            velocity.y = jumpForce;
            PieceCarry.LaunchRiders(rb, jumpForce);
        }
        TryStepOverEdge(moveDir);
        jumpPressed = false;
        isGrounded = false;
        blockedLow = false;
        SetVel(velocity);
    }

    void TryStepOverEdge(Vector3 moveDir)
    {
        Vector3 dir = new Vector3(moveDir.x, 0f, moveDir.z);
        if (!isGrounded || !blockedLow || dir.sqrMagnitude < 0.01f)
        {
            return;
        }
        dir.Normalize();
        if (Vector3.Dot(blockedNormal, dir) > -0.5f)
        {
            return;
        }

        Bounds bounds = col.bounds;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + stepHeight + 0.02f, bounds.center.z);
        float reach = Mathf.Abs(dir.x) * bounds.extents.x + Mathf.Abs(dir.z) * bounds.extents.z + 0.1f;
        if (Physics.Raycast(origin, dir, reach, ~0, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        rb.position += Vector3.up * stepLift;
    }

    void OnCollisionStay(Collision collision)
    {
        float lowLimit = col.bounds.min.y + stepHeight;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.normal.y > 0.5f)
            {
                isGrounded = true;
            }
            else if (Mathf.Abs(contact.normal.y) < 0.3f && contact.point.y < lowLimit)
            {
                blockedLow = true;
                blockedNormal = contact.normal;
            }
        }
    }

    public void StopPlayer()
    {
        SetVel(Vector3.zero);
    }

    Vector3 GetVel()
    {
        return rb.linearVelocity;
    }

    void SetVel(Vector3 vel)
    {
        rb.linearVelocity = vel;
    }
}
