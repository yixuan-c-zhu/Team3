using UnityEngine;
using Team3.Analytics;

public class CameraRig : MonoBehaviour
{
    public Camera cam;
    public Transform player;

    public float size2D = 6.5f;
    public float distance2D = 40f;

    public float angle3D = 25f;
    public float minPitch = 5f;
    public float maxPitch = 75f;
    public float mouseSensitivity = 0.25f;

    public float followSpeed = 8f;
    public float turnSpeed = 10f;
    public float offsetY = 0.8f;

    public float wallRadius = 0.35f;
    public float minWallDistance = 1f;
    public float wallReturnSpeed = 4f;

    public float dragStart = 6f;
    public float flattenPull = 140f;
    public float liftPull = 110f;
    public float turnPull = 170f;
    public float previewFlat = 0.35f;
    public float previewLift = 12f;
    public float previewTurn = 18f;
    public float followTime = 0.05f;
    public float settleTime = 0.12f;
    public float turnDip = 0.3f;
    public float riseTime = 0.15f;

    public bool is2D = true;
    public bool canControl = true;

    private float rotY;
    private float targetRotY;
    private float rotX;
    private Vector3 lookPos;
    private float normalFov;
    private float aimBlend;
    private Vector3 aimPosition;
    private Quaternion aimRotation;
    private float aimFov;
    private float shownDistance3D = -1f;

    private float flat = 1f;
    private float flatVelocity;
    private float lift;
    private float liftVelocity;
    private float yawOffset;
    private float yawVelocity;
    private bool rising;
    private float riseVelocity;

    private bool wasControllable;
    private bool dragging;
    private bool dragMoved;
    private bool gestureDone;
    private int dragAxis;
    private Vector2 dragTravel;
    private Vector3 lastMousePos;
    private float pull;
    private float sidePull;

    public event System.Action ViewInput;

    public bool IsDragging { get { return dragMoved; } }

    public float Distance3D
    {
        get { return size2D / Mathf.Tan(GetNormalFov() * 0.5f * Mathf.Deg2Rad); }
    }

    public bool IsTurning()
    {
        if (!is2D) return false;
        float diff = Mathf.Abs(Mathf.DeltaAngle(rotY, targetRotY));
        return diff > 1f;
    }

    public Vector3 GetRightDir()
    {
        Vector3 dir = Quaternion.Euler(0f, targetRotY, 0f) * Vector3.right;
        return RoundDir(dir);
    }

    public Vector3 GetDepthDir()
    {
        Vector3 dir = Quaternion.Euler(0f, targetRotY, 0f) * Vector3.forward;
        return RoundDir(dir);
    }

    public Vector3 GetCamRight()
    {
        return Quaternion.Euler(0f, GetMoveYaw(), 0f) * Vector3.right;
    }

    public Vector3 GetCamForward()
    {
        return Quaternion.Euler(0f, GetMoveYaw(), 0f) * Vector3.forward;
    }

    public int GetViewSide()
    {
        int side = ((Mathf.RoundToInt(targetRotY / 90f) % 4) + 4) % 4;
        return side;
    }

    float GetMoveYaw()
    {
        if (aimBlend <= 0f) return rotY;
        return Mathf.LerpAngle(rotY, aimRotation.eulerAngles.y, aimBlend);
    }

    float GetNormalFov()
    {
        if (normalFov > 0f) return normalFov;
        return cam != null ? cam.fieldOfView : 60f;
    }

