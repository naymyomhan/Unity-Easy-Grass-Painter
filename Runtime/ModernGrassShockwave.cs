using UnityEngine;

namespace ModernGrassTool
{
    [AddComponentMenu("Modern Grass/Grass Shockwave")]
    public class ModernGrassShockwave : MonoBehaviour
    {
        [Header("Shockwave Properties")]
        [Tooltip("Maximum radius of the blast in world units.")]
        [Range(1f, 50f)]
        public float radius = 8f;

        [Tooltip("Flattening push force on the grass blades.")]
        [Range(0.1f, 5f)]
        public float force = 1.5f;

        [Tooltip("Expansion speed of the shockwave ring in meters per second.")]
        [Range(5f, 80f)]
        public float speed = 22f;

        [Tooltip("Thickness of the expanding blast wave crest.")]
        [Range(0.5f, 10f)]
        public float thickness = 2.0f;

        [Header("Grass Cutting (Optional)")]
        [Tooltip("If true, cuts/shaves grass at the blast epicenter into short stubble.")]
        public bool cutGrass = false;

        [Tooltip("Radius of grass cut at the blast epicenter.")]
        [Range(0.5f, 15f)]
        public float cutRadius = 2.0f;

        [Tooltip("Height of stubble left after blast cut (e.g. 0.15m).")]
        [Range(0.02f, 0.5f)]
        public float cutStubbleHeight = 0.15f;

        [Header("Trigger Options")]
        [Tooltip("If true, triggers automatically when Start() runs (ideal for spawned explosion prefabs).")]
        public bool triggerOnStart = true;

        [Tooltip("If true, triggers every time this component or GameObject is enabled (ideal for pooled objects).")]
        public bool triggerOnEnable = false;

        private void Start()
        {
            if (triggerOnStart)
            {
                Trigger();
            }
        }

        private void OnEnable()
        {
            if (triggerOnEnable)
            {
                Trigger();
            }
        }

        [ContextMenu("Trigger Shockwave Now")]
        public void Trigger()
        {
            if (cutGrass)
            {
                ModernGrassManager.CutGrassAt(transform.position, cutRadius, cutStubbleHeight);
            }
            ModernGrassManager.TriggerShockwave(transform.position, radius, force, speed, thickness);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Min(radius, thickness));

            if (cutGrass)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.4f);
                Gizmos.DrawWireSphere(transform.position, cutRadius);
            }
        }
    }
}
