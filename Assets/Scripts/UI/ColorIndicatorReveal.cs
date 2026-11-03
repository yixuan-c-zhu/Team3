using UnityEngine;

/// <summary>
/// Put on the color indicator in a scene with a CheckpointManager. It stays hidden until the player reaches
/// First Room, then pops in, and it gives a little bump each time the player's color changes.
/// HUD fills in the indicator's text and swatch (its Color Text and Color Swatch fields).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ColorIndicatorReveal : MonoBehaviour
{
    [Tooltip("The first room the indicator shows in, counting from 0. 1 = the second room.")]
    public int firstRoom = 1;
    [Tooltip("Seconds it takes to pop in.")]
    public float popTime = 0.35f;
    [Tooltip("How much bigger it gets for a moment when the player's color changes. 0.15 = 15%.")]
    public float bump = 0.15f;
    public float bumpTime = 0.35f;

    private CheckpointManager checkpoints;
    private CutController cutController;
    private CanvasGroup group;
    private Vector3 baseScale;
    private float shown;
    private float sinceChange = Mathf.Infinity;
    private bool knowsColor;
    private PieceColor.PieceColorType lastColor;

    void Awake()
    {
        checkpoints = FindFirstObjectByType<CheckpointManager>();
        cutController = FindFirstObjectByType<CutController>();
        group = GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        baseScale = transform.localScale;
        Apply();
    }

    void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        bool wanted = checkpoints == null || checkpoints.CurrentRoom >= firstRoom;
        shown = Mathf.MoveTowards(shown, wanted ? 1f : 0f, deltaTime / Mathf.Max(0.01f, popTime));

        PieceColor color = cutController != null && cutController.player != null
            ? cutController.player.GetComponent<PieceColor>()
            : null;
        if (color != null)
        {
            if (knowsColor && color.currentColor != lastColor && shown > 0f)
            {
                sinceChange = 0f;
            }
            lastColor = color.currentColor;
            knowsColor = true;
        }
        sinceChange += deltaTime;
        Apply();
    }

    void Apply()
    {
        group.alpha = shown;
        float pop = Mathf.LerpUnclamped(0.8f, 1f, BackOut(shown));
        float extra = sinceChange < bumpTime ? Mathf.Sin(sinceChange / bumpTime * Mathf.PI) * bump : 0f;
        transform.localScale = baseScale * (pop + extra);
    }

    static float BackOut(float t)
    {
        const float overshoot = 1.70158f;
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }
}
