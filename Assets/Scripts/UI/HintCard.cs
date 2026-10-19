using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// The card at the top of the screen that explains how to get past the obstacle the player is at.
/// TutorialHint areas fill it. Write keys in square brackets, like [C], and the card highlights them.
/// </summary>
public class HintCard : MonoBehaviour
{
    public CanvasGroup group;
    public TMP_Text label;
    public TMP_Text title;
    public TMP_Text steps;
    [Tooltip("The 'Still stuck?' row. Shown after the player has been at the obstacle for a while.")]
    public GameObject stuckRow;
    public TMP_Text stuckText;
    [Tooltip("Put in front of the Stuck Hint.")]
    public string stuckPrefix = "<b>Still stuck?</b>  ";

    [Tooltip("Color of the step numbers and highlighted keys.")]
    public Color accent = new Color(1f, 0.835f, 0.29f, 1f);
    public float fadeTime = 0.25f;
    public float slideDistance = 30f;
    [Tooltip("Direction the card slides in from. (-1, 0) = from the left, (0, 1) = from above.")]
    public Vector2 slideFrom = new Vector2(0f, 1f);

    static readonly Regex keyPattern = new Regex(@"\[([^\]]+)\]");

    private RectTransform rect;
    private Vector2 shownPosition;
    private TutorialHint showing;
    private bool showingStuck;
    private bool wantVisible;
    private float visible;
    private float pulse;

    void Awake()
    {
        rect = (RectTransform)transform;
        shownPosition = rect.anchoredPosition;
        group.blocksRaycasts = false;
        group.interactable = false;
        if (stuckRow != null) stuckRow.SetActive(false);
        Apply();
    }

    /// <summary>Shows this hint. Call it every frame the player is at the obstacle.</summary>
    public void Show(TutorialHint hint, bool stuck)
    {
        if (hint != showing)
        {
            showing = hint;
            showingStuck = false;
            label.text = hint.label;
            title.text = Keys(hint.title);
            steps.text = BuildSteps(hint.steps);
            if (stuckRow != null) stuckRow.SetActive(false);
        }

        if (stuck && !showingStuck && stuckRow != null && !string.IsNullOrEmpty(hint.stuckHint))
        {
            showingStuck = true;
            stuckText.text = stuckPrefix + Keys(hint.stuckHint);
            stuckRow.SetActive(true);
            pulse = 1f;
        }
        wantVisible = true;
    }

    /// <summary>Hides the card, if it is showing this hint.</summary>
    public void Hide(TutorialHint hint)
    {
        if (hint == showing) wantVisible = false;
    }

    string BuildSteps(string[] lines)
    {
        if (lines == null) return "";
        string hex = ColorUtility.ToHtmlStringRGB(accent);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append("<color=#").Append(hex).Append("><b>").Append(i + 1).Append("</b></color>");
            sb.Append("<indent=1.3em>").Append(Keys(lines[i])).Append("</indent>");
        }
        return sb.ToString();
    }

    string Keys(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string hex = ColorUtility.ToHtmlStringRGB(accent);
        return keyPattern.Replace(text, "<mark=#" + hex + "33><color=#" + hex + "><b>\u00A0$1\u00A0</b></color></mark>");
    }

    void Update()
    {
        visible = Mathf.MoveTowards(visible, wantVisible ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
        pulse = Mathf.MoveTowards(pulse, 0f, Time.unscaledDeltaTime * 1.5f);
        if (visible <= 0f && !wantVisible) showing = null;
        Apply();
    }

    void Apply()
    {
        float eased = 1f - (1f - visible) * (1f - visible);
        group.alpha = eased;
        rect.anchoredPosition = shownPosition + slideFrom.normalized * slideDistance * (1f - eased);
        float bump = 1f + 0.04f * Mathf.Sin(pulse * Mathf.PI * 3f) * pulse;
        rect.localScale = new Vector3(bump, bump, 1f);
    }
}
