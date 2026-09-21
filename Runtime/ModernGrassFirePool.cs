using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace ModernGrassTool
{
    /// <summary>
    /// Object pool and lifecycle manager for discrete clustered fire VFX nodes.
    /// Distributes compact, realistic flame instances along the active organic flame wavefront perimeter.
    /// </summary>
    public class ModernGrassFirePool : MonoBehaviour
    {
        public static ModernGrassFirePool Instance { get; private set; }

        private static readonly ObjectPool<HashSet<long>> _cellSetPool = new ObjectPool<HashSet<long>>(
            createFunc: () => new HashSet<long>(1024),
            actionOnGet: set => set.Clear(),
            actionOnRelease: set => set.Clear(),
            actionOnDestroy: null,
            collectionCheck: false,
            defaultCapacity: 4,
            maxSize: 32
        );

        public static HashSet<long> GetPooledCellSet() => _cellSetPool.Get();
        public static void ReleasePooledCellSet(HashSet<long> set)
        {
            if (set != null) _cellSetPool.Release(set);
        }

        public class ClusteredFireNode
        {
            public ParticleSystem ps;
            public uint burnId;
            public float angle;
            public Vector3 currentPos;
            public Vector3 targetPos;
            public long cellKey;
            public float spawnTime;
            public float extinguishTime;
            public bool isExtinguishing;
        }

        private readonly List<ClusteredFireNode> _activeNodes = new List<ClusteredFireNode>();
        private readonly Queue<ParticleSystem> _nodePool = new Queue<ParticleSystem>();
        private readonly List<ParticleSystem> _allInstantiatedNodes = new List<ParticleSystem>();

        public int activeNodeCount => _activeNodes.Count;

        public static ModernGrassFirePool GetOrCreate()
        {
            if (Instance == null)
            {
                var existing = Object.FindAnyObjectByType<ModernGrassFirePool>();
                if (existing != null)
                {
                    Instance = existing;
                }
                else
                {
                    var go = new GameObject("ModernGrassFirePool");
                    Instance = go.AddComponent<ModernGrassFirePool>();
                }
            }
            return Instance;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(gameObject);
        }

        public void StopAllFire()
        {
            ClearAllNodes();
        }

        public void ClearAllNodes()
        {
            for (int i = 0; i < _activeNodes.Count; i++)
            {
                var node = _activeNodes[i];
                if (node.ps != null)
                {
                    node.ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    node.ps.gameObject.SetActive(false);
                    _nodePool.Enqueue(node.ps);
                }
            }
            _activeNodes.Clear();
        }

        public bool IsFireActiveAt(Vector3 position, float radius = 0.5f)
        {
            for (int i = 0; i < _activeNodes.Count; i++)
            {
                var node = _activeNodes[i];
                if (node.ps == null || node.isExtinguishing) continue;
                if (Vector3.Distance(node.currentPos, position) <= radius + 1.2f)
                {
                    return true;
                }
            }
            return false;
        }

        // Backwards compatibility overloads
        public void SpawnFire(Vector3 position, float initialRadius, float maxRadius, float spreadSpeed, float burnDuration, ParticleSystem prefab, float densityMultiplier = 1.0f)
        {
        }

        public void SpawnFire(Vector3 position, float initialRadius, float maxRadius, float spreadSpeed, float burnDuration, ParticleSystem prefab, float densityMultiplier, HashSet<long> reachableCells, float seed = 0f, Vector2 windDir = default)
        {
            if (reachableCells != null) ReleasePooledCellSet(reachableCells);
        }

        public void SpawnFire(Vector3 position, float radius, float duration, ParticleSystem prefab)
        {
        }

        private void Update()
        {
            var mgr = ModernGrassManager.Instance;
            if (mgr == null || !mgr.enableFireSimulation || mgr.activeBurnCount == 0)
            {
                if (_activeNodes.Count > 0)
                {
                    for (int i = _activeNodes.Count - 1; i >= 0; i--)
                    {
                        var node = _activeNodes[i];
                        if (node.ps != null)
                        {
                            if (!node.isExtinguishing)
                            {
                                node.isExtinguishing = true;
                                node.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                            }
                            if (!node.ps.IsAlive(true))
                            {
                                node.ps.gameObject.SetActive(false);
                                _nodePool.Enqueue(node.ps);
                                _activeNodes.RemoveAt(i);
                            }
                        }
                        else
                        {
                            _activeNodes.RemoveAt(i);
                        }
                    }
                }
                return;
            }

            int targetMaxNodes = Mathf.Clamp(mgr.maxFireNodes, 4, 32);
            float minDist = Mathf.Max(0.6f, mgr.fireNodeMinDistance);
            float now = Time.time;

            // 1. Process active nodes: track outward along their sector angle, or extinguish when combusted
            for (int i = _activeNodes.Count - 1; i >= 0; i--)
            {
                var node = _activeNodes[i];
                if (node.ps == null)
                {
                    _activeNodes.RemoveAt(i);
                    continue;
                }

                if (node.isExtinguishing)
                {
                    if (!node.ps.IsAlive(true))
                    {
                        node.ps.gameObject.SetActive(false);
                        _nodePool.Enqueue(node.ps);
                        _activeNodes.RemoveAt(i);
                    }
                    continue;
                }

                // If user reduced slider, smoothly retire excess nodes
                if (_activeNodes.Count > targetMaxNodes && i >= targetMaxNodes)
                {
                    node.isExtinguishing = true;
                    node.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    continue;
                }

                // Check if the parent burn still exists or is scorch-only
                if (!mgr.TryGetActiveBurn(node.burnId, out ModernGrassManager.ActiveBurnPoint burn) || burn.isScorchOnly || burn.firePrefab == null)
                {
                    node.isExtinguishing = true;
                    node.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    continue;
                }

                float elapsed = now - burn.time;
                float spreadDist = Mathf.Max(0f, burn.maxRadius - burn.initialRadius);
                float spreadDur = (burn.spreadSpeed > 0.05f) ? (spreadDist / burn.spreadSpeed) : 0f;
                float burnLife = spreadDur + burn.burnDuration;

                if (elapsed >= burnLife)
                {
                    node.isExtinguishing = true;
                    node.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    continue;
                }

                // Evaluate organic flame front and ash core at this node's sector angle
                float a1 = Mathf.Sin(node.angle * 2.0f + burn.seed * 1.0f) * 0.22f;
                float a2 = Mathf.Cos(node.angle * 3.0f - burn.seed * 1.7f) * 0.16f;
                float a3 = Mathf.Sin(node.angle * 5.0f + burn.seed * 2.3f) * 0.10f;
                float a4 = Mathf.Cos(node.angle * 7.0f - burn.seed * 0.9f) * 0.06f;
                float shapeFactor = 1.0f + a1 + a2 + a3 + a4;

                Vector2 dir = new Vector2(Mathf.Cos(node.angle), Mathf.Sin(node.angle));
                float windAlignment = Vector2.Dot(dir, burn.windDir);
                float windStretch = 1.0f + windAlignment * 0.35f;
                float organicMultiplier = Mathf.Max(0.2f, shapeFactor) * windStretch;

                float currentRadius = (burn.spreadSpeed > 0.05f) 
                    ? Mathf.Min(burn.maxRadius, burn.initialRadius + burn.spreadSpeed * elapsed) 
                    : burn.maxRadius;

                float burnedOutRadius = (burn.spreadSpeed > 0.05f) 
                    ? Mathf.Max(0f, (elapsed - burn.burnDuration) * burn.spreadSpeed) 
                    : 0f;

                float flameDist = Mathf.Min(burn.maxRadius, currentRadius * organicMultiplier);
                float ashDist = Mathf.Max(0f, burnedOutRadius * organicMultiplier);

                // If flame has passed and grass has turned to dead ash
                if (flameDist <= ashDist + 0.15f)
                {
                    node.isExtinguishing = true;
                    node.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    continue;
                }

                // Place node directly on the leading frontier of the advancing flame (92% outward towards flame front)
                float nodeDist = Mathf.Lerp(ashDist, flameDist, 0.92f);

                float candX = burn.position.x + dir.x * nodeDist;
                float candZ = burn.position.z + dir.y * nodeDist;

                int cx = Mathf.FloorToInt(candX / ModernGrassManager.SPATIAL_CELL_SIZE);
                int cz = Mathf.FloorToInt(candZ / ModernGrassManager.SPATIAL_CELL_SIZE);
                long key = ((long)cx << 32) | (uint)cz;

                if (!mgr.HasFlammableGrassCell(key) || mgr.IsCellExtinguishedAsh(key))
                {
                    node.isExtinguishing = true;
                    node.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    continue;
                }

                node.cellKey = key;

                // Snap height to ground surface
                float candY = burn.position.y;
                if (Physics.Raycast(new Vector3(candX, burn.position.y + 10f, candZ), Vector3.down, out RaycastHit hit, 25f))
                {
                    candY = hit.point.y;
                }
                node.targetPos = new Vector3(candX, candY, candZ);

                // Zero-lag snapping directly onto the advancing wavefront
                node.currentPos = node.targetPos;
                node.ps.transform.position = node.currentPos;
            }

            // 2. Distribute nodes along active flame wavefronts
            int totalActiveBurns = mgr.activeBurnCount;
            if (totalActiveBurns == 0) return;

            int nodesPerBurn = Mathf.Max(3, targetMaxNodes / totalActiveBurns);

            for (int b = 0; b < totalActiveBurns; b++)
            {
                var burn = mgr.GetActiveBurn(b);
                float elapsed = now - burn.time;
                float spreadDist = Mathf.Max(0f, burn.maxRadius - burn.initialRadius);
                float spreadDur = (burn.spreadSpeed > 0.05f) ? (spreadDist / burn.spreadSpeed) : 0f;
                float burnLife = spreadDur + burn.burnDuration;

                if (burn.isScorchOnly || burn.firePrefab == null) continue;
                if (elapsed >= burnLife) continue;

                float currentRadius = (burn.spreadSpeed > 0.05f) 
                    ? Mathf.Min(burn.maxRadius, burn.initialRadius + burn.spreadSpeed * elapsed) 
                    : burn.maxRadius;

                float burnedOutRadius = (burn.spreadSpeed > 0.05f) 
                    ? Mathf.Max(0f, (elapsed - burn.burnDuration) * burn.spreadSpeed) 
                    : 0f;

                if (burnedOutRadius >= burn.maxRadius) continue;

                float perimeter = 2.0f * Mathf.PI * Mathf.Max(0.5f, currentRadius);
                int desiredCount = Mathf.Clamp(Mathf.RoundToInt(perimeter / minDist), 3, nodesPerBurn);

                // Count active non-extinguishing nodes for this burn
                int activeForBurn = 0;
                for (int n = 0; n < _activeNodes.Count; n++)
                {
                    if (_activeNodes[n].burnId == burn.id && !_activeNodes[n].isExtinguishing)
                    {
                        activeForBurn++;
                    }
                }

                if (activeForBurn >= desiredCount || _activeNodes.Count >= targetMaxNodes) continue;

                ParticleSystem prefabToUse = burn.firePrefab != null ? burn.firePrefab : mgr.defaultFireParticlePrefab;
                if (prefabToUse == null) continue;

                // Sample evenly spaced candidate angles around perimeter
                for (int c = 0; c < desiredCount; c++)
                {
                    if (_activeNodes.Count >= targetMaxNodes) break;

                    float candAngle = (float)c / desiredCount * (2.0f * Mathf.PI);

                    // Check if an existing active node is already near this angle
                    bool angleOccupied = false;
                    for (int n = 0; n < _activeNodes.Count; n++)
                    {
                        var existing = _activeNodes[n];
                        if (existing.burnId == burn.id && !existing.isExtinguishing)
                        {
                            float diff = Mathf.Abs(Mathf.DeltaAngle(candAngle * Mathf.Rad2Deg, existing.angle * Mathf.Rad2Deg));
                            if (diff < (360f / desiredCount) * 0.65f)
                            {
                                angleOccupied = true;
                                break;
                            }
                        }
                    }
                    if (angleOccupied) continue;

                    // Evaluate position at candidate angle
                    float a1 = Mathf.Sin(candAngle * 2.0f + burn.seed * 1.0f) * 0.22f;
                    float a2 = Mathf.Cos(candAngle * 3.0f - burn.seed * 1.7f) * 0.16f;
                    float a3 = Mathf.Sin(candAngle * 5.0f + burn.seed * 2.3f) * 0.10f;
                    float a4 = Mathf.Cos(candAngle * 7.0f - burn.seed * 0.9f) * 0.06f;
                    float shapeFactor = 1.0f + a1 + a2 + a3 + a4;

                    Vector2 dir = new Vector2(Mathf.Cos(candAngle), Mathf.Sin(candAngle));
                    float windAlignment = Vector2.Dot(dir, burn.windDir);
                    float windStretch = 1.0f + windAlignment * 0.35f;
                    float organicMultiplier = Mathf.Max(0.2f, shapeFactor) * windStretch;

                    float flameDist = Mathf.Min(burn.maxRadius, currentRadius * organicMultiplier);
                    float ashDist = Mathf.Max(0f, burnedOutRadius * organicMultiplier);

                    if (flameDist <= ashDist + 0.15f) continue;

                    float nodeDist = Mathf.Lerp(ashDist, flameDist, 0.92f);

                    float candX = burn.position.x + dir.x * nodeDist;
                    float candZ = burn.position.z + dir.y * nodeDist;

                    int cx = Mathf.FloorToInt(candX / ModernGrassManager.SPATIAL_CELL_SIZE);
                    int cz = Mathf.FloorToInt(candZ / ModernGrassManager.SPATIAL_CELL_SIZE);
                    long key = ((long)cx << 32) | (uint)cz;

                    if (!mgr.HasFlammableGrassCell(key) || mgr.IsCellExtinguishedAsh(key)) continue;

                    float candY = burn.position.y;
                    if (Physics.Raycast(new Vector3(candX, burn.position.y + 10f, candZ), Vector3.down, out RaycastHit hit, 25f))
                    {
                        candY = hit.point.y;
                    }
                    Vector3 candidatePos = new Vector3(candX, candY, candZ);

                    // Ensure spacing against all active nodes
                    bool tooClose = false;
                    for (int n = 0; n < _activeNodes.Count; n++)
                    {
                        if (Vector3.Distance(_activeNodes[n].currentPos, candidatePos) < minDist * 0.75f)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;

                    // Spawn node from pool
                    ParticleSystem ps = GetPooledNode(prefabToUse);
                    if (ps != null)
                    {
                        ps.transform.position = candidatePos;
                        ps.transform.rotation = Quaternion.identity;
                        ps.gameObject.SetActive(true);
                        ps.Clear(true);
                        ps.Play(true);

                        _activeNodes.Add(new ClusteredFireNode
                        {
                            ps = ps,
                            burnId = burn.id,
                            angle = candAngle,
                            currentPos = candidatePos,
                            targetPos = candidatePos,
                            cellKey = key,
                            spawnTime = now,
                            extinguishTime = now + burn.burnDuration + 1.0f,
                            isExtinguishing = false
                        });
                    }
                }
            }
        }

        private ParticleSystem GetPooledNode(ParticleSystem prefab)
        {
            while (_nodePool.Count > 0)
            {
                var ps = _nodePool.Dequeue();
                if (ps != null) return ps;
            }

            var instance = Instantiate(prefab, transform);
            instance.name = $"ClusteredFireNode_{_allInstantiatedNodes.Count}";
            foreach (var child in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var shape = child.shape;
                shape.radius = 0.9f;
                shape.radiusThickness = 1.0f;
            }
            _allInstantiatedNodes.Add(instance);
            return instance;
        }
    }
}
