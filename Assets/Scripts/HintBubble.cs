using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A short, friendly hint in a speech bubble next to the player, shown at the moment the level design asks for it
/// (for example "Press [Tab] and have a look?" 2 seconds after falling into the pit). Put one where it's needed,
/// pick its Situation, and HintBubbleView shows the most important bubble that is ready.
/// Write keys in square brackets, like [Tab], and the bubble highlights them.
/// In the text, {color} becomes the player's color and {other} the other color.
/// </summary>
public class HintBubble : MonoBehaviour
{
    /// <summary>What has to be true for the bubble to show.</summary>
    public enum Situation
    {
        /// <summary>The player is in the area.</summary>
        InArea,
        /// <summary>In the area, and too tall to fit under Ceiling.</summary>
        UnderLowCeiling,
        /// <summary>In the area, too wide for the gap, and the view doesn't show the gap.</summary>
        TooWideForGap,
        /// <summary>In the area, too wide for the gap, and the view looks across the gap so it shows.</summary>
        GapInView,
        /// <summary>In the area, and F has a piece to switch into.</summary>
        CanSwitchPiece,
        /// <summary>In the area, and the player just pressed Tab to go from 2D to 3D.</summary>
        SwitchedTo3D,
        /// <summary>In the area, and Door is still closed.</summary>
        DoorLocked,
        /// <summary>The player stands on Aim Zone's spot, with nothing left there yet.</summary>
        OnPieceSpot,
        /// <summary>A slice that fits is waiting on Aim Zone's spot.</summary>
        SliceLeft,
        /// <summary>The player is lining the slice up from Aim Zone's pad.</summary>
        Aiming,
        /// <summary>Aim Zone's slice has become real, and the player has control again.</summary>
        BridgeBuilt,
        /// <summary>In the area, Aim Zone's bridge is built, and the player is the same color as its glass.</summary>
        SameColorAsBridge,
        /// <summary>On Aim Zone's pad in the aim view, but no slice that fits is on the spot (for example, stepped on the pad by accident).</summary>
        AimingWithoutSlice
    }

    public enum ViewNeeded { Any, Only2D, Only3D }

    public enum Anchor { Player, Target, Screen }

    /// <summary>How an icon moves. Press: a key being pressed. Drag: a mouse sliding sideways. Orbit: a mouse moving in a circle.</summary>
    public enum Motion { None, Press, Drag, Orbit }

    [System.Serializable]
    public class Icon
    {
        public Sprite sprite;
        [Tooltip("Press: a key being pressed. Drag: a mouse sliding sideways. Orbit: a mouse moving in a circle.")]
        public Motion motion = Motion.Press;
    }

    public static readonly List<HintBubble> all = new List<HintBubble>();

    [Header("What it says")]
    [Tooltip("The bold first line. Write keys in brackets, like [C].")]
    public string headline;
    [Tooltip("Smaller text under the headline. Write keys in brackets, like [C].")]
    [TextArea(2, 4)]
    public string message;
    [Tooltip("Shown to the left of the text.")]
    public Icon[] icons;
    [Tooltip("Numbers the icons 1, 2, ... for steps done in order. While the player is cutting, step 1 dims.")]
    public bool numberIcons;

    [Header("When it shows")]
    public Situation situation = Situation.InArea;
    [Tooltip("Size of the area, centered on this object. The player's center has to be inside. (0, 0, 0) means anywhere.")]
    public Vector3 size = new Vector3(8f, 4f, 6f);
    public ViewNeeded view = ViewNeeded.Any;
    [Tooltip("Seconds the situation has to last before the bubble appears.")]
    public float delay;
    [Tooltip("More than 0: once it appears, the bubble stays this many seconds and then goes away for good.")]
    public float duration;
    [Tooltip("Pressing one of these keys finishes the hint for good.")]
    public KeyCode[] doneKeys;
    [Tooltip("Finishes the hint for good once the player has turned the 3D camera by dragging with the right mouse button.")]
    public bool doneAfterCameraDrag;
    [Tooltip("Keep showing while the player is drawing a cut. Other bubbles hide so the cut indicator stands out.")]
    public bool showWhileCutting;
    [Tooltip("Stop showing once Aim Zone's slice has become real.")]
    public bool onlyBeforeBuilt;
    [Tooltip("When two bubbles are ready at once, the higher one shows.")]
    public int priority;

    [Header("Situation settings")]
    [Tooltip("Under Low Ceiling: the ceiling the player has to fit under.")]
    public Collider ceiling;
    [Tooltip("Too Wide For Gap and Gap In View: how wide the gap is.")]
    public float gapWidth = 1.4f;
    [Tooltip("Too Wide For Gap and Gap In View: the direction the gap's width is measured in.")]
    public Vector3 gapAxis = Vector3.forward;
    [Tooltip("Door Locked: the door.")]
    public Door door;
    [Tooltip("On Piece Spot, Slice Left, Aiming, Bridge Built, Same Color As Bridge and Only Before Built: the aim setup.")]
    public AimZone aimZone;

