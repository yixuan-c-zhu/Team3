using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MissStrip : MonoBehaviour
{
    public float fallTime = 0.42f;
    public float greyTime = 0.08f;
    public float gravity = 2600f;
    public float spin = 40f;
    public float thickness = 8f;
    public Color grey = new Color(0.62f, 0.63f, 0.66f, 1f);

    class Strip
    {
        public Image image;
        public Vector2 center;
        public float angle;
        public float length;
        public float clock;
        public float turn;
        public bool alive;
    }

    CutController cut;
    Canvas canvas;
    Color lineColor;
    readonly List<Strip> strips = new List<Strip>();

    void Start()
    {
        cut = FindFirstObjectByType<CutController>();
        if (cut != null) cut.CutMissed += OnMissed;
        lineColor = PaperFeel.Ink;
        if (cut != null && cut.lineRenderer != null && cut.lineRenderer.sharedMaterial != null)
        {
            lineColor = cut.lineRenderer.sharedMaterial.color;
        }
        lineColor.a = 1f;

        GameObject host = new GameObject("Miss Strips", typeof(RectTransform));
        host.transform.SetParent(transform, false);
        canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
    }

    void OnDestroy()
    {
        if (cut != null) cut.CutMissed -= OnMissed;
    }

    void OnMissed(Vector3 start, Vector3 end)
    {
        Strip strip = null;
        foreach (Strip free in strips)
        {
            if (!free.alive)
            {
                strip = free;
                break;
            }
        }
        if (strip == null)
        {
            Image image = new GameObject("Strip", typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(canvas.transform, false);
            image.raycastTarget = false;
            strip = new Strip { image = image };
            strips.Add(strip);
        }

        Vector2 a = start;
        Vector2 b = end;
        Vector2 along = b - a;
        strip.center = (a + b) * 0.5f;
        strip.length = along.magnitude;
        strip.angle = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
        strip.turn = (along.x * along.y >= 0f ? -1f : 1f) * spin;
        strip.clock = 0f;
        strip.alive = true;
        strip.image.enabled = true;
        Place(strip);
    }

    void LateUpdate()
    {
        float deltaTime = Time.unscaledDeltaTime;
        foreach (Strip strip in strips)
        {
            if (!strip.alive) continue;
            strip.clock += deltaTime;
            if (strip.clock >= fallTime)
            {
                strip.alive = false;
                strip.image.enabled = false;
                continue;
            }
            Place(strip);
        }
    }

    void Place(Strip strip)
    {
        float scale = Screen.height / 1080f;
        float t = strip.clock / fallTime;
        float fall = 0.5f * gravity * strip.clock * strip.clock * scale;
        RectTransform rect = strip.image.rectTransform;
        rect.position = new Vector3(strip.center.x, strip.center.y - fall, 0f);
        rect.sizeDelta = new Vector2(strip.length, thickness * scale);
        rect.localRotation = Quaternion.Euler(0f, 0f, strip.angle + strip.turn * strip.clock);
        Color color = Color.Lerp(lineColor, grey, Mathf.Clamp01(strip.clock / greyTime));
        color.a = 1f - t * t;
        strip.image.color = color;
    }
}
