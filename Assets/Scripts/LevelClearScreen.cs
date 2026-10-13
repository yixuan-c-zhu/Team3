using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelClearScreen : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text cutsText;
    public GameObject rewardBadge;
    public Button nextButton;
    public Button retryButton;
    public Button menuButton;

    public string mainMenuScene = "MainMenu";
    public int rewardMaxCuts = 1;

    public LevelManager levelManager;
    public CutController cutController;
    public PieceControlManager colorManager;
    public CameraRig camScript;

    private string cutsFormat;
    private bool isShown;

    void Awake()
    {
        if (levelManager == null)
        {
            levelManager = FindFirstObjectByType<LevelManager>();
        }
        if (cutController == null)
        {
            cutController = FindFirstObjectByType<CutController>();
        }
        if (colorManager == null)
        {
            colorManager = FindFirstObjectByType<PieceControlManager>();
        }
        if (camScript == null)
        {
            camScript = FindFirstObjectByType<CameraRig>();
        }

        cutsFormat = cutsText.text;
        panel.SetActive(false);

        nextButton.onClick.AddListener(LoadNextLevel);
        retryButton.onClick.AddListener(Retry);
        menuButton.onClick.AddListener(LoadMainMenu);
    }

    public bool IsShown()
    {
        return isShown;
    }

    public void Show()
    {
        if (isShown)
        {
            return;
        }
        isShown = true;

        HelpPanel.CloseAll();
        Time.timeScale = 0f;
        StopGameInput();

        int used = levelManager.CutsUsed();
        cutsText.text = string.Format(cutsFormat, used, levelManager.maxCuts);

        bool earnedReward = used <= rewardMaxCuts;
        rewardBadge.SetActive(earnedReward);
        if (earnedReward)
        {
            PlayerPrefs.SetInt("SingleCut_" + SceneManager.GetActiveScene().name, 1);
            PlayerPrefs.Save();
        }

        string nextScene = LevelCatalog.NextScene(SceneManager.GetActiveScene().name);
        nextButton.gameObject.SetActive(nextScene != LevelCatalog.MenuScene && LevelCatalog.CanLoad(nextScene));
        menuButton.gameObject.SetActive(Application.CanStreamedLevelBeLoaded(mainMenuScene));

        transform.SetAsLastSibling();
        panel.SetActive(true);
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    void StopGameInput()
    {
        if (cutController != null)
        {
            cutController.enabled = false;
            if (cutController.playerScript != null)
            {
                cutController.playerScript.canMove = false;
            }
        }
        if (colorManager != null)
        {
            colorManager.enabled = false;
        }
        if (camScript != null)
        {
            camScript.canControl = false;
        }
    }

    void LoadNextLevel()
    {
        LevelCatalog.Load(LevelCatalog.NextScene(SceneManager.GetActiveScene().name));
    }

    void Retry()
    {
        LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void LoadMainMenu()
    {
        LevelCatalog.Load(mainMenuScene);
    }

    void LoadScene(int index)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(index);
    }
}
