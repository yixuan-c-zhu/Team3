using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class AimZone : MonoBehaviour
{
    public static List<AimZone> allZones = new List<AimZone>();

    public PieceSpot pieceSpot;
    public Transform target;
    public LineRenderer targetOutline;

    public Vector3 pieceSize = new Vector3(2f, 0.25f, 2f);
    public float sizeTolerance = 0.4f;
    public float maxUnevenness = 1.2f;
    public bool alignTop = true;
    [Tooltip("Any cut shape can become the thing: the outline shows the player's own piece grown across to the Target, and lining it up turns that piece into it. Piece Size then only sets how wide the piece must be along the Target's long side (at least half). Off: the piece has to match Piece Size (the Tutorial).")]
    public bool anyShape = false;
    [Tooltip("Any Shape with Align Top: refuse a piece whose grown top would end more than Max Unevenness below the Target's top at the far end (the end away from the spot), where the player could not climb out.")]
    public bool refuseDeadEnds = false;

    public float cameraFov = 60f;
    public float mouseSpeed = 0.15f;
    public float matchNeeded = 0.8f;

    public bool aimAssist = true;
    public float assistStart = 0.6f;
    public float assistTime = 0.8f;
    public Color glowColor = new Color(1f, 0.92f, 0.55f);
    public bool showSpotGuide = true;

    public Material blueGlass;
    public Material orangeGlass;
    public float warnDistance = 2f;
    public float stopTime = 0.15f;
    public float stopSpeed = 0.3f;
    [Tooltip("Show Spot Message while the player stands on the spot. Turn off when a HintBubble says it instead.")]
    public bool showSpotMessage = true;
    [Tooltip("Warn the player before they step on glass of their own color, and explain the glass rule once it is built. Turn off when a HintBubble teaches it instead.")]
    public bool showGlassHints = true;
    [TextArea] public string aimMessage = "Line up your piece with the outline: {0}%\n<size=70%>Drag to aim. Walk off the pad to step back.</size>";
    [TextArea] public string previewMessage = "<size=70%>Your slice is {0}, so it will turn into {0} glass.</size>";
    [TextArea] public string glassMessage = "<size=70%>It's {0} glass: {0} falls through it, {1} can stand on it.</size>";
    [TextArea] public string fallMessage = "You're {0}, so you'll fall through this {0} glass.\n<size=70%>Press {2} to turn {1} and stand on it.</size>";
    [TextArea] public string noPieceMessage = "Nothing to line up yet. Leave a piece of yourself in the right spot first.\n<size=70%>Walk off the pad to step back.</size>";
    [TextArea] public string wrongSizeMessage = "This piece is the wrong size to fill the outline.\n<size=70%>Walk off the pad to step back.</size>";
    public string lockedMessage = "Lined up!";
    public string doneMessage = "It's real!";
    public string leaveMessage = "Step back onto the pad to aim again.";

    [TextArea] public string spotMessage = "This ledge can hold a slice of you.\n<size=70%>In 2D, press C and cut along the orange line at your feet.</size>";
    [TextArea] public string fitsMessage = "That slice fits!\n<size=70%>Keep the other piece (click it or press {0}) to leave the slice here.</size>";
    [TextArea] public string wrongWayMessage = "This slice is standing up.\n<size=70%>{0} and cut across your body, along the orange line at your feet.</size>";
    [TextArea] public string tiltMessage = "Your cut is tilted.\n<size=70%>{0}, switch to 2D and cut level, along the orange line.</size>";
    [TextArea] public string thickMessage = "This slice is too thick.\n<size=70%>{0} and cut lower, along the orange line at your feet.</size>";
    [TextArea] public string thinMessage = "This slice is too thin.\n<size=70%>{0} and cut a little higher, along the orange line.</size>";
    [TextArea] public string narrowMessage = "This slice is too narrow to fill the outline.\n<size=70%>An earlier cut made your body narrower than this spot needs. Press R to restart.</size>";
    [TextArea] public string deadEndMessage = "This ramp ends too low to climb out.\n<size=70%>{0} and cut level, or make the cut higher on the far side.</size>";
    [TextArea] public string shortMessage = "This piece is too narrow to reach across.\n<size=70%>{0} and leave a wider piece here.</size>";
    public string recutAction = "Press C to cancel";
    public string restartAction = "Press R to restart";
    public string stepBackNote = "<size=70%>Walk off the pad to step back.</size>";

    const float blendTime = 0.8f;
    const float lockTime = 0.45f;
    const float growTime = 1f;
    const float showTime = 1.4f;
    const float showHoldTime = 0.5f;
    const float showPullBack = 5f;
    const float showRise = 2.5f;
    const float startSideOffset = 25f;
    const float startUpOffset = 14f;
    const float sideRange = 60f;
    const float minUpAngle = 8f;
    const float maxUpAngle = 65f;
    const float goalTolerance = 0.1f;
    const float glowWidthScale = 1.8f;
    const float flashWidthScale = 3f;
    const float pieceFlash = 0.85f;
    const float guideWidth = 0.05f;
    const float guideMargin = 0.04f;
    const float guideLift = 0.02f;
    const float minSpanShare = 0.5f;
    const float minGrowChange = 0.1f;

    enum Step { None, Aiming, LockingOn, Growing, Showing, Leaving }
    enum Problem { None, WrongWay, Tilted, Thick, Thin, Narrow, WrongSize, Short, DeadEnd }

    private CameraRig camRig;
    private CutController cutController;
    private PieceControlManager colorManager;
    private LevelManager levelManager;

    private Step step = Step.None;
    private bool done;
    private bool waitForExit;
    private bool playerInside;
    private float stillTimer;
    private bool walkedOff;
    private bool aimDragging;
    private bool startedIn2D;
    private float blend;
    private float timer;

    private CuttablePiece piece;
    private Vector3 orbitCenter;
    private float orbitDistance;
    private float sideAngle;
    private float upAngle;
    private float goalSideAngle;
    private float goalUpAngle;
    private Vector3 lastMousePos;

    private Vector3 cameraPos;
    private Quaternion cameraRot;
    private Vector3 moveFromPos;
    private Quaternion moveFromRot;
    private Vector3 moveToPos;
    private Quaternion moveToRot;

    private Vector3 aimSpot;
    private Vector3 pieceStartPos;
    private float growScale;

    private bool assisting;
    private float sideVelocity;
    private float upVelocity;

    private Material outlineMat;
    private Color outlineColor;
    private Color defaultOutlineColor;
    private float outlineWidth;
    private Renderer pieceRenderer;
    private Color pieceColor;

    private LineRenderer spotGuide;
    private bool playerWasOnSpot;

    private ColorObstacle placedGlass;
    private string shownFallMessage;

    // Any Shape: the piece as it sits on the spot, the same piece grown to where it will end up,
    // and one outline loop per face of the grown piece.
    private ConvexShape pieceShape;
    private ConvexShape goalShape;
    private readonly List<LineRenderer> shapeLines = new List<LineRenderer>();

    /// <summary>True while the player is lining the slice up from the pad.</summary>
    public bool IsAiming { get { return step == Step.Aiming; } }

    /// <summary>True from the moment the player steps on the pad until control comes back.</summary>
    public bool IsBusy { get { return step != Step.None; } }

    /// <summary>True once the slice has grown into the target (for example, the bridge is built).</summary>
    public bool IsBuilt { get { return done; } }

    /// <summary>The live overlap percentage, followed by 100% once alignment is accepted.</summary>
    public bool TryGetAlignmentPercent(out int percent)
    {
        percent = 0;
        if (step == Step.LockingOn || step == Step.Growing || step == Step.Showing)
        {
            percent = 100;
            return true;
        }
        if (step != Step.Aiming || piece == null || target == null) return false;
        Collider body = piece.GetComponent<Collider>();
        if (body == null || GetProblem(piece) != Problem.None) return false;

        percent = Mathf.RoundToInt(Mathf.Clamp01(GetMatch()) * 100f);
        return true;
    }

    /// <summary>The glass the slice turned into, or null.</summary>
    public ColorObstacle PlacedGlass { get { return placedGlass; } }

    /// <summary>True when a slice that fits the outline is waiting on the spot.</summary>
    public bool HasGoodSlice
    {
        get
        {
            if (pieceSpot == null || target == null || done)
            {
                return false;
            }
            CuttablePiece spotPiece = pieceSpot.GetPiece();
            if (spotPiece == null)
            {
                return false;
            }
            return GetProblem(spotPiece) == Problem.None;
        }
    }

    public string SliceProblem
    {
        get
        {
            if (pieceSpot == null || target == null || done)
            {
                return null;
            }
            CuttablePiece spotPiece = pieceSpot.GetPiece();
            if (spotPiece == null)
            {
                return null;
            }
            Problem problem = GetProblem(spotPiece);
            if (problem == Problem.None)
            {
                return null;
            }
            return GetProblemMessage(problem, restartAction).Split('\n')[0];
        }
    }

    public void StepBack()
    {
        if (step != Step.Aiming)
        {
            return;
        }
        waitForExit = true;
        StopAiming();
        levelManager.ShowMessage(leaveMessage);
    }

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        camRig = FindFirstObjectByType<CameraRig>();
        cutController = FindFirstObjectByType<CutController>();
        colorManager = FindFirstObjectByType<PieceControlManager>();
        levelManager = FindFirstObjectByType<LevelManager>();
    }

    void OnEnable()
    {
        allZones.Add(this);
    }

    void OnDisable()
    {
        allZones.Remove(this);
        EndAimDrag();
    }

    void Start()
    {
        DrawOutline();
        if (targetOutline == null)
        {
            return;
        }

        if (showSpotGuide)
        {
            MakeSpotGuide();
        }
        outlineMat = targetOutline.material;
        outlineColor = outlineMat.color;
        defaultOutlineColor = outlineColor;
        outlineWidth = targetOutline.widthMultiplier;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other))
        {
            return;
        }
        playerInside = true;
        stillTimer = 0f;
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other))
        {
            return;
        }
        playerInside = false;
        waitForExit = false;
        if (step == Step.Aiming)
        {
            walkedOff = true;
            StopAiming();
        }
    }

    bool CanRestartAiming()
    {
        bool ready = step == Step.None && playerInside && !done && !waitForExit && Time.timeScale > 0f
            && cutController.state == CutController.State.Normal;
        stillTimer = ready && PlayerStopped() ? stillTimer + Time.deltaTime : 0f;
        return ready && stillTimer >= stopTime;
    }

    bool PlayerStopped()
    {
        Rigidbody body = cutController.player.GetComponent<Rigidbody>();
        if (body == null)
        {
            return true;
        }
        Vector3 velocity = body.linearVelocity;
        return new Vector2(velocity.x, velocity.z).magnitude < stopSpeed && Mathf.Abs(velocity.y) < stopSpeed;
    }

    bool IsPlayer(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null)
        {
            return false;
        }
        return body.gameObject == cutController.player.gameObject;
    }

    void Update()
    {
        UpdateSpotGuide();
        UpdateGlassWarning();
        if (CanRestartAiming())
        {
            StartAiming();
        }
        if (step == Step.None || Time.timeScale == 0f)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        LockControls();
        UpdateBlend(deltaTime);

        switch (step)
        {
            case Step.Aiming:
                UpdateAiming(deltaTime);
                break;
            case Step.LockingOn:
                UpdateLockingOn(deltaTime);
                break;
            case Step.Growing:
                UpdateGrowing(deltaTime);
                break;
            case Step.Showing:
                UpdateShowing(deltaTime);
                break;
            case Step.Leaving:
                if (blend <= 0f)
                {
                    GiveBackControl();
                }
                break;
        }

        float smoothBlend = Mathf.SmoothStep(0f, 1f, blend);
        camRig.SetAim(smoothBlend, cameraPos, cameraRot, cameraFov);
    }

    void UpdateBlend(float deltaTime)
    {
        float goal = 1f;
        if (step == Step.Leaving)
        {
            goal = 0f;
        }
        blend = Mathf.MoveTowards(blend, goal, deltaTime / blendTime);
    }

    void StartAiming()
    {
        step = Step.Aiming;
        blend = 0f;
        assisting = false;
        walkedOff = false;
        aimDragging = false;
        ShowGlow(0f);
        LockControls();
        startedIn2D = camRig.is2D;
        if (camRig.is2D)
        {
            camRig.SwitchView();
        }

        piece = pieceSpot.GetPiece();
        goalShape = null;
        bool usable = piece != null && (anyShape
            ? GetProblem(piece) == Problem.None
            : HasRightSize(piece));
        if (usable)
        {
            SetUpOrbit(piece.GetComponent<Collider>().bounds);
            if (anyShape)
            {
                MakeGoalShape();
            }
        }
        else
        {
            SetUpOrbit(GetExpectedPieceBounds());
        }

        outlineColor = defaultOutlineColor;
        PieceColor.PieceColorType color;
        if (GetGlassColor(piece, out color))
        {
            outlineColor = GetGlassMaterial(color).color;
            outlineColor.a = 1f;
        }

        sideAngle = goalSideAngle + startSideOffset;
        upAngle = Mathf.Clamp(goalUpAngle + startUpOffset, minUpAngle, maxUpAngle);
        PlaceOrbitCamera();

        ShowOutline();
    }

    void UpdateAiming(float deltaTime)
    {
        bool steering = ReadAimInput();
        if (steering)
        {
            assisting = false;
        }
        if (assisting)
        {
            UpdateAssist(deltaTime);
        }
        PlaceOrbitCamera();

        if (piece == null)
        {
            levelManager.ShowMessage(noPieceMessage);
            return;
        }
        Problem problem = GetProblem(piece);
        if (problem != Problem.None)
        {
            string message = GetProblemMessage(problem, restartAction);
            if (problem != Problem.WrongSize)
            {
                message = message + "\n" + stepBackNote;
            }
            levelManager.ShowMessage(message);
            return;
        }

        HideRestartTip();

        float match = GetMatch();
        int percent = Mathf.RoundToInt(match * 100f);
        string aimText = string.Format(aimMessage, percent);
        PieceColor.PieceColorType color;
        if (GetGlassColor(piece, out color))
        {
            aimText = aimText + "\n" + string.Format(previewMessage, GetColorName(color));
        }
        levelManager.ShowMessage(aimText);
        ShowGlow(match);

        if (blend < 1f)
        {
            return;
        }
        if (assisting)
        {
            if (ReachedGoal())
            {
                StartLockingOn();
            }
        }
        else if (match >= matchNeeded)
        {
            StartLockingOn();
        }
        else if (aimAssist && !steering && match >= assistStart)
        {
            StartAssist();
        }
    }

    void StartAssist()
    {
        assisting = true;
        sideVelocity = 0f;
        upVelocity = 0f;
    }

    void UpdateAssist(float deltaTime)
    {
        float smoothTime = assistTime * 0.3f;
        sideAngle = Mathf.SmoothDamp(sideAngle, goalSideAngle, ref sideVelocity, smoothTime, Mathf.Infinity, deltaTime);
        upAngle = Mathf.SmoothDamp(upAngle, goalUpAngle, ref upVelocity, smoothTime, Mathf.Infinity, deltaTime);
    }

    bool ReachedGoal()
    {
        bool sideReached = Mathf.Abs(sideAngle - goalSideAngle) < goalTolerance;
        bool upReached = Mathf.Abs(upAngle - goalUpAngle) < goalTolerance;
        return sideReached && upReached;
    }

    void ShowGlow(float match)
    {
        if (outlineMat == null)
        {
            return;
        }
        float glow = Mathf.Clamp01(match / matchNeeded);
        glow = glow * glow;
        outlineMat.color = Color.Lerp(outlineColor, glowColor, glow);
        SetOutlineWidth(outlineWidth * Mathf.Lerp(1f, glowWidthScale, glow));
    }

    void ShowFlash(float flash)
    {
        if (outlineMat != null)
        {
            outlineMat.color = Color.Lerp(glowColor, Color.white, flash);
            SetOutlineWidth(outlineWidth * Mathf.Lerp(glowWidthScale, flashWidthScale, flash));
        }
        if (pieceRenderer != null)
        {
            pieceRenderer.material.color = Color.Lerp(pieceColor, Color.white, flash * pieceFlash);
        }
    }

    bool ReadAimInput()
    {
        if (Input.GetMouseButtonDown(0) && !GameplayUiInput.IsPointerOverControl())
        {
            aimDragging = true;
            lastMousePos = Input.mousePosition;
            Cursor.visible = false;
        }
        if (aimDragging && !Input.GetMouseButton(0))
        {
            EndAimDrag();
        }
        if (aimDragging)
        {
            Vector3 mouseMove = Input.mousePosition - lastMousePos;
            sideAngle = sideAngle + mouseMove.x * mouseSpeed;
            upAngle = upAngle - mouseMove.y * mouseSpeed;
            lastMousePos = Input.mousePosition;
        }

        sideAngle = Mathf.Clamp(sideAngle, goalSideAngle - sideRange, goalSideAngle + sideRange);
        upAngle = Mathf.Clamp(upAngle, minUpAngle, maxUpAngle);
        return aimDragging;
    }

    void EndAimDrag()
    {
        if (aimDragging)
        {
            Cursor.visible = true;
        }
        aimDragging = false;
    }

    void SetUpOrbit(Bounds pieceBounds)
    {
        orbitCenter = pieceBounds.center;
        Vector3 centerToSpot = GetAimSpot(pieceBounds) - orbitCenter;
        orbitDistance = centerToSpot.magnitude;
        goalSideAngle = Mathf.Atan2(centerToSpot.x, centerToSpot.z) * Mathf.Rad2Deg;
        goalUpAngle = Mathf.Asin(centerToSpot.y / orbitDistance) * Mathf.Rad2Deg;
    }

    void PlaceOrbitCamera()
    {
        Vector3 direction = Quaternion.Euler(-upAngle, sideAngle, 0f) * Vector3.forward;
        cameraPos = orbitCenter + direction * orbitDistance;
        cameraRot = Quaternion.LookRotation(orbitCenter - cameraPos);
    }

    void StartLockingOn()
    {
        step = Step.LockingOn;
        timer = 0f;
        assisting = false;
        FreezePiece();

        Bounds pieceBounds = piece.GetWorldShape().GetBounds();
        growScale = GetScale(pieceBounds.size);
        aimSpot = GetAimSpot(pieceBounds);

        moveFromPos = cameraPos;
        moveFromRot = cameraRot;
        moveToPos = aimSpot;
        moveToRot = Quaternion.LookRotation(pieceBounds.center - aimSpot);
        levelManager.ShowMessage(lockedMessage);

        pieceRenderer = piece.GetComponent<Renderer>();
        pieceColor = pieceRenderer.material.color;
        ShowFlash(1f);
    }

    void UpdateLockingOn(float deltaTime)
    {
        bool finished = MoveCamera(deltaTime, lockTime);
        float fade = 1f - Mathf.Clamp01(timer / lockTime);
        ShowFlash(fade * fade);
        if (finished)
        {
            StartGrowing();
        }
    }

    void StartGrowing()
    {
        step = Step.Growing;
        timer = 0f;
        ShowFlash(0f);
        pieceRenderer = null;
        pieceStartPos = piece.transform.position;
        piece.GetComponent<Collider>().enabled = false;
    }

    void UpdateGrowing(float deltaTime)
    {
        timer = timer + deltaTime;
        float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer / growTime));
        float scale = Mathf.Lerp(1f, growScale, progress);

        Vector3 spotToPiece = pieceStartPos - aimSpot;
        piece.transform.position = aimSpot + spotToPiece * scale;
        piece.transform.localScale = Vector3.one * scale;

        if (timer >= growTime)
        {
            MakePieceReal();
            StartShowing();
        }
    }

    void MakePieceReal()
    {
        ConvexShape grownShape = piece.GetWorldShape();
        piece.transform.localScale = Vector3.one;
        piece.SetWorldShape(grownShape);
        piece.GetComponent<Collider>().enabled = true;
        piece.gameObject.name = "Placed Piece";

        PieceColor.PieceColorType color;
        if (GetGlassColor(piece, out color))
        {
            MakeGlass(piece.gameObject, color);
        }

        Destroy(piece.GetComponent<PieceCarry>());
        Destroy(piece);
        piece = null;

        HideOutline();
    }

    void MakeGlass(GameObject placed, PieceColor.PieceColorType color)
    {
        Destroy(placed.GetComponent<PieceColor>());
        Fadeable fadeable = placed.GetComponent<Fadeable>();
        if (fadeable != null)
        {
            Destroy(fadeable);
        }
        placed.GetComponent<Renderer>().sharedMaterial = GetGlassMaterial(color);

        placedGlass = placed.AddComponent<ColorObstacle>();
        placedGlass.obstacleColor = color;
        placedGlass.blueMaterial = blueGlass;
        placedGlass.orangeMaterial = orangeGlass;
    }

    void StartShowing()
    {
        step = Step.Showing;
        timer = 0f;

        Bounds targetBounds = GetTargetBounds();
        Vector3 awayFromTarget = aimSpot - targetBounds.center;
        awayFromTarget.y = 0f;
        awayFromTarget = awayFromTarget.normalized;

        moveFromPos = cameraPos;
        moveFromRot = cameraRot;
        moveToPos = aimSpot + awayFromTarget * showPullBack + Vector3.up * showRise;
        moveToRot = Quaternion.LookRotation(targetBounds.center - moveToPos);
        levelManager.ShowMessage(GetDoneMessage());
    }

    string GetDoneMessage()
    {
        if (placedGlass == null || !showGlassHints)
        {
            return doneMessage;
        }
        PieceColor.PieceColorType color = placedGlass.obstacleColor;
        string glassText = string.Format(glassMessage, GetColorName(color), GetColorName(GetOtherColor(color)));
        return doneMessage + "\n" + glassText;
    }

    void UpdateShowing(float deltaTime)
    {
        MoveCamera(deltaTime, showTime);
        if (timer >= showTime + showHoldTime)
        {
            done = true;
            StopAiming();
        }
    }

    bool MoveCamera(float deltaTime, float moveTime)
    {
        timer = timer + deltaTime;
        float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer / moveTime));
        cameraPos = Vector3.Lerp(moveFromPos, moveToPos, progress);
        cameraRot = Quaternion.Slerp(moveFromRot, moveToRot, progress);
        return timer >= moveTime;
    }

    void StopAiming()
    {
        step = Step.Leaving;
        EndAimDrag();
        HideOutline();
        if (!startedIn2D)
        {
            camRig.TurnTo(cameraRot.eulerAngles.y);
        }
    }

    void GiveBackControl()
    {
        step = Step.None;
        walkedOff = false;
        cutController.playerScript.canMove = true;
        cutController.playerScript.canJump = true;
        cutController.enabled = true;
        colorManager.enabled = true;
        camRig.canControl = true;
        if (startedIn2D && !camRig.is2D)
        {
            camRig.SwitchView();
        }
        if (done)
        {
            levelManager.ShowMessage(GetDoneMessage());
        }
    }

    void LockControls()
    {
        bool walk = step == Step.Aiming || (step == Step.Leaving && walkedOff);
        cutController.playerScript.canMove = walk;
        cutController.playerScript.canJump = false;
        cutController.enabled = false;
        colorManager.enabled = false;
        camRig.canControl = false;
    }

    void HideRestartTip()
    {
        HUD hud = levelManager.hud;
        if (hud == null)
        {
            return;
        }
        hud.restartTipDelay = Mathf.Infinity;
        if (hud.restartTip != null)
        {
            hud.restartTip.SetActive(false);
        }
    }

    void FreezePiece()
    {
        Rigidbody body = piece.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.zero;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.isKinematic = true;
        piece.transform.position = body.position;
        Physics.SyncTransforms();
    }

    bool HasRightSize(CuttablePiece checkPiece)
    {
        Vector3 size = checkPiece.GetComponent<Collider>().bounds.size;
        bool rightX = Mathf.Abs(size.x - pieceSize.x) <= sizeTolerance;
        bool rightY = Mathf.Abs(size.y - pieceSize.y) <= sizeTolerance;
        bool rightZ = Mathf.Abs(size.z - pieceSize.z) <= sizeTolerance;
        return rightX && rightY && rightZ;
    }

    public static string GetCutAdvice(ConvexShape first, ConvexShape second)
    {
        foreach (AimZone zone in allZones)
        {
            string advice = zone.CheckCut(first, second);
            if (advice != null)
            {
                return advice;
            }
        }
        return null;
    }

    string CheckCut(ConvexShape first, ConvexShape second)
    {
        if (done || step != Step.None || pieceSpot == null)
        {
            return null;
        }

        ConvexShape[] shapes = { first, second };
        int slice = -1;
        for (int i = 0; i < shapes.Length; i++)
        {
            Bounds bounds = shapes[i].GetBounds();
            if (!pieceSpot.Holds(bounds))
            {
                continue;
            }
            if (GetProblem(bounds.size, shapes[i].GetVolume(), shapes[i]) == Problem.None)
            {
                return string.Format(fitsMessage, shapes.Length - i);
            }
            if (slice < 0 || shapes[i].GetVolume() < shapes[slice].GetVolume())
            {
                slice = i;
            }
        }

        if (slice < 0)
        {
            return null;
        }
        Vector3 sliceSize = shapes[slice].GetBounds().size;
        Problem problem = GetProblem(sliceSize, shapes[slice].GetVolume(), shapes[slice]);
        return GetProblemMessage(problem, recutAction);
    }

    Problem GetProblem(CuttablePiece checkPiece)
    {
        Vector3 size = checkPiece.GetComponent<Collider>().bounds.size;
        return GetProblem(size, checkPiece.volume, anyShape ? checkPiece.GetWorldShape() : null);
    }

    Problem GetProblem(Vector3 size, float volume, ConvexShape worldShape = null)
    {
        if (anyShape)
        {
            return GetAnyShapeProblem(size, worldShape);
        }
        int thinSide = GetThinSide(pieceSize);
        int sliceThinSide = GetThinSide(size);
        if (sliceThinSide != thinSide && size[sliceThinSide] + sizeTolerance < size[thinSide])
        {
            return Problem.WrongWay;
        }
        if (GetUnevenness(volume, size) * GetScale(size) > maxUnevenness)
        {
            return Problem.Tilted;
        }
        if (size[thinSide] > pieceSize[thinSide] + sizeTolerance)
        {
            return Problem.Thick;
        }
        if (size[thinSide] < pieceSize[thinSide] - sizeTolerance)
        {
            return Problem.Thin;
        }

        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == thinSide)
            {
                continue;
            }
            if (size[axis] < pieceSize[axis] - sizeTolerance)
            {
                return Problem.Narrow;
            }
            if (size[axis] > pieceSize[axis] + sizeTolerance)
            {
                return Problem.WrongSize;
            }
        }
        return Problem.None;
    }

    string GetProblemMessage(Problem problem, string action)
    {
        switch (problem)
        {
            case Problem.WrongWay:
                return string.Format(wrongWayMessage, action);
            case Problem.Tilted:
                return string.Format(tiltMessage, action);
            case Problem.Thick:
                return string.Format(thickMessage, action);
            case Problem.Thin:
                return string.Format(thinMessage, action);
            case Problem.Narrow:
                return string.Format(narrowMessage, action);
            case Problem.Short:
                return string.Format(shortMessage, action);
            case Problem.DeadEnd:
                return string.Format(deadEndMessage, action);
            default:
                return wrongSizeMessage;
        }
    }

    int GetThinSide(Vector3 size)
    {
        int thinSide = 0;
        if (size.y < size[thinSide])
        {
            thinSide = 1;
        }
        if (size.z < size[thinSide])
        {
            thinSide = 2;
        }
        return thinSide;
    }

    float GetUnevenness(float volume, Vector3 size)
    {
        int thinSide = GetThinSide(size);
        float thickest = size[thinSide];
        float faceArea = size.x * size.y * size.z / thickest;
        float averageThickness = volume / faceArea;
        return (thickest - averageThickness) * 2f;
    }

    float GetScale(Vector3 size)
    {
        Vector3 targetSize = GetTargetBounds().size;
        int longSide = GetLongSide(targetSize);
        return targetSize[longSide] / size[longSide];
    }

    // Any Shape: whatever was cut is fine, as long as growing it along the Target's long side
    // makes sense. A very narrow piece would grow huge in every other direction.
    Problem GetAnyShapeProblem(Vector3 size, ConvexShape worldShape)
    {
        int span = GetLongSide(GetTargetBounds().size);
        if (size[span] < pieceSize[span] * minSpanShare)
        {
            return Problem.Short;
        }
        if (Mathf.Abs(GetScale(size) - 1f) < minGrowChange)
        {
            return Problem.WrongSize;
        }
        if (refuseDeadEnds && alignTop && worldShape != null && pieceSpot != null
            && GetFarEndDrop(worldShape) > maxUnevenness)
        {
            return Problem.DeadEnd;
        }
        return Problem.None;
    }

    // Any Shape: how far below the Target's top the grown piece's top ends, at the end away from the spot.
    // The highest point is always level with the Target's top, so the only thing that can trap the player
    // is a far end too low to climb out of.
    float GetFarEndDrop(ConvexShape worldShape)
    {
        Bounds bounds = worldShape.GetBounds();
        Bounds targetBounds = GetTargetBounds();
        int span = GetLongSide(targetBounds.size);
        float scale = GetScale(bounds.size);
        Vector3 spot = GetAimSpot(bounds);
        bool farIsHigh = targetBounds.center[span] >= pieceSpot.GetFloorCenter()[span];

        float farEnd = farIsHigh ? float.MinValue : float.MaxValue;
        foreach (Vector3 point in GetPoints(worldShape))
        {
            float along = (spot + (point - spot) * scale)[span];
            farEnd = farIsHigh ? Mathf.Max(farEnd, along) : Mathf.Min(farEnd, along);
        }

        float endWidth = 0.05f * targetBounds.size[span];
        float top = float.MinValue;
        foreach (Vector3 point in GetPoints(worldShape))
        {
            Vector3 grown = spot + (point - spot) * scale;
            if (Mathf.Abs(grown[span] - farEnd) <= endWidth)
            {
                top = Mathf.Max(top, grown.y);
            }
        }
        return targetBounds.max.y - top;
    }

    static int GetLongSide(Vector3 size)
    {
        int longSide = 0;
        if (size.y > size[longSide])
        {
            longSide = 1;
        }
        if (size.z > size[longSide])
        {
            longSide = 2;
        }
        return longSide;
    }

    static IEnumerable<Vector3> GetPoints(ConvexShape shape)
    {
        foreach (Vector3[] face in shape.faces)
        {
            foreach (Vector3 point in face)
            {
                yield return point;
            }
        }
    }

    // Any Shape: the player's piece, grown about the aim spot to where it will end up.
    void MakeGoalShape()
    {
        pieceShape = piece.GetWorldShape();
        Bounds bounds = pieceShape.GetBounds();
        float scale = GetScale(bounds.size);
        Vector3 spot = GetAimSpot(bounds);
        Matrix4x4 grow = Matrix4x4.Translate(spot) * Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-spot);
        goalShape = pieceShape.ApplyMatrix(grow);
        if (targetOutline == null)
        {
            return;
        }

        for (int i = 0; i < goalShape.faces.Count; i++)
        {
            if (i >= shapeLines.Count)
            {
                LineRenderer line = Instantiate(targetOutline, targetOutline.transform.parent);
                line.name = "ShapeOutline";
                line.loop = true;
                line.useWorldSpace = true;
                if (outlineMat != null)
                {
                    line.sharedMaterial = outlineMat;
                }
                line.enabled = false;
                shapeLines.Add(line);
            }
            shapeLines[i].positionCount = goalShape.faces[i].Length;
            shapeLines[i].SetPositions(goalShape.faces[i]);
        }
    }

    void ShowOutline()
    {
        if (targetOutline == null)
        {
            return;
        }
        bool ownShape = goalShape != null;
        targetOutline.enabled = !ownShape;
        for (int i = 0; i < shapeLines.Count; i++)
        {
            shapeLines[i].enabled = ownShape && i < goalShape.faces.Count;
        }
    }

    void HideOutline()
    {
        if (targetOutline != null)
        {
            targetOutline.enabled = false;
        }
        foreach (LineRenderer line in shapeLines)
        {
            line.enabled = false;
        }
    }

    void SetOutlineWidth(float width)
    {
        targetOutline.widthMultiplier = width;
        foreach (LineRenderer line in shapeLines)
        {
            line.widthMultiplier = width;
        }
    }

    
