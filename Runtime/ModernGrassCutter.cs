using UnityEngine;

namespace ModernGrassTool
{
    [AddComponentMenu("Modern Grass/Modern Grass Cutter")]
    public class ModernGrassCutter : MonoBehaviour
    {
        [Tooltip("Radius around cutter to cut grass.")]
        public float cutRadius = 1.2f;

        [Tooltip("If true, continuously cuts grass as this object moves.")]
        public bool cutOnMove = true;

        [Tooltip("Minimum distance to move before triggering another cut check.")]
        public float minMoveDistance = 0.2f;

        private Vector3 _lastCutPos;

        private void Start()
        {
            _lastCutPos = transform.position;
        }

        private void Update()
        {
            if (cutOnMove && Vector3.Distance(transform.position, _lastCutPos) >= minMoveDistance)
            {
                CutNow();
                _lastCutPos = transform.position;
            }
        }

        [ContextMenu("Cut Grass Now")]
        public void CutNow()
        {
            var manager = FindAnyObjectByType<ModernGrassManager>();
            if (manager != null)
            {
                manager.CutGrass(transform.position, cutRadius);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, cutRadius);
        }
    }
}
