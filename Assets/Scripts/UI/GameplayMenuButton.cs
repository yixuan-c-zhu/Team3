using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A scene-local way back to chapter select, including the four original tutorials.</summary>
public static class GameplayMenuButton
{
    public static void Create()
    {
        if (!LevelCatalog.CanLoad(LevelCatalog.MenuScene)) return;
        var go = new GameObject("Chapter navigation", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 900);
        scaler.matchWidthOrHeight = 0.5f;

        var buttonGo = new GameObject("Level select", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(go.transform, false);
        var rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, -18);
        rect.sizeDelta = new Vector2(172, 40);
        buttonGo.GetComponent<Image>().color = new Color32(28, 42, 51, 235);
        var button = buttonGo.GetComponent<Button>();
        button.onClick.AddListener(() => LevelCatalog.Load(LevelCatalog.MenuScene));
        var colors = button.colors;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f);
        button.colors = colors;
        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(buttonGo.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var label = textGo.GetComponent<TextMeshProUGUI>();
        label.text = "<  LEVEL SELECT";
        label.fontSize = 15;
        label.characterSpacing = 1;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color32(245, 243, 236, 255);
        label.raycastTarget = false;
    }
}
