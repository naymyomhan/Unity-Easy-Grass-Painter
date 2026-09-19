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

        [Header("Cut Effects / Particles")]
        [Tooltip("Global custom Particle System prefab spawned when grass is cut. If null and layer has no prefab, a procedural grass shred particle is used.")]
        public ParticleSystem cutParticlePrefab;

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

        private Plane[] _frustumPlanes = new Plane[6];
        private Vector4[] _frustumPlaneVectors = new Vector4[6];
        private Vector3 _cachedCamPos;
        private Quaternion _cachedCamRot;
        private List<Bounds> _gizmoBounds = new List<Bounds>();
        private Matrix4x4[] _meshInstancedMatrices = new Matrix4x4[1023];

        private void Awake()
        {
            Instance = this;
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
                string[] guids = UnityEditor.AssetDatabase.FindAssets("ModernGrassBlades t:ComputeShader");
                if (guids != null && guids.Length > 0)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    computeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                }
                if (computeShader == null)
                {
                    computeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                        "Assets/ModernGrassTool/Runtime/ModernGrassBlades.compute"
                    );
                }
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

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (_activeCuts.Count == 0) return;

            if (regrowthMode == GrassRegrowthMode.Timer)
            {
                float now = Time.time;
                for (int i = _activeCuts.Count - 1; i >= 0; i--)
                {
                    var cut = _activeCuts[i];
                    if (now - cut.cutTime >= regrowDelaySeconds)
                    {
                        RestoreCutPoint(cut.layerIndex, cut.pointIndex, cut.position);
                        _activeCuts.RemoveAt(i);
                    }
                }
            }
            else if (regrowthMode == GrassRegrowthMode.Distance)
            {
                Vector3 refPos = Vector3.zero;
                if (playerReference != null)
                {
                    refPos = playerReference.position;
                }
                else if (Camera.main != null)
                {
                    refPos = Camera.main.transform.position;
                }
                else
                {
                    var interactor = UnityEngine.Object.FindFirstObjectByType<ModernGrassInteractor>();
                    if (interactor != null)
                    {
                        playerReference = interactor.transform;
                        refPos = interactor.transform.position;
                    }
                }

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
                if (layer == null || layer.PointCount == 0) continue;

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
                        // Cut near root to leave a visible blunt stubble (MinionsArt style)
                        float targetCut = pPos.y + stubbleHeight;
                        float currentCut = layer.cutHeights[idx];
                        if (currentCut < 0f || targetCut < currentCut)
                        {
                            layer.cutHeights[idx] = targetCut;
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
                                        chunk.cutHeights[cp] = targetCut;
                                        break;
                                    }
                                }
                            }

                            if (regrowthMode == GrassRegrowthMode.Timer || regrowthMode == GrassRegrowthMode.Distance)
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

                    ParticleSystem layerPrefab = layer.cutParticlePrefab != null ? layer.cutParticlePrefab : cutParticlePrefab;
                    ModernGrassCutPool pool = ModernGrassCutPool.GetOrCreate(cutParticlePrefab);
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
            if (camera.name.Contains("Baker") || camera.name.Contains("Terrain") || camera.name.Contains("Temp") || camera.name.Contains("temp")) return;
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
                        _interactorParams[interactorCount] = new Vector4(inter.radius, inter.strength, 0f, 0f);
                        interactorCount++;
                    }
                }
            }

            float currentTime = 0f;
#if UNITY_EDITOR
            currentTime = Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
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

                    RenderCustomMeshLayer(layer, camera, interactorCount);
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

                computeShader.SetInt("_InteractorCount", interactorCount);
                if (interactorCount > 0)
                {
                    computeShader.SetVectorArray("_Interactors", _interactorArray);
                    computeShader.SetVectorArray("_InteractorParams", _interactorParams);
                }

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

        private void RenderCustomMeshLayer(GrassLayer layer, Camera camera, int interactorCount)
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

            // Interactor setup for Custom Mesh foliage
            mat.SetInt("_InteractorCount", interactorCount);
            if (interactorCount > 0)
            {
                mat.SetVectorArray("_Interactors", _interactorArray);
                mat.SetVectorArray("_InteractorParams", _interactorParams);
            }

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
                    continue;

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
        }
    }
}