Vector3 GetAimSpot(Bounds pieceBounds)
    {
        float scale = GetScale(pieceBounds.size);
        Vector3 pieceAnchor = GetAnchor(pieceBounds);
        Vector3 targetAnchor = GetAnchor(GetTargetBounds());
        Vector3 pieceToTarget = targetAnchor - pieceAnchor;
        Vector3 spotToPiece = pieceToTarget / (scale - 1f);
        return pieceAnchor - spotToPiece;
    }

    Vector3 GetAnchor(Bounds bounds)
    {
        if (alignTop)
        {
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
        return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    Bounds GetTargetBounds()
    {
        return new Bounds(target.position, target.lossyScale);
    }

    Bounds GetExpectedPieceBounds()
    {
        Vector3 center = pieceSpot.GetFloorCenter() + Vector3.up * (pieceSize.y / 2f);
        return new Bounds(center, pieceSize);
    }

    float GetMatch()
    {
        Rect pieceRect;
        Rect targetRect;
        bool pieceInFront;
        bool targetInFront;
        if (goalShape != null)
        {
            // Any Shape: compare the piece with its own grown copy, using their real corners.
            pieceInFront = GetScreenRect(GetPoints(pieceShape), out pieceRect);
            targetInFront = GetScreenRect(GetPoints(goalShape), out targetRect);
        }
        else
        {
            pieceInFront = GetScreenRect(GetCorners(piece.GetComponent<Collider>().bounds), out pieceRect);
            targetInFront = GetScreenRect(GetCorners(GetTargetBounds()), out targetRect);
        }
        if (!pieceInFront || !targetInFront)
        {
            return 0f;
        }

        float overlapWidth = Mathf.Min(pieceRect.xMax, targetRect.xMax) - Mathf.Max(pieceRect.xMin, targetRect.xMin);
        float overlapHeight = Mathf.Min(pieceRect.yMax, targetRect.yMax) - Mathf.Max(pieceRect.yMin, targetRect.yMin);
        if (overlapWidth <= 0f || overlapHeight <= 0f)
        {
            return 0f;
        }

        float overlapArea = overlapWidth * overlapHeight;
        float pieceArea = pieceRect.width * pieceRect.height;
        float targetArea = targetRect.width * targetRect.height;
        float totalArea = pieceArea + targetArea - overlapArea;
        return overlapArea / totalArea;
    }

    bool GetScreenRect(IEnumerable<Vector3> points, out Rect rect)
    {
        Vector2 low = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 high = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in points)
        {
            Vector3 point = camRig.cam.WorldToViewportPoint(corner);
            if (point.z <= 0f)
            {
                rect = new Rect();
                return false;
            }
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }
        rect = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        return true;
    }

    Vector3[] GetCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z),
            new Vector3(min.x, max.y, max.z)
        };
        return corners;
    }

    void DrawOutline()
    {
        if (targetOutline == null)
        {
            return;
        }

        SetBoxLines(targetOutline, GetTargetBounds());
        targetOutline.enabled = false;
    }

    void SetBoxLines(LineRenderer line, Bounds box)
    {
        Vector3[] corners = GetCorners(box);
        Vector3[] path =
        {
            corners[0], corners[1], corners[2], corners[3], corners[0],
            corners[4], corners[5], corners[6], corners[7], corners[4],
            corners[5], corners[1], corners[2], corners[6], corners[7], corners[3]
        };
        line.useWorldSpace = true;
        line.positionCount = path.Length;
        line.SetPositions(path);
    }

    void MakeSpotGuide()
    {
        spotGuide = Instantiate(targetOutline, targetOutline.transform.parent);
        spotGuide.name = "SpotGuide";
        spotGuide.enabled = true;
        spotGuide.widthMultiplier = 1f;
        spotGuide.widthCurve = AnimationCurve.Constant(0f, 1f, guideWidth);

        Fadeable fade = spotGuide.gameObject.AddComponent<Fadeable>();
        fade.hideIn2D = true;
    }

    void UpdateSpotGuide()
    {
        if (pieceSpot == null || cutController.state != CutController.State.Normal)
        {
            return;
        }

        Bounds playerBounds = cutController.player.GetComponent<Collider>().bounds;
        bool playerOnSpot = pieceSpot.Holds(playerBounds);
        CuttablePiece spotPiece = pieceSpot.GetPiece();
        bool canStart = step == Step.None && !done;
        if (showSpotMessage && playerOnSpot && canStart && spotPiece == null && levelManager.CanCut())
        {
            if (!playerWasOnSpot || IsShowing(spotMessage))
            {
                levelManager.ShowMessage(spotMessage);
            }
        }
        playerWasOnSpot = playerOnSpot;

        if (spotGuide == null)
        {
            return;
        }
        spotGuide.gameObject.SetActive(canStart);
        if (!canStart)
        {
            return;
        }

        Bounds footprint = GetExpectedPieceBounds();
        if (playerOnSpot)
        {
            footprint = playerBounds;
        }
        else if (spotPiece != null)
        {
            footprint = spotPiece.GetComponent<Collider>().bounds;
        }
        SetBoxLines(spotGuide, GetGuideBox(footprint));
    }

    bool IsShowing(string message)
    {
        HUD hud = levelManager.hud;
        if (hud == null || hud.messageText == null)
        {
            return false;
        }
        return hud.messageText.text == message;
    }

    void UpdateGlassWarning()
    {
        if (!showGlassHints || placedGlass == null || step != Step.None || cutController.state != CutController.State.Normal)
        {
            return;
        }

        string message = GetFallMessage();
        if (message != null)
        {
            if (message != shownFallMessage || IsShowing(message))
            {
                levelManager.ShowMessage(message);
            }
        }
        else if (shownFallMessage != null && IsShowing(shownFallMessage))
        {
            levelManager.ShowMessage("");
        }
        shownFallMessage = message;
    }

    string GetFallMessage()
    {
        PieceColor playerColor = cutController.player.GetComponent<PieceColor>();
        PieceColor.PieceColorType color = placedGlass.obstacleColor;
        if (playerColor == null || playerColor.currentColor != color)
        {
            return null;
        }

        Bounds nearPlayer = cutController.player.GetComponent<Collider>().bounds;
        nearPlayer.Expand(warnDistance * 2f);
        if (!nearPlayer.Intersects(placedGlass.GetComponent<Collider>().bounds))
        {
            return null;
        }
        return string.Format(fallMessage, GetColorName(color), GetColorName(GetOtherColor(color)), GetColorKeyName());
    }

    bool GetGlassColor(CuttablePiece checkPiece, out PieceColor.PieceColorType color)
    {
        color = PieceColor.PieceColorType.Blue;
        if (checkPiece == null || blueGlass == null || orangeGlass == null)
        {
            return false;
        }
        PieceColor pieceColor = checkPiece.GetComponent<PieceColor>();
        if (pieceColor == null)
        {
            return false;
        }
        color = pieceColor.currentColor;
        return true;
    }

    Material GetGlassMaterial(PieceColor.PieceColorType color)
    {
        if (color == PieceColor.PieceColorType.Blue)
        {
            return blueGlass;
        }
        return orangeGlass;
    }

    PieceColor.PieceColorType GetOtherColor(PieceColor.PieceColorType color)
    {
        if (color == PieceColor.PieceColorType.Blue)
        {
            return PieceColor.PieceColorType.Orange;
        }
        return PieceColor.PieceColorType.Blue;
    }

    string GetColorName(PieceColor.PieceColorType color)
    {
        if (color == PieceColor.PieceColorType.Blue)
        {
            return "<color=#5B9BFF>blue</color>";
        }
        return "<color=#FF9A33>orange</color>";
    }

    string GetColorKeyName()
    {
        KeyCode key = colorManager.colorKey;
        string keyName = key.ToString();
        if (key == KeyCode.LeftShift || key == KeyCode.RightShift)
        {
            keyName = "Shift";
        }
        return "<color=#FFD54A>" + keyName + "</color>";
    }

    Bounds GetGuideBox(Bounds footprint)
    {
        Bounds expected = GetExpectedPieceBounds();
        int thinSide = GetThinSide(pieceSize);
        Vector3 min = expected.min;
        Vector3 max = expected.max;
        for (int axis = 0; axis < 3; axis += 2)
        {
            if (axis != thinSide)
            {
                min[axis] = footprint.min[axis];
                max[axis] = footprint.max[axis];
            }
            min[axis] = min[axis] - guideMargin;
            max[axis] = max[axis] + guideMargin;
        }
        min.y = min.y + guideLift;

        Bounds box = new Bounds();
        box.SetMinMax(min, max);
        return box;
    }

    void OnDrawGizmosSelected()
    {
        if (pieceSpot == null || target == null)
        {
            return;
        }

        Bounds pieceBounds = GetExpectedPieceBounds();
        Bounds targetBounds = GetTargetBounds();
        Vector3 spot = GetAimSpot(pieceBounds);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(pieceBounds.center, pieceBounds.size);
        Gizmos.DrawWireCube(targetBounds.center, targetBounds.size);
        Gizmos.DrawLine(spot, GetAnchor(targetBounds));
        Gizmos.DrawSphere(spot, 0.3f);
    }
}
