using UnityEngine;

public class PaperTrail : MonoBehaviour
{
    public float spacing = 0.6f;
    public float minSpeed = 1.5f;
    public float landingSpeed = 5f;
    public int landingBurst = 5;
    public int jumpBurst = 2;
    public float minSize = 0.95f;
    public float maxSize = 1.35f;

    CutController cut;
    PaperBits bits;
    Sprite[] blue;
    Sprite[] orange;
    Vector3 lastPosition;
    float travelled;
    bool wasGrounded = true;
    float fallSpeed;
    bool started;

    void Start()
    {
        cut = FindFirstObjectByType<CutController>();
        bits = GetComponent<PaperBits>();
        blue = new Sprite[3];
        orange = new Sprite[3];
        for (int i = 0; i < 3; i++)
        {
            blue[i] = PaperFeel.PaintScrap(11 + i, PaperFeel.Blue);
            orange[i] = PaperFeel.PaintScrap(31 + i, PaperFeel.Orange);
        }
    }

    void LateUpdate()
    {
        if (cut == null || bits == null || cut.player == null || Time.timeScale <= 0f) return;
        CuttablePiece piece = cut.player;
        Collider body = piece.GetComponent<Collider>();
        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (body == null || rb == null || !body.enabled) return;

        Vector3 position = piece.transform.position;
        Vector3 step = position - lastPosition;
        step.y = 0f;
        lastPosition = position;
        if (!started || step.magnitude > 1.5f)
        {
            started = true;
            travelled = 0f;
            return;
        }

        Bounds bounds = body.bounds;
        bool grounded = Physics.Raycast(new Vector3(bounds.center.x, bounds.min.y + 0.05f, bounds.center.z), Vector3.down,
            0.15f, ~0, QueryTriggerInteraction.Ignore);
        Vector3 velocity = rb.linearVelocity;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

        if (grounded && flat.magnitude > minSpeed)
        {
            travelled += step.magnitude;
            while (travelled >= spacing)
            {
                travelled -= spacing;
                Emit(piece, bounds, flat.normalized, 1f);
            }
        }
        if (grounded && !wasGrounded && fallSpeed > landingSpeed)
        {
            for (int i = 0; i < landingBurst; i++) Emit(piece, bounds, Random.insideUnitSphere, 1.3f);
        }
        if (!grounded && wasGrounded && velocity.y > 2f)
        {
            for (int i = 0; i < jumpBurst; i++) Emit(piece, bounds, -flat.normalized, 0.8f);
        }
        fallSpeed = grounded ? 0f : Mathf.Max(fallSpeed, -velocity.y);
        wasGrounded = grounded;
    }

    void Emit(CuttablePiece piece, Bounds bounds, Vector3 direction, float strength)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
        {
            Vector2 any = Random.insideUnitCircle.normalized;
            direction = new Vector3(any.x, 0f, any.y);
        }
        direction.Normalize();
        Vector3 reach = Vector3.Scale(direction, bounds.extents);
        Vector3 spot = new Vector3(bounds.center.x, bounds.min.y + 0.08f, bounds.center.z) - reach * 0.9f;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Vector3 velocity = -direction * Random.Range(0.4f, 1.1f) * strength
            + side * Random.Range(-0.5f, 0.5f)
            + Vector3.up * Random.Range(1.3f, 2.3f) * strength;

        PieceColor color = piece.GetComponent<PieceColor>();
        bool isOrange = color != null && color.currentColor == PieceColor.PieceColorType.Orange;
        Sprite[] pool = isOrange ? orange : blue;
        bits.Spawn(pool[Random.Range(0, pool.Length)], spot, velocity, Random.Range(minSize, maxSize), Random.Range(0.8f, 1.1f),
            bounds.min.y + 0.02f);
    }
}
