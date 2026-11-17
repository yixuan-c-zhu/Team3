using UnityEngine;

public class CutScraps : MonoBehaviour
{
    public float snipTime = 0.3f;
    public float scissorsSize = 4.5f;
    public int trailScraps = 4;
    public int endScraps = 7;

    CutController cut;
    LevelManager level;
    PaperBits bits;
    Sprite[] blue;
    Sprite[] orange;
    Sprite[] cores;
    SpriteRenderer scissors;

    CutController.State lastState;
    int lastCuts;
    float clock = 1f;
    int dropped;
    Vector3 from;
    Vector3 to;
    Vector3 normal;
    float floor;
    Sprite[] pieceScraps;

    void Start()
    {
        cut = FindFirstObjectByType<CutController>();
        level = FindFirstObjectByType<LevelManager>();
        bits = GetComponent<PaperBits>();
        blue = new Sprite[2];
        orange = new Sprite[2];
        cores = new Sprite[2];
        for (int i = 0; i < 2; i++)
        {
            blue[i] = PaperFeel.PaintScrap(51 + i, PaperFeel.Blue);
            orange[i] = PaperFeel.PaintScrap(61 + i, PaperFeel.Orange);
            cores[i] = PaperFeel.PaintScrap(71 + i, PaperFeel.Kraft);
        }
        scissors = new GameObject("Snip").AddComponent<SpriteRenderer>();
        scissors.transform.SetParent(transform, false);
        scissors.sprite = CutCountView.MakeScissors();
        scissors.color = PaperFeel.Ink;
        scissors.enabled = false;
        if (cut != null) lastState = cut.state;
        if (level != null) lastCuts = level.cutsLeft;
    }

    void LateUpdate()
    {
        if (cut == null || level == null || bits == null) return;
        if (lastState == CutController.State.Choosing && cut.state == CutController.State.Normal && level.cutsLeft < lastCuts)
        {
            Play();
        }
        lastState = cut.state;
        lastCuts = level.cutsLeft;
        Animate();
    }

    void Play()
    {
        CuttablePiece piece = cut.player;
        if (piece == null || piece.shape == null || piece.shape.faces == null) return;
        ConvexShape shape = piece.shape;
        int face = -1;
        for (int i = shape.faces.Count - 1; i >= 0; i--)
        {
            if (shape.IsCutFace(i))
            {
                face = i;
                break;
            }
        }
        if (face < 0) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3[] points = shape.faces[face];
        float best = -1f;
        for (int i = 0; i < points.Length; i++)
        {
            for (int j = i + 1; j < points.Length; j++)
            {
                Vector3 a = piece.transform.TransformPoint(points[i]);
                Vector3 b = piece.transform.TransformPoint(points[j]);
                float d = ((Vector2)cam.WorldToScreenPoint(a) - (Vector2)cam.WorldToScreenPoint(b)).sqrMagnitude;
                if (d > best)
                {
                    best = d;
                    from = a;
                    to = b;
                }
            }
        }
        if (best < 0f) return;

        Vector3 toCamera = -cam.transform.forward;
        Vector3 lift = toCamera * 0.6f;
        Vector3 along = (to - from).normalized;
        from += lift - along * 0.35f;
        to += lift + along * 0.35f;
        normal = cut.CutNormal;
        floor = PaperBits.FloorBelow((from + to) * 0.5f, (from.y + to.y) * 0.5f - 6f);

        PieceColor color = piece.GetComponent<PieceColor>();
        pieceScraps = color != null && color.currentColor == PieceColor.PieceColorType.Orange ? orange : blue;
        clock = 0f;
        dropped = 0;
        scissors.enabled = true;
    }

    void Animate()
    {
        if (clock >= 1f) return;
        clock = Mathf.Min(1f, clock + Time.deltaTime / snipTime);
        Camera cam = Camera.main;
        if (cam == null) return;

        float travel = Mathf.SmoothStep(0f, 1f, clock);
        Vector3 position = Vector3.Lerp(from, to, travel);
        Vector2 screenFrom = cam.WorldToScreenPoint(from);
        Vector2 screenTo = cam.WorldToScreenPoint(to);
        Vector2 way = screenTo - screenFrom;
        float angle = Mathf.Atan2(way.y, way.x) * Mathf.Rad2Deg - 90f;
        float snip = 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(clock * Mathf.PI * 3f));
        float grow = clock < 0.15f ? clock / 0.15f : (clock > 0.85f ? (1f - clock) / 0.15f : 1f);
        Transform t = scissors.transform;
        t.position = position;
        t.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, angle);
        t.localScale = new Vector3(snip, 1f, 1f) * scissorsSize * grow;

        int due = Mathf.FloorToInt(clock * trailScraps);
        while (dropped < due)
        {
            dropped++;
            Drop(position, 1, 0.8f);
        }
        if (clock >= 1f)
        {
            scissors.enabled = false;
            Drop(position, endScraps, 1.2f);
            Drop((from + to) * 0.5f, 3, 1f);
        }
    }

    void Drop(Vector3 position, int count, float strength)
    {
        for (int i = 0; i < count; i++)
        {
            Sprite[] pool = Random.value < 0.4f ? cores : pieceScraps;
            Vector3 side = normal * (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.6f, 2f);
            Vector3 velocity = (side + Random.insideUnitSphere * 0.8f + Vector3.up * Random.Range(1.8f, 3f)) * strength;
            bits.Spawn(pool[Random.Range(0, pool.Length)], position, velocity, Random.Range(1.1f, 1.6f), Random.Range(0.9f, 1.3f), floor);
        }
    }
}
