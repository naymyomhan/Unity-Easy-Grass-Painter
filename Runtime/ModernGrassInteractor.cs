using System.Collections.Generic;
using UnityEngine;

namespace ModernGrassTool
{
    public struct TrailNode
    {
        public Vector3 position;
        public Vector2 moveDirection;
        public float timeStamp;
        public float radius;
        public float strength;
    }

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

        public readonly List<TrailNode> trailHistory = new List<TrailNode>();
        private Vector3 _lastTrailPos;
        private bool _wasMoving = false;
        private const float MinTrailDist = 0.12f;
        private const int MaxTrailNodes = 24;
        private const float MaxTrailLife = 12.0f;

        private void OnEnable()
        {
            if (!ActiveInteractors.Contains(this))
            {
                ActiveInteractors.Add(this);
            }
            _lastTrailPos = transform.position;
            _wasMoving = false;
            trailHistory.Clear();
        }

        private void OnDisable()
        {
            ActiveInteractors.Remove(this);
            trailHistory.Clear();
        }

        private void OnDestroy()
        {
            ActiveInteractors.Remove(this);
            trailHistory.Clear();
        }

        private void Update()
        {
            UpdateTrailHistory();
        }

        public void UpdateTrailHistory()
        {
            float currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
            Vector3 curPos = transform.position;

            float dist = Vector3.Distance(curPos, _lastTrailPos);
            if (dist >= MinTrailDist)
            {
                Vector2 moveDir = new Vector2(curPos.x - _lastTrailPos.x, curPos.z - _lastTrailPos.z).normalized;

                // Record previous resting position when moving away
                if (!_wasMoving && trailHistory.Count < MaxTrailNodes)
                {
                    trailHistory.Insert(0, new TrailNode
                    {
                        position = _lastTrailPos,
                        moveDirection = moveDir,
                        timeStamp = currentTime,
                        radius = radius,
                        strength = strength
                    });
                }
                _wasMoving = true;

                trailHistory.Insert(0, new TrailNode
                {
                    position = curPos,
                    moveDirection = moveDir,
                    timeStamp = currentTime,
                    radius = radius,
                    strength = strength
                });

                while (trailHistory.Count > MaxTrailNodes)
                {
                    trailHistory.RemoveAt(trailHistory.Count - 1);
                }

                _lastTrailPos = curPos;
            }
            else if (dist < 0.01f)
            {
                _wasMoving = false;
            }

            // Prune expired nodes
            for (int i = trailHistory.Count - 1; i >= 0; i--)
            {
                if (currentTime - trailHistory[i].timeStamp > MaxTrailLife)
                {
                    trailHistory.RemoveAt(i);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.1f, 0.8f, 1.0f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
