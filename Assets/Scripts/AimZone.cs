using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class AimZone : MonoBehaviour
{
    public PieceSpot pieceSpot;
    public Transform target;
    public LineRenderer targetOutline;

    public Vector3 pieceSize = new Vector3(2f, 0.25f, 2f);
    public float sizeTolerance = 0.4f;
    public float maxUnevenness = 1.2f;
    public bool alignTop = true;

    public float cameraFov = 60f;
    public float keySpeed = 35f;
    public float mouseSpeed = 0.15f;
    public float matchNeeded = 0.8f;

    [TextArea] public string aimMessage = "Line up your piece with the orange outline: {0}%\n<size=70%>A / D and W / S or drag to aim. Esc to step back.</size>";
    [TextArea] public string noPieceMessage = "Nothing to line up yet. Leave a piece of yourself in the right spot first.\n<size=70%>Esc to step back.</size>";
    [TextArea] public string wrongSizeMessage = "This piece is the wrong size to fill the outline.\n<size=70%>Esc to step back.</size>";
    [TextArea] public string unevenMessage = "Your cut isn't straight, so the piece won't fit.\n<size=70%>Press R to restart and cut it straight. Esc to step back.</size>";
    public string lockedMessage = "Lined up!";
    public string doneMessage = "It's real!";
    public string leaveMessage = "Step back onto the pad to aim again.";

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

    enum Step { None, Aiming, LockingOn, Growing, Showing, Leaving }

    private CameraRig camRig;
    private CutController cutController;
    private PieceControlManager colorManager;
    private LevelManager levelManager;

    private Step step = Step.None;
    private bool done;
    private bool waitForExit;
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

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        camRig = FindFirstObjectByType<CameraRig>();
        cutController = FindFirstObjectByType<CutController>();
        colorManager = FindFirstObjectByType<PieceControlManager>();
        levelManager = FindFirstObjectByType<LevelManager>();
    }

    void Start()
    {
        DrawOutline();
    }

    void OnTriggerEnter(Collider other)
    {
        if (step != Step.None || done || waitForExit)
        {
            return;
        }
        if (IsPlayer(other))
        {
            StartAiming();
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            waitForExit = false;
        }
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
        LockControls();
        cutController.playerScript.StopPlayer();
        if (camRig.is2D)
        {
            camRig.SwitchView();
        }

        piece = pieceSpot.GetPiece();
        if (piece != null && HasRightSize(piece))
        {
            SetUpOrbit(piece.GetComponent<Collider>().bounds);
        }
        else
        {
            SetUpOrbit(GetExpectedPieceBounds());
        }

        sideAngle = goalSideAngle + startSideOffset;
        upAngle = Mathf.Clamp(goalUpAngle + startUpOffset, minUpAngle, maxUpAngle);
        PlaceOrbitCamera();

        if (targetOutline != null)
        {
            targetOutline.enabled = true;
        }
    }

    void UpdateAiming(float deltaTime)
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            waitForExit = true;
            StopAiming();
            levelManager.ShowMessage(leaveMessage);
            return;
        }

        ReadAimInput(deltaTime);
        PlaceOrbitCamera();

        if (piece == null)
        {
            levelManager.ShowMessage(noPieceMessage);
            return;
        }
        if (!HasRightSize(piece))
        {
            levelManager.ShowMessage(wrongSizeMessage);
            return;
        }
        if (!IsCutStraight(piece))
        {
            levelManager.ShowMessage(unevenMessage);
            return;
        }

        HideRestartTip();

        float match = GetMatch();
        int percent = Mathf.RoundToInt(match * 100f);
        levelManager.ShowMessage(string.Format(aimMessage, percent));

        if (match >= matchNeeded && blend >= 1f)
        {
            StartLockingOn();
        }
    }

    void ReadAimInput(float deltaTime)
    {
        float turn = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q))
        {
            turn = turn - 1f;
        }
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.E))
        {
            turn = turn + 1f;
        }

        float lift = 0f;
        if (Input.GetKey(KeyCode.W))
        {
            lift = lift + 1f;
        }
        if (Input.GetKey(KeyCode.S))
        {
            lift = lift - 1f;
        }

        sideAngle = sideAngle + turn * keySpeed * deltaTime;
        upAngle = upAngle + lift * keySpeed * deltaTime;

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            lastMousePos = Input.mousePosition;
        }
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1))
        {
            Vector3 mouseMove = Input.mousePosition - lastMousePos;
            sideAngle = sideAngle + mouseMove.x * mouseSpeed;
            upAngle = upAngle - mouseMove.y * mouseSpeed;
            lastMousePos = Input.mousePosition;
        }

        sideAngle = Mathf.Clamp(sideAngle, goalSideAngle - sideRange, goalSideAngle + sideRange);
        upAngle = Mathf.Clamp(upAngle, minUpAngle, maxUpAngle);
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
        FreezePiece();

        Bounds pieceBounds = piece.GetWorldShape().GetBounds();
        growScale = GetScale(pieceBounds);
        aimSpot = GetAimSpot(pieceBounds);

        moveFromPos = cameraPos;
        moveFromRot = cameraRot;
        moveToPos = aimSpot;
        moveToRot = Quaternion.LookRotation(pieceBounds.center - aimSpot);
        levelManager.ShowMessage(lockedMessage);
    }

    void UpdateLockingOn(float deltaTime)
    {
        bool finished = MoveCamera(deltaTime, lockTime);
        if (finished)
        {
            StartGrowing();
        }
    }

    void StartGrowing()
    {
        step = Step.Growing;
        timer = 0f;
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

        Destroy(piece.GetComponent<PieceCarry>());
        Destroy(piece);
        piece = null;

        if (targetOutline != null)
        {
            targetOutline.enabled = false;
        }
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
        levelManager.ShowMessage(doneMessage);
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
        if (targetOutline != null)
        {
            targetOutline.enabled = false;
        }
        camRig.TurnTo(cameraRot.eulerAngles.y);
    }

    void GiveBackControl()
    {
        step = Step.None;
        cutController.playerScript.canMove = true;
        cutController.enabled = true;
        colorManager.enabled = true;
        camRig.canControl = true;
    }

    void LockControls()
    {
        cutController.playerScript.canMove = false;
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

    bool IsCutStraight(CuttablePiece checkPiece)
    {
        Bounds bounds = checkPiece.GetComponent<Collider>().bounds;
        float grownUnevenness = GetUnevenness(checkPiece, bounds.size) * GetScale(bounds);
        return grownUnevenness <= maxUnevenness;
    }

    float GetUnevenness(CuttablePiece checkPiece, Vector3 size)
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

        float thickest = size[thinSide];
        float faceArea = size.x * size.y * size.z / thickest;
        float averageThickness = checkPiece.volume / faceArea;
        return (thickest - averageThickness) * 2f;
    }

    float GetScale(Bounds pieceBounds)
    {
        Vector3 targetSize = GetTargetBounds().size;
        int longSide = 0;
        if (targetSize.y > targetSize[longSide])
        {
            longSide = 1;
        }
        if (targetSize.z > targetSize[longSide])
        {
            longSide = 2;
        }
        return targetSize[longSide] / pieceBounds.size[longSide];
    }

    Vector3 GetAimSpot(Bounds pieceBounds)
    {
        float scale = GetScale(pieceBounds);
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
        bool pieceInFront = GetScreenRect(piece.GetComponent<Collider>().bounds, out pieceRect);
        bool targetInFront = GetScreenRect(GetTargetBounds(), out targetRect);
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

    bool GetScreenRect(Bounds bounds, out Rect rect)
    {
        Vector2 low = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 high = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in GetCorners(bounds))
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

        Vector3[] corners = GetCorners(GetTargetBounds());
        Vector3[] path =
        {
            corners[0], corners[1], corners[2], corners[3], corners[0],
            corners[4], corners[5], corners[6], corners[7], corners[4],
            corners[5], corners[1], corners[2], corners[6], corners[7], corners[3]
        };
        targetOutline.useWorldSpace = true;
        targetOutline.positionCount = path.Length;
        targetOutline.SetPositions(path);
        targetOutline.enabled = false;
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
