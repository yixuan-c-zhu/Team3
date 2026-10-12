using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HelpPanel : MonoBehaviour
{
    public Button toggleButton;
    public Button closeButton;
    public GameObject panel;

    public TMP_Text body;
    public Button nextHintButton;
    [TextArea(2, 5)]
    public string[] hints;

    public CutController cutController;
    public PieceControlManager colorManager;
    public CameraRig camScript;

    private static HelpPanel openPanel;

    private float savedTimeScale = 1f;
    private PlayerController pausedPlayer;
    private int shownHints = 1;

    void Awake()
    {
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

        panel.SetActive(false);
        toggleButton.onClick.AddListener(Toggle);
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Close);
        }
        if (nextHintButton != null)
        {
            nextHintButton.onClick.AddListener(ShowNextHint);
        }
        RefreshHints();
    }

    public void ShowNextHint()
    {
        if (hints != null && shownHints < hints.Length)
        {
            shownHints++;
            RefreshHints();
        }
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    void RefreshHints()
    {
        bool hasHints = hints != null && hints.Length > 0;
        if (nextHintButton != null)
        {
            nextHintButton.gameObject.SetActive(hasHints && shownHints < hints.Length);
        }
        if (!hasHints || body == null)
        {
            return;
        }

        string text = "";
        for (int i = 0; i < shownHints && i < hints.Length; i++)
        {
            if (i > 0)
            {
                text += "\n\n";
            }
            text += "<color=#FFD54A>" + (i + 1) + ".</color> " + hints[i];
        }
        body.text = text;
    }

    void Update()
    {
        bool canOpen = cutController == null || cutController.state == CutController.State.Normal || IsOpen();
        toggleButton.interactable = canOpen;
    }

    void OnDestroy()
    {
        if (openPanel == this)
        {
            openPanel = null;
        }
    }

    public static void CloseAll()
    {
        if (openPanel != null)
        {
            openPanel.Close();
        }
    }

    public bool IsOpen()
    {
        return openPanel == this;
    }

    public void Toggle()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        if (IsOpen())
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        if (cutController != null && cutController.state != CutController.State.Normal)
        {
            return;
        }
        if (openPanel != null)
        {
            openPanel.Close();
        }

        openPanel = this;
        panel.SetActive(true);

        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        SetGameInput(false);
    }

    public void Close()
    {
        if (!IsOpen())
        {
            return;
        }

        openPanel = null;
        panel.SetActive(false);
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        Time.timeScale = savedTimeScale;
        SetGameInput(true);
    }

    void SetGameInput(bool active)
    {
        if (cutController != null)
        {
            cutController.enabled = active;
        }
        if (colorManager != null)
        {
            colorManager.enabled = active;
        }
        if (camScript != null)
        {
            camScript.canControl = active;
        }

        if (!active)
        {
            pausedPlayer = cutController != null ? cutController.playerScript : null;
            if (pausedPlayer != null)
            {
                pausedPlayer.canMove = false;
            }
        }
        else if (pausedPlayer != null)
        {
            pausedPlayer.canMove = true;
            pausedPlayer = null;
        }
    }
}
