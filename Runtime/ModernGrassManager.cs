using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ModernGrassTool
{
    public enum GrassRegrowthMode
    {
        None,          // Cut grass stays cut permanently
        Timer,         // Auto-regrows after a delay
        Distance,      // Auto-regrows when player/camera moves beyond a distance
        ManualOnly     // Only regrows when game code calls RegrowAll() or RegrowAt()
    }

    public enum GrassBurnFadeMode
    {
        None,          // Scorch stays permanently charred
        Timer,         // Auto-fades scorch over time after Burn Fade Seconds
        Distance,      // Auto-clears scorch when player/camera moves beyond Burn Fade Distance
        ManualOnly     // Only clears when game code calls ClearAllBurn() or ClearBurnAt()
    }

    public enum GrassBurnMapMode
    {
        FixedWorldBounds,       // Fixed bounds over local area (Arena, Dungeon, Test Scene)
        PlayerCenteredFloating  // Toroidal modulo ring buffer following player (10km+ Open World)
    }

    [ExecuteAlways]
    [AddComponentMenu("Modern Grass/Modern Grass Manager")]
    public class ModernGrassManager : MonoBehaviour
    {
        public static ModernGrassManager Instance { get; private set; }

        [Header("Shader References")]
        public ComputeShader computeShader;
        public Shader grassShader;

        [Header("Global Wind Settings")]
        public Vector3 windDirection = new Vector3(1f, 0f, 0.5f);

        [Header("Optimization & Culling")]
        [Tooltip("Enable spatial quadtree frustum culling to skip grass outside camera view.")]
        public bool enableCulling = true;
        [Tooltip("Visualize visible leaf bounds in scene view.")]
        public bool drawCullingGizmos = false;

        [Header("Grass Regrowth Settings")]
        [Tooltip("How cut grass regrows: Timer, Distance, ManualOnly (via code), or None.")]
        public GrassRegrowthMode regrowthMode = GrassRegrowthMode.Timer;
        [Tooltip("Seconds after cutting before grass regrows (used in Timer mode).")]
        public float regrowDelaySeconds = 8.0f;
        [Tooltip("Duration in seconds of the smooth upward growth animation when regrowing (used in Timer mode).")]
        public float regrowAnimationDuration = 2.5f;
        [Tooltip("Distance from player/camera before cut grass regrows (used in Distance mode).")]
        public float regrowDistance = 45.0f;
        [Tooltip("Transform to check distance against (leave null to use Camera.main or Player).")]
        public Transform playerReference;

        [Header("Open-World Spatial Chunk Streaming")]
        [Tooltip("Enable automatic grid chunk streaming. Keeps VRAM low and fixed by only streaming grass around the player/camera.")]
        public bool enableChunkStreaming = true;
        [Tooltip("Size in meters of each spatial chunk cell (e.g. 64m).")]
        public float chunkSize = 64f;
        [Tooltip("Radius in meters around the viewer to stream in active chunks.")]
        public float streamRadius = 120f;
        [Tooltip("Visualize chunk bounds in Scene View: Green for active/loaded chunks, Gray for sleeping chunks.")]
        public bool drawChunkGizmos = false;

        private Vector3 _lastStreamViewerPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private Vector2Int _lastStreamCell = new Vector2Int(int.MinValue, int.MinValue);
        private HashSet<Vector2Int> _activeChunkCoords = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> _streamCoordsBuffer = new HashSet<Vector2Int>();
        public static bool isPaintingActive = false;

        [Header("Grass Layers")]
        public List<GrassLayer> layers = new List<GrassLayer>();

        private struct CutRecord
        {
            public int layerIndex;
            public int pointIndex;
            public float cutTime;
            public Vector3 position;
        }

        private List<CutRecord> _activeCuts = new List<CutRecord>();

        private int _kernelIndex = -1;
        private int _kernelResetArgs = -1;
        private Vector4[] _interactorArray = new Vector4[16];
        private Vector4[] _interactorParams = new Vector4[16];
        private Vector4[] _impulsePoints = new Vector4[32];
        private Vector4[] _impulseParams = new Vector4[32];

        public struct GrassShockwave
        {
            public Vector3 origin;
            public float startTime;
            public float radius;
            public float speed;
            public float force;
            public float thickness;
            public float duration;
        }

        public const int MaxShockwaves = 8;
        private readonly GrassShockwave[] _shockwaves = new GrassShockwave[MaxShockwaves];
        private int _shockwaveCount = 0;
        private Vector4[] _shockwaveOrigins = new Vector4[MaxShockwaves];
        private Vector4[] _shockwaveParams = new Vector4[MaxShockwaves];

        public static void TriggerShockwave(Vector3 origin, float radius = 8f, float force = 1.5f, float speed = 22f, float thickness = 2.0f)
        {
            if (Instance != null)
            {
                Instance.AddShockwave(origin, radius, force, speed, thickness);
            }
            else
            {
                var mgr = FindAnyObjectByType<ModernGrassManager>();
                if (mgr != null)
                {
                    mgr.AddShockwave(origin, radius, force, speed, thickness);
                }
            }
        }

        public void AddShockwave(Vector3 origin, float radius, float force, float speed, float thickness)
        {
            if (radius <= 0.1f || force <= 0.01f || speed <= 0.1f) return;

            float currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
            float travelTime = radius / speed;
            float totalDuration = travelTime + 1.8f;

            int targetIdx = -1;
            float oldestTime = float.MaxValue;
            int oldestIdx = 0;

            for (int i = 0; i < MaxShockwaves; i++)
            {
                if (i >= _shockwaveCount)
                {
                    targetIdx = i;
                    break;
                }

                float age = currentTime - _shockwaves[i].startTime;
                if (age >= _shockwaves[i].duration)
                {
                    targetIdx = i;
                    break;
                }

                if (_shockwaves[i].startTime < oldestTime)
                {
                    oldestTime = _shockwaves[i].startTime;
                    oldestIdx = i;
                }
            }

            if (targetIdx == -1) targetIdx = oldestIdx;

            _shockwaves[targetIdx] = new GrassShockwave
            {
                origin = origin,
                startTime = currentTime,
                radius = radius,
                speed = speed,
                force = force,
                thickness = Mathf.Max(0.5f, thickness),
                duration = totalDuration
            };

            if (targetIdx >= _shockwaveCount)
            {
                _shockwaveCount = targetIdx + 1;
            }
        }

        public struct GrassWindBurst
        {
            public Vector3 origin;
            public Vector3 direction;
            public float radius;
            public float force;
            public float coneAngle;
            public float verticalRange;
            public float flutterSpeed;
            public float startTime;
            public float blowDuration;
            public float recoilDuration;
            public GrassWindMode mode;
            public bool isRecoilOnly;
        }

        public const int MaxWindZones = 8;
        private static readonly List<ModernGrassWindZone> _activeWindZones = new List<ModernGrassWindZone>();
        private readonly List<GrassWindBurst> _windBursts = new List<GrassWindBurst>();
        private Vector4[] _windZoneOrigins = new Vector4[MaxWindZones];
        private Vector4[] _windZoneVectors = new Vector4[MaxWindZones];
        private Vector4[] _windZoneParams = new Vector4[MaxWindZones];
        private Vector4[] _windZoneTimes = new Vector4[MaxWindZones];

        [Header("Fire, Burn & Scorch Simulation")]
        [Tooltip("Enable GPU-accelerated 2D burn map and fire spread simulation.")]
        public bool enableFireSimulation = true;
        [HideInInspector]
        public GrassBurnMapMode burnMapMode = GrassBurnMapMode.PlayerCenteredFloating;
        [HideInInspector]
        public int burnMapResolution = 512;
        [HideInInspector]
        public Vector3 burnMapCenter = Vector3.zero;
        [HideInInspector]
        public float burnMapWorldSize = 256f;
        [HideInInspector]
        public bool autoFitBurnMapBounds = false;
        [Tooltip("How burn and scorch marks fade or clear: None, Timer, Distance, or ManualOnly.")]
        public GrassBurnFadeMode burnFadeMode = GrassBurnFadeMode.Timer;
        [Tooltip("Duration in seconds for scorch marks to fade away (used in Timer mode).")]
        public float burnFadeSeconds = 12.0f;
        [Tooltip("Distance from player/camera before scorch marks clear (used in Distance mode).")]
        public float burnFadeDistance = 45.0f;
        [Range(20f, 100f)]
        [Tooltip("Re-ignition Scorch Threshold (20% to 100%). Scorch percentage below which regrowing grass restores fuel and can be re-ignited. Higher % lets grass re-ignite sooner while regrowing; lower % requires grass to be greener before catching fire.")]
        public float reignitionScorchThreshold = 40f;

        [Header("Clustered Fire VFX Nodes")]
        [Range(4, 32)]
        [Tooltip("Maximum concurrent clustered fire VFX nodes spawned along the active flame wavefront.")]
        public int maxFireNodes = 16;

        [Range(0.8f, 4.0f)]
        [Tooltip("Minimum distance between adjacent fire nodes to prevent overlapping and overdraw.")]
        public float fireNodeMinDistance = 1.8f;

        [Tooltip("Default fire particle prefab used when a grass layer does not assign a custom fireParticlePrefab.")]
        public ParticleSystem defaultFireParticlePrefab;

        // Backwards compatibility properties
        public bool enableBurnFade
        {
            get => burnFadeMode == GrassBurnFadeMode.Timer;
            set => burnFadeMode = value ? GrassBurnFadeMode.Timer : GrassBurnFadeMode.None;
        }
        public float burnFadeDuration
        {
            get => burnFadeSeconds;
            set => burnFadeSeconds = value;
        }

        [Tooltip("Draw burn simulation world bounds in Scene view.")]
        public bool drawBurnGizmo = false;
        [HideInInspector]
        public ComputeShader burnComputeShader;

        private RenderTexture _burnMapA;
        private RenderTexture _burnMapB;
        private bool _burnPingPong = false;
        private bool _hasActiveFire = false;
        private float _lastFireActiveTime = -999f;
        private int _kIgniteStamp = -1;
        private int _kScorchStamp = -1;
        private int _kSimulate = -1;
        private int _kBakeFuel = -1;
        private int _kBakeGrassPointsFuel = -1;
        private int _kClearMap = -1;
        private int _kClearStamp = -1;

        public struct ActiveBurnPoint
        {
            public uint id;
            public Vector3 position;
            public float initialRadius;
            public float maxRadius;
            public float spreadSpeed;
            public float time;
            public float recoverTime;
            public float burnDuration;
            public float seed;
            public Vector2 windDir;
            public ParticleSystem firePrefab;
            public bool isScorchOnly;

            public float radius => maxRadius;
        }
        private static uint _nextBurnId = 1;
        private readonly List<ActiveBurnPoint> _activeBurns = new List<ActiveBurnPoint>();
        public int activeBurnCount => _activeBurns.Count;
        public ActiveBurnPoint GetActiveBurn(int index) => (index >= 0 && index < _activeBurns.Count) ? _activeBurns[index] : default;
        public bool TryGetActiveBurn(uint burnId, out ActiveBurnPoint burn)
        {
            for (int i = 0; i < _activeBurns.Count; i++)
            {
                if (_activeBurns[i].id == burnId)
                {
                    burn = _activeBurns[i];
                    return true;
                }
            }
            burn = default;
            return false;
        }
        private readonly List<int> _tempIgniteQueryIndices = new List<int>();

        private struct ActiveIgnitionSource
        {
            public Vector3 worldPos;
            public Vector2 uvPos;
            public float uvMaxRadius;
            public float maxSpreadRadius;
            public float spreadSpeed;
            public float startTime;
            public float lifetime;
        }
        private readonly List<ActiveIgnitionSource> _activeIgnitions = new List<ActiveIgnitionSource>();
        private readonly Vector4[] _ignitionSourcesArray = new Vector4[16];

        public const float SPATIAL_CELL_SIZE = 0.5f;
        private readonly HashSet<long> _flammableGrassCells = new HashSet<long>();
        private readonly Dictionary<long, byte> _cellLayerMap = new Dictionary<long, byte>();
        private bool _flammableGrassCellsDirty = true;
        private readonly Queue<long> _tempBfsQueue = new Queue<long>(1024);

        public static long GetCellKey(int cx, int cz) => ((long)cx << 32) | (uint)cz;
        public static long GetCellKeyFromWorldPos(float x, float z, float cellSize = SPATIAL_CELL_SIZE)
        {
            int cx = Mathf.FloorToInt(x / cellSize);
            int cz = Mathf.FloorToInt(z / cellSize);
            return ((long)cx << 32) | (uint)cz;
        }

        public void InvalidateSpatialGrid()
        {
            _flammableGrassCellsDirty = true;
        }

        public void EnsureSpatialGrid()
        {
            if (!_flammableGrassCellsDirty && _flammableGrassCells.Count > 0) return;

            _flammableGrassCells.Clear();
            _cellLayerMap.Clear();
            if (layers == null || layers.Count == 0) return;

            for (int l = 0; l < layers.Count; l++)
            {
                var layer = layers[l];
                if (layer == null || !layer.canCatchFire) continue;

                byte layerIdx = (byte)Mathf.Clamp(l, 0, 255);
                if (layer.points != null && layer.points.Count > 0)
                {
                    for (int i = 0; i < layer.points.Count; i++)
                    {
                        var p = layer.points[i].position;
                        int cx = Mathf.FloorToInt(p.x / SPATIAL_CELL_SIZE);
                        int cz = Mathf.FloorToInt(p.z / SPATIAL_CELL_SIZE);
                        long key = ((long)cx << 32) | (uint)cz;
                        _flammableGrassCells.Add(key);
                        if (!_cellLayerMap.ContainsKey(key)) _cellLayerMap[key] = layerIdx;
                    }
                }
                else if (layer.chunks != null && layer.chunks.Count > 0)
                {
                    for (int c = 0; c < layer.chunks.Count; c++)
                    {
                        var chunk = layer.chunks[c];
                        if (chunk == null || chunk.points == null) continue;
                        for (int i = 0; i < chunk.points.Count; i++)
                        {
                            var p = chunk.points[i].position;
                            int cx = Mathf.FloorToInt(p.x / SPATIAL_CELL_SIZE);
                            int cz = Mathf.FloorToInt(p.z / SPATIAL_CELL_SIZE);
                            long key = ((long)cx << 32) | (uint)cz;
                            _flammableGrassCells.Add(key);
                            if (!_cellLayerMap.ContainsKey(key)) _cellLayerMap[key] = layerIdx;
                        }
                    }
                }
            }

            _flammableGrassCellsDirty = false;
        }

        public bool HasFlammableGrassCell(long cellKey)
        {
            EnsureSpatialGrid();
            return _flammableGrassCells.Contains(cellKey);
        }

        public bool HasFlammableGrassAt(Vector3 worldPos, float radius = 0.6f)
        {
            EnsureSpatialGrid();
            if (_flammableGrassCells.Count == 0) return false;

            int centerCx = Mathf.FloorToInt(worldPos.x / SPATIAL_CELL_SIZE);
            int centerCz = Mathf.FloorToInt(worldPos.z / SPATIAL_CELL_SIZE);
            long centerKey = ((long)centerCx << 32) | (uint)centerCz;
            if (_flammableGrassCells.Contains(centerKey)) return true;

            int cellRadius = Mathf.Clamp(Mathf.CeilToInt(radius / SPATIAL_CELL_SIZE), 1, 3);
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dz = -cellRadius; dz <= cellRadius; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    long key = ((long)(centerCx + dx) << 32) | (uint)(centerCz + dz);
                    if (_flammableGrassCells.Contains(key)) return true;
                }
            }
            return false;
        }

        private readonly Dictionary<long, float> _cellIgniteTimes = new Dictionary<long, float>();
        private readonly Dictionary<long, float> _cellExtinguishTimes = new Dictionary<long, float>();
        private readonly Dictionary<long, float> _cellRecoverTimes = new Dictionary<long, float>();

        public bool IsCellBurnedOrBurning(long cellKey)
        {
            float now = Time.time;
            if (_cellRecoverTimes.TryGetValue(cellKey, out float recoverTime))
            {
                if (now < recoverTime) return true;
                _cellRecoverTimes.Remove(cellKey);
                _cellExtinguishTimes.Remove(cellKey);
                _cellIgniteTimes.Remove(cellKey);
            }
            return false;
        }

        public bool IsCellActivelyFlaming(long cellKey)
        {
            float now = Time.time;
            if (_cellRecoverTimes.TryGetValue(cellKey, out float recoverTime))
            {
                if (now >= recoverTime)
                {
                    _cellRecoverTimes.Remove(cellKey);
                    _cellExtinguishTimes.Remove(cellKey);
                    _cellIgniteTimes.Remove(cellKey);
                    return false;
                }

                if (_cellExtinguishTimes.TryGetValue(cellKey, out float extTime))
                {
                    if (now >= extTime) return false;
                    if (_cellIgniteTimes.TryGetValue(cellKey, out float ignTime))
                    {
                        return now >= ignTime;
                    }
                    return true;
                }
            }
            return false;
        }

        public bool IsCellExtinguishedAsh(long cellKey)
        {
            float now = Time.time;
            if (_cellRecoverTimes.TryGetValue(cellKey, out float recoverTime))
            {
                if (now >= recoverTime)
                {
                    _cellRecoverTimes.Remove(cellKey);
                    _cellExtinguishTimes.Remove(cellKey);
                    _cellIgniteTimes.Remove(cellKey);
                    return false;
                }

                if (_cellExtinguishTimes.TryGetValue(cellKey, out float extinguishTime))
                {
                    return now >= extinguishTime;
                }
                return true;
            }
            return false;
        }

        public bool HasUnburntFlammableGrassAt(Vector3 worldPos, float radius, out GrassLayer foundLayer)
        {
            foundLayer = null;
            EnsureSpatialGrid();
            if (_flammableGrassCells.Count == 0) return false;

            int centerCx = Mathf.FloorToInt(worldPos.x / SPATIAL_CELL_SIZE);
            int centerCz = Mathf.FloorToInt(worldPos.z / SPATIAL_CELL_SIZE);
            int cellRadius = Mathf.Clamp(Mathf.CeilToInt(radius / SPATIAL_CELL_SIZE), 1, 3);
            float sqrRad = radius * radius;

            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dz = -cellRadius; dz <= cellRadius; dz++)
                {
                    int cx = centerCx + dx;
                    int cz = centerCz + dz;
                    float wx = (cx + 0.5f) * SPATIAL_CELL_SIZE;
                    float wz = (cz + 0.5f) * SPATIAL_CELL_SIZE;
                    float dSq = (wx - worldPos.x) * (wx - worldPos.x) + (wz - worldPos.z) * (wz - worldPos.z);
                    if (dSq > sqrRad) continue;

                    long key = ((long)cx << 32) | (uint)cz;
                    if (_flammableGrassCells.Contains(key) && !IsCellBurnedOrBurning(key))
                    {
                        if (foundLayer == null && _cellLayerMap.TryGetValue(key, out byte lIdx) && layers != null && lIdx < layers.Count)
                        {
                            foundLayer = layers[lIdx];
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        public bool HasUnburntFlammableGrassAt(Vector3 worldPos, float radius)
        {
            return HasUnburntFlammableGrassAt(worldPos, radius, out _);
        }

        /// <summary>
        /// Computes contiguous flammable grass reachable from the ignition origin within maxRadius, stopping at gaps > maxGap.
        /// Returns the maximum reachable distance and populates reachableCells.
        /// </summary>
        public bool CalculateReachableGrass(
            Vector3 origin,
            float maxRadius,
            float maxGap,
            out float maxReachableRadius,
            out HashSet<long> reachableCells
        )
        {
            maxReachableRadius = 0f;
            reachableCells = null;

            EnsureSpatialGrid();
            if (_flammableGrassCells.Count == 0) return false;

            int startCx = Mathf.FloorToInt(origin.x / SPATIAL_CELL_SIZE);
            int startCz = Mathf.FloorToInt(origin.z / SPATIAL_CELL_SIZE);
            long startKey = ((long)startCx << 32) | (uint)startCz;

            // If starting cell is already burned or burning, cannot reignite
            if (IsCellBurnedOrBurning(startKey)) return false;

            // If starting cell doesn't have grass directly, find closest grass cell within 1.2m
            if (!_flammableGrassCells.Contains(startKey))
            {
                bool found = false;
                float bestDistSq = float.MaxValue;
                int bestCx = startCx, bestCz = startCz;

                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        long k = ((long)(startCx + dx) << 32) | (uint)(startCz + dz);
                        if (_flammableGrassCells.Contains(k) && !IsCellBurnedOrBurning(k))
                        {
                            float wx = (startCx + dx + 0.5f) * SPATIAL_CELL_SIZE;
                            float wz = (startCz + dz + 0.5f) * SPATIAL_CELL_SIZE;
                            float dSq = (wx - origin.x) * (wx - origin.x) + (wz - origin.z) * (wz - origin.z);
                            if (dSq < bestDistSq)
                            {
                                bestDistSq = dSq;
                                bestCx = startCx + dx;
                                bestCz = startCz + dz;
                                found = true;
                            }
                        }
                    }
                }

                if (!found) return false;
                startCx = bestCx;
                startCz = bestCz;
                startKey = ((long)startCx << 32) | (uint)startCz;
            }

            reachableCells = ModernGrassFirePool.GetPooledCellSet();
            reachableCells.Add(startKey);

            _tempBfsQueue.Clear();
            _tempBfsQueue.Enqueue(startKey);

            float sqrMaxRad = maxRadius * maxRadius;
            float maxDistSq = 0f;

            // Step size for gap jumping
            int stepRange = (maxGap > 0.75f) ? 2 : 1;

            Vector2 wind2D = (Instance != null) 
                ? new Vector2(Instance.windDirection.x, Instance.windDirection.z).normalized 
                : Vector2.right;

            Vector2 originUv = Vector2.zero;
            if (Instance != null && Instance.WorldToBurnUV(origin, 0f, out Vector2 oUv, out _))
            {
                originUv = oUv;
            }
            float seed = originUv.x * 123.456f + originUv.y * 789.012f;

            while (_tempBfsQueue.Count > 0)
            {
                long cur = _tempBfsQueue.Dequeue();
                int curX = (int)(cur >> 32);
                int curZ = (int)(cur & 0xFFFFFFFF);

                float cellWorldX = (curX + 0.5f) * SPATIAL_CELL_SIZE;
                float cellWorldZ = (curZ + 0.5f) * SPATIAL_CELL_SIZE;
                float dSq = (cellWorldX - origin.x) * (cellWorldX - origin.x) + (cellWorldZ - origin.z) * (cellWorldZ - origin.z);
                if (dSq > maxDistSq) maxDistSq = dSq;

                for (int dx = -stepRange; dx <= stepRange; dx++)
                {
                    for (int dz = -stepRange; dz <= stepRange; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        if (stepRange > 1 && (dx * dx + dz * dz > 4)) continue;

                        int nx = curX + dx;
                        int nz = curZ + dz;
                        long nKey = ((long)nx << 32) | (uint)nz;

                        if (reachableCells.Contains(nKey)) continue;
                        if (!_flammableGrassCells.Contains(nKey)) continue;
                        if (IsCellBurnedOrBurning(nKey)) continue; // Stop at burned ash / already burning zone

                        float nWorldX = (nx + 0.5f) * SPATIAL_CELL_SIZE;
                        float nWorldZ = (nz + 0.5f) * SPATIAL_CELL_SIZE;
                        float dX = nWorldX - origin.x;
                        float dZ = nWorldZ - origin.z;
                        float nd = Mathf.Sqrt(dX * dX + dZ * dZ);

                        // Match organic multi-harmonic wildfire shape exactly with GPU shader
                        float angle = Mathf.Atan2(dZ, dX);
                        float a1 = Mathf.Sin(angle * 2.0f + seed * 1.0f) * 0.22f;
                        float a2 = Mathf.Cos(angle * 3.0f - seed * 1.7f) * 0.16f;
                        float a3 = Mathf.Sin(angle * 5.0f + seed * 2.3f) * 0.10f;
                        float a4 = Mathf.Cos(angle * 7.0f - seed * 0.9f) * 0.06f;
                        float shapeFactor = 1.0f + a1 + a2 + a3 + a4;

                        Vector2 dir = (nd > 0.0001f) ? new Vector2(dX / nd, dZ / nd) : Vector2.zero;
                        float windAlignment = Vector2.Dot(dir, wind2D);
                        float windStretch = 1.0f + windAlignment * 0.35f;

                        float organicMaxRad = maxRadius * Mathf.Max(0.2f, shapeFactor) * windStretch;
                        if (nd > organicMaxRad) continue;

                        reachableCells.Add(nKey);
                        _tempBfsQueue.Enqueue(nKey);
                    }
                }
            }

            maxReachableRadius = Mathf.Sqrt(maxDistSq);
            return true;
        }

        private static Texture2D _defaultBurnMap;

        public static void RegisterWindZone(ModernGrassWindZone zone)
        {
            if (zone != null && !_activeWindZones.Contains(zone))
            {
                _activeWindZones.Add(zone);
            }
        }

        public static void UnregisterWindZone(ModernGrassWindZone zone)
        {
            if (zone != null)
            {
                _activeWindZones.Remove(zone);
            }
        }

        public static void RecordWindRecoil(Vector3 origin, Vector3 direction, float radius, float force, float coneAngle, float verticalRange, GrassWindMode mode)
        {
            if (Instance != null)
            {
                Instance.AddWindRecoil(origin, direction, radius, force, coneAngle, verticalRange, mode);
            }
            else
            {
                var mgr = FindAnyObjectByType<ModernGrassManager>();
                mgr?.AddWindRecoil(origin, direction, radius, force, coneAngle, verticalRange, mode);
            }
        }

        public void AddWindRecoil(Vector3 origin, Vector3 direction, float radius, float force, float coneAngle, float verticalRange, GrassWindMode mode)
        {
            float currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
            _windBursts.Add(new GrassWindBurst
            {
                origin = origin,
                direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward,
                radius = radius,
                force = force,
                coneAngle = coneAngle,
                verticalRange = verticalRange,
                flutterSpeed = 18f,
                startTime = currentTime,
                blowDuration = 0f,
                recoilDuration = 1.6f,
                mode = mode,
                isRecoilOnly = true
            });
        }

        public static void TriggerWindBurst(Vector3 origin, Vector3 direction, float range = 15f, float force = 2.5f, float duration = 2.0f, float coneAngle = 60f, float flutterSpeed = 18f)
        {
            if (Instance != null)
            {
                Instance.AddWindBurst(origin, direction, range, force, duration, coneAngle, 15f, flutterSpeed, GrassWindMode.Directional);
            }
            else
            {
                var mgr = FindAnyObjectByType<ModernGrassManager>();
                mgr?.AddWindBurst(origin, direction, range, force, duration, coneAngle, 15f, flutterSpeed, GrassWindMode.Directional);
            }
        }

        public static void TriggerOmniWindBurst(Vector3 origin, float radius = 12f, float force = 2.5f, float duration = 2.0f, float verticalRange = 15f, float flutterSpeed = 20f)
        {
            if (Instance != null)
            {
                Instance.AddWindBurst(origin, Vector3.down, radius, force, duration, 360f, verticalRange, flutterSpeed, GrassWindMode.Omnidirectional);
            }
            else
            {
                var mgr = FindAnyObjectByType<ModernGrassManager>();
                mgr?.AddWindBurst(origin, Vector3.down, radius, force, duration, 360f, verticalRange, flutterSpeed, GrassWindMode.Omnidirectional);
            }
        }

        public void AddWindBurst(Vector3 origin, Vector3 direction, float radius, float force, float duration, float coneAngle, float verticalRange, float flutterSpeed, GrassWindMode mode)
        {
            float currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
            _windBursts.Add(new GrassWindBurst
            {
                origin = origin,
                direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward,
                radius = radius,
                force = force,
                coneAngle = coneAngle,
                verticalRange = verticalRange,
                flutterSpeed = flutterSpeed,
                startTime = currentTime,
                blowDuration = duration,
                recoilDuration = 1.6f,
                mode = mode,
                isRecoilOnly = false
            });
        }

        private int CollectActiveWindZones(float currentTime)
        {
            int count = 0;

            // 1. Collect from active ModernGrassWindZone components
            for (int i = 0; i < _activeWindZones.Count; i++)
            {
                var wz = _activeWindZones[i];
                if (wz == null || !wz.isActiveAndEnabled) continue;

                if (wz.IsActive(currentTime, out float fadeWeight) && fadeWeight > 0.001f)
                {
                    float effForce = wz.force * fadeWeight;
                    float cosAngle = Mathf.Cos(wz.coneAngle * 0.5f * Mathf.Deg2Rad);
                    Vector3 fwd = wz.transform.forward;

                    _windZoneOrigins[count] = new Vector4(wz.transform.position.x, wz.transform.position.y, wz.transform.position.z, wz.mode == GrassWindMode.Directional ? 0f : 1f);
                    _windZoneVectors[count] = new Vector4(fwd.x, fwd.y, fwd.z, cosAngle);
                    _windZoneParams[count] = new Vector4(wz.radius, effForce, wz.flutterSpeed, wz.verticalRange);
                    _windZoneTimes[count] = new Vector4(-1f, 1.6f, 0f, 0f); // -1 = actively blowing
                    count++;
                    if (count >= MaxWindZones) break;
                }
            }

            // 2. Collect from dynamic bursts and release recoils
            for (int i = _windBursts.Count - 1; i >= 0; i--)
            {
                var burst = _windBursts[i];
                float elapsed = currentTime - burst.startTime;

                if (burst.isRecoilOnly)
                {
                    // Pure release recoil (e.g. zone turned off)
                    if (elapsed >= burst.recoilDuration || elapsed < 0f)
                    {
                        _windBursts.RemoveAt(i);
                        continue;
                    }

                    if (count < MaxWindZones)
                    {
                        float cosAngle = Mathf.Cos(burst.coneAngle * 0.5f * Mathf.Deg2Rad);
                        _windZoneOrigins[count] = new Vector4(burst.origin.x, burst.origin.y, burst.origin.z, burst.mode == GrassWindMode.Directional ? 0f : 1f);
                        _windZoneVectors[count] = new Vector4(burst.direction.x, burst.direction.y, burst.direction.z, cosAngle);
                        _windZoneParams[count] = new Vector4(burst.radius, burst.force, burst.flutterSpeed, burst.verticalRange);
                        _windZoneTimes[count] = new Vector4(burst.startTime, burst.recoilDuration, 0f, 0f); // >= 0 means in recoil!
                        count++;
                    }
                }
                else
                {
                    // Timed burst (blow phase -> harmonic spring recoil phase)
                    float totalDuration = burst.blowDuration + burst.recoilDuration;
                    if (elapsed >= totalDuration || elapsed < 0f)
                    {
                        _windBursts.RemoveAt(i);
                        continue;
                    }

                    if (count < MaxWindZones)
                    {
                        float cosAngle = Mathf.Cos(burst.coneAngle * 0.5f * Mathf.Deg2Rad);
                        _windZoneOrigins[count] = new Vector4(burst.origin.x, burst.origin.y, burst.origin.z, burst.mode == GrassWindMode.Directional ? 0f : 1f);
                        _windZoneVectors[count] = new Vector4(burst.direction.x, burst.direction.y, burst.direction.z, cosAngle);
                        _windZoneParams[count] = new Vector4(burst.radius, burst.force, burst.flutterSpeed, burst.verticalRange);

                        if (elapsed < burst.blowDuration)
                        {
                            // Actively blowing
                            _windZoneTimes[count] = new Vector4(-1f, burst.recoilDuration, 0f, 0f);
                        }
                        else
                        {
                            // In harmonic spring recoil phase
                            float recoilStart = burst.startTime + burst.blowDuration;
                            _windZoneTimes[count] = new Vector4(recoilStart, burst.recoilDuration, 0f, 0f);
                        }
                        count++;
                    }
                }
            }

            return count;
        }

        private Plane[] _frustumPlanes = new Plane[6];
        private Vector4[] _frustumPlaneVectors = new Vector4[6];
        private Vector3 _cachedCamPos;
        private Quaternion _cachedCamRot;
        private List<Bounds> _gizmoBounds = new List<Bounds>();
        private Matrix4x4[] _meshInstancedMatrices = new Matrix4x4[1023];

        private static Texture2D GetDefaultBurnMap()
        {
            if (_defaultBurnMap == null)
            {
                _defaultBurnMap = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _defaultBurnMap.SetPixel(0, 0, Color.clear);
                _defaultBurnMap.Apply();
            }
            return _defaultBurnMap;
        }

        public Texture GetCurrentBurnMap()
        {
            if (!enableFireSimulation || _burnMapA == null) return GetDefaultBurnMap();
            return _burnPingPong ? _burnMapB : _burnMapA;
        }

        public Bounds GetTotalGrassBounds()
        {
            Bounds total = new Bounds();
            bool hasBounds = false;
            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null || layer.PointCount == 0) continue;
                    Bounds b = layer.CalculateBounds();
                    if (!hasBounds)
                    {
                        total = b;
                        hasBounds = true;
                    }
                    else
                    {
                        total.Encapsulate(b);
                    }
                }
            }
            if (!hasBounds)
            {
                total = new Bounds(transform.position, new Vector3(200f, 20f, 200f));
            }
            return total;
        }

        public void AutoFitBurnMapBounds()
        {
            Bounds total = GetTotalGrassBounds();
            burnMapCenter = new Vector3(total.center.x, 0f, total.center.z);
            float maxDim = Mathf.Max(total.size.x, total.size.z);
            burnMapWorldSize = Mathf.Max(50f, Mathf.Ceil(maxDim * 1.25f));
        }

        public void EnsureBurnResources()
        {
            if (!enableFireSimulation) return;
            ModernGrassFirePool.GetOrCreate();

            if (burnMapMode == GrassBurnMapMode.FixedWorldBounds && autoFitBurnMapBounds && (burnMapWorldSize <= 1f || (burnMapCenter == Vector3.zero && burnMapWorldSize == 300f)))
            {
                AutoFitBurnMapBounds();
            }

            if (burnComputeShader == null)
            {
#if UNITY_EDITOR
                burnComputeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/ModernGrassTool/Runtime/ModernGrassBurnSim.compute");
#endif
            }

            RenderTextureFormat targetFormat = SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                ? RenderTextureFormat.ARGBFloat
                : RenderTextureFormat.ARGBHalf;

            if (_burnMapA == null || _burnMapA.width != burnMapResolution || _burnMapA.format != targetFormat)
            {
                if (_burnMapA != null) { _burnMapA.Release(); UnityEngine.Object.DestroyImmediate(_burnMapA); }
                if (_burnMapB != null) { _burnMapB.Release(); UnityEngine.Object.DestroyImmediate(_burnMapB); }

                int res = Mathf.Clamp(burnMapResolution, 256, 1024);
                TextureWrapMode targetWrap = (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

                _burnMapA = new RenderTexture(res, res, 0, targetFormat, RenderTextureReadWrite.Linear);
                _burnMapA.name = "GrassBurnMap_A";
                _burnMapA.enableRandomWrite = true;
                _burnMapA.filterMode = FilterMode.Bilinear;
                _burnMapA.wrapMode = targetWrap;
                _burnMapA.Create();

                _burnMapB = new RenderTexture(res, res, 0, targetFormat, RenderTextureReadWrite.Linear);
                _burnMapB.name = "GrassBurnMap_B";
                _burnMapB.enableRandomWrite = true;
                _burnMapB.filterMode = FilterMode.Bilinear;
                _burnMapB.wrapMode = targetWrap;
                _burnMapB.Create();

                _burnPingPong = false;
            }
            else
            {
                TextureWrapMode targetWrap = (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                if (_burnMapA.wrapMode != targetWrap) _burnMapA.wrapMode = targetWrap;
                if (_burnMapB.wrapMode != targetWrap) _burnMapB.wrapMode = targetWrap;
            }

            if (burnComputeShader != null && _kIgniteStamp == -1)
            {
                _kIgniteStamp = burnComputeShader.FindKernel("IgniteStamp");
                _kScorchStamp = burnComputeShader.FindKernel("ScorchStamp");
                _kSimulate = burnComputeShader.FindKernel("Simulate");
                _kBakeFuel = burnComputeShader.FindKernel("BakeFuel");
                _kBakeGrassPointsFuel = burnComputeShader.FindKernel("BakeGrassPointsFuel");
                _kClearMap = burnComputeShader.FindKernel("ClearMap");
                _kClearStamp = burnComputeShader.FindKernel("ClearStamp");

                BakeAllFuel();
            }
        }

        public void ClearBurnMap()
        {
            if (_burnMapA == null || burnComputeShader == null || _kClearMap == -1) return;
            int groups = Mathf.CeilToInt(burnMapResolution / 16f);
            burnComputeShader.SetTexture(_kClearMap, "_BurnMapRW", _burnMapA);
            burnComputeShader.SetFloat("_TextureSize", burnMapResolution);
            burnComputeShader.Dispatch(_kClearMap, groups, groups, 1);

            burnComputeShader.SetTexture(_kClearMap, "_BurnMapRW", _burnMapB);
            burnComputeShader.Dispatch(_kClearMap, groups, groups, 1);
        }

        public void BakeAllFuel()
        {
            if (burnComputeShader == null || _burnMapA == null || layers == null) return;
            if (_kBakeGrassPointsFuel == -1)
            {
                _kBakeGrassPointsFuel = burnComputeShader.FindKernel("BakeGrassPointsFuel");
            }
            if (_kBakeGrassPointsFuel == -1) return;

            // Clear burn map so bare dirt starts with zero fuel
            ClearBurnMap();

            InvalidateSpatialGrid();
            EnsureSpatialGrid();

            Vector3 effCenter = burnMapCenter;
            if (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating)
            {
                effCenter = GetEffectivePlayerPos();
            }

            burnComputeShader.SetFloat("_BurnMapWorldSize", Mathf.Max(1f, burnMapWorldSize));
            burnComputeShader.SetVector("_BurnMapCenter", effCenter);
            burnComputeShader.SetFloat("_BurnMapMode", (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? 1f : 0f);
            burnComputeShader.SetFloat("_TextureSize", (float)burnMapResolution);

            for (int l = 0; l < layers.Count; l++)
            {
                var layer = layers[l];
                if (layer == null || !layer.canCatchFire) continue;

                layer.EnsureBuffers();
                if (layer.sourceBuffer == null || layer.PointCount == 0) continue;

                burnComputeShader.SetBuffer(_kBakeGrassPointsFuel, "_GrassPoints", layer.sourceBuffer);
                burnComputeShader.SetInt("_GrassPointCount", layer.PointCount);
                burnComputeShader.SetFloat("_GrassClumpRadius", layer.clumpRadius);
                burnComputeShader.SetFloat("_FireMaxSpreadGap", layer.fireMaxSpreadGap);

                burnComputeShader.SetTexture(_kBakeGrassPointsFuel, "_BurnMapRW", _burnMapA);
                int groups = Mathf.CeilToInt(layer.PointCount / 64f);
                burnComputeShader.Dispatch(_kBakeGrassPointsFuel, groups, 1, 1);
            }

            // Sync both textures in ping-pong chain
            Graphics.CopyTexture(_burnMapA, _burnMapB);
        }

        public bool WorldToBurnUV(Vector3 worldPos, float radius, out Vector2 uv, out float uvRadius)
        {
            uv = Vector2.zero;
            uvRadius = 0f;

            float size = Mathf.Max(1f, burnMapWorldSize);

            if (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating)
            {
                // Toroidal Modulo Ring Buffer: frac(worldPos / size)
                // Maps seamlessly and infinitely across the entire 10km+ world
                float u = Mathf.Repeat(worldPos.x / size, 1.0f);
                float v = Mathf.Repeat(worldPos.z / size, 1.0f);
                uv = new Vector2(u, v);
                uvRadius = radius / size;
                return true;
            }
            else
            {
                // Fixed World Bounds Mode (Clamped bounds)
                float u = (worldPos.x - burnMapCenter.x) / size + 0.5f;
                float v = (worldPos.z - burnMapCenter.z) / size + 0.5f;

                uv = new Vector2(u, v);
                uvRadius = radius / size;

                return (u >= 0f && u <= 1f && v >= 0f && v <= 1f);
            }
        }

        public GrassLayer GetGrassLayerAt(Vector3 worldPos, float maxDistance = 4.0f)
        {
            if (layers == null || layers.Count == 0) return null;
            float bestDistSq = float.MaxValue;
            GrassLayer bestLayer = null;
            float maxDistSq = maxDistance * maxDistance;

            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                if (l == null || !l.canCatchFire) continue;

                if (l.chunks != null && l.chunks.Count > 0)
                {
                    for (int c = 0; c < l.chunks.Count; c++)
                    {
                        var chunk = l.chunks[c];
                        if (chunk != null)
                        {
                            if (chunk.bounds.Contains(worldPos))
                            {
                                return l;
                            }
                            float dSq = chunk.bounds.SqrDistance(worldPos);
                            if (dSq < bestDistSq)
                            {
                                bestDistSq = dSq;
                                bestLayer = l;
                            }
                        }
                    }
                }
                else
                {
                    Bounds b = l.CalculateBounds();
                    if (b.Contains(worldPos)) return l;
                    float dSq = b.SqrDistance(worldPos);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestLayer = l;
                    }
                }
            }

            return (bestDistSq <= maxDistSq) ? bestLayer : null;
        }

        public float GetGrassSpreadRadiusAt(Vector3 worldPos)
        {
            var l = GetGrassLayerAt(worldPos);
            return (l != null && l.canCatchFire) ? l.fireSpreadRadius : 3.5f;
        }

        public float GetGrassSpreadSpeedAt(Vector3 worldPos)
        {
            var l = GetGrassLayerAt(worldPos);
            return (l != null && l.canCatchFire) ? l.fireSpreadSpeed : 1.0f;
        }

        /// <summary>
        /// Static check: Returns true if flammable unburnt grass exists at the given position and can be ignited.
        /// Returns false if the ground is bare dirt/stone, or if the grass is already burned to ash / currently on fire.
        /// </summary>
        public static bool CanIgnite(Vector3 worldPos, float radius = 1.5f)
        {
            if (Instance != null) return Instance.CanIgniteAt(worldPos, radius, out _);
            var mgr = FindAnyObjectByType<ModernGrassManager>();
            return mgr != null && mgr.CanIgniteAt(worldPos, radius, out _);
        }

        /// <summary>
        /// Validates whether flammable unburnt grass exists at worldPos that is eligible to catch fire.
        /// Returns false if no flammable grass exists nearby, or if the area is already burning or burned to ash stubbles.
        /// </summary>
        public bool CanIgniteAt(Vector3 worldPos, float radius, out GrassLayer hitLayer)
        {
            hitLayer = null;
            if (!enableFireSimulation || layers == null || layers.Count == 0) return false;

            // 1. Spatial Grass Presence & Layer Lookup: O(1) check ensuring unburnt flammable grass exists at this position
            float queryRadius = radius + 0.6f;
            if (!HasUnburntFlammableGrassAt(worldPos, queryRadius, out hitLayer) || hitLayer == null)
            {
                return false; // Bare ground, stone, or already burned down to black ash!
            }

            int centerCx = Mathf.FloorToInt(worldPos.x / SPATIAL_CELL_SIZE);
            int centerCz = Mathf.FloorToInt(worldPos.z / SPATIAL_CELL_SIZE);
            long centerKey = ((long)centerCx << 32) | (uint)centerCz;
            if (IsCellBurnedOrBurning(centerKey))
            {
                return false; // Center contact point is already on fire or burned to black ash stubbles!
            }

            // 2. Active Burn / Ash Stubble Check: Ensure this spot isn't already burning or burned to ash
            float now = Time.time;
            for (int i = 0; i < _activeBurns.Count; i++)
            {
                var burn = _activeBurns[i];
                if (now >= burn.recoverTime) continue; // Burn has recovered/regrown

                // Calculate the currently reached burn/fire radius
                float elapsed = now - burn.time;
                float currentBurnRadius = (burn.spreadSpeed > 0.05f) 
                    ? Mathf.Min(burn.maxRadius, burn.initialRadius + elapsed * burn.spreadSpeed) 
                    : burn.maxRadius;

                float distSq = (worldPos.x - burn.position.x) * (worldPos.x - burn.position.x) + 
                               (worldPos.z - burn.position.z) * (worldPos.z - burn.position.z);
                float effectiveRadius = currentBurnRadius + radius * 0.2f;

                if (distSq <= effectiveRadius * effectiveRadius)
                {
                    // Already burning or already burned to ash stubble!
                    return false;
                }
            }

            // 3. Active Fire Pool Particles Check: Also verify no active fire particle system is currently covering this spot
            if (ModernGrassFirePool.Instance != null && ModernGrassFirePool.Instance.IsFireActiveAt(worldPos, radius))
            {
                return false;
            }

            return true;
        }

        public static void IgniteAt(Vector3 worldPos, float radius = 1.5f, float maxSpreadRadius = -1f)
        {
            if (Instance != null) Instance.IgniteInternal(worldPos, radius, maxSpreadRadius);
            else FindAnyObjectByType<ModernGrassManager>()?.IgniteInternal(worldPos, radius, maxSpreadRadius);
        }

        public void IgniteInternal(Vector3 worldPos, float radius, float maxSpreadRadius = -1f)
        {
            if (!enableFireSimulation) return;
            EnsureBurnResources();
            if (burnComputeShader == null || _burnMapA == null || _kIgniteStamp == -1) return;

            // Validate that flammable unburnt grass actually exists at this position
            if (!CanIgniteAt(worldPos, radius, out GrassLayer hitLayer))
            {
                return;
            }

            if (maxSpreadRadius <= 0f)
            {
                maxSpreadRadius = (hitLayer != null && hitLayer.canCatchFire) ? hitLayer.fireSpreadRadius : GetGrassSpreadRadiusAt(worldPos);
            }
            maxSpreadRadius = Mathf.Max(radius, maxSpreadRadius);
            float spreadSpeed = (hitLayer != null && hitLayer.canCatchFire) ? hitLayer.fireSpreadSpeed : GetGrassSpreadSpeedAt(worldPos);

            if (WorldToBurnUV(worldPos, radius, out Vector2 uv, out float uvRadius))
            {
                WorldToBurnUV(worldPos, maxSpreadRadius, out _, out float uvSpreadRadius);

                RenderTexture src = _burnPingPong ? _burnMapB : _burnMapA;
                RenderTexture dst = _burnPingPong ? _burnMapA : _burnMapB;

                burnComputeShader.SetTexture(_kIgniteStamp, "_BurnMapRead", src);
                burnComputeShader.SetTexture(_kIgniteStamp, "_BurnMapRW", dst);
                burnComputeShader.SetVector("_StampPos", uv);
                burnComputeShader.SetFloat("_StampRadius", Mathf.Max(0.002f, uvRadius));
                burnComputeShader.SetFloat("_StampIntensity", 1.0f);
                burnComputeShader.SetFloat("_TextureSize", (float)burnMapResolution);
                burnComputeShader.SetFloat("_ReignitionThreshold", Mathf.Clamp(reignitionScorchThreshold / 100f, 0.2f, 1.0f));

                int groups = Mathf.CeilToInt(burnMapResolution / 16f);
                burnComputeShader.Dispatch(_kIgniteStamp, groups, groups, 1);

                // Hardware GPU copy to keep both buffers 100% in sync and prevent any ping-pong alternating/flickering
                Graphics.CopyTexture(dst, src);

                _burnPingPong = !_burnPingPong;
                _hasActiveFire = true;
                _lastFireActiveTime = Time.time;
                _fireActiveTimer = (burnFadeMode == GrassBurnFadeMode.Timer) ? Mathf.Max(_fireActiveTimer, burnFadeSeconds + 30f) : 35f;

                float ignSpreadDist = Mathf.Max(0f, maxSpreadRadius - radius);
                float ignSpreadDur = (spreadSpeed > 0.05f) ? (ignSpreadDist / spreadSpeed) : 0f;
                float ignBurnDur = (hitLayer != null) ? Mathf.Max(1.0f, hitLayer.burnDuration) : 3.0f;

                float fadeTime = (burnFadeMode == GrassBurnFadeMode.Timer) 
                    ? burnFadeSeconds * Mathf.Clamp01(1.0f - (reignitionScorchThreshold / 100f)) 
                    : float.MaxValue;
                float recoverTime = (fadeTime < float.MaxValue) ? (Time.time + ignSpreadDur + ignBurnDur + fadeTime) : float.MaxValue;

                float actualMaxRad = maxSpreadRadius;
                HashSet<long> reachableCells = null;
                float gap = (hitLayer != null) ? hitLayer.fireMaxSpreadGap : 0.6f;
                if (CalculateReachableGrass(worldPos, maxSpreadRadius, gap, out float maxReachable, out reachableCells))
                {
                    actualMaxRad = Mathf.Min(maxSpreadRadius, maxReachable + 0.6f);
                }

                // Register all reached cells in cell burn registry with calculated extinguish and recover times!
                float ignSeed = uv.x * 123.456f + uv.y * 789.012f;
                Vector2 ignWind2D = (windDirection.sqrMagnitude > 0.001f) 
                    ? new Vector2(windDirection.x, windDirection.z).normalized 
                    : Vector2.right;

                if (reachableCells != null && reachableCells.Count > 0)
                {
                    float now = Time.time;
                    foreach (long cellKey in reachableCells)
                    {
                        int cx = (int)(cellKey >> 32);
                        int cz = (int)(cellKey & 0xFFFFFFFF);
                        float cellWorldX = (cx + 0.5f) * SPATIAL_CELL_SIZE;
                        float cellWorldZ = (cz + 0.5f) * SPATIAL_CELL_SIZE;
                        float dX = cellWorldX - worldPos.x;
                        float dZ = cellWorldZ - worldPos.z;
                        float d = Mathf.Sqrt(dX * dX + dZ * dZ);

                        float angle = Mathf.Atan2(dZ, dX);
                        float a1 = Mathf.Sin(angle * 2.0f + ignSeed * 1.0f) * 0.22f;
                        float a2 = Mathf.Cos(angle * 3.0f - ignSeed * 1.7f) * 0.16f;
                        float a3 = Mathf.Sin(angle * 5.0f + ignSeed * 2.3f) * 0.10f;
                        float a4 = Mathf.Cos(angle * 7.0f - ignSeed * 0.9f) * 0.06f;
                        float shapeFactor = 1.0f + a1 + a2 + a3 + a4;

                        Vector2 dir = (d > 0.0001f) ? new Vector2(dX / d, dZ / d) : Vector2.zero;
                        float windAlignment = Vector2.Dot(dir, ignWind2D);
                        float windStretch = 1.0f + windAlignment * 0.35f;
                        float localSpeedMultiplier = Mathf.Max(0.2f, shapeFactor) * windStretch;

                        float effectiveSpeed = spreadSpeed * localSpeedMultiplier;
                        float cellIgniteTime = now + (effectiveSpeed > 0.05f ? (d / effectiveSpeed) : 0f);
                        float cellExtinguishTime = cellIgniteTime + ignBurnDur;
                        float cellRecoverTime = cellExtinguishTime + fadeTime;

                        _cellIgniteTimes[cellKey] = cellIgniteTime;
                        _cellExtinguishTimes[cellKey] = cellExtinguishTime;
                        _cellRecoverTimes[cellKey] = cellRecoverTime;
                    }
                }

                _activeBurns.Add(new ActiveBurnPoint 
                { 
                    id = _nextBurnId++,
                    position = worldPos, 
                    initialRadius = radius, 
                    maxRadius = actualMaxRad, 
                    spreadSpeed = spreadSpeed, 
                    time = Time.time,
                    recoverTime = recoverTime,
                    burnDuration = ignBurnDur,
                    seed = ignSeed,
                    windDir = ignWind2D,
                    firePrefab = (hitLayer != null && hitLayer.fireParticlePrefab != null) ? hitLayer.fireParticlePrefab : defaultFireParticlePrefab
                });

                // Old expanding ring fire VFX removed (Preparing for Part 2: Discrete Clustered Fire Nodes)
                if (reachableCells != null)
                {
                    ModernGrassFirePool.ReleasePooledCellSet(reachableCells);
                }

                // Register or merge active ignition source for max spread radius containment
                float lifetime = ignSpreadDur + ignBurnDur + 0.5f;
                bool merged = false;
                for (int i = 0; i < _activeIgnitions.Count; i++)
                {
                    var ign = _activeIgnitions[i];
                    if (Vector3.Distance(ign.worldPos, worldPos) < 1.2f)
                    {
                        ign.worldPos = worldPos;
                        ign.uvPos = uv;
                        ign.uvMaxRadius = Mathf.Max(ign.uvMaxRadius, Mathf.Max(0.002f, uvSpreadRadius));
                        ign.maxSpreadRadius = Mathf.Max(ign.maxSpreadRadius, maxSpreadRadius);
                        ign.spreadSpeed = spreadSpeed;
                        ign.startTime = Time.time;
                        ign.lifetime = lifetime;
                        _activeIgnitions[i] = ign;
                        merged = true;
                        break;
                    }
                }
                if (!merged)
                {
                    if (_activeIgnitions.Count >= 16)
                    {
                        _activeIgnitions.RemoveAt(0);
                    }
                    _activeIgnitions.Add(new ActiveIgnitionSource
                    {
                        worldPos = worldPos,
                        uvPos = uv,
                        uvMaxRadius = Mathf.Max(0.002f, uvSpreadRadius),
                        maxSpreadRadius = maxSpreadRadius,
                        spreadSpeed = spreadSpeed,
                        startTime = Time.time,
                        lifetime = lifetime
                    });
                }
            }
        }

        public static void ScorchAt(Vector3 worldPos, float radius = 2.5f, float intensity = 1.0f)
        {
            if (Instance != null) Instance.ScorchInternal(worldPos, radius, intensity);
            else FindAnyObjectByType<ModernGrassManager>()?.ScorchInternal(worldPos, radius, intensity);
        }

        public void ScorchInternal(Vector3 worldPos, float radius, float intensity)
        {
            if (!enableFireSimulation) return;
            EnsureBurnResources();
            if (burnComputeShader == null || _burnMapA == null || _kScorchStamp == -1) return;

            if (WorldToBurnUV(worldPos, radius, out Vector2 uv, out float uvRadius))
            {
                RenderTexture src = _burnPingPong ? _burnMapB : _burnMapA;
                RenderTexture dst = _burnPingPong ? _burnMapA : _burnMapB;

                burnComputeShader.SetTexture(_kScorchStamp, "_BurnMapRead", src);
                burnComputeShader.SetTexture(_kScorchStamp, "_BurnMapRW", dst);
                burnComputeShader.SetVector("_StampPos", uv);
                burnComputeShader.SetFloat("_StampRadius", Mathf.Max(0.002f, uvRadius));
                burnComputeShader.SetFloat("_StampIntensity", Mathf.Clamp01(intensity));
                burnComputeShader.SetFloat("_TextureSize", (float)burnMapResolution);

                int groups = Mathf.CeilToInt(burnMapResolution / 16f);
                burnComputeShader.Dispatch(_kScorchStamp, groups, groups, 1);

                // Hardware GPU copy to keep both buffers 100% in sync and prevent any ping-pong alternating/flickering
                Graphics.CopyTexture(dst, src);

                _burnPingPong = !_burnPingPong;
                _hasActiveFire = true;
                _fireActiveTimer = (burnFadeMode == GrassBurnFadeMode.Timer) ? Mathf.Max(_fireActiveTimer, burnFadeSeconds + 5f) : 25f;

                float fadeTime = (burnFadeMode == GrassBurnFadeMode.Timer) 
                    ? burnFadeSeconds * Mathf.Clamp01(1.0f - (reignitionScorchThreshold / 100f)) 
                    : float.MaxValue;
                float recoverTime = (fadeTime < float.MaxValue) ? (Time.time + fadeTime) : float.MaxValue;

                _activeBurns.Add(new ActiveBurnPoint 
                { 
                    id = _nextBurnId++,
                    position = worldPos, 
                    initialRadius = radius, 
                    maxRadius = radius, 
                    spreadSpeed = 0f, 
                    time = Time.time,
                    recoverTime = recoverTime,
                    burnDuration = 0f,
                    seed = 0f,
                    windDir = Vector2.right,
                    firePrefab = null,
                    isScorchOnly = true
                });
            }
        }

        /// <summary>
        /// Clears/restores scorch marks within a specific radius back to healthy grass.
        /// </summary>
        public static void ClearBurnAt(Vector3 worldPos, float radius = 3.0f)
        {
            if (Instance != null) Instance.ClearBurnInternal(worldPos, radius);
            else FindAnyObjectByType<ModernGrassManager>()?.ClearBurnInternal(worldPos, radius);
        }

        /// <summary>
        /// Clears all scorch and burn marks across the entire map immediately.
        /// </summary>
        public static void ClearAllBurn()
        {
            if (Instance != null) Instance.ClearAllBurnInternal();
            else FindAnyObjectByType<ModernGrassManager>()?.ClearAllBurnInternal();
        }

        public void ClearAllBurnInternal()
        {
            _activeBurns.Clear();
            _activeIgnitions.Clear();
            _cellIgniteTimes.Clear();
            _cellExtinguishTimes.Clear();
            _cellRecoverTimes.Clear();
            _hasActiveFire = false;
            _fireActiveTimer = 0f;
            ModernGrassFirePool.Instance?.StopAllFire();
            BakeAllFuel();
        }

        public void ClearBurnInternal(Vector3 worldPos, float radius)
        {
            if (!enableFireSimulation) return;
            EnsureBurnResources();
            if (burnComputeShader == null || _burnMapA == null || _kClearStamp == -1) return;

            // Clear cells within radius from cell burn registry
            int clearCx = Mathf.FloorToInt(worldPos.x / SPATIAL_CELL_SIZE);
            int clearCz = Mathf.FloorToInt(worldPos.z / SPATIAL_CELL_SIZE);
            int r = Mathf.CeilToInt(radius / SPATIAL_CELL_SIZE) + 1;
            float sqrR = radius * radius;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    int cx = clearCx + dx;
                    int cz = clearCz + dz;
                    float wx = (cx + 0.5f) * SPATIAL_CELL_SIZE;
                    float wz = (cz + 0.5f) * SPATIAL_CELL_SIZE;
                    if ((wx - worldPos.x) * (wx - worldPos.x) + (wz - worldPos.z) * (wz - worldPos.z) <= sqrR)
                    {
                        long key = ((long)cx << 32) | (uint)cz;
                        _cellIgniteTimes.Remove(key);
                        _cellExtinguishTimes.Remove(key);
                        _cellRecoverTimes.Remove(key);
                    }
                }
            }

            for (int i = _activeIgnitions.Count - 1; i >= 0; i--)
            {
                if (Vector3.Distance(_activeIgnitions[i].worldPos, worldPos) <= radius)
                {
                    _activeIgnitions.RemoveAt(i);
                }
            }

            for (int i = _activeBurns.Count - 1; i >= 0; i--)
            {
                float d = Vector3.Distance(_activeBurns[i].position, worldPos);
                if (d <= radius + _activeBurns[i].maxRadius)
                {
                    _activeBurns.RemoveAt(i);
                }
            }

            if (WorldToBurnUV(worldPos, radius, out Vector2 uv, out float uvRadius))
            {
                RenderTexture src = _burnPingPong ? _burnMapB : _burnMapA;
                RenderTexture dst = _burnPingPong ? _burnMapA : _burnMapB;

                burnComputeShader.SetTexture(_kClearStamp, "_BurnMapRead", src);
                burnComputeShader.SetTexture(_kClearStamp, "_BurnMapRW", dst);
                burnComputeShader.SetVector("_StampPos", uv);
                burnComputeShader.SetFloat("_StampRadius", Mathf.Max(0.005f, uvRadius));
                burnComputeShader.SetFloat("_TextureSize", (float)burnMapResolution);
                burnComputeShader.SetFloat("_ReignitionThreshold", Mathf.Clamp(reignitionScorchThreshold / 100f, 0.2f, 1.0f));

                int groups = Mathf.CeilToInt(burnMapResolution / 16f);
                burnComputeShader.Dispatch(_kClearStamp, groups, groups, 1);

                Graphics.CopyTexture(dst, src);
                _burnPingPong = !_burnPingPong;
            }
        }

        private void ReleaseBurnResources()
        {
            if (_burnMapA != null) { _burnMapA.Release(); UnityEngine.Object.DestroyImmediate(_burnMapA); _burnMapA = null; }
            if (_burnMapB != null) { _burnMapB.Release(); UnityEngine.Object.DestroyImmediate(_burnMapB); _burnMapB = null; }
            _kIgniteStamp = -1;
            _kScorchStamp = -1;
            _kSimulate = -1;
            _kBakeFuel = -1;
            _kBakeGrassPointsFuel = -1;
            _kClearMap = -1;
            _kClearStamp = -1;
            _activeBurns.Clear();
            _activeIgnitions.Clear();
        }

        private float _fireActiveTimer = 0f;

        public void UpdateBurnSimulation()
        {
            if (!enableFireSimulation || burnComputeShader == null) return;
            if (_kSimulate == -1) EnsureBurnResources();
            if (_burnMapA == null || _kSimulate == -1) return;

            if (_hasActiveFire)
            {
                _fireActiveTimer = 35f;
                _hasActiveFire = false;
            }

            if (_fireActiveTimer <= 0f) return;
            _fireActiveTimer -= Time.deltaTime;

            // Get average burn duration & spread radius across flammable layers
            float avgBurnDuration = 3.5f;
            float avgSpreadSpeed = 1.0f;
            if (layers != null && layers.Count > 0)
            {
                float sumDuration = 0f;
                float sumSpread = 0f;
                int count = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    var l = layers[i];
                    if (l != null && l.canCatchFire)
                    {
                        sumDuration += Mathf.Max(0.5f, l.burnDuration);
                        sumSpread += Mathf.Max(0.1f, l.fireSpreadSpeed);
                        count++;
                    }
                }
                if (count > 0)
                {
                    avgBurnDuration = sumDuration / count;
                    avgSpreadSpeed = sumSpread / count;
                }
            }

            // Prune expired ignition sources
            float now = Time.time;
            for (int i = _activeIgnitions.Count - 1; i >= 0; i--)
            {
                if (now - _activeIgnitions[i].startTime > _activeIgnitions[i].lifetime)
                {
                    _activeIgnitions.RemoveAt(i);
                }
            }

            int ignCount = Mathf.Min(_activeIgnitions.Count, 16);
            for (int i = 0; i < 16; i++)
            {
                if (i < ignCount)
                {
                    var ign = _activeIgnitions[i];
                    _ignitionSourcesArray[i] = new Vector4(ign.uvPos.x, ign.uvPos.y, ign.uvMaxRadius, Mathf.Max(0.05f, ign.spreadSpeed));
                }
                else
                {
                    _ignitionSourcesArray[i] = Vector4.zero;
                }
            }
            burnComputeShader.SetInt("_IgnitionSourceCount", ignCount);
            burnComputeShader.SetVectorArray("_IgnitionSources", _ignitionSourcesArray);

            RenderTexture src = _burnPingPong ? _burnMapB : _burnMapA;
            RenderTexture dst = _burnPingPong ? _burnMapA : _burnMapB;

            burnComputeShader.SetTexture(_kSimulate, "_BurnMapRead", src);
            burnComputeShader.SetTexture(_kSimulate, "_BurnMapRW", dst);
            burnComputeShader.SetFloat("_DeltaTime", Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f));
            burnComputeShader.SetFloat("_BurnDuration", avgBurnDuration);
            burnComputeShader.SetFloat("_SpreadSpeed", avgSpreadSpeed);
            Vector2 wind2D = new Vector2(windDirection.x, windDirection.z).normalized;
            burnComputeShader.SetVector("_WindVector", wind2D);
            burnComputeShader.SetFloat("_TextureSize", (float)burnMapResolution);
            burnComputeShader.SetFloat("_BurnMapWorldSize", Mathf.Max(1f, burnMapWorldSize));

            float fadeRate = (burnFadeMode == GrassBurnFadeMode.Timer) ? (1.0f / Mathf.Max(0.1f, burnFadeSeconds)) : 0.0f;
            burnComputeShader.SetFloat("_FadeRate", fadeRate);
            burnComputeShader.SetFloat("_ReignitionThreshold", Mathf.Clamp(reignitionScorchThreshold / 100f, 0.2f, 1.0f));

            int groups = Mathf.CeilToInt(burnMapResolution / 16f);
            burnComputeShader.Dispatch(_kSimulate, groups, groups, 1);

            _burnPingPong = !_burnPingPong;
        }

        private void Awake()
        {
            Instance = this;
            EnsureTerrainBaker();
            EnsureBurnResources();
        }

        private void Start()
        {
            EnsureSpatialGrid();
            if (enableFireSimulation)
            {
                BakeAllFuel();
            }
        }

        private ModernRenderTerrainMap _cachedTerrainBaker;

        public ModernRenderTerrainMap EnsureTerrainBaker()
        {
            if (_cachedTerrainBaker != null) return _cachedTerrainBaker;
            var existing = FindAnyObjectByType<ModernRenderTerrainMap>();
            if (existing != null)
            {
                _cachedTerrainBaker = existing;
                return _cachedTerrainBaker;
            }

            GameObject mapGo = new GameObject("ModernRenderTerrainMap");
            mapGo.transform.SetParent(transform);
            mapGo.transform.localPosition = Vector3.zero;
            var baker = mapGo.AddComponent<ModernRenderTerrainMap>();
            baker.SetupAndBake();
            _cachedTerrainBaker = baker;
            return _cachedTerrainBaker;
        }

        private void OnEnable()
        {
            Instance = this;
            ValidateReferences();
            InitializeChunksAndStreaming();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void LateUpdate()
        {
            if (enableChunkStreaming)
            {
                UpdateStreaming(GetViewerPosition(), force: false);
            }
        }

        public void InitializeChunksAndStreaming()
        {
            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null) continue;
                    layer.AutoMigratePointsToChunks(chunkSize);
                }
            }

            if (enableChunkStreaming)
            {
                UpdateStreaming(GetViewerPosition(), force: true);
            }
        }

        public Vector3 GetViewerPosition()
        {
            if (playerReference != null) return playerReference.position;

            if (ModernGrassInteractor.ActiveInteractors != null && ModernGrassInteractor.ActiveInteractors.Count > 0)
            {
                for (int i = 0; i < ModernGrassInteractor.ActiveInteractors.Count; i++)
                {
                    var inter = ModernGrassInteractor.ActiveInteractors[i];
                    if (inter != null && inter.gameObject.activeInHierarchy)
                    {
                        return inter.transform.position;
                    }
                }
            }

            if (Camera.main != null) return Camera.main.transform.position;

#if UNITY_EDITOR
            if (UnityEditor.SceneView.lastActiveSceneView != null && UnityEditor.SceneView.lastActiveSceneView.camera != null)
            {
                return UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
            }
#endif
            return transform.position;
        }

        public void UpdateStreaming(Vector3 viewerPos, bool force = false)
        {
            if (!enableChunkStreaming) return;
            if (isPaintingActive) return;
            if (chunkSize <= 0f) chunkSize = 64f;

            Vector2Int currentCell = GrassLayer.PositionToChunkCoord(viewerPos, chunkSize);
            if (!force && currentCell == _lastStreamCell) return;

            _lastStreamCell = currentCell;
            _lastStreamViewerPos = viewerPos;

            int chunkRadius = Mathf.CeilToInt(streamRadius / chunkSize);
            int centerCx = Mathf.FloorToInt(viewerPos.x / chunkSize);
            int centerCz = Mathf.FloorToInt(viewerPos.z / chunkSize);

            _streamCoordsBuffer.Clear();
            float streamRadSqr = (streamRadius + chunkSize * 0.7f) * (streamRadius + chunkSize * 0.7f);

            for (int dx = -chunkRadius; dx <= chunkRadius; dx++)
            {
                for (int dz = -chunkRadius; dz <= chunkRadius; dz++)
                {
                    int cx = centerCx + dx;
                    int cz = centerCz + dz;
                    float chunkCenterX = cx * chunkSize + chunkSize * 0.5f;
                    float chunkCenterZ = cz * chunkSize + chunkSize * 0.5f;
                    float dX = chunkCenterX - viewerPos.x;
                    float dZ = chunkCenterZ - viewerPos.z;
                    if (dX * dX + dZ * dZ <= streamRadSqr)
                    {
                        _streamCoordsBuffer.Add(new Vector2Int(cx, cz));
                    }
                }
            }

            if (!force && _activeChunkCoords.SetEquals(_streamCoordsBuffer)) return;

            _activeChunkCoords.Clear();
            _activeChunkCoords.UnionWith(_streamCoordsBuffer);

            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer == null) continue;
                    layer.StreamActiveChunks(_activeChunkCoords, chunkSize);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawChunkGizmos || layers == null) return;

            for (int l = 0; l < layers.Count; l++)
            {
                var layer = layers[l];
                if (layer == null || layer.chunks == null) continue;

                for (int c = 0; c < layer.chunks.Count; c++)
                {
                    var chunk = layer.chunks[c];
                    if (chunk == null) continue;

                    bool isActive = _activeChunkCoords != null && _activeChunkCoords.Contains(chunk.coord);
                    Gizmos.color = isActive ? new Color(0.1f, 0.9f, 0.2f, 0.7f) : new Color(0.6f, 0.6f, 0.6f, 0.25f);
                    Gizmos.DrawWireCube(chunk.bounds.center, chunk.bounds.size);

                    if (isActive)
                    {
                        Gizmos.color = new Color(0.1f, 0.9f, 0.2f, 0.05f);
                        Gizmos.DrawCube(chunk.bounds.center, chunk.bounds.size);
                    }
                }
            }
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseAllBuffers();
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseAllBuffers();
        }

        public void ValidateReferences()
        {
            if (grassShader == null)
            {
                grassShader = Shader.Find("ModernGrassTool/ModernGrassShader");
            }

#if UNITY_EDITOR
            if (computeShader == null)
            {
                computeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Assets/ModernGrassTool/Runtime/ModernGrassBlades.compute"
                );
            }
