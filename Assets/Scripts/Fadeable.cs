using System.Collections.Generic;
using UnityEngine;

public class Fadeable : MonoBehaviour
{
    public static List<Fadeable> allFadeables = new List<Fadeable>();

    public bool hideIn2D = false;

    private Renderer rend;
    private Color startColor;
    private bool isFaded;
    private float fadeAlpha = 1f;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        startColor = rend.material.color;
    }

    public Bounds GetBounds()
    {
        return rend.bounds;
    }

    void OnEnable()
    {
        allFadeables.Add(this);
    }

    void OnDisable()
    {
        allFadeables.Remove(this);
    }

    public void SetFade(bool fade, float alpha)
    {
        if (fade != isFaded)
        {
            isFaded = fade;
            fadeAlpha = alpha;
            ApplyColor();
        }
    }

    public void RefreshBaseColor()
    {
        if (rend == null)
        {
            rend = GetComponent<Renderer>();
        }
        startColor = rend.sharedMaterial.color;
        if (isFaded)
        {
            ApplyColor();
        }
    }

    void ApplyColor()
    {
        if (hideIn2D)
        {
            rend.enabled = !isFaded;
            return;
        }

        Color color = startColor;
        if (isFaded)
        {
            color.a = startColor.a * fadeAlpha;
        }
        rend.material.color = color;
    }
}
