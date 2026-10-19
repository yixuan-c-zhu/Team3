using Team3.Analytics;
using UnityEditor;
using UnityEngine;

public static class AnalyticsSettingsMenu
{
    [MenuItem("Tools/Analytics/Firebase Settings")]
    static void SelectSettings()
    {
        Selection.activeObject = Resources.Load<FirebaseAnalyticsSettings>(FirebaseAnalyticsSettings.ResourceName);
        if (Selection.activeObject != null) EditorGUIUtility.PingObject(Selection.activeObject);
    }
}