    [Header("Where it shows")]
    public Anchor anchor = Anchor.Player;
    [Tooltip("Anchor = Target: the bubble points at this object instead of the player.")]
    public Transform target;
    [Tooltip("Anchor = Screen: the center of the bubble, from (0, 0) at the bottom left to (1, 1) at the top right.")]
    public Vector2 screenPoint = new Vector2(0.5f, 0.8f);
    [Tooltip("Optional. A bouncing arrow points at this object while the bubble shows.")]
    public Transform pointAt;

    const float switchWindow = 0.6f;
    const float dragToFinish = 160f;

    private CutController cutController;
    private CameraRig camRig;
    private LevelManager levelManager;

    private bool wants;
    private float heldFor;
    private bool finished;
    private bool latched;
    private float shownFor;
    private bool wasIn2D;
    private float sinceSwitch = Mathf.Infinity;
    private bool gapSeen;
    private float clearance = -1f;
    private bool rightHeld;
    private Vector3 lastMouse;
    private float dragged;

    /// <summary>True when the bubble should be on screen now (HintBubbleView still checks cut mode, pauses and priority).</summary>
    public bool IsReady
    {
        get { return !finished && (latched || (wants && heldFor >= delay)); }
    }

    public bool IsFinished
    {
        get { return finished; }
    }

    void Awake()
    {
        cutController = FindFirstObjectByType<CutController>();
        camRig = FindFirstObjectByType<CameraRig>();
        levelManager = FindFirstObjectByType<LevelManager>();
        wasIn2D = camRig == null || camRig.is2D;
    }

    void OnEnable()
    {
        all.Add(this);
        HintBubbleView.Ensure();
    }

    void OnDisable()
    {
        all.Remove(this);
    }

