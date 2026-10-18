using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class HintZone : MonoBehaviour
{
    [TextArea] public string message = "The road ends here in 2D.\n<size=70%>Press <color=#FFD54A>Tab</color> to look around in 3D.</size>";
    public bool only2D = true;

    private BoxCollider area;
    private CutController cutController;
    private CameraRig camRig;
    private LevelManager levelManager;
    private bool showing;

    void Awake()
    {
        area = GetComponent<BoxCollider>();
        area.isTrigger = true;
        cutController = FindFirstObjectByType<CutController>();
        camRig = FindFirstObjectByType<CameraRig>();
        levelManager = FindFirstObjectByType<LevelManager>();
    }

    void Update()
    {
        bool show = ShouldShow();
        if (show)
        {
            if (!showing || IsShowing())
            {
                levelManager.ShowMessage(message);
            }
        }
        else if (showing && IsShowing())
        {
            levelManager.ShowMessage("");
        }
        showing = show;
    }

    bool ShouldShow()
    {
        if (cutController.state != CutController.State.Normal || !camRig.canControl)
        {
            return false;
        }
        if (only2D && !camRig.is2D)
        {
            return false;
        }
        return area.bounds.Contains(cutController.player.transform.position);
    }

    bool IsShowing()
    {
        HUD hud = levelManager.hud;
        if (hud == null || hud.messageText == null)
        {
            return false;
        }
        return hud.messageText.text == message;
    }
}
