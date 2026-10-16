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
    [Tooltip("Text that displays the player's current color.")]
    public TMP_Text colorText;

    [Tooltip("UI Image displayed beside the color text.")]
    public Image colorSwatch;

    [Tooltip("This should match the player's blue material.")]
    public Color blueColor =
        new Color(0.10f, 0.55f, 1.00f, 1f);

    [Tooltip("This should match the player's orange material.")]
    public Color orangeColor =
        new Color(1.00f, 0.42f, 0.05f, 1f);

    [Header("Hint System")]
    [Tooltip("The complete panel containing the hint.")]
    public GameObject hintPanel;

    [Tooltip("The TextMeshPro object that displays the hint.")]
    public TMP_Text hintText;

    [Tooltip("Seconds before an automatic hint appears.")]
    public float stuckDelay = 12f;

    [Tooltip("Seconds the hint remains visible.")]
    public float hintDuration = 6f;

    [Tooltip("Movement required to count as making progress.")]
    public float movementResetDistance = 0.75f;

    [Tooltip("The available hints. Element 0 is hint number 0.")]
    [TextArea(2, 4)]
    public string[] hints =
    {
        "Hint: Press <b>Tab</b> to switch between the 2D and 3D views.",
        "Hint: Press <b>Left Shift</b> to switch your color.",
        "Hint: Try leaving a cut piece on the pressure plate."
    };

    [Header("Restart Tip")]
    public GameObject restartTip;
    public float restartTipDelay = 20f;

    [Header("Scene References")]
    public LevelManager levelManager;
    public CuttablePiece player;
    public CameraRig camScript;
    public CutController cutScript;

    [Header("Message Settings")]
    public float messageTime = 2.5f;

    private bool showedRestartTip;
    private float messageTimer;
    private string statsFormat;

    private CuttablePiece cachedPlayer;
    private Renderer cachedPlayerRenderer;

    private CuttablePiece trackedHintPlayer;
    private Vector3 lastProgressPosition;
    private float stuckTimer;
    private float visibleHintTimer;
    private int nextHintIndex;

    private void Awake()
    {
        if (messageText != null)
        {
            messageText.text = "";
        }

        if (restartTip != null)
        {
            restartTip.SetActive(false);
        }

        if (hintPanel != null)
        {
            hintPanel.SetActive(false);
        }

        if (statsText != null)
        {
            statsFormat = statsText.text;
        }

        ResetHintTracking();
        UpdateColorIndicator();
    }

    private void Update()
    {
        UpdateStats();
        UpdateMode();
        UpdateColorIndicator();
        UpdateHintSystem();
        UpdateMessageTimer();
        UpdateRestartTip();
    }

    // ---------------------------------------------------------
    // STATS
    // ---------------------------------------------------------

    private void UpdateStats()
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

    // ---------------------------------------------------------
    // MODE INDICATOR
    // ---------------------------------------------------------

    private void UpdateMode()
    {
        if (modeText == null ||
            cutScript == null ||
            camScript == null)
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

    private void SetMode(string title, string smallText)
    {
        if (modeText == null)
        {
            return;
        }

        modeText.text =
            title +
            "\n<size=70%>" +
            smallText +
            "</size>";
    }

    private string GetViewName()
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

    private string GetText(
        TMP_Text textObject,
        string fallback)
    {
        if (textObject != null)
        {
            return textObject.text;
        }

        return fallback;
    }

    // ---------------------------------------------------------
    // COLOR INDICATOR
    // ---------------------------------------------------------

    private void UpdateColorIndicator()
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
            actualColor =
                currentMaterial.GetColor("_BaseColor");
        }
        // Standard materials normally use _Color.
        else if (currentMaterial.HasProperty("_Color"))
        {
            actualColor =
                currentMaterial.GetColor("_Color");
        }

        float blueDistance =
            ColorDistance(actualColor, blueColor);

        float orangeDistance =
            ColorDistance(actualColor, orangeColor);

        bool isBlue =
            blueDistance <= orangeDistance;

        Color displayColor =
            isBlue ? blueColor : orangeColor;

        string colorName =
            isBlue ? "BLUE" : "ORANGE";

        if (colorText != null)
        {
            colorText.text =
                "CURRENT COLOR: " + colorName;

            colorText.color = displayColor;
        }

        if (colorSwatch != null)
        {
            colorSwatch.color = displayColor;
        }
    }

    private float ColorDistance(
        Color firstColor,
        Color secondColor)
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

    // ---------------------------------------------------------
    // AUTOMATIC HINT SYSTEM
    // ---------------------------------------------------------

    private void UpdateHintSystem()
    {
        if (hintPanel == null ||
            hintText == null ||
            player == null)
        {
            return;
        }

        // Reset the tracking when control changes to a new piece.
        if (trackedHintPlayer != player)
        {
            ResetHintTracking();
        }

        // Do not show hints while cutting or choosing a piece.
        if (cutScript != null &&
            cutScript.state != CutController.State.Normal)
        {
            HideHint();
            ResetHintTracking();
            return;
        }

        // Let the restart tip take priority when no cuts remain.
        if (levelManager != null &&
            levelManager.cutsLeft == 0)
        {
            HideHint();
            ResetHintTracking();
            return;
        }

        // Using one of the suggested controls dismisses the hint.
        if (Input.GetKeyDown(KeyCode.Tab) ||
            Input.GetKeyDown(KeyCode.LeftShift))
        {
            HideHint();
            ResetHintTracking();
            return;
        }

        float distanceMoved = Vector3.Distance(
            player.transform.position,
            lastProgressPosition
        );

        // The player has moved far enough to count as progress.
        if (distanceMoved >= movementResetDistance)
        {
            lastProgressPosition =
                player.transform.position;

            stuckTimer = 0f;
            HideHint();
            return;
        }

        // Count down while a hint is visible.
        if (hintPanel.activeSelf)
        {
            visibleHintTimer -=
                Time.unscaledDeltaTime;

            if (visibleHintTimer <= 0f)
            {
                HideHint();

                stuckTimer = 0f;
                lastProgressPosition =
                    player.transform.position;
            }

            return;
        }

        // The player has not made progress.
        stuckTimer += Time.unscaledDeltaTime;

        if (stuckTimer >= stuckDelay)
        {
            ShowNextHint();
        }
    }

    private void ShowNextHint()
    {
        if (hints == null || hints.Length == 0)
        {
            return;
        }

        ShowHintByIndex(nextHintIndex);

        nextHintIndex++;

        if (nextHintIndex >= hints.Length)
        {
            nextHintIndex = 0;
        }
    }

    // Call this method to select one particular hint.
    // Example: ShowHintByIndex(1);
    public void ShowHintByIndex(int hintIndex)
    {
        if (hints == null ||
            hintIndex < 0 ||
            hintIndex >= hints.Length)
        {
            Debug.LogWarning(
                "HUD: Invalid hint index: " + hintIndex
            );

            return;
        }

        ShowHint(hints[hintIndex]);
    }

    // You can also call this method with custom text.
    public void ShowHint(string hint)
    {
        if (hintPanel == null || hintText == null)
        {
            return;
        }

        hintText.text = hint;
        hintPanel.SetActive(true);

        visibleHintTimer = hintDuration;
        stuckTimer = 0f;
    }

    public void HideHint()
    {
        visibleHintTimer = 0f;

        if (hintPanel != null)
        {
            hintPanel.SetActive(false);
        }
    }

    private void ResetHintTracking()
    {
        trackedHintPlayer = player;
        stuckTimer = 0f;
        visibleHintTimer = 0f;

        if (player != null)
        {
            lastProgressPosition =
                player.transform.position;
        }
    }

    // ---------------------------------------------------------
    // NORMAL MESSAGE
    // ---------------------------------------------------------

    public void ShowMessage(string message)
    {
        if (messageText == null)
        {
            return;
        }

        messageText.text = message;
        messageTimer = messageTime;
    }

    private void UpdateMessageTimer()
    {
        if (messageTimer <= 0f ||
            cutScript == null ||
            cutScript.state != CutController.State.Normal)
        {
            return;
        }

        messageTimer -= Time.unscaledDeltaTime;

        if (messageTimer <= 0f &&
            messageText != null)
        {
            messageText.text = "";
        }
    }

    // ---------------------------------------------------------
    // RESTART TIP
    // ---------------------------------------------------------

    private void UpdateRestartTip()
    {
        if (showedRestartTip ||
            levelManager == null ||
            cutScript == null)
        {
            return;
        }

        bool hasNoCuts =
            levelManager.cutsLeft == 0;

        bool isNormalMode =
            cutScript.state == CutController.State.Normal;

        bool delayFinished =
            Time.time - levelManager.lastCutTime >
            restartTipDelay;

        if (hasNoCuts &&
            isNormalMode &&
            delayFinished)
        {
            showedRestartTip = true;
            HideHint();

            if (restartTip != null)
            {
                restartTip.SetActive(true);
            }
        }
    }

    // Call this when restarting or loading a checkpoint.
    public void ResetRestartTip()
    {
        showedRestartTip = false;

        if (restartTip != null)
        {
            restartTip.SetActive(false);
        }

        HideHint();
        ResetHintTracking();
    }
}