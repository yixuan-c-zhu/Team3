using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TutorialControlTrigger : MonoBehaviour
{
    [Header("Shared Instruction UI")]
    [SerializeField] private GameObject instructionPanel;

    [SerializeField] private Image primaryControlImage;
    [SerializeField] private TMP_Text primaryInstructionText;

    [SerializeField] private Image secondaryControlImage;
    [SerializeField] private TMP_Text secondaryInstructionText;

    [Header("Primary Instruction")]
    [SerializeField] private Sprite primaryControlSprite;

    [TextArea(2, 5)]
    [SerializeField] private string primaryMessage;

    [Header("Secondary Instruction")]
    [SerializeField] private Sprite secondaryControlSprite;

    [TextArea(2, 5)]
    [SerializeField] private string secondaryMessage;

    [Header("Trigger Settings")]
    [SerializeField] private bool showOnlyOnce = true;
    [SerializeField] private bool pauseGameWhileShowing = true;

    [Header("Close Settings")]
    [SerializeField] private KeyCode closeKey = KeyCode.Space;
    [SerializeField] private float closeDelay = 0.25f;

    private bool hasShown;
    private bool isShowing;
    private float canCloseTime;

    private void Start()
    {
        if (instructionPanel != null)
        {
            instructionPanel.SetActive(false);
        }
    }

    private void Update()
    {
        if (!isShowing)
        {
            return;
        }

        if (Time.unscaledTime < canCloseTime)
        {
            return;
        }

        if (Input.GetKeyDown(closeKey))
        {
            HideInstruction();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        bool colliderIsPlayer = other.CompareTag("Player");
        bool rootIsPlayer = other.transform.root.CompareTag("Player");

        if (!colliderIsPlayer && !rootIsPlayer)
        {
            return;
        }

        if (showOnlyOnce && hasShown)
        {
            return;
        }

        ShowInstruction();
    }

    private void ShowInstruction()
    {
        if (instructionPanel == null)
        {
            Debug.LogWarning(
                "Instruction Panel is not assigned.",
                this
            );

            return;
        }

        SetInstruction(
            primaryControlImage,
            primaryInstructionText,
            primaryControlSprite,
            primaryMessage
        );

        SetInstruction(
            secondaryControlImage,
            secondaryInstructionText,
            secondaryControlSprite,
            secondaryMessage
        );

        hasShown = true;
        isShowing = true;
        canCloseTime = Time.unscaledTime + closeDelay;

        instructionPanel.SetActive(true);
        instructionPanel.transform.SetAsLastSibling();

        if (pauseGameWhileShowing)
        {
            Time.timeScale = 0f;
        }
    }

    private void SetInstruction(
        Image targetImage,
        TMP_Text targetText,
        Sprite targetSprite,
        string targetMessage
    )
    {
        if (targetImage != null)
        {
            targetImage.sprite = targetSprite;
            targetImage.preserveAspect = true;
            targetImage.gameObject.SetActive(
                targetSprite != null
            );
        }

        if (targetText != null)
        {
            targetText.text = targetMessage;
            targetText.gameObject.SetActive(
                !string.IsNullOrWhiteSpace(targetMessage)
            );
        }
    }

    private void HideInstruction()
    {
        isShowing = false;

        if (instructionPanel != null)
        {
            instructionPanel.SetActive(false);
        }

        if (pauseGameWhileShowing)
        {
            Time.timeScale = 1f;
        }
    }

    private void OnDisable()
    {
        if (isShowing && pauseGameWhileShowing)
        {
            Time.timeScale = 1f;
        }

        isShowing = false;
    }
}