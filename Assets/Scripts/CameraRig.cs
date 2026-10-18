using UnityEngine;
using Team3.Analytics;

public class CameraRig : MonoBehaviour
{
    public Camera cam;
    public Transform player;

    public float size2D = 6.5f;
    public float distance2D = 40f;

    public float distance3D = 15f;
    public float minZoom = 7f;
    public float maxZoom = 28f;
    public float angle3D = 25f;
    public float rotateSpeed = 120f;
    public float mouseSensitivity = 0.25f;

    public float followSpeed = 8f;
    public float turnSpeed = 10f;
    public float offsetY = 0.8f;

    public bool is2D = true;
    public bool canControl = true;

    private float rotY;
    private float targetRotY;
    private float rotX;
    private Vector3 lookPos;
    private Vector3 lastMousePos;
    private float normalFov;
    private float aimBlend;
    private Vector3 aimPosition;
    private Quaternion aimRotation;
    private float aimFov;
    private float analyticsTurnDirection;

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
        return Quaternion.Euler(0f, rotY, 0f) * Vector3.right;
    }

    public Vector3 GetCamForward()
    {
        return Quaternion.Euler(0f, rotY, 0f) * Vector3.forward;
    }

    public int GetViewSide()
    {
        int side = ((Mathf.RoundToInt(targetRotY / 90f) % 4) + 4) % 4;
        return side;
    }

    void Start()
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }
        normalFov = cam.fieldOfView;
        rotX = angle3D;
        if (player != null)
        {
            lookPos = player.position;
        }
        MoveCamera();
    }

    void LateUpdate()
    {
        float deltaTime = Time.unscaledDeltaTime;
        if (canControl)
        {
            CheckInput(deltaTime);
        }
        else analyticsTurnDirection = 0f;

        rotY = Mathf.LerpAngle(rotY, targetRotY, 1f - Mathf.Exp(-turnSpeed * deltaTime));
        if (Mathf.Abs(Mathf.DeltaAngle(rotY, targetRotY)) < 0.05f)
        {
            rotY = targetRotY;
        }

        if (player != null && Time.timeScale > 0f)
        {
            lookPos = Vector3.Lerp(lookPos, player.position, 1f - Mathf.Exp(-followSpeed * deltaTime));
        }
        MoveCamera();
    }

    void CheckInput(float deltaTime)
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            SwitchViewFromInput();
        }

        if (is2D)
        {
            if (Input.GetKeyDown(KeyCode.Q)) TurnFromInput(-1f, deltaTime);
            if (Input.GetKeyDown(KeyCode.E)) TurnFromInput(1f, deltaTime);
        }
        else
        {
            float turn = 0f;
            if (Input.GetKey(KeyCode.E)) turn = turn + 1f;
            if (Input.GetKey(KeyCode.Q)) turn = turn - 1f;
            if (turn != 0f)
            {
                TurnFromInput(turn, deltaTime);
            }
            else analyticsTurnDirection = 0f;

            if (Input.GetMouseButtonDown(1))
            {
                lastMousePos = Input.mousePosition;
            }
            if (Input.GetMouseButton(1))
            {
                Vector3 mouseMove = Input.mousePosition - lastMousePos;
                targetRotY += mouseMove.x * mouseSensitivity;
                rotY = targetRotY;
                rotX = Mathf.Clamp(rotX - mouseMove.y * mouseSensitivity, 5f, 75f);
                lastMousePos = Input.mousePosition;
            }

            distance3D = Mathf.Clamp(distance3D - Input.mouseScrollDelta.y, minZoom, maxZoom);
        }
    }

    public void SwitchView()
    {
        is2D = !is2D;
        analyticsTurnDirection = 0f;
        if (is2D)
        {
            targetRotY = Mathf.Round(rotY / 90f) * 90f;
        }
    }

    void SwitchViewFromInput()
    {
        SwitchView();
        CutAnalytics.RecordMechanic(Mechanic.ViewSwitch, "key_tab");
    }

    void TurnFromInput(float direction, float deltaTime)
    {
        if (direction == 0f) { analyticsTurnDirection = 0f; return; }
        if (is2D || analyticsTurnDirection != direction)
            CutAnalytics.RecordMechanic(Mechanic.Camera, direction < 0f ? "key_q" : "key_e");
        analyticsTurnDirection = direction;
        if (is2D) targetRotY += direction * 90f;
        else
        {
            targetRotY += direction * rotateSpeed * deltaTime;
            rotY = targetRotY;
        }
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

    void MoveCamera()
    {
        Vector3 center = lookPos + Vector3.up * offsetY;

        float camAngle = rotX;
        if (is2D) camAngle = 0f;
        Quaternion camRot = Quaternion.Euler(camAngle, rotY, 0f);

        float distance = distance3D;
        if (is2D) distance = distance2D;

        Vector3 camPos = center - camRot * Vector3.forward * distance;
        cam.orthographic = is2D;
        cam.orthographicSize = size2D;
        cam.fieldOfView = normalFov;

        if (aimBlend > 0f)
        {
            camPos = Vector3.Lerp(camPos, aimPosition, aimBlend);
            camRot = Quaternion.Slerp(camRot, aimRotation, aimBlend);
            cam.orthographic = false;
            cam.fieldOfView = Mathf.Lerp(normalFov, aimFov, aimBlend);
        }

        cam.transform.SetPositionAndRotation(camPos, camRot);
    }

    static Vector3 RoundDir(Vector3 dir)
    {
        return new Vector3(Mathf.Round(dir.x), 0f, Mathf.Round(dir.z));
    }
}
