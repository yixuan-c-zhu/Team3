using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Keep clicks on UI controls out of world-space cutting and piece selection.</summary>
public static class GameplayUiInput
{
    static readonly List<RaycastResult> hits = new List<RaycastResult>();
    static readonly Dictionary<KeyCode, int> controlFrames = new Dictionary<KeyCode, int>();

    public static void ReportControl(KeyCode equivalentKey)
    {
        controlFrames[equivalentKey] = Time.frameCount;
    }

    public static bool WasControlPressed(KeyCode key)
    {
        // Hints may update before the EventSystem, so allow the next frame too.
        return controlFrames.TryGetValue(key, out int frame) && Time.frameCount - frame <= 1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInput() => controlFrames.Clear();

    public static bool IsPointerOverControl()
    {
        var system = EventSystem.current;
        if (system == null) return false;
        // Raycast now rather than relying on the EventSystem's Update order.
        var pointer = new PointerEventData(system) { position = Input.mousePosition };
        hits.Clear();
        system.RaycastAll(pointer, hits);
        foreach (var hit in hits)
            if (hit.module is GraphicRaycaster && hit.gameObject.GetComponentInParent<Selectable>() != null)
                return true;
        return false;
    }
}