#endif

            if (computeShader != null)
            {
                _kernelIndex = computeShader.FindKernel("CSMain");
                _kernelResetArgs = computeShader.FindKernel("CSResetArgs");
            }
        }

        public void ReleaseAllBuffers()
        {
            ReleaseBurnResources();
            if (layers == null) return;
            for (int i = 0; i < layers.Count; i++)
            {
                layers[i]?.ReleaseBuffers();
            }
        }

        public GrassLayer AddLayer(string name = "New Grass Layer", ModernGrassType grassType = null)
        {
            GrassLayer layer = new GrassLayer
            {
                layerName = name,
                grassType = grassType
            };
            layers.Add(layer);
            return layer;
        }

        public GrassLayer AddLayer(ModernGrassType grassType)
        {
            return AddLayer(grassType != null ? grassType.typeName : "New Layer", grassType);
        }

        public void RemoveLayer(int index)
        {
            if (index >= 0 && index < layers.Count)
            {
                layers[index]?.ReleaseBuffers();
                layers.RemoveAt(index);
            }
        }

        public Vector3 GetEffectivePlayerPos()
        {
            if (playerReference != null) return playerReference.position;
            if (Camera.main != null) return Camera.main.transform.position;
            var player = UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayerController>();
            if (player != null)
            {
                playerReference = player.transform;
                return player.transform.position;
            }
            return transform.position;
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            UpdateBurnSimulation();

            // 1. Distance-based burn clearing (when explicitly in Distance mode)
            if (burnFadeMode == GrassBurnFadeMode.Distance && _activeBurns.Count > 0)
            {
                Vector3 refPos = GetEffectivePlayerPos();
                float sqrDistThreshold = burnFadeDistance * burnFadeDistance;
                for (int i = _activeBurns.Count - 1; i >= 0; i--)
                {
                    var burn = _activeBurns[i];
                    float sqrDist = (refPos.x - burn.position.x) * (refPos.x - burn.position.x) + (refPos.z - burn.position.z) * (refPos.z - burn.position.z);
                    if (sqrDist >= sqrDistThreshold)
                    {
                        ClearBurnInternal(burn.position, burn.maxRadius * 1.5f);
                        _activeBurns.RemoveAt(i);
                    }
                }
            }
            // 2. Open-World Toroidal Ring Buffer Eviction (when burns scroll outside the window)
            else if (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating && _activeBurns.Count > 0)
            {
                Vector3 refPos = GetEffectivePlayerPos();
                float evictDist = Mathf.Max(10f, burnMapWorldSize * 0.48f);
                float sqrEvict = evictDist * evictDist;
                for (int i = _activeBurns.Count - 1; i >= 0; i--)
                {
                    var burn = _activeBurns[i];
                    float sqrDist = (refPos.x - burn.position.x) * (refPos.x - burn.position.x) + (refPos.z - burn.position.z) * (refPos.z - burn.position.z);
                    if (sqrDist >= sqrEvict)
                    {
                        ClearBurnInternal(burn.position, burn.maxRadius * 1.5f);
                        _activeBurns.RemoveAt(i);
                    }
                }
            }
            // 3. Timer Mode Burn Expiration (when scorch has faded enough to allow reignition)
            else if (burnFadeMode == GrassBurnFadeMode.Timer && _activeBurns.Count > 0)
            {
                float now = Time.time;
                for (int i = _activeBurns.Count - 1; i >= 0; i--)
                {
                    if (now >= _activeBurns[i].recoverTime)
                    {
                        _activeBurns.RemoveAt(i);
                    }
                }
            }

            if (_activeCuts.Count == 0) return;

            // Note: Timer regrowth mode is 100% GPU-driven on the compute shader (_Time - cutTime),
            // requiring zero CPU polling or frame-by-frame buffer uploads!
            if (regrowthMode == GrassRegrowthMode.Distance)
            {
                Vector3 refPos = GetEffectivePlayerPos();
                float sqrDistThreshold = regrowDistance * regrowDistance;
                for (int i = _activeCuts.Count - 1; i >= 0; i--)
                {
                    var cut = _activeCuts[i];
                    float sqrDist = (refPos.x - cut.position.x) * (refPos.x - cut.position.x) + (refPos.z - cut.position.z) * (refPos.z - cut.position.z);
                    if (sqrDist >= sqrDistThreshold)
                    {
                        RestoreCutPoint(cut.layerIndex, cut.pointIndex, cut.position);
                        _activeCuts.RemoveAt(i);
                    }
                }
            }
        }

        [Header("Grass Cutting Settings")]
        [Tooltip("Height of the remaining stubble above grass root when cut (e.g., 0.18m for a short flat cut stump).")]
        public float defaultStubbleHeight = 0.18f;

        private void RestoreCutPoint(int layerIndex, int pointIndex, Vector3 pos)
        {
            if (layers != null && layerIndex >= 0 && layerIndex < layers.Count)
            {
                GrassLayer layer = layers[layerIndex];
                if (layer != null && layer.cutHeights != null && pointIndex >= 0 && pointIndex < layer.cutHeights.Length)
                {
                    layer.cutHeights[pointIndex] = -1.0f;
                    layer.cutDirty = true;
                }

                var chunk = layer?.GetChunk(GrassLayer.PositionToChunkCoord(pos, chunkSize));
                if (chunk != null && chunk.cutHeights != null)
                {
                    for (int cp = 0; cp < chunk.points.Count; cp++)
                    {
                        if (Vector3.SqrMagnitude(chunk.points[cp].position - pos) < 0.01f)
                        {
                            chunk.cutHeights[cp] = -1.0f;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// External helper: Cut grass at a specific world position and radius.
        /// </summary>
        public static void CutGrassAt(Vector3 hitPoint, float radius, float stubbleHeight = -1f)
        {
            if (Instance != null)
            {
                Instance.CutGrass(hitPoint, radius, stubbleHeight);
            }
            else
            {
                var mgr = UnityEngine.Object.FindFirstObjectByType<ModernGrassManager>();
                mgr?.CutGrass(hitPoint, radius, stubbleHeight);
            }
        }

        /// <summary>
        /// Regrows all cut grass across all layers immediately.
        /// </summary>
        public void RegrowAll()
        {
            _activeCuts.Clear();
            if (layers == null) return;
            for (int l = 0; l < layers.Count; l++)
            {
                var layer = layers[l];
                if (layer != null)
                {
                    if (layer.cutHeights != null)
                    {
                        for (int i = 0; i < layer.cutHeights.Length; i++)
                        {
                            layer.cutHeights[i] = -1.0f;
                        }
                        layer.cutDirty = true;
                    }

                    if (layer.chunks != null)
                    {
                        for (int c = 0; c < layer.chunks.Count; c++)
                        {
                            var chk = layer.chunks[c];
                            if (chk != null && chk.cutHeights != null)
                            {
                                for (int i = 0; i < chk.cutHeights.Length; i++) chk.cutHeights[i] = -1.0f;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Regrows cut grass within a specific spherical radius.
        /// </summary>
        public void RegrowAt(Vector3 center, float radius)
        {
            float sqrRadius = radius * radius;
            for (int i = _activeCuts.Count - 1; i >= 0; i--)
            {
                var cut = _activeCuts[i];
                float sqrDist = (center.x - cut.position.x) * (center.x - cut.position.x) + (center.z - cut.position.z) * (center.z - cut.position.z);
                if (sqrDist <= sqrRadius)
                {
                    RestoreCutPoint(cut.layerIndex, cut.pointIndex, cut.position);
                    _activeCuts.RemoveAt(i);
                }
            }

            if (layers != null)
            {
                List<int> queryIndices = new List<int>();
                for (int l = 0; l < layers.Count; l++)
                {
                    var layer = layers[l];
                    if (layer == null || layer.cutHeights == null) continue;
                    queryIndices.Clear();
                    if (layer.cullingTree != null)
                        layer.cullingTree.ReturnLeafList(center, queryIndices, radius);
                    else
                        for (int i = 0; i < layer.PointCount; i++) queryIndices.Add(i);

                    for (int i = 0; i < queryIndices.Count; i++)
                    {
                        int idx = queryIndices[i];
                        if (idx >= 0 && idx < layer.points.Count && layer.cutHeights[idx] >= 0f)
                        {
                            Vector3 pPos = layer.points[idx].position;
                            float sqrDist = (center.x - pPos.x) * (center.x - pPos.x) + (center.z - pPos.z) * (center.z - pPos.z);
                            if (sqrDist <= sqrRadius)
                            {
                                RestoreCutPoint(l, idx, pPos);
                            }
                        }
                    }
                }
            }
        }

        public void CutGrass(Vector3 hitPoint, float radius, float stubbleHeight = -1f)
        {
            if (layers == null || layers.Count == 0) return;
            if (stubbleHeight < 0f) stubbleHeight = defaultStubbleHeight;

            float sqrRadius = radius * radius;
            List<int> queryIndices = new List<int>();

            for (int l = 0; l < layers.Count; l++)
            {
                GrassLayer layer = layers[l];
                if (layer == null || layer.PointCount == 0 || !layer.canBeCut) continue;

                layer.EnsureBuffers();
                queryIndices.Clear();

                if (layer.cullingTree != null)
                {
                    layer.cullingTree.ReturnLeafList(hitPoint, queryIndices, radius);
                }
                else
                {
                    for (int i = 0; i < layer.PointCount; i++) queryIndices.Add(i);
                }

                int layerCutCount = 0;
                Vector3 avgCutPos = Vector3.zero;
                Color sampleColor = Color.white;

                for (int i = 0; i < queryIndices.Count; i++)
                {
                    int idx = queryIndices[i];
                    if (idx < 0 || idx >= layer.points.Count) continue;

                    Vector3 pPos = layer.points[idx].position;
                    float sqrDist = (hitPoint.x - pPos.x) * (hitPoint.x - pPos.x) + (hitPoint.z - pPos.z) * (hitPoint.z - pPos.z);

                    if (sqrDist <= sqrRadius)
                    {
                        // Burnt grass check: If grass is currently burning or burned down to ash stubble, cannot be cut with sword!
                        int cx = Mathf.FloorToInt(pPos.x / SPATIAL_CELL_SIZE);
                        int cz = Mathf.FloorToInt(pPos.z / SPATIAL_CELL_SIZE);
                        long cellKey = ((long)cx << 32) | (uint)cz;
                        if (IsCellBurnedOrBurning(cellKey)) continue;

                        float currentCut = layer.cutHeights[idx];
                        bool canCut = false;
                        if (currentCut < 0f)
                        {
                            canCut = true;
                        }
                        else if (regrowthMode == GrassRegrowthMode.Timer)
                        {
                            float elapsed = Time.time - currentCut;
                            float animDur = Mathf.Clamp(regrowAnimationDuration, 0.1f, regrowDelaySeconds);
                            float holdDelay = Mathf.Max(0f, regrowDelaySeconds - animDur);
                            if (elapsed >= regrowDelaySeconds || elapsed > holdDelay + 0.2f)
                            {
                                canCut = true;
                            }
                        }

                        if (canCut)
                        {
                            float cutTimeVal = Mathf.Max(0.001f, Time.time);
                            layer.cutHeights[idx] = cutTimeVal;
                            layer.cutDirty = true;
                            layerCutCount++;
                            avgCutPos += pPos;
                            sampleColor = layer.topColor * layer.points[idx].color;
                            sampleColor.a = 1.0f;

                            var chunk = layer.GetChunk(GrassLayer.PositionToChunkCoord(pPos, chunkSize));
                            if (chunk != null)
                            {
                                chunk.EnsureCutHeights();
                                for (int cp = 0; cp < chunk.points.Count; cp++)
                                {
                                    if (Vector3.SqrMagnitude(chunk.points[cp].position - pPos) < 0.01f)
                                    {
                                        chunk.cutHeights[cp] = cutTimeVal;
                                        break;
                                    }
                                }
                            }

                            if (regrowthMode == GrassRegrowthMode.Distance)
                            {
                                _activeCuts.Add(new CutRecord
                                {
                                    layerIndex = l,
                                    pointIndex = idx,
                                    cutTime = Time.time,
                                    position = pPos
                                });
                            }
                        }
                    }
                }

                // Spawn cut shred particle from pool at the slash center (1 burst per cut action)
                if (layerCutCount > 0)
                {
                    avgCutPos /= layerCutCount;
                    Vector3 spawnPos = new Vector3(hitPoint.x, avgCutPos.y + 0.25f, hitPoint.z);

                    ParticleSystem layerPrefab = layer.cutParticlePrefab;
                    ModernGrassCutPool pool = ModernGrassCutPool.GetOrCreate(layerPrefab);
                    pool.SpawnCutParticle(spawnPos, sampleColor, layerPrefab);

                    // If wide area slash (e.g. radius > 2.5m), spawn peripheral bursts to cover the wide radius
                    if (radius > 2.5f)
                    {
                        int extraBursts = Mathf.Min(3, Mathf.FloorToInt(radius / 1.5f));
                        for (int b = 0; b < extraBursts; b++)
                        {
                            float angle = (b * (360f / extraBursts)) * Mathf.Deg2Rad;
                            Vector3 extraPos = spawnPos + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radius * 0.55f);
                            pool.SpawnCutParticle(extraPos, sampleColor, layerPrefab);
                        }
                    }
                }
            }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null) return;
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            if (camera.name.Contains("Baker") || camera.name.Contains("Terrain") || camera.name.Contains("Temp") || camera.name.Contains("temp") || camera.name.Contains("TestCam")) return;
            if (layers == null || layers.Count == 0) return;
            if (computeShader == null || grassShader == null || _kernelIndex < 0 || _kernelResetArgs < 0)
            {
                ValidateReferences();
                if (computeShader == null || grassShader == null || _kernelIndex < 0 || _kernelResetArgs < 0) return;
            }

            // In Edit Mode, stream ONLY for SceneView camera and ONLY when not actively painting
            if (!Application.isPlaying && enableChunkStreaming && !isPaintingActive && camera != null && camera.cameraType == CameraType.SceneView)
            {
                UpdateStreaming(camera.transform.position, force: false);
            }

            // Gather active interactors
            int interactorCount = 0;
            if (ModernGrassInteractor.ActiveInteractors != null)
            {
                int maxInteractors = Mathf.Min(ModernGrassInteractor.ActiveInteractors.Count, 16);
                for (int i = 0; i < maxInteractors; i++)
                {
                    var inter = ModernGrassInteractor.ActiveInteractors[i];
                    if (inter != null && inter.gameObject.activeInHierarchy)
                    {
                        Vector3 pos = inter.transform.position;
                        _interactorArray[interactorCount] = new Vector4(pos.x, pos.y, pos.z, inter.radius);
                        _interactorParams[interactorCount] = new Vector4(inter.radius, inter.strength, inter.moveDirection.x * inter.currentSpeed, inter.moveDirection.y * inter.currentSpeed);
                        interactorCount++;
                    }
                }
            }

            float currentTime = 0f;
#if UNITY_EDITOR
            currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
#else
            currentTime = Time.time;
#endif

            // Camera frustum planes for GPU culling & custom mesh culling
            bool cameraChanged = (_cachedCamPos != camera.transform.position || _cachedCamRot != camera.transform.rotation);
            if (cameraChanged)
            {
                GeometryUtility.CalculateFrustumPlanes(camera, _frustumPlanes);
                for (int p = 0; p < 6; p++)
                {
                    Vector3 n = _frustumPlanes[p].normal;
                    _frustumPlaneVectors[p] = new Vector4(n.x, n.y, n.z, _frustumPlanes[p].distance);
                }
                _cachedCamPos = camera.transform.position;
                _cachedCamRot = camera.transform.rotation;
                if (drawCullingGizmos) _gizmoBounds.Clear();
            }

            if (enableFireSimulation) EnsureBurnResources();
            Texture burnTex = GetCurrentBurnMap();
            Vector3 effCenter = (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? GetEffectivePlayerPos() : burnMapCenter;
            var terrainBaker = EnsureTerrainBaker();
            Vector3 camPosTerrain = terrainBaker != null ? terrainBaker.orthographicPos : transform.position;
            float camSizeTerrain = terrainBaker != null ? Mathf.Max(1f, terrainBaker.orthographicSize) : 60f;
            float cutAnimDuration = Mathf.Clamp(regrowAnimationDuration, 0.1f, regrowDelaySeconds);
            Vector4 grassCutSettings = new Vector4(
                Mathf.Max(0.1f, regrowDelaySeconds),
                cutAnimDuration,
                defaultStubbleHeight,
                (float)regrowthMode
            );

            for (int i = 0; i < layers.Count; i++)
            {
                GrassLayer layer = layers[i];
                if (layer == null || !layer.isVisible || layer.PointCount == 0) continue;

                layer.EnsureBuffers();

                // If CustomMesh mode, use GPU Instancing to draw 3D meshes/flowers/bushes
                if (layer.RenderMode == FoliageRenderMode.CustomMesh)
                {
                    if (enableCulling && layer.cullingTree != null)
                    {
                        if (cameraChanged || layer.visibleIDs.Count == 0)
                        {
                            layer.visibleIDs.Clear();
                            layer.cullingTree.RetrieveLeaves(_frustumPlanes, drawCullingGizmos ? _gizmoBounds : null, layer.visibleIDs);
                        }
                        if (layer.visibleIDs.Count == 0) continue;
                    }
                    else
                    {
                        if (layer.visibleIDs.Count != layer.PointCount)
                        {
                            layer.visibleIDs.Clear();
                            for (int p = 0; p < layer.PointCount; p++) layer.visibleIDs.Add(p);
                        }
                    }

                    RenderCustomMeshLayer(layer, camera, interactorCount, currentTime);
                    continue;
                }

                if (layer.sourceBuffer == null || layer.drawBuffer == null || layer.argsBuffer == null) continue;

                int totalBladeCount = layer.TotalBladeCount;
                int totalVertexCount = layer.VertexCount;

                if (layer.layerMaterial == null)
                {
                    layer.layerMaterial = new Material(grassShader)
                    {
                        hideFlags = HideFlags.DontSave
                    };
                }

                // 1. Reset indirect draw args on GPU
                computeShader.SetBuffer(_kernelResetArgs, "_DrawArgs", layer.argsBuffer);
                computeShader.Dispatch(_kernelResetArgs, 1, 1, 1);

                // 2. Set compute uniforms & buffers
                computeShader.SetBuffer(_kernelIndex, "_SourcePoints", layer.sourceBuffer);
                computeShader.SetBuffer(_kernelIndex, "_CutBuffer", layer.cutBuffer);
                computeShader.SetBuffer(_kernelIndex, "_DrawVertices", layer.drawBuffer);
                computeShader.SetBuffer(_kernelIndex, "_DrawArgs", layer.argsBuffer);

                computeShader.SetInt("_TotalBlades", totalBladeCount);
                computeShader.SetInt("_MaxDrawVertices", totalVertexCount);
                computeShader.SetInt("_BladesPerClump", layer.BladesPerClump);
                computeShader.SetFloat("_ClumpRadius", layer.clumpRadius);
                computeShader.SetFloat("_ClumpTilt", layer.clumpTilt);

                // GPU Frustum Culling
                computeShader.SetFloat("_EnableFrustumCulling", enableCulling ? 1f : 0f);
                computeShader.SetVectorArray("_FrustumPlanes", _frustumPlaneVectors);
                computeShader.SetFloat("_CastShadows", layer.castShadows ? 1f : 0f);

                // Blade Shape & Geometry
                computeShader.SetInt("_BladeSegments", layer.BladeSegments);
                computeShader.SetFloat("_EnableSegmentLOD", layer.enableSegmentLOD ? 1f : 0f);
                computeShader.SetFloat("_SegmentLODStartDist", layer.segmentLODStartDist);
                computeShader.SetFloat("_SegmentLODEndDist", layer.segmentLODEndDist);
                computeShader.SetInt("_MinSegmentCount", layer.minSegmentCount);
                computeShader.SetInt("_TipShape", (int)layer.tipShape);
                computeShader.SetFloat("_TipWidth", layer.tipWidth);
                computeShader.SetFloat("_BellyWidth", layer.bellyWidth);

                computeShader.SetFloat("_DefaultWidth", layer.defaultWidth);
                computeShader.SetFloat("_DefaultHeight", layer.defaultHeight);
                computeShader.SetFloat("_BottomWidth", layer.bottomWidth);
                computeShader.SetFloat("_BottomWidthVar", layer.bottomWidthVariation);
                computeShader.SetFloat("_WidthVariation", layer.widthVariation);
                computeShader.SetFloat("_HeightVariation", layer.heightVariation);
                computeShader.SetFloat("_BladeForward", layer.bladeForward);
                computeShader.SetFloat("_BladeCurve", layer.bladeCurve);
                computeShader.SetFloat("_UprightIntensity", layer.uprightIntensity);
                computeShader.SetFloat("_NormalUpBlend", layer.normalUpBlend);
                computeShader.SetVector("_TopColor", layer.topColor.linear);
                computeShader.SetVector("_BottomColor", layer.bottomColor.linear);

                // World-Space Noise Color Tint & Waves
                computeShader.SetFloat("_EnableColorNoise", layer.enableColorNoise ? 1f : 0f);
                computeShader.SetVector("_NoiseTopColor", layer.noiseTopColor.linear);
                computeShader.SetVector("_NoiseBottomColor", layer.noiseBottomColor.linear);
                computeShader.SetFloat("_NoiseScale", layer.noiseScale);
                computeShader.SetFloat("_NoiseContrast", layer.noiseContrast);
                computeShader.SetFloat("_EnableNoiseWindWaves", layer.enableNoiseWindWaves ? 1f : 0f);

                // World-Space Noise Height Variations & Waves
                computeShader.SetFloat("_EnableHeightNoise", layer.enableHeightNoise ? 1f : 0f);
                computeShader.SetFloat("_HeightNoiseScale", layer.heightNoiseScale);
                computeShader.SetFloat("_HeightNoiseContrast", layer.heightNoiseContrast);
                computeShader.SetFloat("_HeightNoiseIntensity", layer.heightNoiseIntensity);
                computeShader.SetFloat("_EnableHeightNoiseWaves", layer.enableHeightNoiseWaves ? 1f : 0f);
                computeShader.SetFloat("_HeightWaveIntensity", layer.heightWaveIntensity);

                computeShader.SetVector("_CameraPositionWS", camera.transform.position);
                computeShader.SetFloat("_MinFadeDist", layer.minFadeDistance);
                computeShader.SetFloat("_MaxFadeDist", layer.maxDrawDistance);

                computeShader.SetFloat("_WindStrength", layer.windStrength);
                computeShader.SetFloat("_WindSpeed", layer.windSpeed);
                computeShader.SetFloat("_WindScale", layer.windScale);
                computeShader.SetFloat("_WindElasticity", layer.windElasticity);
                computeShader.SetFloat("_WindSmoothness", layer.windSmoothness);
                computeShader.SetVector("_WindDirection", windDirection);
                computeShader.SetFloat("_Time", currentTime);

                // Layer-based interaction uniforms
                bool layerInteract = layer.enableInteraction;
                computeShader.SetFloat("_EnableInteraction", layerInteract ? 1f : 0f);
                computeShader.SetFloat("_ElasticOscillation", layer.elasticOscillation);

                computeShader.SetInt("_InteractorCount", layerInteract ? interactorCount : 0);
                if (layerInteract && interactorCount > 0)
                {
                    computeShader.SetVectorArray("_Interactors", _interactorArray);
                    computeShader.SetVectorArray("_InteractorParams", _interactorParams);
                }

                int impulseCount = 0;
                if (layerInteract && layer.elasticOscillation > 0.01f && ModernGrassInteractor.ActiveInteractors != null)
                {
                    float maxImpulseDuration = Mathf.Lerp(1.2f, 3.5f, Mathf.Clamp01(layer.elasticOscillation / 10f));
                    for (int interIdx = 0; interIdx < ModernGrassInteractor.ActiveInteractors.Count; interIdx++)
                    {
                        var inter = ModernGrassInteractor.ActiveInteractors[interIdx];
                        if (inter == null || !inter.gameObject.activeInHierarchy || inter.impulseCount == 0) continue;

                        int count = inter.impulseCount;
                        for (int k = 0; k < count; k++)
                        {
                            int idx = (inter.ImpulseHead - 1 - k + ModernGrassInteractor.MaxImpulses) % ModernGrassInteractor.MaxImpulses;
                            var imp = inter.impulses[idx];
                            float age = currentTime - imp.time;
                            if (age >= 0f && age < maxImpulseDuration)
                            {
                                _impulsePoints[impulseCount] = new Vector4(imp.position.x, imp.position.y, imp.position.z, imp.time);
                                _impulseParams[impulseCount] = new Vector4(imp.direction.x, imp.direction.y, imp.radius, maxImpulseDuration);
                                impulseCount++;
                                if (impulseCount >= 32) break;
                            }
                        }
                        if (impulseCount >= 32) break;
                    }
                }

                computeShader.SetInt("_ImpulseCount", impulseCount);
                if (impulseCount > 0)
                {
                    computeShader.SetVectorArray("_ImpulsePoints", _impulsePoints);
                    computeShader.SetVectorArray("_ImpulseParams", _impulseParams);
                }

                int activeShockwaveCount = 0;
                for (int s = 0; s < _shockwaveCount; s++)
                {
                    var sw = _shockwaves[s];
                    float age = currentTime - sw.startTime;
                    if (age >= 0f && age < sw.duration)
                    {
                        _shockwaveOrigins[activeShockwaveCount] = new Vector4(sw.origin.x, sw.origin.y, sw.origin.z, sw.startTime);
                        _shockwaveParams[activeShockwaveCount] = new Vector4(sw.radius, sw.speed, sw.force, sw.thickness);
                        activeShockwaveCount++;
                    }
                }

                computeShader.SetInt("_ShockwaveCount", activeShockwaveCount);
                if (activeShockwaveCount > 0)
                {
                    computeShader.SetVectorArray("_ShockwaveOrigins", _shockwaveOrigins);
                    computeShader.SetVectorArray("_ShockwaveParams", _shockwaveParams);
                }

                int activeWindZoneCount = CollectActiveWindZones(currentTime);
                computeShader.SetInt("_WindZoneCount", activeWindZoneCount);
                if (activeWindZoneCount > 0)
                {
                    computeShader.SetVectorArray("_WindZoneOrigins", _windZoneOrigins);
                    computeShader.SetVectorArray("_WindZoneVectors", _windZoneVectors);
                    computeShader.SetVectorArray("_WindZoneParams", _windZoneParams);
                    computeShader.SetVectorArray("_WindZoneTimes", _windZoneTimes);
                }

                // Fire, Burn & Charred Simulation
                computeShader.SetTexture(_kernelIndex, "_GrassBurnMap", burnTex);
                computeShader.SetFloat("_EnableGrassBurnMap", (enableFireSimulation && burnTex != null) ? 1f : 0f);
                computeShader.SetFloat("_CanCatchFire", layer.canCatchFire ? 1f : 0f);
                computeShader.SetVector("_CharredColor", layer.charredColor.linear);
                computeShader.SetVector("_GrassBurnMapCenter", effCenter);
                computeShader.SetFloat("_GrassBurnMapSize", Mathf.Max(1f, burnMapWorldSize));
                computeShader.SetVector("_GrassBurnMapParams", new Vector4(effCenter.x, effCenter.y, effCenter.z, Mathf.Max(1f, burnMapWorldSize)));
                computeShader.SetVector("_GrassBurnSettings", new Vector4(
                    (enableFireSimulation && burnTex != null) ? 1f : 0f,
                    layer.canCatchFire ? 1f : 0f,
                    (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? 1f : 0f,
                    0f
                ));

                computeShader.SetVector("_OrthographicCamPosTerrain", camPosTerrain);
                computeShader.SetFloat("_OrthographicCamSizeTerrain", camSizeTerrain);

                // Cutting & 100% GPU-Driven Smooth Regrowth Settings
                computeShader.SetVector("_GrassCutSettings", grassCutSettings);

                // 3. Dispatch GPU culling & blade generation
                int threadGroups = Mathf.CeilToInt(totalBladeCount / 64f);
                computeShader.Dispatch(_kernelIndex, threadGroups, 1, 1);

                // 4. Set material properties
                layer.layerMaterial.SetBuffer("_DrawVertices", layer.drawBuffer);
                layer.layerMaterial.SetFloat("_Translucency", layer.translucency);
                layer.layerMaterial.SetFloat("_EdgeHighlight", layer.edgeHighlight);
                layer.layerMaterial.SetFloat("_TipShape", (float)layer.tipShape);

                // Real-time Additional Lights (point lights, spotlights, magic, torches)
                layer.layerMaterial.EnableKeyword("_ADDITIONAL_LIGHTS");

                // Stylized Soft Shadow controls
                layer.layerMaterial.SetFloat("_ShadowCastDensity", layer.castShadows ? layer.shadowCastDensity : 0f);
                layer.layerMaterial.SetFloat("_BladeShadowStrength", layer.bladeShadowStrength);

                // Blade Texture (Procedural Texture mode)
                if (layer.RenderMode == FoliageRenderMode.ProceduralTexture && layer.bladeTexture != null)
                {
                    layer.layerMaterial.EnableKeyword("_USE_BLADE_TEXTURE");
                    layer.layerMaterial.SetFloat("_UseBladeTexture", 1f);
                    layer.layerMaterial.SetTexture("_MainTex", layer.bladeTexture);
                    layer.layerMaterial.SetFloat("_AlphaCutoff", layer.alphaCutoff);
                    layer.layerMaterial.SetFloat("_BlendWithBladeColor", layer.blendWithBladeColor ? 1f : 0f);
                }
                else
                {
                    layer.layerMaterial.DisableKeyword("_USE_BLADE_TEXTURE");
                    layer.layerMaterial.SetFloat("_UseBladeTexture", 0f);
                }

                // Additive ground blend
                if (layer.enableGroundBlend)
                {
                    layer.layerMaterial.EnableKeyword("_GROUND_BLEND_ON");
                    layer.layerMaterial.SetFloat("_EnableGroundBlend", 1f);
                    layer.layerMaterial.SetFloat("_GroundBlendFade", layer.groundBlendFade);
                    layer.layerMaterial.SetFloat("_GroundBlendStretch", layer.groundBlendStretch);
                    layer.layerMaterial.SetFloat("_GroundBlendBrightness", layer.groundBlendBrightness);
                    layer.layerMaterial.SetFloat("_GroundBlendSaturation", layer.groundBlendSaturation);
                    layer.layerMaterial.SetColor("_AmbientAdjustmentColor", layer.ambientAdjustmentColor);
                }
                else
                {
                    layer.layerMaterial.DisableKeyword("_GROUND_BLEND_ON");
                    layer.layerMaterial.SetFloat("_EnableGroundBlend", 0f);
                }

                Bounds bounds = layer.CalculateBounds();
                ShadowCastingMode shadowMode = layer.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

                // 5. Draw procedural indirect (reads vertex count directly from GPU argsBuffer)
                Graphics.DrawProceduralIndirect(
                    layer.layerMaterial,
                    bounds,
                    MeshTopology.Triangles,
                    layer.argsBuffer,
                    0,
                    camera,
                    null,
                    shadowMode,
                    true
                );
            }
        }

        private void RenderCustomMeshLayer(GrassLayer layer, Camera camera, int interactorCount, float currentTime)
        {
            if (layer.grassType == null || layer.grassType.customMesh == null || layer.grassType.customMaterial == null)
                return;

            Mesh mesh = layer.grassType.customMesh;
            Material mat = layer.grassType.customMaterial;
            ShadowCastingMode shadowMode = layer.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

            // Ground blend setup for Custom Mesh foliage
            if (layer.enableGroundBlend)
            {
                mat.EnableKeyword("_GROUND_BLEND_ON");
                mat.SetFloat("_EnableGroundBlend", 1f);
                mat.SetFloat("_GroundBlendFade", layer.groundBlendFade);
                mat.SetFloat("_GroundBlendStretch", layer.groundBlendStretch);
                mat.SetFloat("_GroundBlendBrightness", layer.groundBlendBrightness);
                mat.SetFloat("_GroundBlendSaturation", layer.groundBlendSaturation);
                mat.SetColor("_AmbientAdjustmentColor", layer.ambientAdjustmentColor);
            }
            else
            {
                mat.DisableKeyword("_GROUND_BLEND_ON");
                mat.SetFloat("_EnableGroundBlend", 0f);
            }

            // Layer-based interaction setup for Custom Mesh foliage
            bool layerInteract = layer.enableInteraction;
            mat.SetFloat("_EnableInteraction", layerInteract ? 1f : 0f);
            mat.SetFloat("_ElasticOscillation", layer.elasticOscillation);

            mat.SetInt("_InteractorCount", layerInteract ? interactorCount : 0);
            if (layerInteract && interactorCount > 0)
            {
                mat.SetVectorArray("_Interactors", _interactorArray);
                mat.SetVectorArray("_InteractorParams", _interactorParams);
            }

            int impulseCount = 0;
            if (layerInteract && layer.elasticOscillation > 0.01f && ModernGrassInteractor.ActiveInteractors != null)
            {
                float maxImpulseDuration = Mathf.Lerp(1.2f, 3.5f, Mathf.Clamp01(layer.elasticOscillation / 10f));
                for (int interIdx = 0; interIdx < ModernGrassInteractor.ActiveInteractors.Count; interIdx++)
                {
                    var inter = ModernGrassInteractor.ActiveInteractors[interIdx];
                    if (inter == null || !inter.gameObject.activeInHierarchy || inter.impulseCount == 0) continue;

                    int count = inter.impulseCount;
                    for (int k = 0; k < count; k++)
                    {
                        int idx = (inter.ImpulseHead - 1 - k + ModernGrassInteractor.MaxImpulses) % ModernGrassInteractor.MaxImpulses;
                        var imp = inter.impulses[idx];
                        float age = currentTime - imp.time;
                        if (age >= 0f && age < maxImpulseDuration)
                        {
                            _impulsePoints[impulseCount] = new Vector4(imp.position.x, imp.position.y, imp.position.z, imp.time);
                            _impulseParams[impulseCount] = new Vector4(imp.direction.x, imp.direction.y, imp.radius, maxImpulseDuration);
                            impulseCount++;
                            if (impulseCount >= 32) break;
                        }
                    }
                    if (impulseCount >= 32) break;
                }
            }

            mat.SetInt("_ImpulseCount", impulseCount);
            if (impulseCount > 0)
            {
                mat.SetVectorArray("_ImpulsePoints", _impulsePoints);
                mat.SetVectorArray("_ImpulseParams", _impulseParams);
            }

            int activeMeshShockwaveCount = 0;
            for (int s = 0; s < _shockwaveCount; s++)
            {
                var sw = _shockwaves[s];
                float age = currentTime - sw.startTime;
                if (age >= 0f && age < sw.duration)
                {
                    _shockwaveOrigins[activeMeshShockwaveCount] = new Vector4(sw.origin.x, sw.origin.y, sw.origin.z, sw.startTime);
                    _shockwaveParams[activeMeshShockwaveCount] = new Vector4(sw.radius, sw.speed, sw.force, sw.thickness);
                    activeMeshShockwaveCount++;
                }
            }

            mat.SetInt("_ShockwaveCount", activeMeshShockwaveCount);
            if (activeMeshShockwaveCount > 0)
            {
                mat.SetVectorArray("_ShockwaveOrigins", _shockwaveOrigins);
                mat.SetVectorArray("_ShockwaveParams", _shockwaveParams);
            }

            int activeMeshWindZoneCount = CollectActiveWindZones(currentTime);
            mat.SetInt("_WindZoneCount", activeMeshWindZoneCount);
            if (activeMeshWindZoneCount > 0)
            {
                mat.SetVectorArray("_WindZoneOrigins", _windZoneOrigins);
                mat.SetVectorArray("_WindZoneVectors", _windZoneVectors);
                mat.SetVectorArray("_WindZoneParams", _windZoneParams);
                mat.SetVectorArray("_WindZoneTimes", _windZoneTimes);
            }

            // Fire, Burn & Charred Simulation
            EnsureBurnResources();
            Texture meshBurnTex = GetCurrentBurnMap();
            mat.SetTexture("_GrassBurnMap", meshBurnTex);
            mat.SetFloat("_EnableGrassBurnMap", (enableFireSimulation && meshBurnTex != null) ? 1f : 0f);
            mat.SetFloat("_CanCatchFire", layer.canCatchFire ? 1f : 0f);
            Vector3 effCenterMesh = (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? GetEffectivePlayerPos() : burnMapCenter;
            mat.SetColor("_CharredColor", layer.charredColor);
            mat.SetVector("_GrassBurnMapCenter", effCenterMesh);
            mat.SetFloat("_GrassBurnMapSize", Mathf.Max(1f, burnMapWorldSize));
            mat.SetFloat("_GrassBurnMode", (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? 1f : 0f);

            var terrainBakerMesh = EnsureTerrainBaker();
            Vector3 camPosTerrainMesh = terrainBakerMesh != null ? terrainBakerMesh.orthographicPos : transform.position;
            float camSizeTerrainMesh = terrainBakerMesh != null ? Mathf.Max(1f, terrainBakerMesh.orthographicSize) : 60f;
            mat.SetVector("_OrthographicCamPosTerrain", camPosTerrainMesh);
            mat.SetFloat("_OrthographicCamSizeTerrain", camSizeTerrainMesh);

            int visibleCount = layer.visibleIDs.Count;
            if (visibleCount == 0) return;

            Vector2 scaleRange = layer.grassType.scaleRange;
            if (scaleRange.x <= 0f) scaleRange.x = 0.5f;
            if (scaleRange.y < scaleRange.x) scaleRange.y = scaleRange.x;

            bool alignToNormal = layer.grassType.alignToNormal;
            bool randomYRot = layer.grassType.randomYRotation;

            int batchIndex = 0;
            for (int i = 0; i < visibleCount; i++)
            {
                int pointIndex = layer.visibleIDs[i];
                if (pointIndex < 0 || pointIndex >= layer.points.Count) continue;

                // If cut, hide custom mesh foliage (until regrown)
                if (layer.cutHeights != null && pointIndex < layer.cutHeights.Length && layer.cutHeights[pointIndex] >= 0f)
                {
                    if (regrowthMode == GrassRegrowthMode.Timer)
                    {
                        if (currentTime - layer.cutHeights[pointIndex] < regrowDelaySeconds)
                            continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                GrassPoint pt = layer.points[pointIndex];

                int hash = (int)(pt.position.x * 73856093) ^ (int)(pt.position.z * 83492791);
                float rand01 = Mathf.Abs((hash & 0xFFFF) / 65535.0f);
                float randYaw = Mathf.Abs(((hash >> 8) & 0xFFFF) / 65535.0f * 360f);

                float scaleVal = Mathf.Lerp(scaleRange.x, scaleRange.y, rand01);
                Vector3 scale = new Vector3(scaleVal, scaleVal, scaleVal);

                Quaternion rot;
                if (alignToNormal && pt.normal.sqrMagnitude > 0.01f)
                {
                    Quaternion normalRot = Quaternion.FromToRotation(Vector3.up, pt.normal);
                    rot = randomYRot ? normalRot * Quaternion.Euler(0f, randYaw, 0f) : normalRot;
                }
                else
                {
                    rot = randomYRot ? Quaternion.Euler(0f, randYaw, 0f) : Quaternion.identity;
                }

                _meshInstancedMatrices[batchIndex++] = Matrix4x4.TRS(pt.position, rot, scale);

                if (batchIndex == 1023)
                {
                    Graphics.DrawMeshInstanced(mesh, 0, mat, _meshInstancedMatrices, batchIndex, null, shadowMode, true, 0, camera);
                    batchIndex = 0;
                }
            }

            if (batchIndex > 0)
            {
                Graphics.DrawMeshInstanced(mesh, 0, mat, _meshInstancedMatrices, batchIndex, null, shadowMode, true, 0, camera);
            }
        }

        private void OnDrawGizmos()
        {
            if (drawCullingGizmos && _gizmoBounds != null)
            {
                Gizmos.color = new Color(0f, 1f, 0.4f, 0.35f);
                for (int i = 0; i < _gizmoBounds.Count; i++)
                {
                    Gizmos.DrawWireCube(_gizmoBounds[i].center, _gizmoBounds[i].size);
                }
            }

            if (enableFireSimulation && drawBurnGizmo)
            {
                Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.45f);
                Vector3 gizmoCenter = (burnMapMode == GrassBurnMapMode.PlayerCenteredFloating) ? GetEffectivePlayerPos() : burnMapCenter;
                Gizmos.DrawWireCube(new Vector3(gizmoCenter.x, transform.position.y, gizmoCenter.z), new Vector3(burnMapWorldSize, 12f, burnMapWorldSize));
            }
        }
    }
}
