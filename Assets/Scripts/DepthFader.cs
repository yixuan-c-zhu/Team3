using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class DepthFader : MonoBehaviour
{
    public CameraRig camScript;
    public CuttablePiece player;
    public float fadeAlpha = 0.2f;
    public float sideViewDepth = 4f;

    const float gap = 0.001f;

    private Collider playerCol;
    private Transform playerTransform;
    private int levelAxis;
    private bool inSideView;
    private readonly List<Renderer> scenery = new List<Renderer>();
    private readonly List<bool> sceneryFades = new List<bool>();
    private readonly HashSet<Renderer> hidden = new HashSet<Renderer>();

    void Awake()
    {
        playerCol = player.GetComponent<Collider>();
        playerTransform = player.transform;
    }

    void Start()
    {
        levelAxis = FindLevelAxis();
    }

    void LateUpdate()
    {
        FollowPlayer();
        if (!playerCol.enabled) return;

        Vector3 depthDir = camScript.GetDepthDir();
        int axis = 2;
        if (Mathf.Abs(depthDir.x) > 0.5f) axis = 0;

        Bounds playerBounds = playerCol.bounds;
        float playerMin = playerBounds.min[axis];
        float playerMax = playerBounds.max[axis];

        foreach (Fadeable obj in Fadeable.allFadeables)
        {
            Bounds bounds = obj.GetBounds();
            bool notOverlapping = false;
            if (bounds.max[axis] <= playerMin + gap || bounds.min[axis] >= playerMax - gap)
            {
                notOverlapping = true;
            }
            obj.SetFade(camScript.is2D && notOverlapping, fadeAlpha);
        }

        bool sideView = camScript.is2D && axis == levelAxis;
        if (sideView && !inSideView)
        {
            FindScenery();
        }
        inSideView = sideView;
        if (sideView)
        {
            HideFarScenery(axis, depthDir[axis], playerMin, playerMax);
        }
        else
        {
            ShowScenery();
        }
    }

    void OnDisable()
    {
        ShowScenery();
        inSideView = false;
    }

    void FollowPlayer()
    {
        if (camScript.player == null || camScript.player == playerTransform) return;
        Collider col = camScript.player.GetComponent<Collider>();
        if (col == null) return;
        playerTransform = camScript.player;
        playerCol = col;
    }

    int FindLevelAxis()
    {
        bool found = false;
        Bounds level = new Bounds();
        foreach (MeshRenderer rend in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (rend.GetComponentInParent<Rigidbody>() != null) continue;
            if (!found)
            {
                level = rend.bounds;
                found = true;
            }
            else
            {
                level.Encapsulate(rend.bounds);
            }
        }
        return level.size.z > level.size.x ? 2 : 0;
    }

    void FindScenery()
    {
        scenery.Clear();
        sceneryFades.Clear();
        foreach (MeshRenderer rend in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            scenery.Add(rend);
            sceneryFades.Add(rend.GetComponent<Fadeable>() != null);
        }
    }

    void HideFarScenery(int axis, float facing, float playerMin, float playerMax)
    {
        for (int i = 0; i < scenery.Count; i++)
        {
            Renderer rend = scenery[i];
            if (rend == null) continue;

            Bounds bounds = rend.bounds;
            float before = playerMin - bounds.max[axis];
            float after = bounds.min[axis] - playerMax;
            bool hide = false;
            if (before >= -gap || after >= -gap)
            {
                bool inFront = facing > 0f ? before > after : after > before;
                hide = Mathf.Max(before, after) > sideViewDepth || (inFront && !sceneryFades[i]);
            }

            if (hide)
            {
                if (hidden.Contains(rend))
                {
                    rend.forceRenderingOff = true;
                }
                else if (!rend.forceRenderingOff)
                {
                    rend.forceRenderingOff = true;
                    hidden.Add(rend);
                }
            }
            else if (hidden.Remove(rend))
            {
                rend.forceRenderingOff = false;
            }
        }
    }

    void ShowScenery()
    {
        if (hidden.Count == 0) return;
        foreach (Renderer rend in hidden)
        {
            if (rend != null) rend.forceRenderingOff = false;
        }
        hidden.Clear();
    }
}