    void Update()
    {
        if (cutController == null || cutController.player == null || camRig == null)
        {
            wants = false;
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        if (wasIn2D && !camRig.is2D)
        {
            sinceSwitch = 0f;
        }
        else
        {
            sinceSwitch += deltaTime;
        }
        wasIn2D = camRig.is2D;

        if (finished)
        {
            wants = false;
            return;
        }

        wants = Check();
        if (!wants)
        {
            heldFor = 0f;
            rightHeld = false;
            return;
        }

        // The delay only runs while the game runs (or while cutting, for bubbles that show then).
        bool cutting = cutController.state == CutController.State.Drawing;
        if (Time.timeScale > 0f || (cutting && showWhileCutting))
        {
            heldFor += deltaTime;
        }
        CheckDone();
    }

    /// <summary>Called by HintBubbleView every frame the bubble is on screen.</summary>
    public void WhileShown(float deltaTime)
    {
        shownFor += deltaTime;
        if (duration > 0f)
        {
            latched = true;
            if (shownFor >= duration)
            {
                Finish();
            }
        }
    }

    /// <summary>Retires the hint for good.</summary>
    public void Finish()
    {
        finished = true;
        latched = false;
        wants = false;
    }

    bool Check()
    {
        Collider body = cutController.player.GetComponent<Collider>();
        if (body == null || !body.enabled)
        {
            return false;
        }
        Bounds bounds = body.bounds;

        if (view == ViewNeeded.Only2D && !camRig.is2D) return false;
        if (view == ViewNeeded.Only3D && camRig.is2D) return false;
        if (onlyBeforeBuilt && aimZone != null && aimZone.IsBuilt) return false;
        if (!InArea(bounds.center)) return false;

        switch (situation)
        {
            case Situation.InArea:
                return true;
            case Situation.UnderLowCeiling:
                return ceiling != null && bounds.size.y > Clearance() - 0.02f;
            case Situation.TooWideForGap:
            {
                bool gapShows = GapInView();
                return TooWide(bounds) && !gapShows;
            }
            case Situation.GapInView:
            {
                bool gapShows = GapInView();
                return TooWide(bounds) && gapShows;
            }
            case Situation.CanSwitchPiece:
                return cutController.HasLeftover;
            case Situation.SwitchedTo3D:
                return !camRig.is2D && sinceSwitch < switchWindow;
            case Situation.DoorLocked:
                return door != null && !door.isOpen;
            case Situation.OnPieceSpot:
                return aimZone != null && aimZone.pieceSpot != null && !aimZone.IsBuilt && !aimZone.IsBusy
                    && aimZone.pieceSpot.Holds(bounds) && aimZone.pieceSpot.GetPiece() == null
                    && (levelManager == null || levelManager.CanCut());
            case Situation.SliceLeft:
                return aimZone != null && !aimZone.IsBusy && aimZone.HasGoodSlice;
            case Situation.Aiming:
                return aimZone != null && aimZone.IsAiming && aimZone.HasGoodSlice;
            case Situation.BridgeBuilt:
                return aimZone != null && aimZone.IsBuilt && !aimZone.IsBusy;
            case Situation.SameColorAsBridge:
                return aimZone != null && aimZone.IsBuilt && aimZone.PlacedGlass != null
                    && IsPlayerColor(aimZone.PlacedGlass.obstacleColor);
            case Situation.AimingWithoutSlice:
                return aimZone != null && aimZone.IsAiming && !aimZone.HasGoodSlice;
        }
        return false;
    }

    /// <summary>True for situations that happen in the aim view, while the camera is taken over.</summary>
    public bool InAimView
    {
        get { return situation == Situation.Aiming || situation == Situation.AimingWithoutSlice; }
    }

    void CheckDone()
    {
        if (doneKeys != null)
        {
            foreach (KeyCode key in doneKeys)
            {
                if (Input.GetKeyDown(key))
                {
                    Finish();
                    return;
                }
            }
        }

        if (!doneAfterCameraDrag)
        {
            return;
        }
        bool canDrag = camRig.canControl && !camRig.is2D && Time.timeScale > 0f;
        bool held = canDrag && Input.GetMouseButton(1);
        if (held && rightHeld)
        {
            float pixels = (Input.mousePosition - lastMouse).magnitude;
            dragged += pixels * 1080f / Mathf.Max(1, Screen.height);
            if (dragged >= dragToFinish)
            {
                Finish();
            }
        }
        rightHeld = held;
        lastMouse = Input.mousePosition;
    }

    public bool InArea(Vector3 point)
    {
        if (size == Vector3.zero)
        {
            return true;
        }
        Vector3 d = point - transform.position;
        return Mathf.Abs(d.x) <= size.x * 0.5f && Mathf.Abs(d.y) <= size.y * 0.5f && Mathf.Abs(d.z) <= size.z * 0.5f;
    }

    bool TooWide(Bounds bounds)
    {
        return CutHint.Extent(bounds, gapAxis.normalized) * 2f > gapWidth;
    }

    // The gap shows when the camera looks across it (a side view in 2D, or turned far enough in 3D).
    // Two limits keep the bubble from flickering when the view sits right at the edge.
    bool GapInView()
    {
        Vector3 forward = camRig.GetCamForward();
        forward.y = 0f;
        float facing = Mathf.Abs(Vector3.Dot(forward.normalized, gapAxis.normalized));
        gapSeen = facing < (gapSeen ? 0.75f : 0.55f);
        return gapSeen;
    }

    // Height between the ceiling's underside and the floor below it.
    float Clearance()
    {
        if (clearance < 0f)
        {
            Bounds top = ceiling.bounds;
            Vector3 from = new Vector3(top.center.x, top.min.y - 0.01f, top.center.z);
            float floor = top.min.y - 100f;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody == null)
                {
                    floor = Mathf.Max(floor, hit.point.y);
                }
            }
            clearance = top.min.y - floor;
        }
        return clearance;
    }

    bool IsPlayerColor(PieceColor.PieceColorType color)
    {
        PieceColor playerColor = cutController.player.GetComponent<PieceColor>();
        return playerColor != null && playerColor.currentColor == color;
    }

    public string GetHeadline()
    {
        return FillColors(headline);
    }

    public string GetMessage()
    {
        return FillColors(message);
    }

    string FillColors(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
        {
            return text;
        }
        PieceColor playerColor = cutController != null && cutController.player != null
            ? cutController.player.GetComponent<PieceColor>()
            : null;
        bool blue = playerColor == null || playerColor.currentColor == PieceColor.PieceColorType.Blue;
        return text.Replace("{color}", ColorName(blue)).Replace("{other}", ColorName(!blue));
    }

    static string ColorName(bool blue)
    {
        return blue ? "<color=#5B9BFF>blue</color>" : "<color=#FF9A33>orange</color>";
    }

    /// <summary>The world point the bubble's tail points at. False for Anchor = Screen.</summary>
    public bool TryGetAnchor(out Vector3 point)
    {
        point = Vector3.zero;
        if (anchor == Anchor.Player && cutController != null && cutController.player != null)
        {
            Bounds bounds = cutController.player.GetComponent<Collider>().bounds;
            point = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            return true;
        }
        if (anchor == Anchor.Target && target != null)
        {
            point = TopOf(target);
            return true;
        }
        return false;
    }

    /// <summary>The top center of an object's visible bounds.</summary>
    public static Vector3 TopOf(Transform thing)
    {
        Renderer rend = thing.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Bounds bounds = rend.bounds;
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        Collider col = thing.GetComponentInChildren<Collider>();
        if (col != null)
        {
            Bounds bounds = col.bounds;
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        return thing.position;
    }

    void OnDrawGizmos()
    {
        if (size == Vector3.zero)
        {
            return;
        }
        Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.5f);
        Gizmos.DrawWireCube(transform.position, size);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.Label(transform.position + Vector3.up * (size.y * 0.5f + 0.3f), name);
    }
#endif
}
