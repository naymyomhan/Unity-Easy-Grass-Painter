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

        private void OnEnable()
        {
            if (!ActiveInteractors.Contains(this))
            {
                ActiveInteractors.Add(this);
            }
            _lastPos = transform.position;
            _smoothedSpeed = 0f;
            currentSpeed = 0f;
            moveDirection = Vector2.zero;
        }

        private void OnDisable()
        {
            ActiveInteractors.Remove(this);
            _smoothedSpeed = 0f;
            currentSpeed = 0f;
            moveDirection = Vector2.zero;
        }

        private void OnDestroy()
        {
            ActiveInteractors.Remove(this);
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

                if (instantSpeed > 0.05f)
                {
                    moveDirection = xzDelta.normalized;
                    _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, Mathf.Clamp(instantSpeed, 0f, 6f), dt * 10f);
                }
                else
                {
                    _smoothedSpeed = Mathf.MoveTowards(_smoothedSpeed, 0f, dt * 4f);
                    if (_smoothedSpeed <= 0.01f)
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
