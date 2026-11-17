using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(150)]
public class PopUpPaths : MonoBehaviour
{
    public float riseTime = 0.42f;
    public float foldTime = 0.22f;
    public float stagger = 0.035f;
    public float maxDelay = 0.45f;
    public float reach = 45f;
    public float overshoot = 2.2f;
    public float flatHeight = 1.2f;

    enum Motion { Still, Rise, Fold }

    class Path
    {
        public Fadeable fade;
        public Renderer renderer;
        public Mesh mesh;
        public Material[] solid;
        public bool wasGhost;
        public Motion motion;
        public float clock;
        public float delay;
        public bool hiddenByUs;
        public Vector3 away;
        public bool flat;
    }

    readonly List<Path> paths = new List<Path>();
    CameraRig rig;
    CutController cut;
    bool was2D;

    void Start()
    {
        rig = FindFirstObjectByType<CameraRig>();
        cut = FindFirstObjectByType<CutController>();
        foreach (Fadeable fade in FindObjectsByType<Fadeable>(FindObjectsSortMode.None))
        {
            if (fade.fadeMaterial == null) continue;
            Renderer renderer = fade.GetComponent<Renderer>();
            MeshFilter filter = fade.GetComponent<MeshFilter>();
            if (renderer == null || filter == null || filter.sharedMesh == null) continue;
            Path path = new Path { fade = fade, renderer = renderer, mesh = filter.sharedMesh };
            path.solid = renderer.sharedMaterial != fade.fadeMaterial ? renderer.sharedMaterials : null;
            paths.Add(path);
        }
        was2D = rig == null || rig.is2D;
    }

    void LateUpdate()
    {
        if (rig == null || cut == null || cut.player == null) return;
        bool is2D = rig.is2D;
        Vector3 player = cut.player.transform.position;
        if (is2D != was2D)
        {
            foreach (Path path in paths) Begin(path, is2D ? Motion.Fold : Motion.Rise, player);
        }
        was2D = is2D;

        float deltaTime = Time.unscaledDeltaTime;
        foreach (Path path in paths)
        {
            if (path.renderer == null) continue;
            bool ghost = path.renderer.sharedMaterial == path.fade.fadeMaterial;
            if (!ghost && path.motion == Motion.Still) path.solid = path.renderer.sharedMaterials;
            path.wasGhost = ghost;
            if (path.motion != Motion.Still) Animate(path, deltaTime);
        }
    }

    void Begin(Path path, Motion motion, Vector3 player)
    {
        if (path.renderer == null || path.solid == null) return;
        bool ghost = path.renderer.sharedMaterial == path.fade.fadeMaterial;
        bool wanted = motion == Motion.Rise ? path.wasGhost : ghost;
        Bounds bounds = path.renderer.bounds;
        float distance = Vector3.Distance(bounds.ClosestPoint(player), player);
        if (!wanted || distance > reach)
        {
            Stop(path);
            return;
        }
        if (path.motion == Motion.Still)
        {
            if (path.renderer.forceRenderingOff) return;
            path.renderer.forceRenderingOff = true;
            path.hiddenByUs = true;
        }

        Vector3 away = bounds.center - player;
        away.y = 0f;
        path.away = Mathf.Abs(away.x) > Mathf.Abs(away.z)
            ? new Vector3(Mathf.Sign(away.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(away.z));
        path.flat = bounds.size.y < flatHeight;
        path.motion = motion;
        path.clock = 0f;
        path.delay = motion == Motion.Rise ? Mathf.Min(maxDelay, distance * stagger) : 0f;
    }

    void Animate(Path path, float deltaTime)
    {
        path.clock += deltaTime;
        float duration = path.motion == Motion.Rise ? riseTime : foldTime;
        float t = (path.clock - path.delay) / duration;
        if (t >= 1f)
        {
            Stop(path);
            return;
        }

        float open;
        if (path.motion == Motion.Rise)
        {
            open = t <= 0f ? 0f : PaperFeel.BackOut(t, overshoot);
        }
        else
        {
            float u = Mathf.Clamp01(t);
            open = 1f - u * u;
        }
        if (open < 0.01f) return;

        Bounds bounds = path.renderer.bounds;
        Vector3 nearFace = bounds.center - Vector3.Scale(path.away, bounds.extents);
        Matrix4x4 pose;
        if (path.flat)
        {
            Vector3 scale = Vector3.one;
            if (path.away.x != 0f) scale.x = open;
            else scale.z = open;
            pose = Matrix4x4.Translate(nearFace) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-nearFace);
        }
        else
        {
            Vector3 hinge = new Vector3(nearFace.x, bounds.min.y, nearFace.z);
            Vector3 axis = Vector3.Cross(Vector3.up, path.away);
            float angle = 90f * (1f - open);
            pose = Matrix4x4.Translate(hinge) * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, axis)) * Matrix4x4.Translate(-hinge);
        }
        Matrix4x4 matrix = pose * path.renderer.localToWorldMatrix;

        int count = Mathf.Min(path.mesh.subMeshCount, path.solid.Length);
        for (int i = 0; i < count; i++)
        {
            if (path.solid[i] == null) continue;
            RenderParams draw = new RenderParams(path.solid[i]);
            draw.layer = path.renderer.gameObject.layer;
            draw.shadowCastingMode = ShadowCastingMode.On;
            draw.receiveShadows = true;
            draw.worldBounds = new Bounds(bounds.center, bounds.size * 3f + Vector3.one * 2f);
            Graphics.RenderMesh(draw, path.mesh, i, matrix);
        }
    }

    void Stop(Path path)
    {
        if (path.hiddenByUs && path.renderer != null) path.renderer.forceRenderingOff = false;
        path.hiddenByUs = false;
        path.motion = Motion.Still;
    }

    void OnDisable()
    {
        foreach (Path path in paths) Stop(path);
    }
}
