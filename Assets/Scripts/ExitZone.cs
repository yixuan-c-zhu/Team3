using TMPro;
using UnityEngine;

public class ExitZone : MonoBehaviour
{
    public LevelManager levelManager;
    public TMP_Text exitText;
    public float labelHeight = 1.2f;

    private Vector3 labelPos;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void Start()
    {
        Bounds bounds = GetComponent<Collider>().bounds;
        labelPos = new Vector3(bounds.center.x, bounds.max.y + labelHeight, bounds.center.z);
    }

    void LateUpdate()
    {
        if (exitText == null || Camera.main == null) return;

        Vector3 worldPos = labelPos + Vector3.up * Mathf.Sin(Time.unscaledTime * 3f) * 0.2f;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0f)
        {
            exitText.enabled = false;
        }
        else
        {
            exitText.enabled = true;
            exitText.rectTransform.position = screenPos;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null && rb.GetComponent<PlayerController>() != null)
        {
            levelManager.CompleteLevel();
        }
    }
}
