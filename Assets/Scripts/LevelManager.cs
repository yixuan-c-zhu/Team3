using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelManager : MonoBehaviour
{
    public int maxCuts = 2;
    public float deathY = -12f;

    public CuttablePiece player;
    public HUD hud;

    public int cutsLeft;
    public float lastCutTime;

    private bool isLoading;
    private bool isRespawning;

    private Vector3 checkpointPosition;
    private Quaternion checkpointRotation;

    void Awake()
    {
        Time.timeScale = 1f;
        cutsLeft = maxCuts;
    }

    void Start()
    {
        GameplayMenuButton.Create();

        // The player's starting location is the default checkpoint.
        if (player != null)
        {
            checkpointPosition = player.transform.position;
            checkpointRotation = player.transform.rotation;
        }
    }

    void Update()
    {
        // R still completely restarts the level.
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartLevel();
        }

        // Falling now respawns the controlled piece instead
        // of restarting the complete level.
        if (player != null &&
            player.transform.position.y < deathY &&
            !isRespawning)
        {
            RespawnPlayer();
        }
    }

    public void SetCheckpoint(Transform checkpoint)
    {
        if (checkpoint == null)
        {
            return;
        }

        checkpointPosition = checkpoint.position;
        checkpointRotation = checkpoint.rotation;

        ShowMessage("Checkpoint reached!");
    }

    public void RespawnPlayer()
    {
        if (player == null || isRespawning)
        {
            return;
        }

        isRespawning = true;

        Rigidbody rb = player.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.position = checkpointPosition;
            rb.rotation = checkpointRotation;
        }
        else
        {
            player.transform.SetPositionAndRotation(
                checkpointPosition,
                checkpointRotation);
        }

        Physics.SyncTransforms();

        isRespawning = false;
    }

    public bool CanCut()
    {
        return cutsLeft > 0;
    }

    public void UseCut()
    {
        cutsLeft = cutsLeft - 1;

        if (cutsLeft < 0)
        {
            cutsLeft = 0;
        }

        lastCutTime = Time.time;
    }

    public int CutsUsed()
    {
        return maxCuts - cutsLeft;
    }

    public void RestartLevel()
    {
        if (!isLoading)
        {
            isLoading = true;
            Time.timeScale = 1f;

            SceneManager.LoadScene(
                SceneManager.GetActiveScene().buildIndex);
        }
    }

    public void CompleteLevel()
    {
        if (isLoading)
        {
            return;
        }

        LevelCatalog.MarkComplete(
            SceneManager.GetActiveScene().name);

        LevelClearScreen clearScreen =
            FindFirstObjectByType<LevelClearScreen>();

        if (clearScreen != null)
        {
            clearScreen.Show();
        }
        else
        {
            NextLevel();
        }
    }

    public void NextLevel()
    {
        if (!isLoading)
        {
            isLoading = true;
            Time.timeScale = 1f;

            LevelCatalog.Load(
                LevelCatalog.NextScene(
                    SceneManager.GetActiveScene().name));
        }
    }

    public void ShowMessage(string msg)
    {
        if (hud != null)
        {
            hud.ShowMessage(msg);
        }
        else
        {
            Debug.Log(msg);
        }
    }
}