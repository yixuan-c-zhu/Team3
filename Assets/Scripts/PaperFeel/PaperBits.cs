using System.Collections.Generic;
using UnityEngine;

public class PaperBits : MonoBehaviour
{
    public int capacity = 72;
    public float gravity = 9f;
    public float drag = 1.6f;
    public float fadeTime = 0.3f;
    public float flutter = 2.2f;

    class Bit
    {
        public SpriteRenderer renderer;
        public Vector3 velocity;
        public float spin;
        public float angle;
        public float age;
        public float life;
        public float size;
        public float floor;
        public float phase;
        public bool resting;
        public bool alive;
    }

    readonly List<Bit> bits = new List<Bit>();
    int next;

    void Awake()
    {
        for (int i = 0; i < capacity; i++)
        {
            SpriteRenderer renderer = new GameObject("Bit").AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(transform, false);
            renderer.enabled = false;
            bits.Add(new Bit { renderer = renderer });
        }
    }

    public void Spawn(Sprite sprite, Vector3 position, Vector3 velocity, float size, float life, float floor)
    {
        Bit bit = bits[next];
        next = (next + 1) % bits.Count;
        bit.renderer.sprite = sprite;
        bit.renderer.enabled = true;
        bit.renderer.color = Color.white;
        bit.renderer.transform.position = position;
        bit.velocity = velocity;
        bit.spin = Random.Range(-540f, 540f);
        bit.angle = Random.Range(0f, 360f);
        bit.age = 0f;
        bit.life = life;
        bit.size = size;
        bit.floor = floor;
        bit.phase = Random.Range(0f, Mathf.PI * 2f);
        bit.resting = false;
        bit.alive = true;
    }

    public static float FloorBelow(Vector3 position, float fallback)
    {
        RaycastHit hit;
        if (Physics.Raycast(position + Vector3.up * 0.05f, Vector3.down, out hit, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y + 0.02f;
        }
        return fallback;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float deltaTime = Time.deltaTime;
        Quaternion facing = cam.transform.rotation;
        foreach (Bit bit in bits)
        {
            if (!bit.alive) continue;
            bit.age += deltaTime;
            if (bit.age >= bit.life)
            {
                bit.alive = false;
                bit.renderer.enabled = false;
                continue;
            }

            Transform t = bit.renderer.transform;
            Vector3 position = t.position;
            if (!bit.resting)
            {
                bit.velocity.y -= gravity * deltaTime;
                float slow = Mathf.Exp(-drag * deltaTime);
                bit.velocity.x *= slow;
                bit.velocity.z *= slow;
                if (bit.velocity.y < 0f) bit.velocity.y *= Mathf.Exp(-flutter * deltaTime);
                position += bit.velocity * deltaTime;
                bit.angle += bit.spin * deltaTime;
                if (position.y <= bit.floor && bit.velocity.y < 0f)
                {
                    position.y = bit.floor;
                    bit.resting = true;
                }
            }
            t.position = position;

            float sway = bit.resting ? 0f : Mathf.Sin(bit.age * 9f + bit.phase) * 0.35f;
            if (bit.resting && !cam.orthographic)
            {
                t.rotation = Quaternion.LookRotation(Vector3.down, facing * Vector3.up) * Quaternion.Euler(0f, 0f, bit.angle);
            }
            else
            {
                t.rotation = facing * Quaternion.Euler(0f, 0f, bit.angle);
            }
            t.localScale = new Vector3(bit.size, bit.size * (1f - Mathf.Abs(sway)), bit.size);

            float alpha = Mathf.Clamp01((bit.life - bit.age) / fadeTime);
            bit.renderer.color = new Color(1f, 1f, 1f, alpha);
        }
    }
}
