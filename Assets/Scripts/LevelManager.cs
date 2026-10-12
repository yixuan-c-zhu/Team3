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

    void Awake()
    {
        Time.timeScale = 1f;
        cutsLeft = maxCuts;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartLevel();
        }

        if (player != null && player.transform.position.y < deathY)
        {
            RestartLevel();
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
    }

    public void RestartLevel()
    {
        if (!isLoading)
        {
            isLoading = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    public int CutsUsed()
    {
        return maxCuts - cutsLeft;
    }

    public void CompleteLevel()
    {
        if (isLoading)
        {
            return;
        }

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
            int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextIndex >= SceneManager.sceneCountInBuildSettings)
            {
                nextIndex = 0;
            }
            SceneManager.LoadScene(nextIndex);
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
