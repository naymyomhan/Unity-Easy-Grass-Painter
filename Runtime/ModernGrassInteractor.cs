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

        private Vector3 _lastPos;

        private void OnEnable()
        {
            if (!ActiveInteractors.Contains(this))
            {
                ActiveInteractors.Add(this);
            }
            _lastPos = transform.position;
        }

        private void OnDisable()
        {
            ActiveInteractors.Remove(this);
        }

        private void OnDestroy()
        {
            ActiveInteractors.Remove(this);
        }

        private void Update()
        {
            Vector3 curPos = transform.position;
            Vector3 delta = curPos - _lastPos;
            if (delta.sqrMagnitude > 0.0001f)
            {
                moveDirection = new Vector2(delta.x, delta.z).normalized;
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
