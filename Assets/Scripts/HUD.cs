using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HUD : MonoBehaviour
{
    [Header("Existing HUD")]
    public TMP_Text statsText;
    public TMP_Text modeText;
    public TMP_Text messageText;

    public TMP_Text title2D;
    public TMP_Text title3D;
    public TMP_Text titleCut;
    public TMP_Text titleChoose;
    public TMP_Text viewFront;
    public TMP_Text viewLeft;
    public TMP_Text viewBack;
    public TMP_Text viewRight;
    public TMP_Text viewFree;
    public TMP_Text chooseKeys;

    [Header("Color Shift Indicator")]
    [Tooltip("Text that always displays the player's current color.")]
    public TMP_Text colorText;

    [Tooltip("Optional UI Image displayed beside the color text.")]
    public Image colorSwatch;

    [Tooltip("The blue used by the player material.")]
    public Color blueColor = new Color(0.10f, 0.55f, 1.00f, 1f);

    [Tooltip("The orange used by the player material.")]
    public Color orangeColor = new Color(1.00f, 0.42f, 0.05f, 1f);

    [Header("Always-Visible Controls")]
    [Tooltip("The TextMeshPro object used to display the controls.")]
    public TMP_Text controlsText;

    [TextArea(2, 5)]
    public string controls2D =
        "<b>MOVE</b>  A / D     " +
        "<b>JUMP</b>  Space     " +
        "<b>2D / 3D</b>  Tab     " +
        "<b>ROTATE</b>  Q / E\n" +
        "<b>COLOR SHIFT</b>  1 Blue / 2 Orange     " +
        "<b>CUT</b>  C     " +
        "<b>RESTART</b>  R     " +
        "<b>MENU</b>  Esc";

    [TextArea(2, 5)]
    public string controls3D =
        "<b>MOVE</b>  W / A / S / D     " +
        "<b>JUMP</b>  Space     " +
        "<b>2D / 3D</b>  Tab     " +
        "<b>CAMERA</b>  Hold RMB + Drag\n" +
        "<b>COLOR SHIFT</b>  1 Blue / 2 Orange     " +
        "<b>CUT</b>  C     " +
        "<b>RESTART</b>  R     " +
        "<b>MENU</b>  Esc";

    [TextArea(2, 4)]
    public string controlsCut =
        "<b>CUT MODE</b>     " +
        "Hold LMB and drag across the player to aim the cut     " +
        "<b>CANCEL</b>  C";

    [TextArea(2, 4)]
    public string controlsChoose =
        "<b>CHOOSE A PIECE</b>     " +
        "Left-click the piece you want to control";

    [Header("Restart Tip")]
    public GameObject restartTip;
    public float restartTipDelay = 20f;

    private bool showedRestartTip;

    [Header("Scene References")]
    public LevelManager levelManager;
    public CuttablePiece player;
    public CameraRig camScript;
    public CutController cutScript;

    public float messageTime = 2.5f;

    private float timer;
    private string statsFormat;

    private CuttablePiece cachedPlayer;
    private Renderer cachedPlayerRenderer;

    void Awake()
    {
        if (messageText != null)
        {
            messageText.text = "";
        }

        if (restartTip != null)
        {
            restartTip.SetActive(false);
        }

        if (statsText != null)
        {
            statsFormat = statsText.text;
        }

        UpdateColorIndicator();
        UpdateControls();
    }

    void Update()
    {
        UpdateStats();
        UpdateMode();
        UpdateColorIndicator();
        UpdateControls();
        UpdateMessageTimer();
        UpdateRestartTip();
    }

    void UpdateStats()
    {
        if (statsText == null ||
            levelManager == null ||
            player == null ||
            string.IsNullOrEmpty(statsFormat))
        {
            return;
        }

        Vector3 size = player.GetSize();

        statsText.text = string.Format(
            statsFormat,
            levelManager.cutsLeft,
            levelManager.maxCuts,
            size.x.ToString("0.0"),
            size.y.ToString("0.0"),
            size.z.ToString("0.0"),
            player.volume.ToString("0.0")
        );
    }

    void UpdateMode()
    {
        if (modeText == null || cutScript == null || camScript == null)
        {
            return;
        }

        if (cutScript.state == CutController.State.Drawing)
        {
            SetMode(
                GetText(titleCut, "CUT MODE"),
                GetViewName()
            );
        }
        else if (cutScript.state == CutController.State.Choosing)
        {
            SetMode(
                GetText(titleChoose, "CHOOSE PIECE"),
                GetText(chooseKeys, "Left click")
            );
        }
        else if (camScript.is2D)
        {
            SetMode(
                GetText(title2D, "2D MODE"),
                GetViewName()
            );
        }
        else
        {
            SetMode(
                GetText(title3D, "3D MODE"),
                GetViewName()
            );
        }
    }

    void UpdateColorIndicator()
    {
        if (colorText == null && colorSwatch == null)
        {
            return;
        }

        // The controlled player may change after a cut.
        if (cachedPlayer != player)
        {
            cachedPlayer = player;

            cachedPlayerRenderer =
                player != null
                ? player.GetComponentInChildren<Renderer>()
                : null;
        }

        Material currentMaterial =
            cachedPlayerRenderer != null
            ? cachedPlayerRenderer.sharedMaterial
            : null;

        if (currentMaterial == null)
        {
            if (colorText != null)
            {
                colorText.text = "CURRENT COLOR: --";
            }

            return;
        }

        Color actualColor = Color.white;

        // URP materials normally use _BaseColor.
        if (currentMaterial.HasProperty("_BaseColor"))
        {
            actualColor = currentMaterial.GetColor("_BaseColor");
        }
        // Standard materials normally use _Color.
        else if (currentMaterial.HasProperty("_Color"))
        {
            actualColor = currentMaterial.GetColor("_Color");
        }

        float blueDistance = ColorDistance(actualColor, blueColor);
        float orangeDistance = ColorDistance(actualColor, orangeColor);

        bool isBlue = blueDistance <= orangeDistance;

        Color displayColor = isBlue ? blueColor : orangeColor;
        string colorName = isBlue ? "BLUE" : "ORANGE";

        if (colorText != null)
        {
            colorText.text = "CURRENT COLOR: " + colorName;
            colorText.color = displayColor;
        }

        if (colorSwatch != null)
        {
            colorSwatch.color = displayColor;
        }
    }

    void UpdateControls()
    {
        if (controlsText == null || cutScript == null)
        {
            return;
        }

        if (cutScript.state == CutController.State.Drawing)
        {
            controlsText.text = controlsCut;
        }
        else if (cutScript.state == CutController.State.Choosing)
        {
            controlsText.text = controlsChoose;
        }
        else if (camScript != null && camScript.is2D)
        {
            controlsText.text = controls2D;
        }
        else
        {
            controlsText.text = controls3D;
        }
    }

    void UpdateMessageTimer()
    {
        if (timer <= 0f ||
            cutScript == null ||
            cutScript.state != CutController.State.Normal)
        {
            return;
        }

        timer -= Time.unscaledDeltaTime;

        if (timer <= 0f && messageText != null)
        {
            messageText.text = "";
        }
    }

    void UpdateRestartTip()
    {
        if (showedRestartTip ||
            levelManager == null ||
            cutScript == null)
        {
            return;
        }

        bool hasNoCuts = levelManager.cutsLeft == 0;
        bool isNormalMode =
            cutScript.state == CutController.State.Normal;

        bool delayFinished =
            Time.time - levelManager.lastCutTime > restartTipDelay;

        if (hasNoCuts && isNormalMode && delayFinished)
        {
            showedRestartTip = true;

            if (restartTip != null)
            {
                restartTip.SetActive(true);
            }
        }
    }

    void SetMode(string title, string small)
    {
        if (modeText == null)
        {
            return;
        }

        modeText.text =
            title +
            "\n<size=70%>" +
            small +
            "</size>";
    }

    string GetViewName()
    {
        if (camScript == null || !camScript.is2D)
        {
            return GetText(viewFree, "Free Camera");
        }

        int side = camScript.GetViewSide();

        if (side == 0)
        {
            return GetText(viewFront, "Front");
        }

        if (side == 1)
        {
            return GetText(viewLeft, "Left");
        }

        if (side == 2)
        {
            return GetText(viewBack, "Back");
        }

        return GetText(viewRight, "Right");
    }

    string GetText(TMP_Text textObject, string fallback)
    {
        if (textObject != null)
        {
            return textObject.text;
        }

        return fallback;
    }

    float ColorDistance(Color firstColor, Color secondColor)
    {
        float redDifference =
            firstColor.r - secondColor.r;

        float greenDifference =
            firstColor.g - secondColor.g;

        float blueDifference =
            firstColor.b - secondColor.b;

        return
            redDifference * redDifference +
            greenDifference * greenDifference +
            blueDifference * blueDifference;
    }

    public void ShowMessage(string msg)
    {
        if (messageText == null)
        {
            return;
        }

        messageText.text = msg;
        timer = messageTime;
    }

    // Hides the restart tip and allows it to appear again later.
    public void ResetRestartTip()
    {
        showedRestartTip = false;

        if (restartTip != null)
        {
            restartTip.SetActive(false);
        }
    }
}