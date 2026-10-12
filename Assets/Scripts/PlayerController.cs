using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public CameraRig camScript;
    public float speed = 6f;
    public float jumpForce = 8.5f;

    public bool canMove = true;

    private Rigidbody rb;
    private bool isGrounded;
    private bool jumpPressed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
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
        jumpPressed = false;
        isGrounded = false;
        SetVel(velocity);
    }

    void OnCollisionStay(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                isGrounded = true;
                return;
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
