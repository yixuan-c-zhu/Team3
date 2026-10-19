using UnityEngine;

/// <summary>
/// An area around an obstacle. While the player is inside it, the HintCard explains how to get past.
/// After Stuck Delay seconds inside, the card also shows the Stuck Hint.
/// The hint hides while the camera is taken over (for example while aiming).
/// </summary>
public class TutorialHint : MonoBehaviour
{
    [Tooltip("Small line above the title.")]
    public string label = "HOW TO GET PAST";
    [Tooltip("What is in the player's way. Write keys in brackets, like [Tab].")]
    public string title;
    [Tooltip("One short step per line. Write keys in brackets, like [C].")]
    [TextArea(2, 4)]
    public string[] steps;
    [Tooltip("Extra help shown after the player has been here for Stuck Delay seconds.")]
    [TextArea(2, 4)]
    public string stuckHint;
    public float stuckDelay = 12f;
    [Tooltip("Size of the area, centered on this object. The hint shows while the player's center is inside.")]
    public Vector3 size = new Vector3(8f, 10f, 8f);

    static TutorialHint current;

    private CutController cutController;
    private CameraRig camRig;
    private HintCard card;
    private bool inside;
    private float timeInside;

    void Awake()
    {
        cutController = FindFirstObjectByType<CutController>();
        camRig = FindFirstObjectByType<CameraRig>();
        card = FindFirstObjectByType<HintCard>(FindObjectsInactive.Include);
    }

    void Update()
    {
        if (card == null || cutController == null || cutController.player == null) return;

        bool nowInside = Contains(cutController.player.transform.position);
        if (nowInside && !inside)
        {
            timeInside = 0f;
            current = this;
        }
        inside = nowInside;

        if (!inside)
        {
            if (current == this) current = null;
            card.Hide(this);
            return;
        }
        if (current == null) current = this;
        if (current != this) return;

        timeInside += Time.deltaTime;
        if (camRig != null && !camRig.canControl)
        {
            card.Hide(this);
            return;
        }
        card.Show(this, timeInside >= stuckDelay);
    }

    public bool Contains(Vector3 point)
    {
        Vector3 d = point - transform.position;
        return Mathf.Abs(d.x) <= size.x * 0.5f && Mathf.Abs(d.y) <= size.y * 0.5f && Mathf.Abs(d.z) <= size.z * 0.5f;
    }

    void OnDestroy()
    {
        if (current == this) current = null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.84f, 0.29f, 0.5f);
        Gizmos.DrawWireCube(transform.position, size);
    }
}
