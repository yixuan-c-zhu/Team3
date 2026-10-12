using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PieceCarry : MonoBehaviour
{
    public static List<PieceCarry> allPieces = new List<PieceCarry>();

    public float moveThreshold = 0.05f;

    const RigidbodyConstraints moveLock = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
    const int maxStackDepth = 16;

    private Rigidbody rb;
    private Rigidbody carrier;
    private Rigidbody support;
    private float carrierSpeed = -1f;
    private bool onGround;
    private bool unlocked;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.sleepThreshold = 0f;
    }

    void OnEnable()
    {
        allPieces.Add(this);
    }

    void OnDisable()
    {
        allPieces.Remove(this);
    }

    void FixedUpdate()
    {
        support = carrier;

        if (IsControlled())
        {
            unlocked = false;
        }
        else if (carrier != null && carrierSpeed > moveThreshold)
        {
            Unlock();
            Vector3 velocity = rb.linearVelocity;
            Vector3 carrierVel = carrier.linearVelocity;
            velocity.x = carrierVel.x;
            velocity.z = carrierVel.z;
            rb.linearVelocity = velocity;
        }
        else if (onGround)
        {
            Lock();
        }

        carrier = null;
        carrierSpeed = -1f;
        onGround = false;
    }

    void OnCollisionStay(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                onGround = true;
                Rigidbody other = collision.rigidbody;
                if (other != null && other != rb)
                {
                    Vector3 otherVel = other.linearVelocity;
                    float speed = new Vector2(otherVel.x, otherVel.z).magnitude;
                    if (speed > carrierSpeed)
                    {
                        carrier = other;
                        carrierSpeed = speed;
                    }
                }
                return;
            }
        }
    }

    public static void LaunchRiders(Rigidbody body, float upSpeed)
    {
        LaunchRiders(body, upSpeed, 0);
    }

    static void LaunchRiders(Rigidbody body, float upSpeed, int depth)
    {
        if (body == null || depth >= maxStackDepth)
        {
            return;
        }

        foreach (PieceCarry piece in allPieces)
        {
            if (piece.support == body && piece.rb != body && piece.rb.worldCenterOfMass.y > body.worldCenterOfMass.y)
            {
                Vector3 velocity = piece.rb.linearVelocity;
                velocity.y = upSpeed;
                piece.rb.linearVelocity = velocity;
                LaunchRiders(piece.rb, upSpeed, depth + 1);
            }
        }
    }

    bool IsControlled()
    {
        PlayerController playerScript = GetComponent<PlayerController>();
        return playerScript != null && playerScript.canMove;
    }

    void Unlock()
    {
        if ((rb.constraints & moveLock) != 0)
        {
            rb.constraints &= ~moveLock;
            unlocked = true;
        }
    }

    void Lock()
    {
        if (!unlocked)
        {
            return;
        }
        unlocked = false;
        Vector3 velocity = rb.linearVelocity;
        velocity.x = 0f;
        velocity.z = 0f;
        rb.linearVelocity = velocity;
        rb.constraints |= moveLock;
    }
}
