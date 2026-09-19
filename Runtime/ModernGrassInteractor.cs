using System.Collections.Generic;
using UnityEngine;

namespace ModernGrassTool
{
    [ExecuteAlways]
    [AddComponentMenu("Modern Grass/Grass Interactor")]
    public class ModernGrassInteractor : MonoBehaviour
    {
        public static readonly List<ModernGrassInteractor> ActiveInteractors = new List<ModernGrassInteractor>();

        [Header("Interaction Settings")]
        [Tooltip("Effective radius in world units.")]
        [Range(0.1f, 5f)]
        public float radius = 0.8f;

        [Tooltip("Bending strength/push force. Set to 0 to disable bending completely.")]
        [Range(0f, 2f)]
        public float strength = 0.5f;

        [HideInInspector]
        public Vector2 moveDirection = Vector2.zero;

        [HideInInspector]
        public float currentSpeed = 0f;

        private Vector3 _lastPos;
        private float _smoothedSpeed = 0f;

        public struct GrassImpulse
        {
            public Vector3 position;
            public float time;
            public Vector2 direction;
            public float radius;
            public float strength;
        }

        public const int MaxImpulses = 32;
        public readonly GrassImpulse[] impulses = new GrassImpulse[MaxImpulses];
        public int impulseCount = 0;
        private int _impulseHead = 0;
        public int ImpulseHead => _impulseHead;
        private Vector3 _lastImpulsePos;
        private float _lastImpulseTime = 0f;
        private bool _wasMoving = false;

        private void OnEnable()
        {
            if (!ActiveInteractors.Contains(this))
            {
                ActiveInteractors.Add(this);
            }
            _lastPos = transform.position;
            _lastImpulsePos = transform.position;
            _smoothedSpeed = 0f;
            currentSpeed = 0f;
            moveDirection = Vector2.zero;
            impulseCount = 0;
            _impulseHead = 0;
            _lastImpulseTime = 0f;
            _wasMoving = false;
        }

        private void OnDisable()
        {
            ActiveInteractors.Remove(this);
            _smoothedSpeed = 0f;
            currentSpeed = 0f;
            moveDirection = Vector2.zero;
            impulseCount = 0;
            _impulseHead = 0;
            _lastImpulseTime = 0f;
            _wasMoving = false;
        }

        private void OnDestroy()
        {
            ActiveInteractors.Remove(this);
        }

        private void RecordImpulse(Vector3 pos, float time, Vector2 dir, float rad, float str)
        {
            impulses[_impulseHead] = new GrassImpulse
            {
                position = pos,
                time = time,
                direction = dir,
                radius = rad,
                strength = str
            };
            _impulseHead = (_impulseHead + 1) % MaxImpulses;
            impulseCount = Mathf.Min(impulseCount + 1, MaxImpulses);
        }

        private void Update()
        {
            Vector3 curPos = transform.position;
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            if (dt > 0.0001f)
            {
                Vector3 delta = curPos - _lastPos;
                Vector2 xzDelta = new Vector2(delta.x, delta.z);
                float instantSpeed = xzDelta.magnitude / dt;

                bool isMoving = instantSpeed > 0.05f;
                if (isMoving)
                {
                    moveDirection = xzDelta.normalized;
                    _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, Mathf.Clamp(instantSpeed, 0f, 6f), dt * 10f);

                    float currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
                    float distFromLast = Vector3.Distance(curPos, _lastImpulsePos);

                    bool shouldLog = !_wasMoving ||
                                     distFromLast >= radius * 0.35f ||
                                     (currentTime - _lastImpulseTime >= 0.25f && distFromLast >= 0.05f);

                    if (shouldLog)
                    {
                        RecordImpulse(curPos, currentTime, moveDirection, radius, strength);
                        _lastImpulsePos = curPos;
                        _lastImpulseTime = currentTime;
                    }
                    _wasMoving = true;
                }
                else
                {
                    if (_wasMoving)
                    {
                        float currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
                        RecordImpulse(curPos, currentTime, moveDirection, radius, strength);
                        _lastImpulsePos = curPos;
                        _lastImpulseTime = currentTime;
                        _wasMoving = false;
                    }

                    _smoothedSpeed = Mathf.MoveTowards(_smoothedSpeed, 0f, dt * 1.0f);
                    if (_smoothedSpeed <= 0.001f)
                    {
                        _smoothedSpeed = 0f;
                        moveDirection = Vector2.zero;
                    }
                }
                currentSpeed = _smoothedSpeed;
            }
            _lastPos = curPos;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.1f, 0.8f, 1.0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