    void Start()
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }
        normalFov = cam.fieldOfView;
        rotX = angle3D;
        flat = is2D ? 1f : 0f;
        if (player != null)
        {
            lookPos = player.position;
        }
        MoveCamera(0f);
    }

    void OnDisable()
    {
        EndDrag();
    }

    void LateUpdate()
    {
        float deltaTime = Time.unscaledDeltaTime;
        ReadDrag();
        UpdateShape(deltaTime);

        rotY = Mathf.LerpAngle(rotY, targetRotY, 1f - Mathf.Exp(-turnSpeed * deltaTime));
        if (Mathf.Abs(Mathf.DeltaAngle(rotY, targetRotY)) < 0.05f)
        {
            rotY = targetRotY;
        }

        if (player != null && Time.timeScale > 0f)
        {
            lookPos = Vector3.Lerp(lookPos, player.position, 1f - Mathf.Exp(-followSpeed * deltaTime));
        }
        MoveCamera(deltaTime);
    }

    void ReadDrag()
    {
        if (!canControl || Time.timeScale <= 0f)
        {
            EndDrag();
            wasControllable = false;
            return;
        }

        bool fresh = !wasControllable;
        wasControllable = true;
        if (!fresh && Input.GetMouseButtonDown(0) && !GameplayUiInput.IsPointerOverControl())
        {
            dragging = true;
            dragMoved = false;
            gestureDone = false;
            dragAxis = 0;
            dragTravel = Vector2.zero;
            pull = 0f;
            sidePull = 0f;
            lastMousePos = Input.mousePosition;
        }
        if (!dragging) return;
        if (!Input.GetMouseButton(0))
        {
            EndDrag();
            return;
        }

        Vector2 move = Input.mousePosition - lastMousePos;
        lastMousePos = Input.mousePosition;
        if (!dragMoved)
        {
            dragTravel += move;
            if (dragTravel.magnitude * PixelScale() < dragStart) return;
            dragMoved = true;
            move = dragTravel;
            dragTravel = Vector2.zero;
            Cursor.visible = false;
            CutAnalytics.RecordMechanic(Mechanic.Camera, "mouse_drag");
        }
        if (gestureDone) return;

        if (is2D) Drag2D(move);
        else Drag3D(move);
    }

    void EndDrag()
    {
        if (dragMoved)
        {
            Cursor.visible = true;
        }
        dragging = false;
        dragMoved = false;
        gestureDone = false;
        pull = 0f;
        sidePull = 0f;
    }

    void Drag3D(Vector2 move)
    {
        float scale = PixelScale();
        rising = false;
        targetRotY += move.x * mouseSensitivity;
        rotY = targetRotY;

        if (move.y > 0f)
        {
            float room = Mathf.Max(0f, rotX - minPitch) / mouseSensitivity;
            float used = Mathf.Min(move.y, room);
            rotX -= used * mouseSensitivity;
            pull += (move.y - used) * scale;
        }
        else
        {
            float back = Mathf.Min(-move.y * scale, pull);
            pull -= back;
            float rest = -move.y - back / scale;
            rotX = Mathf.Min(maxPitch, rotX + rest * mouseSensitivity);
        }

        if (pull >= flattenPull)
        {
            Flatten();
        }
    }

    void Drag2D(Vector2 move)
    {
        Vector2 scaled = move * PixelScale();
        if (dragAxis == 0)
        {
            dragTravel += scaled;
            if (dragTravel.magnitude < dragStart * 2f) return;
            dragAxis = Mathf.Abs(dragTravel.x) > Mathf.Abs(dragTravel.y) ? 1 : 2;
            scaled = dragTravel;
            dragTravel = Vector2.zero;
        }

        if (dragAxis == 2)
        {
            pull = Mathf.Max(0f, pull - scaled.y);
            if (pull >= liftPull)
            {
                Lift();
            }
        }
        else if (!IsTurning())
        {
            sidePull += scaled.x;
            if (Mathf.Abs(sidePull) >= turnPull)
            {
                TurnPage(Mathf.Sign(sidePull));
            }
        }
    }

    void Flatten()
    {
        gestureDone = true;
        pull = 0f;
        rotX = minPitch;
        is2D = true;
        targetRotY = Mathf.Round(rotY / 90f) * 90f;
        ViewInput?.Invoke();
        CutAnalytics.RecordMechanic(Mechanic.ViewSwitch, "mouse_flatten");
    }

    void Lift()
    {
        gestureDone = true;
        pull = 0f;
        rotX = Mathf.Clamp(rotX + lift, minPitch, maxPitch);
        lift = 0f;
        liftVelocity = 0f;
        rising = true;
        riseVelocity = 0f;
        is2D = false;
        ViewInput?.Invoke();
        CutAnalytics.RecordMechanic(Mechanic.ViewSwitch, "mouse_lift");
    }

    void TurnPage(float direction)
    {
        gestureDone = true;
        sidePull = 0f;
        rotY += yawOffset;
        yawOffset = 0f;
        yawVelocity = 0f;
        targetRotY = Mathf.Round(targetRotY / 90f) * 90f + direction * 90f;
        ViewInput?.Invoke();
        CutAnalytics.RecordMechanic(Mechanic.Camera, "mouse_turn");
    }

    void UpdateShape(float deltaTime)
    {
        float goal = is2D ? 1f : 0f;
        float liftGoal = 0f;
        float yawGoal = 0f;
        bool following = dragMoved && !gestureDone;
        if (following && !is2D)
        {
            goal += previewFlat * Ease(pull / flattenPull);
        }
        else if (following && dragAxis == 2)
        {
            float amount = Ease(pull / liftPull);
            goal -= previewFlat * amount;
            liftGoal = previewLift * amount;
        }
        else if (following && dragAxis == 1)
        {
            float amount = Ease(Mathf.Abs(sidePull) / turnPull);
            goal -= previewFlat * 0.5f * amount;
            yawGoal = Mathf.Sign(sidePull) * previewTurn * amount;
        }

        float smooth = following ? followTime : settleTime;
        flat = Mathf.Clamp01(Mathf.SmoothDamp(flat, goal, ref flatVelocity, smooth, Mathf.Infinity, deltaTime));
        lift = Mathf.SmoothDamp(lift, liftGoal, ref liftVelocity, smooth, Mathf.Infinity, deltaTime);
        yawOffset = Mathf.SmoothDamp(yawOffset, yawGoal, ref yawVelocity, smooth, Mathf.Infinity, deltaTime);
        if (deltaTime > 0f && Mathf.Abs(flat - goal) < 0.0005f)
        {
            flat = goal;
            flatVelocity = 0f;
        }

        if (rising)
        {
            rotX = Mathf.SmoothDamp(rotX, angle3D, ref riseVelocity, riseTime, Mathf.Infinity, deltaTime);
            if (Mathf.Abs(rotX - angle3D) < 0.05f)
            {
                rotX = angle3D;
                rising = false;
            }
        }
    }

    float ShownFlat()
    {
        float shown = flat;
        if (is2D)
        {
            float turn = Mathf.Abs(Mathf.DeltaAngle(rotY, targetRotY));
            if (turn > 0.05f)
            {
                float progress = 1f - Mathf.Clamp01(turn / 90f);
                shown -= turnDip * Mathf.Sin(progress * Mathf.PI);
            }
        }
        return Mathf.Clamp01(shown);
    }

    public void SwitchView()
    {
        is2D = !is2D;
        rising = false;
        if (is2D)
        {
            targetRotY = Mathf.Round(rotY / 90f) * 90f;
        }
    }

    public void SetFrontView2D(float angle = 0f)
    {
        is2D = true;
        rising = false;
        targetRotY = Mathf.Round(angle / 90f) * 90f;
        rotY = targetRotY;
        flat = 1f;
        flatVelocity = 0f;
        lift = 0f;
        yawOffset = 0f;
    }

    public void SetAim(float blend, Vector3 position, Quaternion rotation, float fov)
    {
        aimBlend = blend;
        aimPosition = position;
        aimRotation = rotation;
        aimFov = fov;
    }

    public void TurnTo(float angle)
    {
        if (is2D) return;
        targetRotY = angle;
        rotY = angle;
    }

    void MoveCamera(float deltaTime)
    {
        Vector3 center = lookPos + Vector3.up * offsetY;
        float shownFlat = ShownFlat();
        float pitch = Mathf.Lerp(rotX + lift, 0f, shownFlat);
        Quaternion camRot = Quaternion.Euler(pitch, rotY + yawOffset, 0f);

        float distance3D = distance2D;
        if (shownFlat < 1f)
        {
            distance3D = GetShownDistance3D(center, camRot, deltaTime);
        }
        else
        {
            shownDistance3D = -1f;
        }

        float baseTan = Mathf.Tan(GetNormalFov() * 0.5f * Mathf.Deg2Rad);
        float distance = Mathf.Lerp(distance3D, distance2D, shownFlat);
        float frame = Mathf.Lerp(baseTan * distance3D, size2D, shownFlat);
        float fov = 2f * Mathf.Atan(frame / distance) * Mathf.Rad2Deg;
        float ortho = shownFlat * shownFlat;

        Vector3 camPos = center - camRot * Vector3.forward * distance;
        if (aimBlend > 0f)
        {
            camPos = Vector3.Lerp(camPos, aimPosition, aimBlend);
            camRot = Quaternion.Slerp(camRot, aimRotation, aimBlend);
            fov = Mathf.Lerp(fov, aimFov, aimBlend);
            ortho *= 1f - aimBlend;
        }

        cam.transform.SetPositionAndRotation(camPos, camRot);
        cam.orthographicSize = size2D;
        if (shownFlat >= 1f && aimBlend <= 0f)
        {
            cam.ResetProjectionMatrix();
            cam.orthographic = true;
            cam.fieldOfView = GetNormalFov();
        }
        else if (ortho <= 0.0001f)
        {
            cam.ResetProjectionMatrix();
            cam.orthographic = false;
            cam.fieldOfView = shownFlat <= 0f && aimBlend <= 0f ? GetNormalFov() : fov;
        }
        else
        {
            cam.orthographic = false;
            cam.fieldOfView = fov;
            cam.projectionMatrix = BlendProjection(fov, frame, ortho);
        }
    }

    Matrix4x4 BlendProjection(float fov, float frame, float ortho)
    {
        float aspect = cam.aspect;
        float near = cam.nearClipPlane;
        float far = cam.farClipPlane;
        Matrix4x4 perspective = Matrix4x4.Perspective(fov, aspect, near, far);
        Matrix4x4 flatView = Matrix4x4.Ortho(-frame * aspect, frame * aspect, -frame, frame, near, far);
        Matrix4x4 blended = new Matrix4x4();
        for (int i = 0; i < 16; i++)
        {
            blended[i] = Mathf.Lerp(perspective[i], flatView[i], ortho);
        }
        return blended;
    }

    float GetShownDistance3D(Vector3 center, Quaternion camRot, float deltaTime)
    {
        float wanted = Distance3D;
        Vector3 back = camRot * Vector3.back;
        float clearDistance = wanted;
        foreach (RaycastHit hit in Physics.SphereCastAll(center, wallRadius, back, wanted, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance > 0f && BlocksView(hit.collider))
            {
                clearDistance = Mathf.Min(clearDistance, hit.distance);
            }
        }
        clearDistance = Mathf.Max(clearDistance, minWallDistance);

        if (shownDistance3D < 0f || clearDistance < shownDistance3D)
        {
            shownDistance3D = clearDistance;
        }
        else
        {
            shownDistance3D = Mathf.Lerp(shownDistance3D, clearDistance, 1f - Mathf.Exp(-wallReturnSpeed * deltaTime));
        }
        return shownDistance3D;
    }

    bool BlocksView(Collider other)
    {
        return other.attachedRigidbody == null && other.GetComponent<ColorObstacle>() == null;
    }

    static float PixelScale()
    {
        return 1080f / Mathf.Max(1, Screen.height);
    }

    static float Ease(float amount)
    {
        amount = Mathf.Clamp01(amount);
        return amount * (2f - amount);
    }

    static Vector3 RoundDir(Vector3 dir)
    {
        return new Vector3(Mathf.Round(dir.x), 0f, Mathf.Round(dir.z));
    }
}
