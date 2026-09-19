using UnityEngine;

namespace ModernGrassTool
{
    public enum GrassWindMode
    {
        Directional,      // Blasts forward in a conical/directional stream (spells, fans, jet blasts)
        Omnidirectional   // Radiates 360 degrees outward horizontally (helicopter rotor wash, power charging aura)
    }

    public enum GrassWindDuration
    {
        Continuous,       // Blows indefinitely while active (helicopter, continuous fan, charging aura)
        TimedBurst        // Blows for a set duration and smoothly fades out (wind gust spell, burst pulse)
    }

    [AddComponentMenu("Modern Grass/Grass Wind Zone")]
    [ExecuteAlways]
    public class ModernGrassWindZone : MonoBehaviour
    {
        [Header("Wind Zone Settings")]
        [Tooltip("Directional: Focused cone forward. Omnidirectional: 360 degree radial outward wash.")]
        public GrassWindMode mode = GrassWindMode.Omnidirectional;

        [Tooltip("Continuous: Runs indefinitely while enabled. TimedBurst: Runs for burstDuration and fades.")]
        public GrassWindDuration durationMode = GrassWindDuration.Continuous;

        [Tooltip("Maximum reach/radius of the wind in world units.")]
        [Range(1f, 100f)]
        public float radius = 12f;

        [Tooltip("Bending push strength applied to grass blades.")]
        [Range(0.1f, 5f)]
        public float force = 2.0f;

        [Header("Directional Settings")]
        [Tooltip("Cone spread angle in degrees (for Directional mode).")]
        [Range(10f, 180f)]
        public float coneAngle = 60f;

        [Header("Omnidirectional / Helicopter Settings")]
        [Tooltip("Maximum vertical distance (height above ground) for rotor wash to reach terrain.")]
        [Range(1f, 50f)]
        public float verticalRange = 15f;

        [Header("Flutter & Dynamic Ripples")]
        [Tooltip("Frequency of dynamic wind ripple waves travelling through the grass.")]
        [Range(1f, 40f)]
        public float flutterSpeed = 18f;

        [Header("Timed Burst Settings")]
        [Tooltip("Duration in seconds for TimedBurst mode.")]
        public float burstDuration = 2.0f;

        [System.NonSerialized]
        public float burstStartTime = -999f;

        private void OnEnable()
        {
            burstStartTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
            ModernGrassManager.RegisterWindZone(this);
        }

        private void OnDisable()
        {
            ModernGrassManager.UnregisterWindZone(this);
            if (Application.isPlaying && force > 0.01f && radius > 0.1f)
            {
                Vector3 dir = mode == GrassWindMode.Directional ? transform.forward : Vector3.down;
                ModernGrassManager.RecordWindRecoil(transform.position, dir, radius, force, coneAngle, verticalRange, mode);
            }
        }

        [ContextMenu("Trigger Burst Now")]
        public void TriggerBurst()
        {
            burstStartTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
        }

        public bool IsActive(float currentTime, out float fadeWeight)
        {
            fadeWeight = 1.0f;
            if (durationMode == GrassWindDuration.Continuous)
            {
                return true;
            }

            float elapsed = currentTime - burstStartTime;
            if (elapsed < 0f || elapsed >= burstDuration)
            {
                return false;
            }

            float progress = elapsed / burstDuration;
            fadeWeight = Mathf.SmoothStep(1.0f, 0.0f, progress);
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.6f);

            if (mode == GrassWindMode.Directional)
            {
                Vector3 origin = transform.position;
                Vector3 forward = transform.forward;
                Gizmos.DrawRay(origin, forward * radius);

                float halfAngleRad = (coneAngle * 0.5f) * Mathf.Deg2Rad;
                float coneBaseRadius = Mathf.Tan(halfAngleRad) * radius;
                Vector3 endCenter = origin + forward * radius;

                Vector3 up = transform.up * coneBaseRadius;
                Vector3 right = transform.right * coneBaseRadius;

                Gizmos.DrawLine(origin, endCenter + up);
                Gizmos.DrawLine(origin, endCenter - up);
                Gizmos.DrawLine(origin, endCenter + right);
                Gizmos.DrawLine(origin, endCenter - right);

                Gizmos.DrawLine(endCenter + up, endCenter + right);
                Gizmos.DrawLine(endCenter + right, endCenter - up);
                Gizmos.DrawLine(endCenter - up, endCenter - right);
                Gizmos.DrawLine(endCenter - right, endCenter + up);
            }
            else
            {
                Vector3 center = transform.position;
                Gizmos.DrawWireSphere(center, radius);

                // Draw vertical cylinder boundaries for rotor wash
                Gizmos.color = new Color(0.2f, 0.9f, 0.7f, 0.35f);
                Vector3 bottomCenter = center - Vector3.up * verticalRange;
                Gizmos.DrawLine(center, bottomCenter);
                Gizmos.DrawWireSphere(bottomCenter, radius);
            }
        }
    }
}
