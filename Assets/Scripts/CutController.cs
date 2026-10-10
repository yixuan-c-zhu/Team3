using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CutController : MonoBehaviour
{
    public CuttablePiece player;
    public PlayerController playerScript;
    public CameraRig camScript;
    public LevelManager levelManager;
    public LineRenderer lineRenderer;

    public Material piece1Mat;
    public Material piece2Mat;
    public Material leftoverMat;

    public float minSize = 0.25f;
    public bool canRotateLeftover = false;
    public bool canPushLeftover = false;
    public float lineWidth = 4f;

    public TMP_Text noCutsMsg;
    public TMP_Text cutModeMsg;
    public TMP_Text cancelMsg;
    public TMP_Text missMsg;
    public TMP_Text tooSmallMsg;
    public TMP_Text chooseMsg;
    public TMP_Text niceCutMsg;
    public TMP_Text switchMsg;

    public KeyCode switchKey = KeyCode.F;

    public enum State { Normal, Drawing, Choosing }
    public State state = State.Normal;

    private Vector3 mouseStart;
    private bool isDragging;
    private GameObject[] previewObjs = new GameObject[2];
    private ConvexShape[] pieces = new ConvexShape[2];
    private Renderer playerRend;
    private Collider playerCol;
    private CuttablePiece lastLeftover;

    void Awake()
    {
        playerRend = player.GetComponent<Renderer>();
        playerCol = player.GetComponent<Collider>();
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
    }

    void Update()
    {
        switch (state)
        {
            case State.Normal:
                if (Input.GetKeyDown(KeyCode.C))
                {
                    StartCut();
                }
                else if (Input.GetKeyDown(switchKey))
                {
                    SwitchPiece();
                }
                break;

            case State.Drawing:
                if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.Escape))
                {
                    EndCut(cancelMsg.text);
                    break;
                }
                DrawLine();
                break;

            case State.Choosing:
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    PickPiece(0);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    PickPiece(1);
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelCut();
                }
                else if (Input.GetMouseButtonDown(0))
                {
                    ClickPiece();
                }
                break;
        }
    }

    void StartCut()
    {
        if (!levelManager.CanCut())
        {
            levelManager.ShowMessage(noCutsMsg.text);
            return;
        }
        state = State.Drawing;
        playerScript.StopPlayer();
        playerScript.canMove = false;
        Time.timeScale = 0f;
        levelManager.ShowMessage(cutModeMsg.text);
    }

    void EndCut(string msg)
    {
        state = State.Normal;
        Time.timeScale = 1f;
        playerScript.canMove = true;
        camScript.canControl = true;
        isDragging = false;
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
        if (msg != null)
        {
            levelManager.ShowMessage(msg);
        }
    }

    void DrawLine()
    {
        if (Input.GetMouseButtonDown(0))
        {
            mouseStart = Input.mousePosition;
            isDragging = true;
        }

        if (isDragging)
        {
            UpdateLine(mouseStart, Input.mousePosition);

            if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
                if ((Input.mousePosition - mouseStart).magnitude > 12f)
                {
                    DoCut(mouseStart, Input.mousePosition);
                }
                else if (lineRenderer != null)
                {
                    lineRenderer.enabled = false;
                }
            }
        }
    }

    void UpdateLine(Vector3 start, Vector3 end)
    {
        if (lineRenderer == null) return;

        Camera cam = camScript.cam;
        float depth = cam.nearClipPlane + 0.5f;
        lineRenderer.enabled = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, cam.ScreenToWorldPoint(new Vector3(start.x, start.y, depth)));
        lineRenderer.SetPosition(1, cam.ScreenToWorldPoint(new Vector3(end.x, end.y, depth)));

        float pixelSize;
        if (cam.orthographic)
        {
            pixelSize = 2f * cam.orthographicSize / Screen.height;
        }
        else
        {
            pixelSize = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
        }
        lineRenderer.startWidth = lineWidth * pixelSize;
        lineRenderer.endWidth = lineWidth * pixelSize;
    }

    void DoCut(Vector3 start, Vector3 end)
    {
        Camera cam = camScript.cam;
        Ray ray1 = cam.ScreenPointToRay(start);
        Ray ray2 = cam.ScreenPointToRay(end);

        Vector3 p1 = ray1.GetPoint(1f);
        Vector3 p2 = ray2.GetPoint(1f);
        Vector3 p3 = ray1.GetPoint(5f);
        Vector3 normal = Vector3.Cross(p2 - p1, p3 - p1);
        if (normal.sqrMagnitude < 1e-10f)
        {
            lineRenderer.enabled = false;
            return;
        }
        normal.Normalize();
        float planeDist = Vector3.Dot(normal, p1);

        ConvexShape front;
        ConvexShape back;
        if (!player.GetWorldShape().Cut(normal, planeDist, out front, out back))
        {
            levelManager.ShowMessage(missMsg.text);
            lineRenderer.enabled = false;
            return;
        }
        if (front.GetVolume() < minSize || back.GetVolume() < minSize)
        {
            levelManager.ShowMessage(tooSmallMsg.text);
            lineRenderer.enabled = false;
            return;
        }

        pieces[0] = front;
        pieces[1] = back;

        GameObject obj1 = new GameObject("Piece 1 (preview)");
        Mesh mesh1 = front.MakeMesh();
        obj1.AddComponent<MeshFilter>().sharedMesh = mesh1;
        obj1.AddComponent<MeshRenderer>().sharedMaterial = piece1Mat;
        MeshCollider col1 = obj1.AddComponent<MeshCollider>();
        col1.convex = true;
        col1.isTrigger = true;
        col1.sharedMesh = mesh1;
        previewObjs[0] = obj1;

        GameObject obj2 = new GameObject("Piece 2 (preview)");
        Mesh mesh2 = back.MakeMesh();
        obj2.AddComponent<MeshFilter>().sharedMesh = mesh2;
        obj2.AddComponent<MeshRenderer>().sharedMaterial = piece2Mat;
        MeshCollider col2 = obj2.AddComponent<MeshCollider>();
        col2.convex = true;
        col2.isTrigger = true;
        col2.sharedMesh = mesh2;
        previewObjs[1] = obj2;

        playerRend.enabled = false;
        playerCol.enabled = false;
        lineRenderer.enabled = false;
        camScript.canControl = false;
        state = State.Choosing;
        levelManager.ShowMessage(chooseMsg.text);
    }

    void ClickPiece()
    {
        Ray ray = camScript.cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, 1000f, ~0, QueryTriggerInteraction.Collide)) return;

        for (int i = 0; i < 2; i++)
        {
            if (previewObjs[i] != null && hit.collider.gameObject == previewObjs[i])
            {
                PickPiece(i);
                return;
            }
        }
    }

    void PickPiece(int index)
    {
        ConvexShape keepShape = pieces[index];
        ConvexShape leftShape = pieces[1 - index];

        player.SetWorldShape(keepShape);
        playerRend.enabled = true;
        playerCol.enabled = true;
        MakeLeftover(leftShape);

        for (int i = 0; i < 2; i++)
        {
            if (previewObjs[i] != null)
            {
                Destroy(previewObjs[i].GetComponent<MeshFilter>().sharedMesh);
                Destroy(previewObjs[i]);
                previewObjs[i] = null;
            }
        }

        levelManager.UseCut();
        EndCut(niceCutMsg.text);
    }

    void CancelCut()
    {
        for (int i = 0; i < 2; i++)
        {
            if (previewObjs[i] != null)
            {
                Destroy(previewObjs[i].GetComponent<MeshFilter>().sharedMesh);
                Destroy(previewObjs[i]);
                previewObjs[i] = null;
            }
        }
        playerRend.enabled = true;
        playerCol.enabled = true;
        EndCut(cancelMsg.text);
    }

    void MakeLeftover(ConvexShape leftShape)
    {
        GameObject obj = new GameObject("Leftover Piece");
        CuttablePiece piece = obj.AddComponent<CuttablePiece>();
        piece.density = player.density;
        piece.SetWorldShape(leftShape);
        obj.GetComponent<MeshRenderer>().sharedMaterial = leftoverMat;
        obj.GetComponent<MeshCollider>().sharedMaterial = playerCol.sharedMaterial;

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        RigidbodyConstraints freeze = RigidbodyConstraints.None;
        if (!canRotateLeftover)
        {
            freeze |= RigidbodyConstraints.FreezeRotation;
        }
        if (!canPushLeftover)
        {
            freeze |= RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
        }
        rb.constraints = freeze;

        obj.AddComponent<Fadeable>();

        lastLeftover = piece;
    }

    void SwitchPiece()
    {
        if (lastLeftover == null) return;
        CuttablePiece other = lastLeftover;

        Rigidbody playerRb = player.GetComponent<Rigidbody>();
        Rigidbody otherRb = other.GetComponent<Rigidbody>();
        Vector3 playerVel = playerRb.linearVelocity;
        Vector3 otherVel = otherRb.linearVelocity;

        ConvexShape playerShape = player.GetWorldShape();
        ConvexShape otherShape = other.GetWorldShape();
        player.SetWorldShape(otherShape);
        other.SetWorldShape(playerShape);

        playerRb.linearVelocity = otherVel;
        playerRb.angularVelocity = Vector3.zero;
        otherRb.linearVelocity = playerVel;
        otherRb.angularVelocity = Vector3.zero;

        if (switchMsg != null)
        {
            levelManager.ShowMessage(switchMsg.text);
        }
    }

}
