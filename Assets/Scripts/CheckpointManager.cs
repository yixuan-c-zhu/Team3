using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs one scene as a row of rooms. Every room ends in an ExitZone, and each one except the
/// last room's is a checkpoint. Reaching a checkpoint starts the next room like a new level:
/// the player becomes a fresh cube at that room's spawn, cuts refill and leftover pieces are
/// removed. R or falling reloads the scene in the room the player has reached.
/// </summary>
public class CheckpointManager : MonoBehaviour
{
    [System.Serializable]
    public class Room
    {
        [Tooltip("Shown as the level title while the player is in this room.")]
        public string title;
        [Tooltip("Shown as the tip text while the player is in this room.")]
        [TextArea(2, 6)]
        public string tips;
        [Min(0)]
        public int cuts = 2;
        [Tooltip("Where the player starts this room, as a fresh cube.")]
        public Transform spawn;
        [Tooltip("Parent of this room's objects. The 2D side views only draw the current room.")]
        public Transform root;
        [Tooltip("The exit at the end of this room. A checkpoint, unless this is the last room.")]
        public ExitZone exit;
    }

    public Room[] rooms;

    public LevelManager levelManager;
    public CutController cutController;
    public PieceControlManager colorManager;
    public HUD hud;
    public CameraRig camScript;
    public TMP_Text titleText;
    public TMP_Text tipText;

    public string checkpointMessage = "Checkpoint! Fresh cube, cuts refilled";
    [Tooltip("The 2D side views look down the corridor, which lines every room up behind each other. When on, those views only draw the current room.")]
    public bool hideOtherRoomsInSideView = true;

    [Tooltip("Editor only: the room to start in when you press Play in this scene (0 = the first room).")]
    public int testStartRoom;

    public int CurrentRoom { get; private set; }

    private Renderer[][] roomRenderers;
    private bool finished;
    private int drawnRoom = -2;

    void Awake()
    {
        if (levelManager == null) levelManager = FindFirstObjectByType<LevelManager>();
        if (cutController == null) cutController = FindFirstObjectByType<CutController>();
        if (colorManager == null) colorManager = FindFirstObjectByType<PieceControlManager>();
        if (hud == null) hud = FindFirstObjectByType<HUD>();
        if (camScript == null) camScript = FindFirstObjectByType<CameraRig>();

        if (!RoomsAreSetUp())
        {
            enabled = false;
            return;
        }
        levelManager.checkpoints = this;

        roomRenderers = new Renderer[rooms.Length][];
        for (int i = 0; i < rooms.Length; i++)
        {
            roomRenderers[i] = rooms[i].root != null
                ? rooms[i].root.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
        }

        int start = LevelCatalog.TakeStartCheckpoint(SceneManager.GetActiveScene().name);
#if UNITY_EDITOR
        if (start < 0)
        {
            start = testStartRoom;
        }
#endif
        CurrentRoom = Mathf.Clamp(start, 0, rooms.Length - 1);

        // Awake runs before the camera's Start, so the camera starts on the player.
        Vector3 spawn = rooms[CurrentRoom].spawn.position;
        cutController.player.transform.position = spawn;
        cutController.player.GetComponent<Rigidbody>().position = spawn;
        ApplyRoom();
    }

    bool RoomsAreSetUp()
    {
        if (rooms == null || rooms.Length == 0)
        {
            Debug.LogError("CheckpointManager has no rooms.", this);
            return false;
        }
        for (int i = 0; i < rooms.Length; i++)
        {
            if (rooms[i].spawn == null || rooms[i].exit == null)
            {
                Debug.LogError("CheckpointManager: room " + (i + 1) + " needs a Spawn and an Exit.", this);
                return false;
            }
        }
        return true;
    }

    /// <summary>Called by an ExitZone when the player touches it.</summary>
    public void Reach(ExitZone exit)
    {
        if (finished) return;

        int index = System.Array.FindIndex(rooms, room => room.exit == exit);
        if (index < 0)
        {
            levelManager.CompleteLevel();
            return;
        }
        if (index < CurrentRoom)
        {
            return;
        }

        string scene = SceneManager.GetActiveScene().name;
        for (int i = CurrentRoom; i < index; i++)
        {
            LevelCatalog.MarkComplete(scene, i);
        }
        CurrentRoom = index;

        if (index == rooms.Length - 1)
        {
            finished = true;
            levelManager.CompleteLevel();
            return;
        }

        LevelCatalog.MarkComplete(scene, index);
        CurrentRoom = index + 1;
        RemoveLeftovers();
        ResetPlayer(rooms[CurrentRoom].spawn.position);
        ApplyRoom();
        levelManager.ShowMessage(checkpointMessage);
    }

