using TMPro;
using UnityEngine;

public class HUD : MonoBehaviour
{
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

    public GameObject restartTip;
    public float restartTipDelay = 20f;
    private bool showedRestartTip;

    public LevelManager levelManager;
    public CuttablePiece player;
    public CameraRig camScript;
    public CutController cutScript;

    public float messageTime = 2.5f;
    private float timer;

    private string statsFormat;

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
        statsFormat = statsText.text;
    }

    void Update()
    {
        Vector3 size = player.GetSize();
        statsText.text = string.Format(statsFormat,
            levelManager.cutsLeft, levelManager.maxCuts,
            size.x.ToString("0.0"), size.y.ToString("0.0"), size.z.ToString("0.0"),
            player.volume.ToString("0.0"));

        if (cutScript.state == CutController.State.Drawing)
        {
            SetMode(titleCut.text, GetViewName());
        }
        else if (cutScript.state == CutController.State.Choosing)
        {
            SetMode(titleChoose.text, chooseKeys.text);
        }
        else
        {
            if (camScript.is2D)
            {
                SetMode(title2D.text, GetViewName());
            }
            else
            {
                SetMode(title3D.text, GetViewName());
            }
        }

        if (timer > 0f && cutScript.state == CutController.State.Normal)
        {
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f)
            {
                messageText.text = "";
            }
        }

        if (!showedRestartTip && levelManager.cutsLeft == 0 && cutScript.state == CutController.State.Normal)
        {
            if (Time.time - levelManager.lastCutTime > restartTipDelay)
            {
                showedRestartTip = true;
                if (restartTip != null)
                {
                    restartTip.SetActive(true);
                }
            }
        }
    }

    void SetMode(string title, string small)
    {
        modeText.text = title + "\n<size=70%>" + small + "</size>";
    }

    string GetViewName()
    {
        if (!camScript.is2D) return viewFree.text;

        int side = camScript.GetViewSide();
        if (side == 0) return viewFront.text;
        if (side == 1) return viewLeft.text;
        if (side == 2) return viewBack.text;
        return viewRight.text;
    }

    public void ShowMessage(string msg)
    {
        if (messageText == null) return;
        messageText.text = msg;
        timer = messageTime;
    }

    // Hides the "Stuck? Press R" tip and lets it show again later, for a new room.
    public void ResetRestartTip()
    {
        showedRestartTip = false;
        if (restartTip != null)
        {
            restartTip.SetActive(false);
        }
    }
}
