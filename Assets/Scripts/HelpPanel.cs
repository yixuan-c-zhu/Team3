using System.Text.RegularExpressions;
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
    private static readonly Regex goldText = new Regex("<color=#FFD54A>(.*?)</color>");

    private bool paper;

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
        if (PaperFeel.IsOn)
        {
            UsePaperLook();
        }
        RefreshHints();
    }

    void UsePaperLook()
    {
        paper = true;
        Dress(toggleButton.GetComponent<Image>(), toggleButton, PaperFeel.Cardstock, 3f, 12f);
        Dress(panel.GetComponent<Image>(), null, PaperFeel.Cardstock, 3.5f, 16f);
        if (closeButton != null)
        {
            Dress(closeButton.GetComponent<Image>(), closeButton, PaperFeel.Cardstock, 2.5f, 10f);
        }
        if (nextHintButton != null)
        {
            Dress(nextHintButton.GetComponent<Image>(), nextHintButton, PaperFeel.Gold, 2.5f, 10f);
        }
        foreach (TMP_Text label in GetComponentsInChildren<TMP_Text>(true))
        {
            label.color = PaperFeel.Ink;
        }
        if (body != null)
        {
            body.color = new Color(0.25f, 0.28f, 0.36f, 1f);
        }
    }

    static void Dress(Image image, Button button, Color fill, float border, float radius)
    {
        if (image == null)
        {
            return;
        }
        GameObject host = new GameObject("Paper", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialHudGraphic));
        RectTransform rect = host.GetComponent<RectTransform>();
        rect.SetParent(image.transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        TutorialHudGraphic card = host.GetComponent<TutorialHudGraphic>();
        card.shape = TutorialHudGraphic.Shape.Panel;
        card.color = fill;
        card.edge = PaperFeel.Ink;
        card.border = border;
        card.radius = radius;
        card.shadow = PaperFeel.Shadow;
        card.shadowOffset = PaperFeel.ShadowOffset;
        card.raycastTarget = false;
        image.color = new Color(1f, 1f, 1f, 0f);
        if (button != null)
        {
            button.targetGraphic = card;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.selectedColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.disabledColor = new Color(0.85f, 0.85f, 0.85f, 0.6f);
            button.colors = colors;
        }
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
            if (paper)
            {
                text += "<b><color=#22283A>" + (i + 1) + ".</color></b> " + goldText.Replace(hints[i], "<b><color=#22283A>$1</color></b>");
            }
            else
            {
                text += "<color=#FFD54A>" + (i + 1) + ".</color> " + hints[i];
            }
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
