using UnityEngine;
using UnityEngine.SceneManagement;
using Team3.Analytics;

public class LevelManager : MonoBehaviour
{
    public int maxCuts = 2;
    public float deathY = -12f;

    public CuttablePiece player;
    public HUD hud;
    [System.NonSerialized] public CheckpointManager checkpoints;

    public int cutsLeft;
    public float lastCutTime;

    private bool isLoading;
    private bool isCompleted;

    void Awake()
    {
        Time.timeScale = 1f;
        cutsLeft = maxCuts;
    }

    void Start()
    {
        GameplayMenuButton.Create();
        if (checkpoints == null)
            CutAnalytics.EnterRoom(SceneManager.GetActiveScene().name, cutsLeft, maxCuts);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartLevel("manual");
        }

        if (player != null && player.transform.position.y < deathY)
        {
            RestartLevel("fall");
        }
    }

    public bool CanCut()
    {
        return cutsLeft > 0;
    }

    public void UseCut()
    {
        cutsLeft = cutsLeft - 1;
        if (cutsLeft < 0) cutsLeft = 0;
        lastCutTime = Time.time;
        CutAnalytics.UpdateCuts(cutsLeft, maxCuts);
    }

    public void RestartLevel()
    {
        RestartLevel("manual");
    }

    public void RestartLevel(string reason)
    {
        if (!isLoading)
        {
            isLoading = true;
            CutAnalytics.RestartRoom(reason, cutsLeft, maxCuts);
            Time.timeScale = 1f;
            HelpPanel.CloseAll();
            if (checkpoints != null) checkpoints.RememberForRestart();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    public int CutsUsed()
    {
        return maxCuts - cutsLeft;
    }

    public void CompleteLevel()
    {
        if (isLoading || isCompleted)
        {
            return;
        }

        isCompleted = true;
        CutAnalytics.CompleteRoom(cutsLeft, maxCuts);
        LevelCatalog.MarkComplete(SceneManager.GetActiveScene().name, checkpoints != null ? checkpoints.CurrentRoom : 0);

        LevelClearScreen clearScreen = FindFirstObjectByType<LevelClearScreen>();
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
            LevelCatalog.Load(LevelCatalog.NextScene(SceneManager.GetActiveScene().name));
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
