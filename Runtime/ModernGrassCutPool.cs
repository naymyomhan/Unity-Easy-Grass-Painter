using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace ModernGrassTool
{
    public class ModernGrassCutPool : MonoBehaviour
    {
        public static ModernGrassCutPool Instance { get; private set; }

        [Tooltip("Default custom Particle System prefab. If null, a procedural grass cut burst particle system is created.")]
        public ParticleSystem particlePrefab;
        public int defaultCapacity = 20;
        public int maxPoolSize = 100;

        private readonly Dictionary<ParticleSystem, IObjectPool<ParticleSystem>> _prefabPools = new Dictionary<ParticleSystem, IObjectPool<ParticleSystem>>();
        private IObjectPool<ParticleSystem> _proceduralPool;

        public static ModernGrassCutPool GetOrCreate(ParticleSystem defaultPrefab = null)
        {
            if (Instance == null)
            {
                var existing = Object.FindAnyObjectByType<ModernGrassCutPool>();
                if (existing != null)
                {
                    Instance = existing;
                }
                else
                {
                    var go = new GameObject("ModernGrassCutPool");
                    Instance = go.AddComponent<ModernGrassCutPool>();
                }
            }

            if (defaultPrefab != null && Instance.particlePrefab == null)
            {
                Instance.particlePrefab = defaultPrefab;
            }

            return Instance;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) Destroy(gameObject);
        }

        private IObjectPool<ParticleSystem> GetPoolForPrefab(ParticleSystem prefab)
        {
            if (prefab == null)
            {
                if (_proceduralPool == null)
                {
                    _proceduralPool = new ObjectPool<ParticleSystem>(
                        () => CreateProceduralItem(_proceduralPool),
                        OnTakeFromPool,
                        OnReturnedToPool,
                        OnDestroyPoolObject,
                        true,
                        defaultCapacity,
                        maxPoolSize
                    );
                }
                return _proceduralPool;
            }

            if (!_prefabPools.TryGetValue(prefab, out var pool) || pool == null)
            {
                IObjectPool<ParticleSystem> newPool = null;
                newPool = new ObjectPool<ParticleSystem>(
                    () => CreatePrefabItem(prefab, newPool),
                    OnTakeFromPool,
                    OnReturnedToPool,
                    OnDestroyPoolObject,
                    true,
                    defaultCapacity,
                    maxPoolSize
                );
                _prefabPools[prefab] = newPool;
                return newPool;
            }

            return pool;
        }

        private ParticleSystem CreatePrefabItem(ParticleSystem prefab, IObjectPool<ParticleSystem> pool)
        {
            ParticleSystem ps = Instantiate(prefab, transform);
            var returner = ps.gameObject.GetComponent<ModernReturnToCutPool>();
            if (returner == null) returner = ps.gameObject.AddComponent<ModernReturnToCutPool>();
            returner.system = ps;
            returner.pool = pool;
            return ps;
        }

        private ParticleSystem CreateProceduralItem(IObjectPool<ParticleSystem> pool)
        {
            // Procedural burst particle system for grass shreds
            GameObject go = new GameObject("ProceduralGrassCutParticle");
            go.transform.SetParent(transform);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = 0.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Callback;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 10, 16) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            Material fallbackMat = null;
#if UNITY_EDITOR
            string[] matGuids = UnityEditor.AssetDatabase.FindAssets("GrassCutParticle_Material t:Material");
            if (matGuids != null && matGuids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(matGuids[0]);
                fallbackMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            if (fallbackMat == null)
            {
                fallbackMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ModernGrassTool/Prefabs/GrassCutParticle_Material.mat");
            }
#endif
            if (fallbackMat != null)
            {
                renderer.sharedMaterial = fallbackMat;
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (shader != null) renderer.material = new Material(shader);
            }

            var returner = go.AddComponent<ModernReturnToCutPool>();
            returner.system = ps;
            returner.pool = pool;

            return ps;
        }

        private void OnTakeFromPool(ParticleSystem ps)
        {
            if (ps != null) ps.gameObject.SetActive(true);
        }

        private void OnReturnedToPool(ParticleSystem ps)
        {
            if (ps != null) ps.gameObject.SetActive(false);
        }

        private void OnDestroyPoolObject(ParticleSystem ps)
        {
            if (ps != null) Destroy(ps.gameObject);
        }

        public void SpawnCutParticle(Vector3 position, Color color, ParticleSystem customPrefab = null)
        {
            ParticleSystem targetPrefab = customPrefab != null ? customPrefab : particlePrefab;
            IObjectPool<ParticleSystem> pool = GetPoolForPrefab(targetPrefab);
            var ps = pool.Get();
            ps.transform.position = position;

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);

            ps.Clear();
            ps.Play();
        }

        // Backwards compatibility overload
        public void SpawnCutParticle(Vector3 position, Color color)
        {
            SpawnCutParticle(position, color, null);
        }
    }
}