    /// <summary>Makes the next load of this scene start in the current room. LevelManager calls it before restarting.</summary>
    public void RememberForRestart()
    {
        LevelCatalog.SetStartCheckpoint(SceneManager.GetActiveScene().name, CurrentRoom);
    }

    void ApplyRoom()
    {
        Room room = rooms[CurrentRoom];
        levelManager.maxCuts = room.cuts;
        levelManager.cutsLeft = room.cuts;

        if (titleText != null) titleText.text = room.title;
        if (tipText != null) tipText.text = room.tips;
        if (hud != null) hud.ResetRestartTip();

        for (int i = 0; i < rooms.Length; i++)
        {
            rooms[i].exit.showLabel = i == CurrentRoom;
        }
        drawnRoom = -2;
    }

    void ResetPlayer(Vector3 position)
    {
        CuttablePiece player = cutController.player;
        player.SetWorldShape(ConvexShape.MakeBox(player.boxSize).MoveBy(position));

        Rigidbody rb = player.GetComponent<Rigidbody>();
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        PieceColor color = player.GetComponent<PieceColor>();
        if (color != null && colorManager != null)
        {
            color.SetColor(PieceColor.PieceColorType.Blue, colorManager.blueMaterial, colorManager.orangeMaterial);
        }
    }

    // Leftovers can't follow the player into the next room, and F can't switch back to them.
    void RemoveLeftovers()
    {
        cutController.ForgetLeftover();
        foreach (CuttablePiece piece in FindObjectsByType<CuttablePiece>(FindObjectsSortMode.None))
        {
            if (piece == cutController.player) continue;

            MeshFilter filter = piece.GetComponent<MeshFilter>();
            if (filter.sharedMesh != null && filter.sharedMesh.name == "CutPiece")
            {
                Destroy(filter.sharedMesh);
            }
            Destroy(piece.gameObject);
        }
    }

    void FixedUpdate()
    {
        // Also counts a checkpoint the player got past without touching it,
        // by jumping over it or walking around it in 3D.
        if (finished) return;

        Room room = rooms[CurrentRoom];
        Vector3 fromExit = cutController.player.transform.position - room.exit.transform.position;
        fromExit.y = 0f;
        if (Vector3.Dot(fromExit, Ahead(room)) > 0f)
        {
            Reach(room.exit);
        }
    }

    void LateUpdate()
    {
        int drawOnly = -1;
        if (hideOtherRoomsInSideView && camScript != null && camScript.is2D)
        {
            Vector3 ahead = Ahead(rooms[CurrentRoom]);
            if (ahead.sqrMagnitude > 0.0001f && Mathf.Abs(Vector3.Dot(camScript.GetCamForward(), ahead.normalized)) > 0.7071f)
            {
                drawOnly = CurrentRoom;
            }
        }
        if (drawOnly == drawnRoom) return;
        drawnRoom = drawOnly;

        for (int i = 0; i < roomRenderers.Length; i++)
        {
            bool hide = drawOnly >= 0 && i != drawOnly;
            foreach (Renderer rend in roomRenderers[i])
            {
                if (rend != null) rend.forceRenderingOff = hide;
            }
        }
    }

    // The flat direction from a room's spawn to its exit.
    static Vector3 Ahead(Room room)
    {
        Vector3 ahead = room.exit.transform.position - room.spawn.position;
        ahead.y = 0f;
        return ahead;
    }

    void OnDrawGizmos()
    {
        if (rooms == null) return;

        Vector3 size = new Vector3(2f, 2f, 2f);
        if (cutController != null && cutController.player != null)
        {
            size = cutController.player.boxSize;
        }

        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        foreach (Room room in rooms)
        {
            if (room == null || room.spawn == null) continue;
            Gizmos.DrawWireCube(room.spawn.position, size);
            if (room.exit != null)
            {
                Gizmos.DrawLine(room.spawn.position, room.exit.transform.position);
            }
        }
    }
}
